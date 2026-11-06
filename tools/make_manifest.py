"""
Builds a release folder, ready to attach to a GitHub release or to point the launcher at locally:

  manifest.json (+ .sig)   the launcher's mod list
  mods.json (+ .sig)       the same mods in the original OverTheHillPlus format, for Salochi Core's in-game installer
  OTH.*.dll                every mod
  BepInEx-*.zip            the pinned mod loader (release/bepinex/, see README)
  launcher.json (+ .sig)   the newest launcher build, and SalochiLauncher.exe itself (from dotnet publish)
  THIRD-PARTY-NOTICES.md   the third-party notices and licence texts (OverTheHillPlus MIT, BepInEx LGPL, ...)

Each mod's DLL comes from its "repo" (default oth-cars): a checkout next to this one, or under --repos.

    python tools/make_manifest.py --out release/out [--tag v1.0.0] [--no-sign]
    SalochiLauncher.exe --manifest release/out/manifest.json      # try it against local builds

The launcher's download URLs are relative to its manifest, so the same folder works locally and as release assets;
with --tag they are absolute links to that release. mods.json always needs absolute URLs (Core only downloads from
this repo): without --tag they point at the latest release. Both lists are signed with the release key
(tools/sign.cs, %USERPROFILE%\\.salochi\\release-key.pem).
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
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
    ap.add_argument("--repos", default=os.path.join(here, "..", ".."),
                    help="the folder holding the mod checkouts (oth-cars, oth-salochi-fixes, ...); default: next to this one")
    ap.add_argument("--out", required=True)
    ap.add_argument("--tag", help="write absolute URLs to this release tag")
    ap.add_argument("--bepinex", default=os.path.join(here, "..", "release", "bepinex", "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788.zip"),
                    help="the BepInEx zip to ship (mirrored as a release asset)")
    ap.add_argument("--bepinex-version", default="6.0.0-be.788")
    ap.add_argument("--launcher", default=os.path.join(here, "..", "src", "Salochi.Launcher", "bin", "Release", "net10.0", "win-x64", "publish", "SalochiLauncher.exe"),
                    help="the published launcher exe (dotnet publish src/Salochi.Launcher -c Release)")
    ap.add_argument("--launcher-version", help="default: <Version> in Directory.Build.props")
    ap.add_argument("--notes", default="", help="what changed in the launcher, one or two sentences")
    ap.add_argument("--no-sign", action="store_true", help="skip the signatures (the launcher then warns about a local list)")
    args = ap.parse_args()

    with open(args.src, encoding="utf-8") as f:
        src = json.load(f)
    os.makedirs(args.out, exist_ok=True)

    def url(name):
        return f"{REPO}/releases/download/{args.tag}/{name}" if args.tag else name

    def abs_url(name):
        return f"{REPO}/releases/download/{args.tag}/{name}" if args.tag else f"{REPO}/releases/latest/download/{name}"

    mods = []
    for m in src["mods"]:
        repo = os.path.join(args.repos, m.get("repo", "oth-cars"))
        dll = os.path.join(repo, m["build"])
        if not os.path.isfile(dll):
            sys.exit(f"{m['id']}: no build at {dll} (build {m.get('repo', 'oth-cars')} first)")
        shutil.copy2(dll, os.path.join(args.out, m["file"]))
        entry = {k: v for k, v in m.items() if k not in ("build", "image", "repo")}
        entry.update(url=url(m["file"]), sha256=sha256(dll), size=os.path.getsize(dll))
        if m.get("image"):
            img = os.path.join(repo, m["image"])
            name = m["id"] + ".png"
            shutil.copy2(img, os.path.join(args.out, name))
            entry.update(image=url(name), imageSha256=sha256(img))
        mods.append(entry)

    manifest = {"schema": 1, "testedGameBuilds": src.get("testedGameBuilds", []), "mods": mods}
    if args.bepinex and os.path.isfile(args.bepinex):
        name = os.path.basename(args.bepinex)
        shutil.copy2(args.bepinex, os.path.join(args.out, name))
        manifest["bepinex"] = {"version": args.bepinex_version, "url": url(name),
                               "sha256": sha256(args.bepinex), "size": os.path.getsize(args.bepinex)}
    elif args.bepinex:
        print(f"no BepInEx zip at {args.bepinex}: the manifest won't offer one")

    # Salochi Core's in-game installer reads the original OverTheHillPlus format
    oth = {"schema": 1, "game": "over the hill", "mods": [
        {"id": m["id"], "name": m["name"], "version": m["version"], "file": m["file"], "url": abs_url(m["file"]),
         "sha256": m["sha256"], "size": m["size"], "requires": m.get("requires", []),
         "description": m.get("description", ""), "category": m.get("kind", "Feature").lower(),
         "changes": m.get("changes", [])}
        for m in mods]}

    # flat, next to the downloads: a GitHub release has no folders
    for f in os.listdir(os.path.join(here, "..", "licenses")):
        shutil.copy2(os.path.join(here, "..", "licenses", f), os.path.join(args.out, f))

    docs = [("manifest.json", manifest), ("mods.json", oth)]
    if args.launcher and os.path.isfile(args.launcher):
        version = args.launcher_version
        if not version:
            props = open(os.path.join(here, "..", "Directory.Build.props"), encoding="utf-8").read()
            version = re.search(r"<Version>([^<]+)</Version>", props).group(1)
        shutil.copy2(args.launcher, os.path.join(args.out, "SalochiLauncher.exe"))
        docs.append(("launcher.json", {"schema": 1, "version": version, "url": url("SalochiLauncher.exe"),
                                       "sha256": sha256(args.launcher), "size": os.path.getsize(args.launcher),
                                       "notes": args.notes}))
    elif args.launcher:
        print(f"no launcher at {args.launcher} (dotnet publish first): no launcher.json this time")

    for name, doc in docs:
        path = os.path.join(args.out, name)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")
        if not args.no_sign:
            subprocess.run(["dotnet", "run", os.path.join(here, "sign.cs"), "--", "sign", path], check=True,
                           stdout=subprocess.DEVNULL)
        elif os.path.exists(path + ".sig"):
            os.remove(path + ".sig")
    print(f"wrote {args.out}: {', '.join(m['id'] for m in mods)}" + (", launcher" if len(docs) > 2 else "") +
          ("" if args.no_sign else " (signed)"))


if __name__ == "__main__":
    main()
