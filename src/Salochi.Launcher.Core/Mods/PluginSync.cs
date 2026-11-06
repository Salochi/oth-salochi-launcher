using System.Text.Json;

namespace Salochi.Launcher.Core.Mods;

/// <summary>What a sync will do to BepInEx/plugins/Salochi.</summary>
public sealed record SyncPlan(IReadOnlyList<ModEntry> Install, IReadOnlyList<string> Remove, IReadOnlyList<ModEntry> Keep)
{
    public bool IsEmpty => Install.Count == 0 && Remove.Count == 0;
}

/// <summary>
/// Makes BepInEx/plugins/Salochi hold exactly the picked mods: the ones not picked are removed (the launcher's store
/// keeps its copy). "Managed" files are the manifest's, plus every file the launcher installed before (its receipt,
/// .salochi-installed.json), so a DLL a later release renamed or dropped doesn't keep loading. Anything else in the
/// folder (a personal build a dev deploy put there) and anything outside it is left alone.
/// </summary>
public static class PluginSync
{
    public const string ReceiptName = ".salochi-installed.json";
    private const string StageSuffix = ".salochi-new";

    /// <summary>
    /// The manifest mods installed in <paramref name="targetDir"/> (by file name, whatever their version). Core's in-game
    /// installer writes to the same folder, so this is what the player last picked.
    /// </summary>
    public static IReadOnlyList<string> InstalledIds(Manifest manifest, string targetDir) =>
        manifest.Mods.Where(m => File.Exists(Path.Combine(targetDir, m.File))).Select(m => m.Id).ToList();

    /// <summary>The picked ids plus every required mod and everything they require, in manifest order.</summary>
    public static IReadOnlyList<ModEntry> Resolve(Manifest manifest, IEnumerable<string> picked)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(picked.Concat(manifest.Mods.Where(m => m.Required).Select(m => m.Id)));
        while (stack.Count > 0)
        {
            string id = stack.Pop();
            var mod = manifest.Find(id);
            if (mod == null || !want.Add(mod.Id)) continue;
            foreach (var r in mod.Requires) stack.Push(r);
        }
        return manifest.Mods.Where(m => want.Contains(m.Id)).ToList();
    }

    /// <param name="managed">Every mod in the manifest. Only their files, and the files in the launcher's receipt, are
    /// ever removed.</param>
    /// <param name="updateInstalled">False: a mod already in the folder stays as it is, even when the manifest has
    /// another version (the player switched automatic mod updates off). Only missing mods get installed.</param>
    public static SyncPlan Plan(string targetDir, IReadOnlyList<ModEntry> mods, IEnumerable<ModEntry>? managed = null, bool updateInstalled = true)
    {
        var install = new List<ModEntry>();
        var keep = new List<ModEntry>();
        foreach (var m in mods)
        {
            string p = Path.Combine(targetDir, m.File);
            if (File.Exists(p) && (!updateInstalled || IsCurrent(p, m))) keep.Add(m);
            else install.Add(m);
        }
        var wanted = new HashSet<string>(mods.Select(m => m.File), StringComparer.OrdinalIgnoreCase);
        var remove = (managed ?? mods).Select(m => m.File)
            .Concat(ReadReceipt(targetDir))
            .Where(f => !wanted.Contains(f) && File.Exists(Path.Combine(targetDir, f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SyncPlan(install, remove, keep);
    }

    /// <summary>The installed mods whose file isn't the manifest's version: the updates waiting.</summary>
    public static IReadOnlyList<ModEntry> Outdated(Manifest manifest, string targetDir) =>
        manifest.Mods.Where(m => File.Exists(Path.Combine(targetDir, m.File)) && !IsCurrent(Path.Combine(targetDir, m.File), m)).ToList();

    private static bool IsCurrent(string path, ModEntry m) =>
        string.Equals(FileUtil.Sha256(path), m.Sha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies <paramref name="plan"/> all or nothing. First every new file is copied next to its place and its hash
    /// checked, while nothing installed has changed yet. Then the files to replace or remove are moved into a backup
    /// folder and the new ones renamed into place; if any step fails, the backup is put back. Core's queued changes for
    /// the same files are dropped (they'd undo this at the next game start), and the receipt records what's installed.
    /// The game must be closed: Windows locks loaded DLLs.
    /// </summary>
    public static void Apply(string targetDir, SyncPlan plan, Func<ModEntry, string> storePath)
    {
        Directory.CreateDirectory(targetDir);
        var staged = new List<(string stage, string dest)>();
        // outside BepInEx/plugins (BepInEx loads every .dll under it): BepInEx/salochi-moved/sync-<time>
        string backup = Path.GetFullPath(Path.Combine(targetDir, "..", "..", "salochi-moved", "sync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
        var moved = new List<(string backupPath, string original)>();
        var placed = new List<string>();
        try
        {
            // 1. stage and check every new file: nothing installed changes if a copy fails or the disk fills
            foreach (var m in plan.Install)
            {
                string dest = Path.Combine(targetDir, m.File), stage = dest + StageSuffix;
                File.Copy(storePath(m), stage, overwrite: true);
                staged.Add((stage, dest));
                if (!IsCurrent(stage, m)) throw new IOException($"{m.File}: the copy in the game folder doesn't match the mod list");
            }
            // 2. back up what goes away or gets replaced, then put the new files in
            foreach (var f in plan.Remove.Concat(plan.Install.Select(m => m.File)))
            {
                string original = Path.Combine(targetDir, f);
                if (!File.Exists(original)) continue;
                Directory.CreateDirectory(backup);
                string to = Path.Combine(backup, f);
                File.Move(original, to);
                moved.Add((to, original));
            }
            foreach (var (stage, dest) in staged)
            {
                File.Move(stage, dest);
                placed.Add(dest);
            }
        }
        catch
        {
            foreach (var p in placed) TryDelete(p);
            foreach (var (b, o) in moved) { try { File.Move(b, o, overwrite: true); } catch (IOException) { /* stays in the backup */ } }
            foreach (var (stage, _) in staged) TryDelete(stage);
            throw;
        }
        TryDeleteDir(backup);

        CoreQueue.Drop(targetDir, plan.Install.Select(m => m.File).Concat(plan.Remove));
        WriteReceipt(targetDir, plan.Install.Concat(plan.Keep).Select(m => m.File));
    }

    // ---------------------------------------------------------------- the receipt

    /// <summary>The files the launcher installed here last time (empty when there's no receipt).</summary>
    public static IReadOnlyList<string> ReadReceipt(string targetDir)
    {
        try
        {
            string path = Path.Combine(targetDir, ReceiptName);
            if (!File.Exists(path)) return [];
            var files = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
            // plain .dll names only: a damaged or edited receipt must never point outside the folder
            return files.Where(f => !string.IsNullOrWhiteSpace(f) && f == Path.GetFileName(f) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    private static void WriteReceipt(string targetDir, IEnumerable<string> files)
    {
        try { FileUtil.WriteAtomic(Path.Combine(targetDir, ReceiptName), JsonSerializer.Serialize(files.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList())); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* only means old files may linger */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Salochi Core's own queue of mod changes for the next game start (&lt;folder&gt;/.pending/actions.json, with the
/// staged downloads as &lt;file&gt;.new). When the launcher sets a file itself, a queued replace or delete of that file
/// would undo it, so it's dropped. Renames of other creators' plugins in the same queue are left alone.
/// </summary>
public static class CoreQueue
{
    public static void Drop(string targetDir, IEnumerable<string> files)
    {
        string pending = Path.Combine(targetDir, ".pending");
        string queue = Path.Combine(pending, "actions.json");
        var names = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0 || !Directory.Exists(pending)) return;
        foreach (var f in names)
        {
            string staged = Path.Combine(pending, f + ".new");
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LauncherLog.Info($"couldn't drop Core's staged {f}: {e.Message}"); }
        }
        if (!File.Exists(queue)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(queue));
            if (!doc.RootElement.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array) return;
            var keep = new List<JsonElement>();
            int dropped = 0;
            foreach (var a in actions.EnumerateArray())
            {
                string kind = a.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                string file = a.TryGetProperty("file", out var fl) ? fl.GetString() ?? "" : "";
                bool ours = kind is "replace" or "delete" && names.Contains(file);
                if (ours) dropped++; else keep.Add(a.Clone());
            }
            if (dropped == 0) return;
            if (keep.Count == 0) { File.Delete(queue); return; }
            FileUtil.WriteAtomic(queue, JsonSerializer.Serialize(new { actions = keep }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Core quarantines a queue it can't read; leaving it as it is is the safe choice here
            LauncherLog.Info($"couldn't read Core's queue {queue}: {e.Message}");
        }
    }
}
