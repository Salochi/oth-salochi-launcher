# Salochi Launcher

A launcher for the Salochi mods for **Over The Hill**. Pick the mods you want (Core, the Accessory Editor, cars), press
**Play**, and the launcher installs them and starts the game through Steam. **Play without mods** starts the game
without any mods, and your mods stay installed for next time.

Salochi mods run on a fork of [OverTheHillPlus](https://github.com/legomaster188/OverTheHillPlus-QoL) by legomaster188
(MIT), on BepInEx 6 IL2CPP.

## Getting started

1. Download `SalochiLauncher.exe` from the [latest release](https://github.com/Salochi/oth-salochi-launcher/releases/latest)
   and put it anywhere (your desktop, a games folder). It needs no installing, and it keeps itself up to date.
2. **Windows will warn you the first time you open it** ("Windows protected your PC", from SmartScreen). The launcher
   isn't signed with a paid code-signing certificate, and Windows warns about any new program it hasn't seen many people
   download. Click **More info**, then **Run anyway**. Some antivirus tools flag unsigned programs that download files
   the same way. The launcher's source is all here, and every mod it installs is checked against a signed list.
3. Pick your cars and features, and press **Play**. The first time, the launcher installs BepInEx (the mod loader).
   **The first modded start takes a few minutes**: BepInEx prepares the game before the menu appears, and the game can
   look frozen while it does. Leave it running. Later starts are quick.

To play without mods, use **Play without mods**. Nothing gets uninstalled, and **Play** brings the mods back.

## What it does to your game folder

| | |
|---|---|
| **Mods** | It only writes to `BepInEx/plugins/Salochi/`, the folder Salochi Core's in-game installer uses too. It installs the listed mods you picked, removes the listed mods you didn't, and leaves every other file alone. Every download's SHA-256 is checked against the signed mod list. |
| **Play without mods** | Sets `enabled = false` in `doorstop_config.ini`, so BepInEx doesn't load at all. Nothing is deleted. **Play** turns it back on. Starting the game from Steam uses whichever mode you last played. |
| **Mod loader** | If BepInEx isn't installed, **Play** installs the pinned build from the mod list. |
| **Original OverTheHillPlus** | Its Core has the same plugin id as Salochi Core, so only one of them loads. The launcher detects it and won't launch modded until it's moved to `BepInEx/salochi-moved/<time>/`. Nothing is deleted. Other OTH mods get a warning and are left alone. |

The launcher keeps its own files in `%LOCALAPPDATA%\Salochi\Launcher`: `settings.json`, `launcher.log` (every error in
full, for bug reports), the downloaded mods in `store/`, and downloaded launcher builds in `launcher/`.

## Updates

Both are on by default, and both can be switched off on the Game page.

- **Mods:** when the launcher opens (and the game is closed), newer versions of the installed mods are downloaded and
  installed. Players who start the game from Steam stay current as long as they open the launcher now and then.
  Switched off, Play keeps the installed versions, and the Game page offers the updates.
- **The launcher:** it reads `launcher.json` from the latest release. That's a small signed file whose format never
  changes, so an old launcher can always find its update, even after the mod list's format moves on. A newer build is
  downloaded into `launcher/`. The old launcher then starts the new one with `--finish-update` and exits. The new one
  waits for it to close, takes its place (the old exe stays as `SalochiLauncher.exe.old` until the next start deletes
  it) and starts. A single-file app reads parts of itself from its exe while it runs, which is why the old one never
  replaces itself. If anything fails, the old launcher starts again and says why. Development builds (not published
  single-file) never update themselves.

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
# 1. build the mods (oth-cars and oth-salochi-fixes: ./deploy.sh, or dotnet build -c Release -p:NoDeploy=true), and the launcher
#    (bump <Version> in Directory.Build.props when it changed)
dotnet publish src/Salochi.Launcher -c Release
# 2. the release folder: the lists and launcher.json (signed), the DLLs, the car pictures, the BepInEx zip, the exe,
#    and the licence texts (licenses/: OverTheHillPlus MIT, BepInEx LGPL, Barlow OFL, the third-party notices)
python tools/make_manifest.py --out release/out --tag v0.1.0 --notes "What changed in the launcher."
# 3. try it locally first (--no-launch does everything but start the game; --page cars|features|game)
SalochiLauncher.exe --manifest release/out/manifest.json
# 4. publish
gh release create v0.1.0 release/out/*
```

`release/mods.src.json` is the hand-written part: names, descriptions, credits, and which build each DLL comes from.
Only publish cars whose model licence allows redistribution.

The BepInEx zip isn't in git. Put `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788.zip` in `release/bepinex/`. Get it from
<https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip>
(SHA-256 `f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a`). BepInEx is LGPL-2.1, and releases ship
it unmodified.

Both lists are signed with the Salochi release key (see [docs/manifest.md](docs/manifest.md#signatures)).

## Layout

| | |
|---|---|
| `src/Salochi.Launcher.Core` | Everything the launcher does to the game, with no UI: finding the game through Steam, the doorstop switch, the plugin scan, the manifest, the download store, syncing mods into the game. |
| `src/Salochi.Launcher` | The Avalonia app. |
| `tests/` | xUnit tests for the core. |
| `tools/make_manifest.py` | Builds a release folder from the oth-cars builds. |
| `tools/sign.cs` | The release key: make it, sign with it. |
| `tools/make_logo.py` | Draws the mark, a trail blaze with an S cut out of the paint (`Assets/logo.png`, `Assets/salochi.ico`). |
| `src/Salochi.Launcher/Assets/Fonts` | Barlow and Barlow Condensed (SIL Open Font License, `OFL.txt`). |

Licence: MIT (see [LICENSE](LICENSE)). Over The Hill is made by Funselektor Labs; this is an unofficial fan project.
