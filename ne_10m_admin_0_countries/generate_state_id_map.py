"""
generate_state_id_map.py

Builds the state/province-level ID map from Natural Earth's "Admin 1 -
States, Provinces" dataset:

  1. Loads + topology-builds the states dataset (shared-arc borders, same
     technique as the country pipeline, so adjacent states' borders don't
     drift apart from each other either).
  2. Walks the states in a fixed roster order, rasterizing each one. If a
     state comes out smaller than AREA_THRESHOLD_PX pixels, it is ANNEXED on
     the spot into an already-processed (i.e. lower-ID) same-country
     neighbor: its pixels are painted with the neighbor's ID, and the ID
     counter does NOT advance, so the next state in the roster takes the ID
     the small state would have used. Because color is derived from the ID,
     the annexed pixels automatically take the neighbor's color in both view
     modes. This is part of the main loop, not a post-processing pass.
  3. Records each state's OWNING COUNTRY (structural/origin data only; current
     ownership lives at runtime), its pixel neighbors (used by Unity to pick
     high-contrast colors), and what it absorbed (merged_from).
  4. Writes state_id_map.png (R+G 16-bit ids), state_id_lookup.json,
     state_owner_map.png and state_merge_report.json.

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

OUTPUT_ID_MAP_PATH = "../Assets/Textures/Earth/Countries/country_id_map.png"
OUTPUT_LOOKUP_PATH = "../Assets/Scripts/state_id_lookup.json"
OUTPUT_OWNERSHIP_TEX_PATH = "state_owner_map.png"
OUTPUT_MERGE_REPORT_PATH = "../Assets/Scripts/state_merge_report.json"

WIDTH = 16384  
HEIGHT = 8192
FLIP_V = False

# States' finer detail can usually tolerate a smaller epsilon than the
# country map without exploding vertex counts -- tune empirically.
SIMPLIFY_EPSILON_DEG = 0.02
QUANTIZATION = 1e6

# Natural Earth admin-1's field naming.
ADM0_FIELD = "adm0_a3"
NAME_FIELD = "name"
CODE_FIELD = "adm1_code"

# ---- Merging ---------------------------------------------------------------

MERGE_ENABLED = True

# A state covering fewer ID-map pixels than this is annexed into a neighbor.
AREA_THRESHOLD_PX = 700

# Roster order. Annexation can only target states that were already
# processed, so the order decides how many small states find a neighbor:
#   "area_desc"    - within each country, biggest first. Every small state
#                    then has its larger neighbors processed before it, so
#                    nearly all of them can merge. (Default.)
#   "alphabetical" - (country, name). Stable and easy to read, but a small
#                    state whose neighbors all sort later stays standalone.
PROCESS_ORDER = "area_desc"

# Which eligible neighbor wins:
#   "longest_border" - the neighbor sharing the most boundary pixels. (Default.)
#   "largest"        - the biggest neighbor by pixel area.
ANNEX_BY = "longest_border"

# A state with ZERO pixels (smaller than one pixel center) has no raster
# contact to find neighbors with, so it is matched to the nearest processed
# same-country state by geometry distance, up to this many degrees.
SUBPIXEL_ANNEX_MAX_DISTANCE_DEG = 1.0

# Natural Earth's admin-0 and admin-1 files disagree on a few country codes
# (South Sudan is SSD in one and SDS in the other). Tried when the direct
# lookup fails, so no hand-editing of the JSON is needed.
ISO_ALIASES = {"SDS": "SSD", "SSD": "SDS"}


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


# ---- Annexation helpers -----------------------------------------------------

def neighbor_contact_counts(id_array, region, mask):
    """Returns {neighbor_id: contact_pixel_count} for every already-painted
    state touching `mask`. Called BEFORE this state is painted, and later
    states haven't been painted yet, so every ID found here is by
    construction a lower, already-processed ID."""
    col_min, col_max, row_min, row_max = region
    sub = id_array[row_min:row_max, col_min:col_max]

    dilated = mask.copy()
    dilated[1:, :] |= mask[:-1, :]
    dilated[:-1, :] |= mask[1:, :]
    dilated[:, 1:] |= mask[:, :-1]
    dilated[:, :-1] |= mask[:, 1:]
    ring = dilated & ~mask

    ids = sub[ring]
    ids = ids[ids > 0]
    if ids.size == 0:
        return {}
    uniq, counts = np.unique(ids, return_counts=True)
    return {int(i): int(c) for i, c in zip(uniq, counts)}


def choose_annex_target(contacts, id_country, state_area, country_iso):
    """Picks the neighbor to annex into from `contacts`, restricted to the
    same country. Ties fall to the lower ID."""
    candidates = {sid: c for sid, c in contacts.items()
                  if id_country.get(sid) == country_iso}
    if not candidates:
        return None
    if ANNEX_BY == "largest":
        return max(candidates, key=lambda s: (state_area[s], candidates[s], -s))
    return max(candidates, key=lambda s: (candidates[s], state_area[s], -s))


def nearest_same_country(geom, country_iso, processed_geoms):
    """Fallback for zero-pixel states: nearest processed same-country state
    by geometry distance, within SUBPIXEL_ANNEX_MAX_DISTANCE_DEG."""
    best_id, best_d = None, None
    for sid, g in processed_geoms.get(country_iso, []):
        d = geom.distance(g)
        if best_d is None or d < best_d:
            best_id, best_d = sid, d
    if best_id is not None and best_d <= SUBPIXEL_ANNEX_MAX_DISTANCE_DEG:
        return best_id
    return None


def compute_adjacency(id_array):
    """{id: set(neighbor ids)} from pixel contact in the FINAL id map
    (so it reflects merges), including wrap-around at the antimeridian."""
    packed = []

    def add(a, b):
        m = (a != b) & (a > 0) & (b > 0)
        if not m.any():
            return
        lo = np.minimum(a[m], b[m]).astype(np.uint32)
        hi = np.maximum(a[m], b[m]).astype(np.uint32)
        packed.append(np.unique((lo << 16) | hi))

    add(id_array[:, :-1], id_array[:, 1:])
    add(id_array[:-1, :], id_array[1:, :])
    add(id_array[:, -1], id_array[:, 0])

    adjacency = {}
    if not packed:
        return adjacency
    for p in np.unique(np.concatenate(packed)):
        lo, hi = int(p >> 16), int(p & 0xFFFF)
        adjacency.setdefault(lo, set()).add(hi)
        adjacency.setdefault(hi, set()).add(lo)
    return adjacency


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

    # Deterministic roster order, so state IDs are stable between runs.
    gdf["_area_deg2"] = gdf.geometry.area
    if PROCESS_ORDER == "area_desc":
        gdf = gdf.sort_values([ADM0_FIELD, "_area_deg2", NAME_FIELD],
                              ascending=[True, False, True])
    else:
        gdf = gdf.sort_values([ADM0_FIELD, NAME_FIELD])
    gdf = gdf.reset_index(drop=True)

    lon_grid, lat_grid = pixel_grid_latlon(WIDTH, HEIGHT, FLIP_V)
    id_array = np.zeros((HEIGHT, WIDTH), dtype=np.uint16)

    state_lookup = {}
    id_country = {}        # state id -> adm0 code string (for same-country checks)
    state_area = {}        # state id -> running pixel area (grows as states are absorbed)
    processed_geoms = {}   # adm0 code -> [(state id, geometry)] for sub-pixel fallback
    merge_report = []
    unplaced = []
    left_small = []

    next_id = 1
    for _, row in gdf.iterrows():
        geom = row.geometry
        if geom is None or geom.is_empty:
            continue

        name = row.get(NAME_FIELD, "")
        code = row.get(CODE_FIELD, "")
        country_iso = row.get(ADM0_FIELD, "-99")

        region = bbox_to_pixel_range(geom.bounds, WIDTH, HEIGHT, FLIP_V)
        col_min, col_max, row_min, row_max = region
        sub_lon = lon_grid[row_min:row_max, col_min:col_max]
        sub_lat = lat_grid[row_min:row_max, col_min:col_max]
        mask = shapely.vectorized.contains(geom, sub_lon, sub_lat)
        area_px = int(mask.sum())

        # ---- Annex into an already-processed neighbor? ---------------------
        if MERGE_ENABLED and area_px < AREA_THRESHOLD_PX:
            if area_px > 0:
                contacts = neighbor_contact_counts(id_array, region, mask)
                target_id = choose_annex_target(contacts, id_country, state_area, country_iso)
            else:
                target_id = nearest_same_country(geom, country_iso, processed_geoms)

            if target_id is not None:
                if area_px > 0:
                    id_array[row_min:row_max, col_min:col_max][mask] = target_id
                state_area[target_id] += area_px
                target = state_lookup[target_id]
                target["area_px"] = state_area[target_id]
                target["merged_from"].append(name)
                merge_report.append({
                    "name": name, "adm1_code": code, "country_iso": country_iso,
                    "area_px": area_px,
                    "into_id": target_id, "into_name": target["name"],
                })
                print(f"  annexed '{name}' ({area_px}px) -> ID {target_id} '{target['name']}'")
                continue  # no ID consumed: the next state reuses it

            if area_px == 0:
                unplaced.append({"name": name, "adm1_code": code, "country_iso": country_iso})
                print(f"  WARNING: '{name}' [{country_iso}] has no pixels and no "
                      f"processed same-country state within "
                      f"{SUBPIXEL_ANNEX_MAX_DISTANCE_DEG} deg -- not placed")
                continue

            left_small.append(name)  # tiny, but no eligible lower-ID neighbor

        # ---- Normal state: takes the next ID ------------------------------
        if next_id > 65535:
            raise RuntimeError("Exceeded 65,535 states -- out of ID space.")
        state_id = next_id
        next_id += 1

        country_id = iso_to_country_id.get(country_iso)
        if country_id is None and country_iso in ISO_ALIASES:
            country_id = iso_to_country_id.get(ISO_ALIASES[country_iso])
        if country_id is None:
            print(f"WARNING: owning country '{country_iso}' for state "
                  f"'{name}' not found in {COUNTRY_ID_LOOKUP_PATH} "
                  f"-- origin_country_id will be 0")

        id_array[row_min:row_max, col_min:col_max][mask] = state_id

        state_lookup[state_id] = {
            "name": name,
            "origin_country_id": country_id if country_id is not None else 0,
            "origin_country_iso": country_iso,
            "area_px": area_px,
            "merged_from": [],
        }
        id_country[state_id] = country_iso
        state_area[state_id] = area_px
        processed_geoms.setdefault(country_iso, []).append((state_id, geom))
        print(f"ID {state_id}: {name} [{country_iso}] {area_px}px")

    # ---- Adjacency (final map, post-merge) -----------------------------------
    print("Computing neighbor lists...")
    adjacency = compute_adjacency(id_array)
    for state_id, info in state_lookup.items():
        info["neighbors"] = sorted(adjacency.get(state_id, []))

    with open(OUTPUT_LOOKUP_PATH, "w") as f:
        json.dump(state_lookup, f, indent=2)
    print(f"Saved {OUTPUT_LOOKUP_PATH} ({len(state_lookup)} states)")

    with open(OUTPUT_MERGE_REPORT_PATH, "w") as f:
        json.dump({"merged": merge_report, "unplaced": unplaced,
                   "left_small_no_neighbor": left_small}, f, indent=2)
    print(f"Merge summary: {len(merge_report)} annexed, "
          f"{len(left_small)} small but left standalone (no lower-ID same-country "
          f"neighbor), {len(unplaced)} unplaced. Details in {OUTPUT_MERGE_REPORT_PATH}")

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