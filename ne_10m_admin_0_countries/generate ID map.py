"""
generate_national_id_map.py

Rasterizes EVERY country in a Natural Earth "Admin 0 - Countries" shapefile
into a single equirectangular ID map texture, one unique ID per country.

Channel layout (intentionally reserved now for a future state/province pass):
    R = country ID          (0-255, this file; ne_110 has ~177 countries)
    G = 0, reserved         (future: high byte of a 16-bit country ID,
                             id16 = R + G*256, needed only once you add
                             non-state actors/factions pushing the country
                             count past 255)
    B = 0, reserved         (future: low byte of a 16-bit STATE id)
    A = 255, reserved       (future: high byte of a 16-bit STATE id --
                             NOT transparency. See Unity import note below.)

Only R is meaningful today. CountryPicker/the shader should keep reading
R alone until a state pass is added -- this file's job is just to make
sure adding that pass later never requires touching existing R data.

Requirements:
    pip install geopandas shapely numpy pillow

Usage:
    1. Download & unzip ne_110m_admin_0_countries from naturalearthdata.com
    2. Set SHAPEFILE_PATH below
    3. Run: python generate_national_id_map.py
    4. Produces:
         - country_id_map.png     (the ID map itself)
         - country_id_lookup.json (ID -> {iso_a3, name}, for mapping IDs
                                    back to CountryData assets in Unity)
"""

import json

import numpy as np
import geopandas as gpd
import shapely.vectorized
from PIL import Image

# ---- Configuration ------------------------------------------------------

SHAPEFILE_PATH = "ne_10m_admin_0_countries_usa.shp"
OUTPUT_ID_MAP_PATH = "country_id_map.png"
OUTPUT_LOOKUP_PATH = "country_id_lookup.json"

WIDTH = 4096
HEIGHT = 2048

# Must match flipV in RuntimeUVSphere / CountryPicker / the shader.
FLIP_V = False


# ---- Pixel -> lat/lon, matching the C# formulas exactly -----------------

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


def resolve_iso(row):
    """Handles the known ISO_A3 == '-99' gotcha (France, Norway, etc.)."""
    iso = row.get("ISO_A3", "-99")
    if iso == "-99":
        iso = row.get("ADM0_A3", "-99")
    return iso


# ---- Rasterize ------------------------------------------------------------

def main():
    gdf = gpd.read_file(SHAPEFILE_PATH)

    id_array = np.zeros((HEIGHT, WIDTH), dtype=np.uint8)
    lon_grid, lat_grid = pixel_grid_latlon(WIDTH, HEIGHT, FLIP_V)

    id_lookup = {}
    next_id = 1  # 0 is reserved for "no country"

    for _, row in gdf.iterrows():
        geom = row.geometry
        if geom is None or geom.is_empty:
            continue

        iso_code = str(resolve_iso(row))
        if iso_code == "BLM" or iso_code == "SMR" or iso_code == "VAT" or iso_code == "MCO" or iso_code == "AND":
            continue

        if next_id > 255:
            raise RuntimeError(
                "Exceeded 255 countries -- single-channel R assignment can't "
                "go further. This is where the future 16-bit (R+G) country "
                "ID comes in; not needed for the 110m dataset today."
            )

        country_id = next_id
        next_id += 1

        mask = shapely.vectorized.contains(geom, lon_grid, lat_grid)
        id_array[mask] = country_id

        id_lookup[country_id] = {
            "iso_a3": resolve_iso(row),
            "name": row.get("NAME", ""),
        }
        print(f"{resolve_iso(row):>4}  ID {country_id:>3}  "
              f"{int(mask.sum()):>7} px  {row.get('NAME', '')}")

    with open(OUTPUT_LOOKUP_PATH, "w") as f:
        json.dump(id_lookup, f, indent=2)
    print(f"Saved {OUTPUT_LOOKUP_PATH} ({len(id_lookup)} countries)")

    # Full RGBA, with G/B reserved-zero and A reserved-255 (data, not alpha).
    rgba = np.zeros((HEIGHT, WIDTH, 4), dtype=np.uint8)
    rgba[..., 0] = id_array      # R = country ID (today's only live channel)
    rgba[..., 1] = 0             # G reserved
    rgba[..., 2] = 0             # B reserved
    rgba[..., 3] = 255           # A reserved -- constant today, so Unity's
                                  # alpha-is-transparency dilation has no
                                  # fully-transparent pixels to bleed from;
                                  # see import note below for why this still
                                  # needs to be checked by hand later.

    img = Image.fromarray(np.flipud(rgba), mode="RGBA")
    img.save(OUTPUT_ID_MAP_PATH)
    print(f"Saved {OUTPUT_ID_MAP_PATH} ({WIDTH}x{HEIGHT})")


if __name__ == "__main__":
    main()