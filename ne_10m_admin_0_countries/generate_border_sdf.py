"""
generate_border_sdf.py

Generates a border DISTANCE FIELD from an existing country ID map, for use
as a second, separate texture that lets the shader draw anti-aliased
borders at any zoom level -- something the ID map itself can never do,
since ID values are arbitrary labels, not a continuous/interpolatable
quantity.

Each pixel's value = distance (in source pixels, clamped and normalized
0-1) to the nearest border edge. Unlike the ID map, THIS texture is safe
to bilinear-filter and even lightly compress, because smoothing a distance
value produces a smooth, meaningful result -- smoothing an ID does not.

Requirements:
    pip install numpy pillow scipy

Usage:
    python generate_border_sdf.py
"""

import numpy as np
from PIL import Image
from scipy import ndimage

ID_MAP_PATH = "country_id_map.png"
OUTPUT_PATH = "country_border_sdf.png"

# Distance (in source pixels) at which the field saturates to 1.0.
# Smaller = thinner line capability / more contrast near the edge;
# larger = smoother falloff, more tolerant of extreme zoom.
MAX_DISTANCE_PX = 6.0


def main():
    id_img = Image.open(ID_MAP_PATH).convert("RGB")
    id_array = np.array(id_img)
    id_r = id_array[..., 0].astype(np.int32)
    id_g = id_array[..., 1].astype(np.int32)
    ids = id_r + id_g * 256  # matches SampleID16 in the shader

    # Border pixels: where this pixel's ID differs from a direct neighbor.
    # (Matches the shader's existing 4-neighbor isBorder check, done once
    # here across the whole image instead of per-fragment every frame.)
    right = np.roll(ids, -1, axis=1)
    left = np.roll(ids, 1, axis=1)
    up = np.roll(ids, -1, axis=0)
    down = np.roll(ids, 1, axis=0)
    is_border = (ids != right) | (ids != left) | (ids != up) | (ids != down)

    # distance_transform_edt gives distance to the nearest ZERO in the input,
    # so invert: border pixels become 0, everything else becomes 1, then we
    # measure distance away from those border pixels.
    distance = ndimage.distance_transform_edt(~is_border)

    normalized = np.clip(distance / MAX_DISTANCE_PX, 0.0, 1.0)
    sdf_u8 = (normalized * 255).astype(np.uint8)

    # Single channel is enough; store it in R, duplicate isn't needed, but
    # keep the file RGB for broad import compatibility.
    out = np.stack([sdf_u8, sdf_u8, sdf_u8], axis=-1)
    Image.fromarray(out, mode="RGB").save(OUTPUT_PATH)
    print(f"Saved {OUTPUT_PATH}")


if __name__ == "__main__":
    main()