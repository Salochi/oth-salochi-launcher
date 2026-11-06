using System.Security.Cryptography;
using System.Text;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.Tests;

public sealed class ModTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salochi-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string ManifestJson(string coreUrl = "OTH.Core.dll") => $$"""
        {
          "schema": 1,
          "testedGameBuilds": ["26326013"],
          "mods": [
            { "id": "core", "name": "Salochi Core", "version": "1.3.3-salochi.1", "kind": "Core", "required": true,
              "file": "OTH.Core.dll", "url": "{{coreUrl}}", "sha256": "{{Sha("core")}}" },
            { "id": "accessories", "name": "Accessory Editor", "version": "1.0.0", "requires": ["core"],
              "file": "OTH.Accessories.dll", "url": "OTH.Accessories.dll", "sha256": "{{Sha("acc")}}" },
            { "id": "bajabug", "name": "Baja Bug", "version": "1.0.0", "kind": "Car", "networked": true, "requires": ["core"],
              "file": "OTH.BajaBug.dll", "url": "OTH.BajaBug.dll", "sha256": "{{Sha("bug")}}" }
          ]
        }
        """;

    private static readonly Uri Release = new("https://github.com/Salochi/oth-salochi-launcher/releases/download/v1/manifest.json");

    [Fact]
    public void Manifest_ResolvesRelativeUrlsAgainstItsLocation()
    {
        var m = Manifest.Parse(ManifestJson(), Release);
        Assert.Equal("https://github.com/Salochi/oth-salochi-launcher/releases/download/v1/OTH.Core.dll", m.ResolveUrl(m.Find("core")!.Url).ToString());
        Assert.Equal(ModKind.Car, m.Find("bajabug")!.Kind);
    }

    [Theory]
    [InlineData("https://example.com/OTH.Core.dll")]
    [InlineData("http://github.com/x/OTH.Core.dll")]
    [InlineData("../../evil.dll")]
    public void Manifest_RefusesDownloadsOffGitHub(string url) =>
        Assert.Throws<ManifestException>(() => Manifest.Parse(ManifestJson(url), Release));

    [Fact]
    public void Manifest_RefusesUnknownSchemaAndMissingRequirement()
    {
        Assert.Throws<ManifestException>(() => Manifest.Parse(ManifestJson().Replace("\"schema\": 1", "\"schema\": 2"), Release));
        Assert.Throws<ManifestException>(() => Manifest.Parse(ManifestJson().Replace("\"requires\": [\"core\"]", "\"requires\": [\"nope\"]"), Release));
    }

    [Fact]
    public void Resolve_AlwaysIncludesRequiredMods()
    {
        var m = Manifest.Parse(ManifestJson(), Release);
        Assert.Equal(["core", "bajabug"], PluginSync.Resolve(m, ["bajabug"]).Select(x => x.Id));
        Assert.Equal(["core"], PluginSync.Resolve(m, []).Select(x => x.Id));
    }

    [Fact]
    public void Sync_InstallsPickedAndRemovesTheRest()
    {
        var m = Manifest.Parse(ManifestJson(), Release);
        string store = Path.Combine(_dir, "store"), target = Path.Combine(_dir, "plugins", "Salochi");
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, "OTH.Core.dll"), "core");
        File.WriteAllText(Path.Combine(store, "OTH.BajaBug.dll"), "bug");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "OTH.Accessories.dll"), "acc"); // ticked off since

        var plan = PluginSync.Plan(target, PluginSync.Resolve(m, ["bajabug"]));
        Assert.Equal(["core", "bajabug"], plan.Install.Select(x => x.Id));
        Assert.Equal(["OTH.Accessories.dll"], plan.Remove);
        PluginSync.Apply(target, plan, e => Path.Combine(store, e.File));

        Assert.Equal(["OTH.BajaBug.dll", "OTH.Core.dll"], Directory.GetFiles(target).Select(Path.GetFileName).Order());
        Assert.True(PluginSync.Plan(target, PluginSync.Resolve(m, ["bajabug"])).IsEmpty);
    }

    [Fact]
    public async Task Store_RefusesAFileWithTheWrongHash()
    {
        string src = Path.Combine(_dir, "OTH.Core.dll");
        File.WriteAllText(src, "tampered");
        var m = Manifest.Parse(ManifestJson(), new Uri(Path.Combine(_dir, "manifest.json")));
        var store = new ModStore(Path.Combine(_dir, "launcher"), new HttpClient());
        await Assert.ThrowsAsync<IOException>(() => store.EnsureAsync(m, m.Find("core")!, null, default));
        Assert.False(File.Exists(store.PathOf(m.Find("core")!)));

        File.WriteAllText(src, "core");
        Assert.Equal(store.PathOf(m.Find("core")!), await store.EnsureAsync(m, m.Find("core")!, null, default));
        Assert.True(store.Has(m.Find("core")!));
    }

    [Fact]
    public void Reader_SeesTheFamilyMarker()
    {
        var info = AssemblyInfoReader.Read(typeof(ModTests).Assembly.Location);
        Assert.Equal("Salochi", info?.Metadata[AssemblyInfoReader.FamilyKey]);
        Assert.False(AssemblyInfoReader.Read(typeof(Manifest).Assembly.Location)!.Metadata.ContainsKey(AssemblyInfoReader.FamilyKey));
    }

    [Fact]
    public void Classify_TellsOurCoreFromTheOriginal()
    {
        string dll = Path.Combine(_dir, "OTH.Core.dll");
        File.WriteAllText(dll, "some build");
        var noMarker = new AssemblyInfo("OTH.Core", new Version(1, 3, 3), new Dictionary<string, string>());
        var marked = new AssemblyInfo("OTH.Core", new Version(1, 3, 3), new Dictionary<string, string> { [AssemblyInfoReader.FamilyKey] = "Salochi" });
        var none = new HashSet<string>();

        Assert.Equal(PluginOrigin.ForeignCore, PluginScanner.Classify(dll, noMarker, none));
        Assert.Equal(PluginOrigin.Salochi, PluginScanner.Classify(dll, marked, none));
        Assert.Equal(PluginOrigin.Salochi, PluginScanner.Classify(dll, noMarker, new HashSet<string> { Sha("some build") }));
        Assert.Equal(PluginOrigin.OverTheHillPlus, PluginScanner.Classify(dll, noMarker with { Name = "OTH.Vera" }, none));
        Assert.Equal(PluginOrigin.Other, PluginScanner.Classify(dll, null, none));

        // an older build of one of our mods, from before the marker: known by its file name (but never Core's)
        string bug = Path.Combine(_dir, "OTH.BajaBug.dll");
        File.WriteAllText(bug, "old bug");
        var ourFiles = new HashSet<string>(["OTH.BajaBug.dll"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(PluginOrigin.Salochi, PluginScanner.Classify(bug, noMarker with { Name = "OTH.BajaBug" }, none, ourFiles));
        Assert.Equal(PluginOrigin.ForeignCore, PluginScanner.Classify(dll, noMarker, none, new HashSet<string>(["OTH.Core.dll"])));
    }

    [Fact]
    public void Issues_FlagForeignCoreAndLooseCopies()
    {
        var files = new[]
        {
            new PluginFile("a", "OverTheHillMods\\OTH.Core.dll", "OTH.Core", PluginOrigin.ForeignCore, false),
            new PluginFile("b", "OTH.BajaBug.dll", "OTH.BajaBug", PluginOrigin.Salochi, false),
            new PluginFile("c", "Salochi\\OTH.Core.dll", "OTH.Core", PluginOrigin.Salochi, true),
            new PluginFile("d", "OTH.Vera.dll", "OTH.Vera", PluginOrigin.OverTheHillPlus, false),
        };
        var issues = PluginScanner.Issues(files);
        Assert.Equal([PluginIssueKind.ForeignCore, PluginIssueKind.LooseSalochiCopies, PluginIssueKind.OverTheHillPlusMods], issues.Select(i => i.Kind));
        Assert.Equal(["b"], issues[1].Files.Select(f => f.Path));
    }
}
