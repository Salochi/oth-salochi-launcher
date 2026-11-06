namespace Salochi.Launcher.Core.Mods;

/// <summary>
/// The launcher's own copy of every mod it downloaded: &lt;root&gt;/store/&lt;id&gt;/&lt;version&gt;/&lt;file&gt;. The game folder
/// only ever gets files copied from here, after their SHA-256 matched the manifest.
/// </summary>
public sealed class ModStore(string root, HttpClient http)
{
    public string Root { get; } = root;

    public string PathOf(ModEntry mod) => Path.Combine(Root, "store", mod.Id, Sanitize(mod.Version), mod.File);

    /// <summary>True when the mod's file is already here with the right hash.</summary>
    public bool Has(ModEntry mod)
    {
        string p = PathOf(mod);
        return File.Exists(p) && string.Equals(FileUtil.Sha256(p), mod.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The mod's file in the store, downloaded (and verified) first when missing.</summary>
    public async Task<string> EnsureAsync(Manifest manifest, ModEntry mod, IProgress<double>? progress, CancellationToken ct)
    {
        string dest = PathOf(mod);
        if (Has(mod)) return dest;
        await DownloadVerifiedAsync(http, manifest.ResolveUrl(mod.Url, $"mod '{mod.Id}'"), dest, mod.Sha256, mod.Size, progress, ct);
        return dest;
    }

    /// <summary>The mod's picture in the store (downloaded first when missing), or null when it has none or the
    /// download failed: a picture is never worth an error.</summary>
    public async Task<string?> EnsureImageAsync(Manifest manifest, ModEntry mod, CancellationToken ct)
    {
        if (mod.Image == null || mod.ImageSha256 == null) return null;
        string dest = Path.Combine(Root, "images", mod.ImageSha256 + ".png");
        if (File.Exists(dest)) return dest;
        try
        {
            await DownloadVerifiedAsync(http, manifest.ResolveUrl(mod.Image, $"mod '{mod.Id}' image"), dest, mod.ImageSha256, 0, null, ct);
            return dest;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or ManifestException) { return null; }
    }

    /// <summary>Downloads <paramref name="url"/> (or copies it, for a file:// URL while developing) to
    /// <paramref name="dest"/> through a .part file, and only keeps it when size and SHA-256 match.</summary>
    public static async Task DownloadVerifiedAsync(HttpClient http, Uri url, string dest, string sha256, long size, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string part = dest + ".part";
        try
        {
            if (url.IsFile)
            {
                File.Copy(url.LocalPath, part, overwrite: true);
                progress?.Report(1);
            }
            else
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                // HttpClient follows GitHub's redirect to its download host: check where it ended up as well.
                var final = resp.RequestMessage?.RequestUri;
                if (final != null && !DownloadHosts.IsAllowed(final)) throw new IOException($"the download was redirected away from GitHub ({final.Host})");
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? size;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(part);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report(Math.Min(1, (double)done / total));
                }
            }
            if (size > 0 && new FileInfo(part).Length != size)
                throw new IOException($"{Path.GetFileName(dest)}: expected {size} bytes, got {new FileInfo(part).Length}");
            string got = FileUtil.Sha256(part);
            if (!string.Equals(got, sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"{Path.GetFileName(dest)}: the file's SHA-256 doesn't match the manifest (got {got[..12]}…)");
            File.Move(part, dest, overwrite: true);
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    private static string Sanitize(string version) =>
        string.Concat(version.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '+' ? c : '_'));
}
