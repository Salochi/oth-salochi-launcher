"""
Draws the Salochi mark: two hills and a trail that winds up the near one in an S. Writes the app's logo PNG and the
Windows icon (16 to 256 px).

    python tools/make_logo.py
"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "src", "Salochi.Launcher", "Assets")

PLATE = (42, 57, 55)      # ridge
FAR = (111, 143, 132)     # far hill, sage
NEAR = (238, 240, 230)    # near hill, bone
TRAIL = (242, 193, 78)    # trail-marker yellow
SS = 8                    # supersampling


def bezier(p0, p1, p2, p3, n=48):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        pts.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                    u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))
    return pts


def path(*segments):
    pts = []
    for s in segments:
        pts.extend(bezier(*s) if len(s) == 4 else [s])
    return pts


def draw(size):
    S = size * SS
    k = S / 32.0
    sc = lambda pts: [(x * k, y * k) for x, y in pts]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # the plate: a rounded square
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=7 * k, fill=PLATE)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=7 * k, fill=255)

    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    l = ImageDraw.Draw(layer)
    far = path((-1, 22), ((-1, 22), (7, 9), (15, 8), (21, 13)), ((21, 13), (25, 16.5), (29, 19), (33, 20)), (33, 33), (-1, 33))
    l.polygon(sc(far), fill=FAR)
    near = path((-1, 25), ((-1, 25), (8, 18.5), (17, 18), (25, 22.5)), ((25, 22.5), (28, 24.2), (31, 25.5), (33, 26)), (33, 33), (-1, 33))
    l.polygon(sc(near), fill=NEAR)
    # the trail: an S from the bottom edge up to the near hill's crest, thinning as it climbs
    trail = bezier((10, 33), (19, 30), (21, 27.5), (15, 25.6)) + bezier((15, 25.6), (10, 24), (13, 21.2), (17.5, 20))[1:]
    n = len(trail)
    for i in range(n - 1):
        w = (2.6 - 1.4 * i / n) * k
        l.line([sc([trail[i]])[0], sc([trail[i + 1]])[0]], fill=TRAIL, width=int(w))
        x, y = sc([trail[i + 1]])[0]
        l.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=TRAIL)
    img.paste(layer, (0, 0), Image.composite(layer, Image.new("RGBA", (S, S)), mask))
    return img.resize((size, size), Image.LANCZOS)


def main():
    draw(512).save(os.path.join(ASSETS, "logo.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    big = draw(256)
    big.save(os.path.join(ASSETS, "salochi.ico"), sizes=[(s, s) for s in sizes],
             append_images=[draw(s) for s in sizes[:-1]])
    print("wrote logo.png and salochi.ico")


if __name__ == "__main__":
    main()
