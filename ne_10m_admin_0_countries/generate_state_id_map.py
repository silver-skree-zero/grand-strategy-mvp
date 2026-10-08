"""
generate_state_id_map.py

Builds the state/province-level ID map from Natural Earth's "Admin 1 -
States, Provinces" dataset:

  1. Loads + topology-builds the states dataset (shared-arc borders, same
     technique as the country pipeline, so adjacent states' borders don't
     drift apart from each other either).
  2. Records each state's OWNING COUNTRY (structural/origin data, looked
     up via the shared country_id_lookup.json -- never baked into pixels,
     per the ownership-vs-geography split decided earlier).
  3. MERGES any state below an area threshold into its largest same-country
     neighbor, done once here at the vector level, before rasterization --
     so the raster ID map, state_id_lookup.json, and (once built) any
     state-level border/fill meshes all agree on the same merged regions.
  4. Rasterizes the final, merged states into state_id_map.png using the
     same R+G 16-bit channel packing as the national map.

Requirements:
    pip install geopandas topojson shapely numpy pillow

Usage:
    python generate_state_id_map.py
"""

import json

import numpy as np
import geopandas as gpd
from shapely.ops import unary_union
from shapely.validation import make_valid
from shapely.geometry import MultiPolygon
from PIL import Image
import shapely.vectorized

from border_topology_common import build_simplified_gdf, load_iso_to_id

STATES_SHAPEFILE_PATH = "ne_10m_admin_1_states_provinces.shp"
COUNTRY_ID_LOOKUP_PATH = "country_id_lookup.json"

OUTPUT_ID_MAP_PATH = "state_id_map.png"
OUTPUT_LOOKUP_PATH = "state_id_lookup.json"

WIDTH = 4096
HEIGHT = 2048
FLIP_V = False

# States' finer detail can usually tolerate a smaller epsilon than the
# country map without exploding vertex counts -- tune empirically.
SIMPLIFY_EPSILON_DEG = 0.02
QUANTIZATION = 1e6

# Any state smaller than this (in equivalent ID-map pixels, estimated from
# its polygon area) gets annexed into its largest same-country neighbor.
AREA_THRESHOLD_PX = 10

# Natural Earth admin-1's field naming for the owning country's code.
ADM0_FIELD = "adm0_a3"
NAME_FIELD = "name"


def pixel_grid_latlon(width, height, flip_v):
    xs = np.arange(width)
    ys = np.arange(height)
    xx, yy = np.meshgrid(xs, ys)
    u = xx / width
    v = yy / height
    if flip_v:
        v = 1.0 - v
    lon = (u - 0.5) * 360.0
    lat = (v - 0.5) * 180.0
    return lon, lat


def bbox_to_pixel_range(bounds, width, height, flip_v, pad=2):
    """Converts a (minx, miny, maxx, maxy) lon/lat bbox into a pixel
    sub-range of the ID map, so rasterization only touches the small
    region a feature actually covers instead of the whole grid -- with
    thousands of states, grid-wide contains() checks per feature would be
    far too slow."""
    minx, miny, maxx, maxy = bounds
    u_min, u_max = (minx / 360.0) + 0.5, (maxx / 360.0) + 0.5
    v_min, v_max = (miny / 180.0) + 0.5, (maxy / 180.0) + 0.5
    if flip_v:
        v_min, v_max = 1.0 - v_max, 1.0 - v_min

    col_min = max(0, int(u_min * width) - pad)
    col_max = min(width, int(u_max * width) + pad + 1)
    row_min = max(0, int(v_min * height) - pad)
    row_max = min(height, int(v_max * height) + pad + 1)
    return col_min, col_max, row_min, row_max


def _flatten_to_polygons(geom):
    if geom.geom_type == "Polygon":
        return [geom]
    if geom.geom_type == "MultiPolygon":
        return list(geom.geoms)
    if geom.geom_type == "GeometryCollection":
        polys = []
        for g in geom.geoms:
            polys.extend(_flatten_to_polygons(g))
        return polys
    return []  # degenerate point/line artifact -- not useful here


def repair_invalid_geometries(gdf):
    """make_valid rebuilds self-intersecting polygons into clean ones --
    necessary before any unary_union call, since GEOS refuses to merge
    invalid input (the 'side location conflict' error). Admin-1 data's
    denser, more complex coastlines make this noticeably more likely than
    it was for the country-level dataset."""
    def _repair(geom):
        if geom is None or geom.is_empty:
            return geom
        if geom.is_valid:
            return geom
        repaired = make_valid(geom)
        polys = _flatten_to_polygons(repaired)
        if not polys:
            return None
        return polys[0] if len(polys) == 1 else MultiPolygon(polys)

    gdf = gdf.copy()
    gdf["geometry"] = gdf["geometry"].apply(_repair)
    before = len(gdf)
    gdf = gdf[gdf["geometry"].notna() & ~gdf["geometry"].is_empty]
    dropped = before - len(gdf)
    if dropped:
        print(f"  Dropped {dropped} feature(s) with unrepairable geometry")
    return gdf


def merge_small_states(gdf, area_threshold_px, width, height):
    """Iteratively annexes any state below the area threshold into its
    largest same-country touching neighbor. Runs entirely at the vector
    level so every downstream consumer (raster, border lines, fill
    meshes) sees the same, already-merged set of regions."""
    pixels_per_deg2 = (width / 360.0) * (height / 180.0)

    gdf = gdf.copy()  # "_merged_from" is expected to already exist (set in main())
    unmergeable = set()

    while True:
        areas_px = gdf.geometry.area * pixels_per_deg2
        below = gdf[(areas_px < area_threshold_px) & (~gdf.index.isin(unmergeable))]
        if below.empty:
            break

        below_areas = areas_px.loc[below.index]
        small_idx = below_areas.idxmin()
        small_row = gdf.loc[small_idx]
        small_geom = small_row.geometry

        same_country = gdf[
            (gdf[ADM0_FIELD] == small_row[ADM0_FIELD]) & (gdf.index != small_idx)
        ]
        touching = same_country[same_country.geometry.touches(small_geom)]

        if touching.empty:
            unmergeable.add(small_idx)
            print(f"  No eligible same-country neighbor for "
                  f"'{small_row.get(NAME_FIELD, '?')}' -- left as-is "
                  f"(below threshold, {areas_px.loc[small_idx]:.1f}px)")
            continue

        touching_areas = touching.geometry.area * pixels_per_deg2
        target_idx = touching_areas.idxmax()

        try:
            merged_geom = unary_union([gdf.loc[target_idx, "geometry"], small_geom])
        except Exception as e:
            # Shouldn't happen post-repair, but with thousands of features
            # it's safer to skip one bad pair than crash the whole run.
            print(f"  WARNING: union failed for '{small_row.get(NAME_FIELD, '?')}' "
                  f"-> '{gdf.loc[target_idx, NAME_FIELD]}' ({e}); left unmerged")
            unmergeable.add(small_idx)
            continue
        gdf.at[target_idx, "geometry"] = merged_geom
        gdf.at[target_idx, "_merged_from"] = (
            gdf.at[target_idx, "_merged_from"]
            + [small_row.get(NAME_FIELD, "?")]
            + small_row["_merged_from"]
        )

        print(f"  Merged '{small_row.get(NAME_FIELD, '?')}' "
              f"({areas_px.loc[small_idx]:.1f}px) into "
              f"'{gdf.loc[target_idx, NAME_FIELD]}'")

        gdf = gdf.drop(index=small_idx)

    return gdf


def main():
    print("Building state topology (this can take a while for admin-1 data)...")
    gdf = build_simplified_gdf(
        shapefile_path=STATES_SHAPEFILE_PATH,
        epsilon=SIMPLIFY_EPSILON_DEG,
        quantization=QUANTIZATION,
    )

    print("Repairing invalid geometry...")
    gdf = repair_invalid_geometries(gdf)
    gdf["_merged_from"] = [[] for _ in range(len(gdf))]

    iso_to_country_id = load_iso_to_id(COUNTRY_ID_LOOKUP_PATH)

    print("Merging states below the area threshold...")
    gdf = merge_small_states(gdf, AREA_THRESHOLD_PX, WIDTH, HEIGHT)

    # Assign final sequential state IDs only after merging has settled, so
    # IDs aren't wasted on regions that no longer exist as their own unit.
    gdf = gdf.sort_values([ADM0_FIELD, NAME_FIELD]).reset_index(drop=True)

    lon_grid, lat_grid = pixel_grid_latlon(WIDTH, HEIGHT, FLIP_V)
    id_array = np.zeros((HEIGHT, WIDTH), dtype=np.uint16)
    state_lookup = {}

    next_id = 1
    for _, row in gdf.iterrows():
        geom = row.geometry
        if geom is None or geom.is_empty:
            continue
        if next_id > 65535:
            raise RuntimeError("Exceeded 65,535 states -- out of ID space.")

        state_id = next_id
        next_id += 1

        country_iso = row.get(ADM0_FIELD, "-99")
        country_id = iso_to_country_id.get(country_iso)
        if country_id is None:
            print(f"WARNING: owning country '{country_iso}' for state "
                  f"'{row.get(NAME_FIELD, '?')}' not found in "
                  f"{COUNTRY_ID_LOOKUP_PATH} -- origin_country_id will be null")

        col_min, col_max, row_min, row_max = bbox_to_pixel_range(
            geom.bounds, WIDTH, HEIGHT, FLIP_V
        )
        sub_lon = lon_grid[row_min:row_max, col_min:col_max]
        sub_lat = lat_grid[row_min:row_max, col_min:col_max]
        mask = shapely.vectorized.contains(geom, sub_lon, sub_lat)
        id_array[row_min:row_max, col_min:col_max][mask] = state_id

        area_px = int(mask.sum())
        state_lookup[state_id] = {
            "name": row.get(NAME_FIELD, ""),
            "origin_country_id": country_id,
            "origin_country_iso": country_iso,
            "area_px": area_px,
            "merged_from": row["_merged_from"],
        }
        merged_note = f" (absorbed {len(row['_merged_from'])})" if row["_merged_from"] else ""
        print(f"ID {state_id}: {row.get(NAME_FIELD, '')} "
              f"[{country_iso}] {area_px}px{merged_note}")

    with open(OUTPUT_LOOKUP_PATH, "w") as f:
        json.dump(state_lookup, f, indent=2)
    print(f"Saved {OUTPUT_LOOKUP_PATH} ({len(state_lookup)} states)")

    rgb = np.zeros((HEIGHT, WIDTH, 3), dtype=np.uint8)
    rgb[..., 0] = id_array & 0xFF
    rgb[..., 1] = (id_array >> 8) & 0xFF
    rgb[..., 2] = 0

    img = Image.fromarray(np.flipud(rgb), mode="RGB")
    img.save(OUTPUT_ID_MAP_PATH)
    print(f"Saved {OUTPUT_ID_MAP_PATH} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()