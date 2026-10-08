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
  3. Rasterizes every state into state_id_map.png using the same R+G
     16-bit channel packing as the national map. (Small-state merging has
     been removed for now; every feature that survives geometry repair
     gets its own ID and its own lookup entry.)

Requirements:
    pip install geopandas topojson shapely numpy pillow

Usage:
    python generate_state_id_map.py
"""

import json

import numpy as np
import geopandas as gpd
from shapely.validation import make_valid
from shapely.geometry import MultiPolygon
from PIL import Image
import shapely.vectorized

from border_topology_common import build_simplified_gdf, load_iso_to_id

STATES_SHAPEFILE_PATH = "ne_10m_admin_1_states_provinces.shp"
COUNTRY_ID_LOOKUP_PATH = "country_id_lookup.json"

OUTPUT_ID_MAP_PATH = "state_id_map.png"
OUTPUT_LOOKUP_PATH = "state_id_lookup.json"
OUTPUT_OWNERSHIP_TEX_PATH = "state_owner_map.png"

WIDTH = 4096
HEIGHT = 2048
FLIP_V = False

# States' finer detail can usually tolerate a smaller epsilon than the
# country map without exploding vertex counts -- tune empirically.
SIMPLIFY_EPSILON_DEG = 0.02
QUANTIZATION = 1e6

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
    """make_valid rebuilds self-intersecting polygons into clean ones, so
    rasterization and any later geometry operations never see invalid
    input. Admin-1 data's denser, more complex coastlines make invalid
    geometry noticeably more likely than in the country-level dataset."""
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


def main():
    print("Building state topology (this can take a while for admin-1 data)...")
    gdf = build_simplified_gdf(
        shapefile_path=STATES_SHAPEFILE_PATH,
        epsilon=SIMPLIFY_EPSILON_DEG,
        quantization=QUANTIZATION,
    )

    print("Repairing invalid geometry...")
    gdf = repair_invalid_geometries(gdf)
    print(f"  {len(gdf)} features after repair")

    iso_to_country_id = load_iso_to_id(COUNTRY_ID_LOOKUP_PATH)

    # Deterministic ordering so state IDs are stable between runs.
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
                  f"{COUNTRY_ID_LOOKUP_PATH} -- origin_country_id will be 0")

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
            "origin_country_id": country_id if country_id is not None else 0,
            "origin_country_iso": country_iso,
            "area_px": area_px,
        }
        print(f"ID {state_id}: {row.get(NAME_FIELD, '')} "
              f"[{country_iso}] {area_px}px")

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

    # Static state -> owning-country lookup, addressed the SAME way the ID
    # map's (R,G) decodes to a state id: x = id % 256 (low byte), y = id //
    # 256 (high byte). 256x256 exactly covers the full 16-bit id space, one
    # texel per possible state id. This is a lookup table, not a geographic
    # raster -- deliberately NOT flipud'd like the ID map above, since
    # there's no lat/lon orientation here to match.
    owner_rgb = np.zeros((256, 256, 3), dtype=np.uint8)
    for state_id, info in state_lookup.items():
        country_id = info["origin_country_id"]
        if not country_id:
            continue  # leave as (0,0,0) -- "unresolved owner"
        x = state_id % 256
        y = state_id // 256
        owner_rgb[y, x, 0] = country_id & 0xFF
        owner_rgb[y, x, 1] = (country_id >> 8) & 0xFF

    Image.fromarray(owner_rgb, mode="RGB").save(OUTPUT_OWNERSHIP_TEX_PATH)
    print(f"Saved {OUTPUT_OWNERSHIP_TEX_PATH} (256x256)")


if __name__ == "__main__":
    main()