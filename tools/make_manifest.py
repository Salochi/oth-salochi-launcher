"""
Builds a release folder: manifest.json plus every mod's DLL (and the pinned BepInEx zip), ready to attach to a GitHub
release or to point the launcher at locally.

    python tools/make_manifest.py --cars ../oth-cars --out release/out [--bepinex BepInEx-...zip --bepinex-version 6.0.0-be.788]
    SalochiLauncher.exe --manifest release/out/manifest.json      # try it against local builds

Download URLs are written relative to the manifest, so the same folder works locally and as release assets. With
--tag, they are absolute links to that release on GitHub instead.
"""
import argparse
import hashlib
import json
import os
import shutil
import sys

REPO = "https://github.com/Salochi/oth-salochi-launcher"


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", default=os.path.join(here, "..", "release", "mods.src.json"))
    ap.add_argument("--cars", required=True, help="the oth-cars checkout the DLLs were built in")
    ap.add_argument("--out", required=True)
    ap.add_argument("--tag", help="write absolute URLs to this release tag")
    ap.add_argument("--bepinex", help="the BepInEx zip to ship (mirrored as a release asset)")
    ap.add_argument("--bepinex-version")
    args = ap.parse_args()

    with open(args.src, encoding="utf-8") as f:
        src = json.load(f)
    os.makedirs(args.out, exist_ok=True)

    def url(name):
        return f"{REPO}/releases/download/{args.tag}/{name}" if args.tag else name

    mods = []
    for m in src["mods"]:
        dll = os.path.join(args.cars, m["build"])
        if not os.path.isfile(dll):
            sys.exit(f"{m['id']}: no build at {dll} (build oth-cars first)")
        shutil.copy2(dll, os.path.join(args.out, m["file"]))
        entry = {k: v for k, v in m.items() if k != "build"}
        entry.update(url=url(m["file"]), sha256=sha256(dll), size=os.path.getsize(dll))
        mods.append(entry)

    manifest = {"schema": 1, "testedGameBuilds": src.get("testedGameBuilds", []), "mods": mods}
    if args.bepinex:
        if not args.bepinex_version:
            sys.exit("--bepinex needs --bepinex-version")
        name = os.path.basename(args.bepinex)
        shutil.copy2(args.bepinex, os.path.join(args.out, name))
        manifest["bepinex"] = {"version": args.bepinex_version, "url": url(name),
                               "sha256": sha256(args.bepinex), "size": os.path.getsize(args.bepinex)}

    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"wrote {os.path.join(args.out, 'manifest.json')}: {', '.join(m['id'] for m in mods)}")


if __name__ == "__main__":
    main()
