using Microsoft.Win32;

namespace Salochi.Launcher.Core.Game;

/// <summary>Finds Over The Hill through Steam: the Steam folder, its library folders, the game's app manifest.</summary>
public static class SteamLibrary
{
    /// <summary>Over The Hill's Steam app id.</summary>
    public const string AppId = "2929250";

    /// <summary>Steam's install folder from the registry (or the usual places), or null.</summary>
    public static string? SteamPath()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string p && Directory.Exists(p)) return Path.GetFullPath(p);
            }
            catch (Exception) { /* no access: try the usual places */ }
        }
        foreach (var p in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            if (Directory.Exists(p)) return p;
        return null;
    }

    /// <summary>Every Steam library folder: Steam's own and the ones listed in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> LibraryFolders(string steamPath)
    {
        var list = new List<string> { steamPath };
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try { list.AddRange(ParseLibraryFolders(File.ReadAllText(vdfPath))); }
            catch (FormatException) { /* a damaged file: Steam's own folder still counts */ }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static IEnumerable<string> ParseLibraryFolders(string vdfText)
    {
        var folders = Vdf.Parse(vdfText).Block("libraryfolders");
        if (folders == null) return [];
        return folders.Blocks.Values.Select(lib => lib.Value("path")).OfType<string>().Where(p => p.Length > 0).ToList();
    }

    /// <summary>The installed game, its folder and Steam build id; null when Steam or the game isn't there.</summary>
    public static GameLocation? FindGame()
    {
        var steam = SteamPath();
        if (steam == null) return null;
        foreach (var lib in LibraryFolders(steam))
        {
            var loc = FromLibrary(lib);
            if (loc != null) return loc;
        }
        return null;
    }

    /// <summary>The game in one library folder, read from its app manifest, or null.</summary>
    public static GameLocation? FromLibrary(string library)
    {
        string acf = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
        if (!File.Exists(acf)) return null;
        Vdf? state;
        try { state = Vdf.Parse(File.ReadAllText(acf)).Block("AppState"); }
        catch (FormatException) { return null; }
        string? dir = state?.Value("installdir");
        if (dir == null) return null;
        string root = Path.Combine(library, "steamapps", "common", dir);
        return Directory.Exists(root) ? new GameLocation(root, state!.Value("buildid")) : null;
    }

    /// <summary>A game folder picked by hand. Its build id is read from the library's manifest when the folder sits in
    /// a Steam library (…/steamapps/common/&lt;game&gt;), otherwise it stays unknown.</summary>
    public static GameLocation FromFolder(string gameRoot)
    {
        var steamapps = Directory.GetParent(gameRoot)?.Parent;
        if (steamapps?.Parent != null && string.Equals(steamapps.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
        {
            var loc = FromLibrary(steamapps.Parent.FullName);
            if (loc != null && string.Equals(Path.GetFullPath(loc.Root), Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase))
                return loc;
        }
        return new GameLocation(gameRoot, null);
    }
}

/// <summary>Where the game is, and the Steam build id installed (null when unknown).</summary>
public sealed record GameLocation(string Root, string? BuildId)
{
    public const string ExeName = "over the hill.exe";
    public string BepInExDir => Path.Combine(Root, "BepInEx");
    public string PluginsDir => Path.Combine(BepInExDir, "plugins");
    public bool LooksValid => File.Exists(Path.Combine(Root, ExeName));
}
