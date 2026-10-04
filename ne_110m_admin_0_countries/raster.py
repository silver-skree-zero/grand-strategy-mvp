"""
generate_country_id_map.py

Rasterizes country polygons from a Natural Earth "Admin 0 - Countries"
shapefile into a flat equirectangular "ID map" texture for Unity's
CountryPicker / PoliticalMapController pipeline.

Each output pixel is RGBA (countryID, 0, 0, 255) -- only the Red channel
carries data, matching EquirectIDMapTest's Color32(id, 0, 0, 255) convention.

Requirements:
    pip install geopandas shapely numpy pillow

Usage:
    1. Download & unzip ne_110m_admin_0_countries from naturalearthdata.com
    2. Set SHAPEFILE_PATH below to the unzipped .shp file
    3. Add entries to COUNTRY_IDS for whichever countries you want baked in
    4. Run: python generate_country_id_map.py
    5. Import the resulting PNG into Unity: set Filter Mode = Point,
       Compression = None, sRGB (Color Texture) = OFF (this is data, not color)
"""

import numpy as np
import geopandas as gpd
import shapely.vectorized
from PIL import Image

# ---- Configuration ------------------------------------------------------

SHAPEFILE_PATH = "ne_110m_admin_0_countries.shp"
OUTPUT_PATH = "country_id_map.png"

WIDTH = 2048
HEIGHT = 1024

# Must match the flipV value used in RuntimeUVSphere / EquirectRectangleTest /
# EquirectIDMapTest in your Unity project, or the baked map won't align with
# your existing calibrated textures (the rectangle/sliver tests).
FLIP_V = False

# ISO_A3 -> in-game country ID. 0 is reserved for "no country" -- never use it.
# NOTE: a handful of countries (France, Norway, Kosovo, Somaliland, N. Cyprus)
# have a known Natural Earth bug where ISO_A3 == "-99". For those, use
# ADM0_A3 instead (this script tries ISO_A3 first, then falls back
# automatically -- see resolve_country_geometry below).
COUNTRY_IDS = {
    "USA": 255,
    "CAN": 128,
    "FRA": 30
}


# ---- Pixel -> lat/lon, matching the C# formulas exactly -----------------

def pixel_grid_latlon(width, height, flip_v):
    """
    Returns (lon_grid, lat_grid) arrays of shape (height, width), where
    row 0 / col 0 corresponds to the same (y=0, x=0) indexing used in
    EquirectIDMapTest's C# loop:
        v = y / height; if flip_v: v = 1 - v; lat = (v - 0.5) * 180
        u = x / width;                        lon = (u - 0.5) * 360
    """
    xs = np.arange(width)
    ys = np.arange(height)
    xx, yy = np.meshgrid(xs, ys)  # yy[0, :] == 0, i.e. row 0 == y = 0

    u = xx / width
    v = yy / height
    if flip_v:
        v = 1.0 - v

    lon = (u - 0.5) * 360.0
    lat = (v - 0.5) * 180.0
    return lon, lat


# ---- Robust country lookup (handles the ISO_A3 == "-99" gotcha) ---------

def resolve_country_geometry(gdf, iso_code):
    matches = gdf[gdf["ISO_A3"] == iso_code]
    if matches.empty and "ADM0_A3" in gdf.columns:
        matches = gdf[gdf["ADM0_A3"] == iso_code]
    if matches.empty:
        return None
    # unary_union merges multiple rows (if any) into one (multi)polygon,
    # and is a no-op if there's only one matching row.
    return matches.geometry.unary_union


# ---- Rasterize ------------------------------------------------------------

def main():
    gdf = gpd.read_file(SHAPEFILE_PATH)

    id_array = np.zeros((HEIGHT, WIDTH), dtype=np.uint8)
    lon_grid, lat_grid = pixel_grid_latlon(WIDTH, HEIGHT, FLIP_V)

    for iso_code, country_id in COUNTRY_IDS.items():
        geom = resolve_country_geometry(gdf, iso_code)
        if geom is None:
            print(f"WARNING: no feature found for '{iso_code}' "
                  f"(tried ISO_A3 and ADM0_A3) -- skipped")
            continue

        # Vectorized point-in-polygon test across the entire pixel grid at
        # once. Correctly handles MultiPolygon (Alaska/Hawaii as separate
        # parts of the same country) and holes (even-odd rule via GEOS),
        # regardless of how concave the coastline is.
        mask = shapely.vectorized.contains(geom, lon_grid, lat_grid)
        id_array[mask] = country_id
        print(f"{iso_code}: filled {int(mask.sum())} pixels with ID {country_id}")

    rgba = np.zeros((HEIGHT, WIDTH, 4), dtype=np.uint8)
    rgba[..., 0] = id_array
    rgba[..., 3] = id_array

    # Our array row 0 == y=0 == v=0 (south pole, when FLIP_V=False), matching
    # Unity's bottom-left texture origin. PIL's Image.fromarray treats array
    # row 0 as the TOP of the saved file, so flip vertically before saving:
    # this makes the PNG look like a normal north-up world map on disk, and
    # Unity's texture import (which re-flips file-top -> texture-top) lands
    # the north pole back at v=1 and south pole at v=0, exactly matching
    # your existing flipV=False textures.
    img = Image.fromarray(np.flipud(rgba), mode="RGBA")
    img.save(OUTPUT_PATH)
    print(f"Saved {OUTPUT_PATH} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()