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
    /// Downloads the manifest's BepInEx zip into the store, checks its hash, and installs it into the game folder (see
    /// <see cref="Extract"/>). Returns the folder the replaced files were backed up to, or null when nothing was replaced.
    /// </summary>
    public static async Task<string?> InstallAsync(GameLocation game, Manifest manifest, string storeRoot, HttpClient http, IProgress<double>? progress, CancellationToken ct)
    {
        var entry = manifest.BepInEx ?? throw new ManifestException("the manifest doesn't say which BepInEx to install");
        string zip = Path.Combine(storeRoot, "bepinex", $"BepInEx-{entry.Version}.zip");
        if (!File.Exists(zip) || !string.Equals(FileUtil.Sha256(zip), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            await ModStore.DownloadVerifiedAsync(http, manifest.ResolveUrl(entry.Url, "bepinex"), zip, entry.Sha256, entry.Size, progress, ct);
        return Extract(zip, game.Root, DateTime.Now);
    }

    /// <summary>
    /// Installs the zip into the game folder, all or nothing. It's unpacked into a staging folder first (entries that
    /// would land outside the game folder are refused), so a damaged zip or a full disk changes nothing. Then every file
    /// it would replace (another loader's winhttp.dll, an older BepInEx) is moved to salochi-loader-backup/&lt;time&gt;/
    /// and the new files moved in; if that fails part way, the backup is put back. An existing doorstop_config.ini is
    /// kept: it holds the player's with/without mods choice. Returns the backup folder, or null when nothing was replaced.
    /// </summary>
    internal static string? Extract(string zipPath, string gameRoot, DateTime now)
    {
        string root = Path.GetFullPath(gameRoot) + Path.DirectorySeparatorChar;
        string staging = Path.Combine(root, ".salochi-loader-staging");
        string backup = Path.Combine(root, "salochi-loader-backup", now.ToString("yyyyMMdd-HHmmss"));
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        var files = new List<string>(); // relative paths
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in archive.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(root, e.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException($"the BepInEx zip has an entry outside the game folder: {e.FullName}");
                    if (e.FullName.EndsWith('/')) continue;
                    string rel = Path.GetRelativePath(root, dest);
                    if (rel.Equals(DoorstopConfig.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                    string to = Path.Combine(staging, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    e.ExtractToFile(to);
                    files.Add(rel);
                }
            }

            var moved = new List<string>();
            var placed = new List<string>();
            try
            {
                foreach (var rel in files)
                {
                    string dest = Path.Combine(root, rel);
                    if (!File.Exists(dest)) continue;
                    string to = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Move(dest, to);
                    moved.Add(rel);
                }
                foreach (var rel in files)
                {
                    string dest = Path.Combine(root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Move(Path.Combine(staging, rel), dest);
                    placed.Add(rel);
                }
            }
            catch
            {
                foreach (var rel in placed) { try { File.Delete(Path.Combine(root, rel)); } catch (IOException) { } }
                foreach (var rel in moved) { try { File.Move(Path.Combine(backup, rel), Path.Combine(root, rel), overwrite: true); } catch (IOException) { } }
                throw;
            }
            if (moved.Count == 0) return null;
            LauncherLog.Info($"installing BepInEx replaced {moved.Count} file(s), backed up to {backup}: {string.Join(", ", moved.Take(10))}");
            return backup;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { /* removed next time */ }
        }
    }
}
