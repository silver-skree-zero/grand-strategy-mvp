#!/usr/bin/env python3
"""Generate a placeholder 4x4 icon atlas (white glyphs on transparent).
Cells are row-major from the BOTTOM-left, matching MapIconLayer's atlasIndex.
Import in Unity as Sprite-less Texture2D: sRGB on, Alpha Is Transparency on,
filter Bilinear, no mip maps (or mips with 'Border Mip Maps' off)."""
import math
from PIL import Image, ImageDraw

CELL, SS, COLS, ROWS = 128, 4, 4, 4          # supersample 4x for clean edges
W = CELL * SS

def poly(n, r, rot=0.0, c=W // 2):
    return [(c + r * math.cos(rot + 2 * math.pi * i / n),
             c + r * math.sin(rot + 2 * math.pi * i / n)) for i in range(n)]

def star(r, c=W // 2):
    pts = []
    for i in range(10):
        rr = r if i % 2 == 0 else r * 0.45
        a = -math.pi / 2 + i * math.pi / 5
        pts.append((c + rr * math.cos(a), c + rr * math.sin(a)))
    return pts

def glyph(i):
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    c, r, w = W // 2, int(W * 0.36), int(W * 0.07)
    wh = (255, 255, 255, 255)
    if i == 0:   d.ellipse([c-r, c-r, c+r, c+r], outline=wh, width=w); d.ellipse([c-r//2, c-r//2, c+r//2, c+r//2], fill=wh)       # city
    elif i == 1: d.polygon(star(r * 1.15), fill=wh)                                                                                # capital
    elif i == 2: d.rectangle([c-r, c-r, c+r, c+r], outline=wh, width=w); [d.line([c-r, c+k, c+r, c+k], fill=wh, width=w//2+1) for k in (-r//2, 0, r//2)]  # datacenter
    elif i == 3: d.polygon(poly(6, r * 1.05, math.pi / 6), outline=wh, width=w); d.rectangle([c-r//3, c-r//3, c+r//3, c+r//3], fill=wh)  # fab
    elif i == 4: d.polygon([(c+r*0.2, c-r*1.05), (c-r*0.65, c+r*0.1), (c-r*0.05, c+r*0.1), (c-r*0.2, c+r*1.05), (c+r*0.65, c-r*0.1), (c+r*0.05, c-r*0.1)], fill=wh)  # power
    elif i == 5: d.polygon(poly(4, r * 1.1, 0), outline=wh, width=w); d.ellipse([c-w, c-w, c+w, c+w], fill=wh)                    # port
    elif i == 6: d.polygon([(c, c-r*1.1), (c+r, c+r*0.9), (c-r, c+r*0.9)], fill=wh)                                                # spaceport
    elif i == 7: d.polygon([(c-r, c-r*0.1), (c, c-r), (c+r, c-r*0.1), (c+r, c+r*0.45), (c, c-r*0.1+r*0.5), (c-r, c+r*0.45)], fill=wh)   # military chevron
    elif i == 8: d.line([c-r, c, c+r, c], fill=wh, width=w); d.line([c, c-r, c, c+r], fill=wh, width=w); d.ellipse([c-r//2, c-r//2, c+r//2, c+r//2], outline=wh, width=w)  # network
    elif i == 9: d.polygon(poly(3, r * 1.15, math.pi / 2), outline=wh, width=w); d.ellipse([c-r//3, c-r//3+r//5, c+r//3, c+r//3+r//5], fill=wh)  # refinery
    elif i == 10: d.ellipse([c-r*0.8, c-r*0.8, c+r*0.8, c+r*0.8], outline=wh, width=w); d.line([c-r*0.8, c, c+r*0.8, c], fill=wh, width=w//2+1)  # lab
    return im.resize((CELL, CELL), Image.LANCZOS)

atlas = Image.new("RGBA", (CELL * COLS, CELL * ROWS), (0, 0, 0, 0))
for i in range(11):
    col, row = i % COLS, i // COLS
    # row 0 is the BOTTOM row in Unity UVs, so flip when pasting into image space
    atlas.paste(glyph(i), (col * CELL, (ROWS - 1 - row) * CELL))
atlas.save("map_icon_atlas.png")
print("wrote map_icon_atlas.png")
