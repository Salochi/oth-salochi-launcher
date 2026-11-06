using System.IO.Compression;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.Core.Game;

/// <summary>The mod loader in the game folder: is it there, and installing it from the manifest's pinned build.</summary>
public static class BepInExInstall
{
    /// <summary>True when the IL2CPP build of BepInEx and its doorstop are in the game folder.</summary>
    public static bool IsInstalled(GameLocation game) =>
        File.Exists(Path.Combine(game.Root, "winhttp.dll")) &&
        File.Exists(Path.Combine(game.Root, DoorstopConfig.FileName)) &&
        File.Exists(Path.Combine(game.BepInExDir, "core", "BepInEx.Unity.IL2CPP.dll"));

    /// <summary>
    /// True until BepInEx has started the game once: on its first start it reads the game's code and generates
    /// BepInEx/interop, which takes a few minutes while the game looks frozen. Once that folder has its DLLs, later
    /// starts are quick.
    /// </summary>
    public static bool FirstStartPending(GameLocation game)
    {
        string interop = Path.Combine(game.BepInExDir, "interop");
        return !Directory.Exists(interop) || !Directory.EnumerateFiles(interop, "*.dll").Any();
    }

    /// <summary>
    /// Downloads the manifest's BepInEx zip into the store, checks its hash, and unpacks it into the game folder. An
    /// existing doorstop_config.ini is kept (it may hold the player's vanilla setting). Entries that would land outside
    /// the game folder are refused.
    /// </summary>
    public static async Task InstallAsync(GameLocation game, Manifest manifest, string storeRoot, HttpClient http, IProgress<double>? progress, CancellationToken ct)
    {
        var entry = manifest.BepInEx ?? throw new ManifestException("the manifest doesn't say which BepInEx to install");
        string zip = Path.Combine(storeRoot, "bepinex", $"BepInEx-{entry.Version}.zip");
        if (!File.Exists(zip) || !string.Equals(FileUtil.Sha256(zip), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            await ModStore.DownloadVerifiedAsync(http, manifest.ResolveUrl(entry.Url, "bepinex"), zip, entry.Sha256, entry.Size, progress, ct);
        Extract(zip, game.Root);
    }

    internal static void Extract(string zipPath, string gameRoot)
    {
        string root = Path.GetFullPath(gameRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var e in archive.Entries)
        {
            string dest = Path.GetFullPath(Path.Combine(root, e.FullName));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException($"the BepInEx zip has an entry outside the game folder: {e.FullName}");
            if (e.FullName.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
            if (e.Name.Equals(DoorstopConfig.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }
}
