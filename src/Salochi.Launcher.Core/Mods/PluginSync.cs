namespace Salochi.Launcher.Core.Mods;

/// <summary>What a sync will do to BepInEx/plugins/Salochi.</summary>
public sealed record SyncPlan(IReadOnlyList<ModEntry> Install, IReadOnlyList<string> Remove, IReadOnlyList<ModEntry> Keep)
{
    public bool IsEmpty => Install.Count == 0 && Remove.Count == 0;
}

/// <summary>
/// Makes BepInEx/plugins/Salochi hold exactly the picked mods of the manifest: the ones not picked are removed (the
/// launcher's store keeps its copy). Files the manifest doesn't list, and anything outside that folder, are left alone.
/// </summary>
public static class PluginSync
{
    /// <summary>
    /// The manifest mods installed in <paramref name="targetDir"/> (by file name, whatever their version). Core's in-game
    /// installer writes to the same folder, so this, not the launcher's own memory, is what the player last picked.
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

    /// <param name="managed">Every mod in the manifest. Only their files are ever removed: anything else in the folder
    /// (a personal build a dev deploy put there, Core's .pending queue) is left alone.</param>
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
        var remove = (managed ?? mods)
            .Select(m => m.File)
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

    /// <summary>Applies <paramref name="plan"/>: each install copies the store's file in (through a temp name, then a
    /// rename), each removal deletes the file. The game must be closed: Windows locks loaded DLLs.</summary>
    public static void Apply(string targetDir, SyncPlan plan, Func<ModEntry, string> storePath)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var f in plan.Remove) File.Delete(Path.Combine(targetDir, f));
        foreach (var m in plan.Install)
        {
            string dest = Path.Combine(targetDir, m.File);
            string tmp = dest + ".salochi-tmp";
            File.Copy(storePath(m), tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
        }
    }
}
