# manifest.json

The mod list the launcher reads. The published copy lives at
`https://github.com/Salochi/oth-salochi-launcher/releases/latest/download/manifest.json`. `tools/make_manifest.py`
writes it from `release/mods.src.json`. For testing, `--manifest <path>` reads a local copy instead.

```jsonc
{
  "schema": 1,                         // the launcher refuses other schemas and asks to be updated
  "testedGameBuilds": ["26326013"],    // Steam build ids these mods were tested on; any other build gets a warning
  "bepinex": {                         // installed by Play when the game has no BepInEx
    "version": "6.0.0-be.788",
    "url": "BepInEx-...zip", "sha256": "…", "size": 0
  },
  "mods": [
    {
      "id": "bajabug",                 // Core's mod id: [a-z0-9_.-], 1-40 characters
      "name": "Baja Bug",
      "version": "0.1.0",
      "kind": "Car",                   // Core | Feature | Car: the section it's listed in
      "required": false,               // true: always installed, can't be unticked (Core)
      "requires": ["core"],            // ticked along with this mod
      "networked": true,               // adds network prefabs: everyone in a session needs the same set
      "description": "…",
      "credits": "Model: … (CC BY 4.0)",
      "file": "OTH.BajaBug.dll",       // the name it gets in BepInEx/plugins/Salochi
      "url": "OTH.BajaBug.dll",        // relative to the manifest, or an absolute GitHub URL
      "sha256": "…", "size": 0,
      "changes": [{ "version": "0.1.0", "date": "2026-11-06", "text": "…" }]
    }
  ]
}
```

## Rules the launcher enforces

- Downloads come only from `github.com` or `*.githubusercontent.com` over HTTPS. A relative URL resolves against the
  manifest's location: a release asset next to it, or a local file while testing. It can't use `..`.
- A file is used only when its size and SHA-256 match. A mismatch throws the download away.
- `file` is a plain `.dll` name, and no two mods can share one.
- Every id in `requires` must be in the manifest.

## Not there yet

- **Signing.** The original OverTheHillPlus signs its `mods.json`. We rely on HTTPS plus the hashes inside the
  manifest, so anyone who can push to this repo's releases can ship code to players. A signature from an offline key
  would close that gap.
