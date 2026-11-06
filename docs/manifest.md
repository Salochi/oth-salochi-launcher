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
      "changes": [{ "version": "0.1.0", "date": "2026-11-06", "text": "…" }],
      "image": "bajabug.png",          // optional: the car's garage silhouette (white on transparent) for its tile
      "imageSha256": "…"
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

## Signatures

`manifest.json.sig` and `mods.json.sig` sit next to the lists: base64 of an ECDSA P-256 / SHA-256 signature (r||s)
by the Salochi release key. The launcher refuses a published list whose signature doesn't check out. A local list
(`--manifest`) is checked when it has a `.sig`, and marked unsigned on the Game page when it doesn't.

`mods.json` is the same set of mods in the original OverTheHillPlus format, for Salochi Core's in-game installer.
That installer only downloads from this repo, and only with the same key.

```bash
dotnet run tools/sign.cs -- keygen          # once: %USERPROFILE%\.salochi\release-key.pem, prints the public half
dotnet run tools/sign.cs -- sign <file>     # make_manifest.py does this for both lists
```

The public half is in `src/Salochi.Launcher.Core/Mods/ReleaseKey.cs` and in oth-cars' `src/Core/Install/ReleaseKey.cs`.
Back up the private key: losing it means a new key, which needs a launcher and Core update.

# launcher.json

The newest launcher build, published (and signed, as `launcher.json.sig`) next to `manifest.json`. Its format is fixed
at schema 1 for good, so every launcher ever released can read it and find its way to the current build.

```json
{
  "schema": 1,
  "version": "0.2.0",
  "url": "SalochiLauncher.exe",
  "sha256": "…",
  "size": 49075991,
  "notes": "One or two sentences on what changed."
}
```

`version` is plain numbers (`1.2.3`), with no pre-release tags. The launcher updates when it's greater than its own
version. `url` follows the same rules as a mod's.
