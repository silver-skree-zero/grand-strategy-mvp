"""
border_topology_common.py

Shared topology-building step for extract_border_topology.py and
triangulate_countries.py. Pulling this into one place guarantees both
scripts triangulate/trace the SAME simplified, snapped geometry -- if they
each built their own topology independently (even with "the same" settings
copy-pasted), any drift between the two config values would show up as a
visible seam between the fill mesh and the border lines.
"""

import geopandas as gpd
import topojson as tp

SHAPEFILE_PATH = "ne_110m_admin_0_countries.shp"
SIMPLIFY_EPSILON_DEG = 0.05
QUANTIZATION = 1e6


def build_simplified_gdf(shapefile_path=SHAPEFILE_PATH,
                          epsilon=SIMPLIFY_EPSILON_DEG,
                          quantization=QUANTIZATION):
    gdf = gpd.read_file(shapefile_path)
    topo = tp.Topology(gdf, prequantize=quantization, topology=True)
    topo = topo.toposimplify(epsilon)
    return topo.to_gdf()


def resolve_iso(row):
    iso = row.get("ISO_A3", "-99")
    if iso == "-99":
        iso = row.get("ADM0_A3", "-99")
    return iso


def load_iso_to_id(id_lookup_path="country_id_lookup.json"):
    import json
    with open(id_lookup_path) as f:
        id_lookup = json.load(f)
    return {v["iso_a3"]: int(k) for k, v in id_lookup.items()}
