namespace Salochi.Launcher.Core.Mods;

/// <summary>Who a DLL in BepInEx/plugins belongs to.</summary>
public enum PluginOrigin
{
    /// <summary>One of ours (it carries the Salochi family marker, or its hash is in our manifest).</summary>
    Salochi,
    /// <summary>The original OverTheHillPlus Core, or a Core build that isn't ours: same plugin id as ours.</summary>
    ForeignCore,
    /// <summary>Another OTH.* mod the launcher doesn't manage (an original OverTheHillPlus mod, or a personal one).</summary>
    OverTheHillPlus,
    /// <summary>Anything else (other BepInEx plugins, native helpers).</summary>
    Other,
}

public sealed record PluginFile(string Path, string RelativePath, string? AssemblyName, PluginOrigin Origin, bool InLauncherFolder);

/// <summary>Something in the plugins folder the player should know about before launching.</summary>
public sealed record PluginIssue(PluginIssueKind Kind, IReadOnlyList<PluginFile> Files);

public enum PluginIssueKind
{
    /// <summary>A Core that isn't ours: it has our Core's plugin id, so BepInEx loads only one of the two and the game
    /// may crash or lose features. Blocks a modded launch until it is moved aside.</summary>
    ForeignCore,
    /// <summary>OTH mods the launcher doesn't manage: if they were made for the original OverTheHillPlus, they may
    /// expect the original Core. A warning.</summary>
    OverTheHillPlusMods,
    /// <summary>Copies of our DLLs outside BepInEx/plugins/Salochi (an older manual install or a dev deploy): they would
    /// load twice. Blocks a modded launch until they are moved aside.</summary>
    LooseSalochiCopies,
}

public static class PluginScanner
{
    /// <summary>The folder under BepInEx/plugins the launcher owns.</summary>
    public const string LauncherFolder = "Salochi";

    /// <param name="pluginsDir">BepInEx/plugins.</param>
    /// <param name="ourHashes">SHA-256 of every DLL our manifest lists.</param>
    /// <param name="ourFiles">The file names of our mods other than Core (OTH.BajaBug.dll…). A DLL with one of these
    /// names is an older build of ours, from before the family marker. Core's name is shared with the original
    /// OverTheHillPlus, so it doesn't count.</param>
    public static IReadOnlyList<PluginFile> Scan(string pluginsDir, IReadOnlySet<string> ourHashes, IReadOnlySet<string>? ourFiles = null)
    {
        var list = new List<PluginFile>();
        if (!Directory.Exists(pluginsDir)) return list;
        string ours = System.IO.Path.Combine(pluginsDir, LauncherFolder) + System.IO.Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFiles(pluginsDir, "*.dll", SearchOption.AllDirectories))
        {
            string rel = System.IO.Path.GetRelativePath(pluginsDir, path);
            bool inOurs = path.StartsWith(ours, StringComparison.OrdinalIgnoreCase);
            var info = AssemblyInfoReader.Read(path);
            list.Add(new PluginFile(path, rel, info?.Name, Classify(path, info, ourHashes, ourFiles), inOurs));
        }
        return list;
    }

    internal static PluginOrigin Classify(string path, AssemblyInfo? info, IReadOnlySet<string> ourHashes, IReadOnlySet<string>? ourFiles = null)
    {
        if (info == null) return PluginOrigin.Other;
        if (info.Metadata.TryGetValue(AssemblyInfoReader.FamilyKey, out var fam) && fam == "Salochi") return PluginOrigin.Salochi;
        bool othNamed = info.Name.StartsWith("OTH.", StringComparison.OrdinalIgnoreCase);
        if (othNamed && ourHashes.Contains(FileUtil.Sha256(path))) return PluginOrigin.Salochi;
        if (string.Equals(info.Name, "OTH.Core", StringComparison.OrdinalIgnoreCase)) return PluginOrigin.ForeignCore;
        if (othNamed && ourFiles != null && ourFiles.Contains(System.IO.Path.GetFileName(path))) return PluginOrigin.Salochi;
        return othNamed ? PluginOrigin.OverTheHillPlus : PluginOrigin.Other;
    }

    public static IReadOnlyList<PluginIssue> Issues(IReadOnlyList<PluginFile> files)
    {
        var issues = new List<PluginIssue>();
        var foreignCore = files.Where(f => f.Origin == PluginOrigin.ForeignCore).ToList();
        if (foreignCore.Count > 0) issues.Add(new PluginIssue(PluginIssueKind.ForeignCore, foreignCore));
        var loose = files.Where(f => f.Origin == PluginOrigin.Salochi && !f.InLauncherFolder).ToList();
        if (loose.Count > 0) issues.Add(new PluginIssue(PluginIssueKind.LooseSalochiCopies, loose));
        var plus = files.Where(f => f.Origin == PluginOrigin.OverTheHillPlus).ToList();
        if (plus.Count > 0) issues.Add(new PluginIssue(PluginIssueKind.OverTheHillPlusMods, plus));
        return issues;
    }

    /// <summary>True for issues that make a modded launch unsafe until they're dealt with.</summary>
    public static bool Blocks(PluginIssueKind kind) => kind is PluginIssueKind.ForeignCore or PluginIssueKind.LooseSalochiCopies;

    /// <summary>
    /// Moves files out of BepInEx/plugins into BepInEx/salochi-moved/&lt;time&gt;/, keeping their relative paths, so nothing
    /// is lost and the player can put them back by hand. Returns the backup folder.
    /// </summary>
    public static string MoveAside(string bepInExDir, IEnumerable<PluginFile> files, DateTime now)
    {
        string backup = System.IO.Path.Combine(bepInExDir, "salochi-moved", now.ToString("yyyyMMdd-HHmmss"));
        foreach (var f in files)
        {
            string dest = System.IO.Path.Combine(backup, f.RelativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            File.Move(f.Path, dest, overwrite: true);
        }
        return backup;
    }
}
