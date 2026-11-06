using System.Text.Json;

namespace Salochi.Launcher.Core;

/// <summary>The launcher's own settings, in %LOCALAPPDATA%\Salochi\Launcher\settings.json.</summary>
public sealed class LauncherSettings
{
    /// <summary>The game folder when picked by hand; null: found through Steam.</summary>
    public string? GameRoot { get; set; }
    /// <summary>The ids of the optional mods the player ticked. Null until the first save: everything starts ticked.</summary>
    public List<string>? Picked { get; set; }
    /// <summary>A manifest to read instead of the published one (a local file while developing).</summary>
    public string? ManifestOverride { get; set; }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Salochi", "Launcher");

    private static string FilePath => Path.Combine(DataDir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (JsonException) { /* a damaged file: start over */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        FileUtil.WriteAtomic(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
