"""Generates the NOXEL eye logo (ICO + PNGs) procedurally.

Usage:  python scripts/make_icons.py
Writes into src/NOXEL.App/Assets/. Requires Pillow.
The logo is original: an almond eye silhouette, violet iris, black pupil,
single catch-light, on a deep-navy rounded tile with faint scanlines.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "NOXEL.App", "Assets")
S = 1024  # master size


def almond_mask(size, cx, cy, w, h):
    """Eye opening: two circular arcs meeting at sharp corners."""
    m = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(m)
    pts = []
    n = 180
    for i in range(n + 1):  # upper lid
        t = i / n
        x = cx - w / 2 + w * t
        y = cy - h / 2 * math.sin(math.pi * t) ** 0.85
        pts.append((x, y))
    for i in range(n + 1):  # lower lid
        t = 1 - i / n
        x = cx - w / 2 + w * t
        y = cy + h / 2 * 0.82 * math.sin(math.pi * t) ** 0.9
        pts.append((x, y))
    d.polygon(pts, fill=255)
    return m


def radial(size, cx, cy, r, inner, outer):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    for y in range(int(cy - r), int(cy + r) + 1):
        for x in range(int(cx - r), int(cx + r) + 1):
            dd = math.hypot(x - cx, y - cy) / r
            if dd <= 1:
                k = dd ** 1.4
                c = tuple(int(inner[i] * (1 - k) + outer[i] * k) for i in range(4))
                px[x, y] = c
    return img


def build(tile=True):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    if tile:
        bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        m = Image.new("L", (S, S), 0)
        ImageDraw.Draw(m).rounded_rectangle([24, 24, S - 24, S - 24], radius=220, fill=255)
        grad = Image.new("RGBA", (S, S))
        gd = ImageDraw.Draw(grad)
        for y in range(S):
            t = y / S
            gd.line([(0, y), (S, y)], fill=(int(14 + 10 * t), int(12 + 6 * t), int(34 + 20 * t), 255))
        # faint scanlines
        for y in range(0, S, 12):
            gd.line([(0, y), (S, y)], fill=(255, 255, 255, 10))
        bg.paste(grad, (0, 0), m)
        # violet rim
        rim = Image.new("L", (S, S), 0)
        ImageDraw.Draw(rim).rounded_rectangle([24, 24, S - 24, S - 24], radius=220, outline=255, width=10)
        bg.paste((150, 110, 255, 120), (0, 0), rim)
        img = Image.alpha_composite(img, bg)

    cx, cy = S / 2, S / 2 + 10
    ew, eh = S * 0.80, S * 0.50
    mask = almond_mask(S, cx, cy, ew, eh)

    # glow behind eye
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    glow.paste((140, 90, 255, 150), (0, 0), mask)
    glow = glow.filter(ImageFilter.GaussianBlur(40))
    img = Image.alpha_composite(img, glow)

    # sclera
    sclera = radial(S, cx, cy, ew * 0.55, (236, 234, 244, 255), (150, 146, 176, 255))
    eye = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    eye = Image.alpha_composite(eye, sclera)

    # iris
    ir = eh * 0.47
    iris = radial(S, cx, cy, ir, (70, 40, 120, 255), (150, 105, 245, 255))
    idraw = ImageDraw.Draw(iris)
    for a in range(0, 360, 6):  # fibres
        r1, r2 = ir * 0.45, ir * 0.95
        rad = math.radians(a)
        idraw.line([(cx + r1 * math.cos(rad), cy + r1 * math.sin(rad)),
                    (cx + r2 * math.cos(rad), cy + r2 * math.sin(rad))], fill=(40, 20, 80, 90), width=4)
    idraw.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], outline=(20, 10, 40, 255), width=14)
    eye = Image.alpha_composite(eye, iris)
    # pupil
    pr = ir * 0.46
    ImageDraw.Draw(eye).ellipse([cx - pr, cy - pr, cx + pr, cy + pr], fill=(6, 4, 12, 255))
    # catch light
    hl = ir * 0.16
    hx, hy = cx + ir * 0.30, cy - ir * 0.34
    ImageDraw.Draw(eye).ellipse([hx - hl, hy - hl, hx + hl, hy + hl], fill=(255, 255, 255, 245))

    clipped = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    clipped.paste(eye, (0, 0), mask)
    # lid shadow at top of the opening
    shadow = Image.new("L", (S, S), 0)
    sm = almond_mask(S, cx, cy - eh * 0.10, ew * 1.02, eh * 0.95)
    shadow = ImageChops.subtract(mask, sm).filter(ImageFilter.GaussianBlur(10))
    clipped.paste((10, 6, 24, 200), (0, 0), shadow)
    img = Image.alpha_composite(img, clipped)

    # outline
    edge = mask.filter(ImageFilter.FIND_EDGES).filter(ImageFilter.MaxFilter(9))
    img.paste((205, 185, 255, 255), (0, 0), edge)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    tile = build(True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    tile.resize((256, 256), Image.LANCZOS).save(
        os.path.join(OUT, "noxel.ico"), sizes=[(s, s) for s in sizes])
    tile.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "noxel-256.png"))
    tile.resize((64, 64), Image.LANCZOS).save(os.path.join(OUT, "noxel-64.png"))
    bare = build(False)
    bare.resize((128, 128), Image.LANCZOS).save(os.path.join(OUT, "noxel-eye.png"))
    print("icons written to", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
