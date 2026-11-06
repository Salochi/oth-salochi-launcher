# Salochi Launcher

A launcher for the Salochi mods for **Over The Hill**. Pick the mods you want (Core, the Accessory Editor, cars), press
**Play**, and the launcher installs them and starts the game through Steam. **Play vanilla** starts the game without
any mods, and your mods stay installed for next time.

Salochi mods run on a fork of [OverTheHillPlus](https://github.com/legomaster188/OverTheHillPlus-QoL) by legomaster188
(MIT), on BepInEx 6 IL2CPP.

## What it does to your game folder

| | |
|---|---|
| **Mods** | It only writes to `BepInEx/plugins/Salochi/`. That folder belongs to the launcher, and it always holds exactly the mods you ticked. Every download's SHA-256 is checked against the mod list before it gets there. |
| **Play vanilla** | Sets `enabled = false` in `doorstop_config.ini`, so BepInEx doesn't load at all. Nothing is deleted. **Play** turns it back on. Starting the game from Steam uses whichever mode you last played. |
| **Mod loader** | If BepInEx isn't installed, **Play** installs the pinned build from the mod list. |
| **Original OverTheHillPlus** | Its Core has the same plugin id as Salochi Core, so only one of them loads. The launcher detects it and won't launch modded until it's moved to `BepInEx/salochi-moved/<time>/`. Nothing is deleted. Other OTH mods get a warning and are left alone. |

The launcher keeps its own files in `%LOCALAPPDATA%\Salochi\Launcher`: settings, and the downloaded mods in `store/`.

## Building

Requires the .NET 10 SDK.

```bash
dotnet test                                    # the core logic's tests
dotnet run --project src/Salochi.Launcher      # run it
dotnet publish src/Salochi.Launcher -c Release # one self-contained SalochiLauncher.exe
```

## Releasing mods

The launcher reads `manifest.json` from the latest GitHub release of this repo (see [docs/manifest.md](docs/manifest.md)).

```bash
# 1. build the mods in oth-cars (./deploy.sh, or dotnet build -c Release -p:NoDeploy=true per mod)
# 2. the release folder: manifest.json + the DLLs (+ the BepInEx zip)
python tools/make_manifest.py --cars ../oth-cars --out release/out --tag v0.1.0 \
       --bepinex BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788.zip --bepinex-version 6.0.0-be.788
# 3. try it locally first
SalochiLauncher.exe --manifest release/out/manifest.json
# 4. publish
gh release create v0.1.0 release/out/*
```

`release/mods.src.json` is the hand-written part: names, descriptions, credits, and which build each DLL comes from.
Only publish cars whose model licence allows redistribution.

## Layout

| | |
|---|---|
| `src/Salochi.Launcher.Core` | Everything the launcher does to the game, with no UI: finding the game through Steam, the doorstop switch, the plugin scan, the manifest, the download store, syncing mods into the game. |
| `src/Salochi.Launcher` | The Avalonia app. |
| `tests/` | xUnit tests for the core. |
| `tools/make_manifest.py` | Builds a release folder from the oth-cars builds. |

Licence: MIT (see [LICENSE](LICENSE)). Over The Hill is made by Funselektor Labs; this is an unofficial fan project.
