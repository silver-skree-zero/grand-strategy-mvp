"""
extract_border_topology.py

Replaces extract_border_vectors.py's raster-contour-tracing approach with a
proper TOPOLOGY built directly from the original shapefile geometry. This
fixes all three symptoms that raster-traced borders have:

  - "Chunky" lines: gone, since we're using real polygon coordinates
    instead of a trace of an already-rasterized, texel-quantized image.
  - Missing small territories: gone, since tiny islands are no longer at
    risk of vanishing below a raster mask's pixel footprint.
  - Borders drifting apart at high zoom: gone, since adjacent countries'
    shared edges are stored as ONE shared arc (via topojson's internal
    snapping + arc-sharing), not two independently-digitized/simplified
    polylines that merely look similar.

Output JSON has the EXACT SAME SHAPE as border_vectors.json, so
BorderLineMeshBuilder.cs needs no changes -- only the source data improves.

Requirements:
    pip install geopandas topojson

Usage:
    python extract_border_topology.py
"""

import json

import geopandas as gpd
import topojson as tp
from shapely.validation import make_valid
from shapely.geometry import Polygon, MultiPolygon, GeometryCollection

SHAPEFILE_PATH = "ne_10m_admin_0_countries.shp"
ID_LOOKUP_PATH = "country_id_lookup.json"   # from generate_national_id_map.py
OUTPUT_PATH = "border_vectors.json"

# How aggressively to simplify the shared topology, in degrees (same units
# as lat/lon). Larger = fewer vertices / smoother but less precise;
# smaller = closer to the original data. Because simplification happens on
# the SHARED arcs (not per-country), shared borders stay coincident at any
# setting -- unlike the old per-country approxPolyDP approach.
SIMPLIFY_EPSILON_DEG = 0.05

# Quantization grid topojson snaps vertices to before building the
# topology. This is what forces nearly-but-not-quite-matching source
# coordinates (a real possibility even in the original shapefile) to
# become exactly coincident. Higher = finer grid = less snapping.
QUANTIZATION = 1e6


def resolve_iso(row):
    iso = row.get("ISO_A3", "-99")
    if iso == "-99":
        iso = row.get("ADM0_A3", "-99")
    return iso


def ring_to_latlon_list(coords):
    # shapely coords are (lon, lat); keep that order to match pixel_to_latlon's
    # [lon, lat] convention used everywhere else in the pipeline.
    return [[round(x, 4), round(y, 4)] for x, y in coords]


def main():
    gdf = gpd.read_file(SHAPEFILE_PATH)

    # Build the shared topology (this is what makes adjacent countries'
    # borders identical rather than independently-approximated), then
    # simplify at the topology level so shared edges stay shared afterward.
    topo = tp.Topology(gdf, prequantize=QUANTIZATION, topology=True)
    topo = topo.toposimplify(SIMPLIFY_EPSILON_DEG)
    simplified_gdf = topo.to_gdf()  # same row order/columns as gdf, geometry replaced

    # Reuse the ID assignment already baked into country_id_map.png / its
    # lookup, rather than re-deriving IDs from row order here -- keeps
    # border line IDs, fill-color IDs, and hit-test IDs all in agreement.
    with open(ID_LOOKUP_PATH) as f:
        id_lookup = json.load(f)
    iso_to_id = {v["iso_a3"]: int(k) for k, v in id_lookup.items()}

    output = {}
    for _, row in simplified_gdf.iterrows():
        iso = resolve_iso(row)
        country_id = iso_to_id.get(iso)
        if country_id is None:
            print(f"WARNING: '{iso}' not found in {ID_LOOKUP_PATH} -- skipped "
                  f"(won't match any ID in country_id_map.png)")
            continue

        geom = row.geometry
        if geom is None or geom.is_empty:
            continue

        if not geom.is_valid:
            geom = make_valid(geom)
            print(f"  Repaired invalid geometry for {iso}")

        polygons = to_polygons(geom)

        rings = []
        for poly in polygons:
            rings.append(ring_to_latlon_list(poly.exterior.coords))
            for interior in poly.interiors:
                rings.append(ring_to_latlon_list(interior.coords))

        output[str(country_id)] = rings
        total_verts = sum(len(r) for r in rings)
        print(f"{iso} (ID {country_id}): {len(rings)} ring(s), {total_verts} vertices")

    with open(OUTPUT_PATH, "w") as f:
        json.dump(output, f)
    print(f"Saved {OUTPUT_PATH}")

def to_polygons(geom):
    """Flattens any repaired geometry down to a clean list of Polygons,
    discarding any degenerate point/line artifacts make_valid can emit."""
    if geom.geom_type == "Polygon":
        return [geom]
    if geom.geom_type == "MultiPolygon":
        return list(geom.geoms)
    if geom.geom_type == "GeometryCollection":
        polys = []
        for g in geom.geoms:
            polys.extend(to_polygons(g))
        return polys
    return []  # Point/LineString artifacts -- not useful here, drop them

if __name__ == "__main__":
    main()