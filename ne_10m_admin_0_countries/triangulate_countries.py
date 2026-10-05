"""
triangulate_countries.py

Triangulates each country's real polygon geometry into fill meshes, for
Unity to render as actual geometry instead of a procedural ID-texture
shader fill. Uses a max-triangle-area constraint specifically to avoid
the "chord sags below the sphere surface" artifact that bit the border
line mesh earlier -- the same root cause (long straight spans / large flat
triangles with too few vertices), fixed proactively here by making sure
no triangle is ever allowed to get large enough for the sag to be visible.

Requirements:
    pip install geopandas topojson triangle shapely

Usage:
    python triangulate_countries.py
"""

import json

import geopandas as gpd
import topojson as tp
import numpy as np
import triangle as tr
from shapely.geometry import Polygon

from extract_border_topology import resolve_iso

OUTPUT_PATH = "country_fill_meshes.json"

# Max triangle area, in degrees^2 of lat/lon space. Smaller = more
# triangles / tighter sphere-hugging, especially noticeable on large
# countries (Russia, Canada); larger = fewer triangles / cheaper, but
# raises the risk of visible faceting on a big, sparsely-detailed interior.
MAX_TRIANGLE_AREA_DEG2 = 4.0

# Minimum angle (degrees) Triangle tries to maintain -- improves mesh
# quality/avoids degenerate slivers; 20-30 is a reasonable range.
MIN_ANGLE_DEG = 25

SHAPEFILE_PATH = "ne_10m_admin_0_countries_usa.shp"
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

def build_pslg(poly: Polygon):
    """Vertices/segments/holes for the `triangle` library from one
    exterior ring plus any interior holes."""
    vertices = []
    segments = []
    holes = []

    def add_ring(coords):
        start_idx = len(vertices)
        pts = list(coords)[:-1]  # drop the closing duplicate point
        vertices.extend(pts)
        n = len(pts)
        for i in range(n):
            segments.append((start_idx + i, start_idx + (i + 1) % n))

    add_ring(poly.exterior.coords)
    for interior in poly.interiors:
        add_ring(interior.coords)
        rep = Polygon(interior.coords).representative_point()
        holes.append((rep.x, rep.y))

    return vertices, segments, holes


def triangulate_polygon(poly: Polygon):
    vertices, segments, holes = build_pslg(poly)
    if len(vertices) < 3:
        return None

    pslg = {
        "vertices": np.array(vertices),
        "segments": np.array(segments),
    }
    if holes:
        pslg["holes"] = np.array(holes)

    opts = f"pq{MIN_ANGLE_DEG}a{MAX_TRIANGLE_AREA_DEG2}"
    result = tr.triangulate(pslg, opts)

    if "triangles" not in result:
        return None  # degenerate input Triangle couldn't mesh

    return result["vertices"].tolist(), result["triangles"].tolist()


def main():
    gdf = gpd.read_file(SHAPEFILE_PATH)

    # Build the shared topology (this is what makes adjacent countries'
    # borders identical rather than independently-approximated), then
    # simplify at the topology level so shared edges stay shared afterward.
    topo = tp.Topology(gdf, prequantize=QUANTIZATION, topology=True)
    topo = topo.toposimplify(SIMPLIFY_EPSILON_DEG)
    simplified_gdf = topo.to_gdf()  # same row order/columns as gdf, geometry replaced
    #simplified_gdf = build_simplified_gdf()
    #iso_to_id = load_iso_to_id()
    with open(ID_LOOKUP_PATH) as f:
        id_lookup = json.load(f)
    iso_to_id = {v["iso_a3"]: int(k) for k, v in id_lookup.items()}

    output = {}
    for _, row in simplified_gdf.iterrows():
        iso = resolve_iso(row)
        country_id = iso_to_id.get(iso)
        if country_id is None:
            print(f"WARNING: '{iso}' not in country_id_lookup.json -- skipped")
            continue

        geom = row.geometry
        if geom is None or geom.is_empty:
            continue

        polygons = geom.geoms if geom.geom_type == "MultiPolygon" else [geom]

        parts = []
        for poly in polygons:
            tri_result = triangulate_polygon(poly)
            if tri_result is None:
                continue
            verts, tris = tri_result
            parts.append({
                "vertices": [[round(x, 4), round(y, 4)] for x, y in verts],
                "triangles": [i for tri in tris for i in tri],
            })

        output[str(country_id)] = parts
        total_tris = sum(len(p["triangles"]) // 3 for p in parts)
        print(f"{iso} (ID {country_id}): {len(parts)} part(s), {total_tris} triangles")

    with open(OUTPUT_PATH, "w") as f:
        json.dump(output, f)
    print(f"Saved {OUTPUT_PATH}")


if __name__ == "__main__":
    main()