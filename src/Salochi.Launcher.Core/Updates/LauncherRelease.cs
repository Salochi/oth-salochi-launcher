using System.Text.Json;
using System.Text.Json.Serialization;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.Core.Updates;

/// <summary>
/// launcher.json: the newest launcher build, published (and signed) next to manifest.json. It's a separate file whose
/// format never changes, so a launcher too old to read a newer manifest can still find the update that reads it.
/// </summary>
public sealed class LauncherRelease
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    /// <summary>One or two sentences on what changed, shown before updating.</summary>
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";

    [JsonIgnore] public Uri? Source { get; set; }
    [JsonIgnore] public Version ParsedVersion { get; private set; } = new();

    public static LauncherRelease Parse(string json, Uri? source)
    {
        LauncherRelease r;
        try { r = JsonSerializer.Deserialize<LauncherRelease>(json) ?? throw new ManifestException("launcher.json is empty"); }
        catch (JsonException e) { throw new ManifestException("launcher.json isn't valid JSON: " + e.Message); }
        if (r.Schema != 1) throw new ManifestException($"launcher.json is schema {r.Schema}; this launcher reads schema 1");
        r.ParsedVersion = ParseVersion(r.Version) ?? throw new ManifestException($"launcher.json: '{r.Version}' isn't a version like 1.2.3");
        if (!Manifest.IsSha256(r.Sha256)) throw new ManifestException("launcher.json: sha256 must be 64 hex characters");
        if (r.Size <= 0) throw new ManifestException("launcher.json: no size");
        r.Source = source;
        _ = r.DownloadUri();
        return r;
    }

    /// <summary>Where the exe downloads from: the same rules as the mods (GitHub over HTTPS, or next to the file).</summary>
    public Uri DownloadUri() => new Manifest { Source = Source }.ResolveUrl(Url, "launcher.json");

    public bool IsNewerThan(Version current) => ParsedVersion > Normalize(current);

    /// <summary>"1.2.3" (or "1.2", "1.2.3.4") as a Version; null for anything else, pre-release tags included.</summary>
    public static Version? ParseVersion(string? s) =>
        System.Version.TryParse(s, out var v) && v.Major >= 0 ? Normalize(v) : null;

    /// <summary>Missing parts count as 0, so 1.2 == 1.2.0 == 1.2.0.0.</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
