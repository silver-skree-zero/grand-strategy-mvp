"""
extract_border_vectors.py

Traces vector border lines directly out of an existing country ID map
(rather than going back to the original shapefile), simplifies them, and
converts them to lat/lon polylines -- ready to be built into a true
constant-pixel-width line mesh in Unity, with no texel-resolution ceiling.

Requirements:
    pip install opencv-python numpy

Usage:
    python extract_border_vectors.py
"""

import json

import cv2
import numpy as np
from PIL import Image

ID_MAP_PATH = "country_id_map.png"
OUTPUT_PATH = "border_vectors.json"

# Must match the ID map's generation settings.
FLIP_V = False

# Contour simplification tolerance, in SOURCE TEXTURE pixels. Larger =
# fewer vertices / smoother-but-less-precise line; smaller = more
# vertices / closer to the raw pixel boundary. Start around 1.5-2.0.
SIMPLIFY_EPSILON_PX = 1.5


def pixel_to_latlon(px, py, width, height, flip_v):
    u = px / width
    v = py / height
    if flip_v:
        v = 1.0 - v
    lon = (u - 0.5) * 360.0
    lat = (v - 0.5) * 180.0
    return lon, lat


def main():
    id_img = Image.open(ID_MAP_PATH).convert("RGB")
    id_array = np.array(id_img)
    id_array = np.flipud(id_array)  # undo the north-up flip applied when the ID map was saved
    height, width = id_array.shape[:2]
    
    id_r = id_array[..., 0].astype(np.int32)
    id_g = id_array[..., 1].astype(np.int32)
    ids = id_r + id_g * 256  # matches the shader's SampleID16 packing

    unique_ids = sorted(i for i in np.unique(ids) if i != 0)

    output = {}
    for country_id in unique_ids:
        mask = (ids == country_id).astype(np.uint8) * 255

        # RETR_CCOMP separates outer boundaries from holes (enclaves),
        # mirroring the even-odd handling the original rasterization used.
        contours, hierarchy = cv2.findContours(
            mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE
        )

        rings = []
        for contour in contours:
            simplified = cv2.approxPolyDP(
                contour, SIMPLIFY_EPSILON_PX, closed=True
            )
            if len(simplified) < 3:
                continue  # degenerate sliver, skip

            ring = []
            for point in simplified:
                px, py = point[0]
                lon, lat = pixel_to_latlon(px, py, width, height, FLIP_V)
                ring.append([round(lon, 4), round(lat, 4)])
            rings.append(ring)

        output[str(country_id)] = rings
        total_verts = sum(len(r) for r in rings)
        print(f"ID {country_id}: {len(rings)} ring(s), {total_verts} vertices "
              f"(from {sum(len(c) for c in contours)} raw contour points)")

    with open(OUTPUT_PATH, "w") as f:
        json.dump(output, f)
    print(f"Saved {OUTPUT_PATH}")


if __name__ == "__main__":
    main()