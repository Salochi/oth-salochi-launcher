"""
Draws the Salochi mark: a trail blaze, the painted rectangle hikers follow from tree to tree, with an S cut out of the
paint. Writes the app's logo PNG and the Windows icon (16 to 256 px).

    python tools/make_logo.py
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "src", "Salochi.Launcher", "Assets")

PLATE = (31, 43, 42)      # pine
TRUNK = (52, 70, 66)      # the tree the blaze is painted on
BLAZE = (242, 193, 78)    # trail-marker yellow
SS = 8                    # supersampling


def bezier(p0, p1, p2, p3, n=64):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        pts.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                    u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))
    return pts


def stroke(draw, pts, width, fill):
    """A round-capped polyline."""
    r = width / 2
    for a, b in zip(pts, pts[1:]):
        draw.line([a, b], fill=fill, width=int(round(width)))
    for x, y in pts:
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def draw(size):
    S = size * SS
    k = S / 32.0
    sc = lambda pts: [(x * k, y * k) for x, y in pts]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=7 * k, fill=255)

    art = Image.new("RGBA", (S, S), PLATE + (255,))
    d = ImageDraw.Draw(art)
    # the trunk: a band up the middle, a little wider than the blaze
    d.rectangle([8.5 * k, -1, 23.5 * k, S + 1], fill=TRUNK)
    # the blaze: a tall painted rectangle
    d.rounded_rectangle([11 * k, 5 * k, 21 * k, 27 * k], radius=1.6 * k, fill=BLAZE)
    # the S, cut out of the paint
    s = bezier((18.7, 10.0), (17.6, 7.6), (13.0, 7.7), (13.2, 11.4)) + \
        bezier((13.2, 11.4), (13.4, 14.6), (18.8, 14.4), (18.8, 18.8))[1:] + \
        bezier((18.8, 18.8), (18.8, 23.6), (13.8, 24.3), (13.1, 21.5))[1:]
    stroke(d, sc(s), 2.9 * k, PLATE)

    img.paste(art, (0, 0), mask)
    return img.resize((size, size), Image.LANCZOS)


def main():
    draw(512).save(os.path.join(ASSETS, "logo.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    draw(256).save(os.path.join(ASSETS, "salochi.ico"), sizes=[(s, s) for s in sizes],
                   append_images=[draw(s) for s in sizes[:-1]])
    print("wrote logo.png and salochi.ico")


if __name__ == "__main__":
    main()
