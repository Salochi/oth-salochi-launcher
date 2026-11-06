using System.Text.Json;
using System.Text.Json.Serialization;

namespace Salochi.Launcher.Core.Mods;

/// <summary>
/// The list of Salochi mods the launcher offers: manifest.json, published with each release (see docs/manifest.md).
/// </summary>
public sealed class Manifest
{
    public const int SupportedSchema = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; }
    /// <summary>Steam build ids of the game these mods were last tested on. Empty: no check.</summary>
    [JsonPropertyName("testedGameBuilds")] public List<string> TestedGameBuilds { get; set; } = [];
    [JsonPropertyName("bepinex")] public BepInExEntry? BepInEx { get; set; }
    [JsonPropertyName("mods")] public List<ModEntry> Mods { get; set; } = [];

    /// <summary>Where the manifest came from; relative download URLs resolve against it.</summary>
    [JsonIgnore] public Uri? Source { get; set; }

    public ModEntry? Find(string id) => Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Manifest Parse(string json, Uri? source)
    {
        Manifest m;
        try { m = JsonSerializer.Deserialize<Manifest>(json, Json) ?? throw new ManifestException("the manifest is empty"); }
        catch (JsonException e) { throw new ManifestException("the manifest isn't valid JSON: " + e.Message); }
        if (m.Schema != SupportedSchema)
            throw new ManifestException($"the manifest is schema {m.Schema}; this launcher reads schema {SupportedSchema} (update the launcher)");
        m.Source = source;
        m.Validate();
        return m;
    }

    private void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods)
        {
            string where = $"mod '{mod.Id}'";
            if (!ModEntry.IsValidId(mod.Id)) throw new ManifestException($"{where}: the id must be 1-40 characters of a-z 0-9 _ . -");
            if (!seen.Add(mod.Id)) throw new ManifestException($"{where} is listed twice");
            if (string.IsNullOrWhiteSpace(mod.Name)) throw new ManifestException($"{where} has no name");
            if (string.IsNullOrWhiteSpace(mod.Version)) throw new ManifestException($"{where} has no version");
            if (!IsPlainFileName(mod.File) || !mod.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ManifestException($"{where}: file must be a plain .dll name, not '{mod.File}'");
            if (!files.Add(mod.File)) throw new ManifestException($"{where}: another mod already installs {mod.File}");
            if (!IsSha256(mod.Sha256)) throw new ManifestException($"{where}: sha256 must be 64 hex characters");
            _ = ResolveUrl(mod.Url, where);
            if (mod.Image != null)
            {
                if (!mod.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ManifestException($"{where}: image must be a .png");
                if (!IsSha256(mod.ImageSha256)) throw new ManifestException($"{where}: imageSha256 must be 64 hex characters");
                _ = ResolveUrl(mod.Image, where + " image");
            }
        }
        foreach (var mod in Mods)
            foreach (var r in mod.Requires)
                if (Find(r) == null) throw new ManifestException($"mod '{mod.Id}' requires '{r}', which isn't in the manifest");
        if (BepInEx != null)
        {
            if (!IsSha256(BepInEx.Sha256)) throw new ManifestException("bepinex: sha256 must be 64 hex characters");
            _ = ResolveUrl(BepInEx.Url, "bepinex");
        }
    }

    /// <summary>
    /// A download's absolute address. Absolute URLs must be HTTPS on github.com or *.githubusercontent.com (the release
    /// hosts); relative ones resolve against the manifest's own location, which also lets a local manifest point at
    /// local builds while developing.
    /// </summary>
    public Uri ResolveUrl(string url, string where = "download")
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ManifestException($"{where}: no url");
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && abs.Scheme != Uri.UriSchemeFile)
        {
            if (!DownloadHosts.IsAllowed(abs)) throw new ManifestException($"{where}: downloads only come from GitHub over HTTPS, not {abs.Host}");
            return abs;
        }
        if (Source == null) throw new ManifestException($"{where}: a relative url needs the manifest's location");
        if (url.Contains("..") || url.Contains('\\') || url.StartsWith('/')) throw new ManifestException($"{where}: '{url}' must stay next to the manifest");
        var resolved = new Uri(Source, url);
        if (resolved.Scheme != Uri.UriSchemeFile && !DownloadHosts.IsAllowed(resolved))
            throw new ManifestException($"{where}: '{url}' resolves outside GitHub");
        return resolved;
    }

    private static bool IsPlainFileName(string? f) =>
        !string.IsNullOrWhiteSpace(f) && f.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && f != "." && f != "..";

    internal static bool IsSha256(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);
}

public enum ModKind { Core, Feature, Car }

public sealed class ModEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("kind")][JsonConverter(typeof(JsonStringEnumConverter<ModKind>))] public ModKind Kind { get; set; } = ModKind.Feature;
    /// <summary>Always installed and can't be switched off (Core).</summary>
    [JsonPropertyName("required")] public bool Required { get; set; }
    [JsonPropertyName("requires")] public List<string> Requires { get; set; } = [];
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Model and code credits shown with the mod (a CC BY car must credit its author).</summary>
    [JsonPropertyName("credits")] public string? Credits { get; set; }
    /// <summary>Adds network prefabs (a car or trailer): everyone in a session needs the same set.</summary>
    [JsonPropertyName("networked")] public bool Networked { get; set; }
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("changes")] public List<ChangeEntry> Changes { get; set; } = [];
    /// <summary>Optional picture for the launcher (a car's garage silhouette, white on transparent PNG).</summary>
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("imageSha256")] public string? ImageSha256 { get; set; }

    /// <summary>The same rule as Core's mod ids: [a-z0-9_.-], 1 to 40 characters.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= 40 } && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-');
}

public sealed class ChangeEntry
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public sealed class BepInExEntry
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

public sealed class ManifestException(string message) : Exception(message);

/// <summary>The hosts downloads may come from: HTTPS on the default port to github.com or *.githubusercontent.com,
/// with no credentials in the URL (the same rule as Core's in-game installer).</summary>
public static class DownloadHosts
{
    public static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return false;
        string host = uri.Host.ToLowerInvariant();
        return host == "github.com" || (host.EndsWith(".githubusercontent.com", StringComparison.Ordinal) && host.Length > ".githubusercontent.com".Length);
    }
}
