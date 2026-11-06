using System.Security.Cryptography;
using System.Text;
using Salochi.Launcher.Core.Mods;
using Salochi.Launcher.Core.Updates;

namespace Salochi.Launcher.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("salochi-update-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static readonly Uri Release = new("https://github.com/Salochi/oth-salochi-launcher/releases/download/v1/launcher.json");

    private static string Json(string version, string url = "SalochiLauncher.exe") =>
        $$"""{ "schema": 1, "version": "{{version}}", "url": "{{url}}", "sha256": "{{Sha("exe")}}", "size": 3, "notes": "Faster downloads." }""";

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1", "0.1.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("1.0.0", "0.9.9.9", true)]
    public void Release_ComparesVersions(string published, string running, bool newer) =>
        Assert.Equal(newer, LauncherRelease.Parse(Json(published), Release).IsNewerThan(Version.Parse(running)));

    [Fact]
    public void Release_ResolvesNextToItselfAndRefusesOtherHosts()
    {
        var r = LauncherRelease.Parse(Json("0.2.0"), Release);
        Assert.Equal("https://github.com/Salochi/oth-salochi-launcher/releases/download/v1/SalochiLauncher.exe", r.DownloadUri().ToString());
        Assert.Throws<ManifestException>(() => LauncherRelease.Parse(Json("0.2.0", "https://example.com/x.exe"), Release));
        Assert.Throws<ManifestException>(() => LauncherRelease.Parse(Json("0.2.0-beta"), Release));
        Assert.Throws<ManifestException>(() => LauncherRelease.Parse(Json("0.2.0").Replace("\"schema\": 1", "\"schema\": 2"), Release));
    }

    [Fact]
    public void Swap_KeepsTheOldExeAside()
    {
        string exe = Path.Combine(_dir, "SalochiLauncher.exe"), fresh = Path.Combine(_dir, "new.exe");
        File.WriteAllText(exe, "v1");
        File.WriteAllText(exe + SelfUpdater.OldSuffix, "left from last time");
        File.WriteAllText(fresh, "v2");

        SelfUpdater.Swap(exe, fresh);

        Assert.Equal("v2", File.ReadAllText(exe));
        Assert.Equal("v1", File.ReadAllText(exe + SelfUpdater.OldSuffix));
    }

    [Fact]
    public void Swap_WorksAroundALockedOldCopy()
    {
        string exe = Path.Combine(_dir, "SalochiLauncher.exe"), fresh = Path.Combine(_dir, "new.exe");
        File.WriteAllText(exe, "v2");
        File.WriteAllText(fresh, "v3");
        File.WriteAllText(exe + SelfUpdater.OldSuffix, "v1, still running");
        using (File.Open(exe + SelfUpdater.OldSuffix, FileMode.Open, FileAccess.Read, FileShare.None)) // locked
            SelfUpdater.Swap(exe, fresh);
        Assert.Equal("v3", File.ReadAllText(exe));
        Assert.Equal("v2", File.ReadAllText(exe + SelfUpdater.OldSuffix + "-1"));
    }

    [Fact]
    public void Swap_PutsTheOldExeBackWhenTheCopyFails()
    {
        string exe = Path.Combine(_dir, "SalochiLauncher.exe");
        File.WriteAllText(exe, "v1");
        Assert.ThrowsAny<IOException>(() => SelfUpdater.Swap(exe, Path.Combine(_dir, "missing.exe")));
        Assert.Equal("v1", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix));
    }

    [Fact]
    public void Restart_DropsTheUpdateBookkeeping()
    {
        string[] args = ["--finish-update", @"C:\x\SalochiLauncher.exe", "--wait-pid", "42", "--manifest", "m.json",
                         "--updated-from", "0.1.0", "--update-failed", "oops", "--page", "game"];
        Assert.Equal(["--manifest", "m.json", "--page", "game"], SelfUpdater.WithoutUpdateArgs(args));
        Assert.Equal(["--manifest", "m.json", "--updated-from", "0.1.0", "--page", "game"], SelfUpdater.WithoutUpdateArgs(args, keepUpdatedFrom: true));
    }

    [Fact]
    public void FinishUpdate_IgnoresANormalStart() => Assert.False(SelfUpdater.TryFinishUpdate(["--page", "game"]));

    [Fact]
    public async Task CleanUp_DeletesTheOldCopy()
    {
        string exe = Path.Combine(_dir, "SalochiLauncher.exe");
        File.WriteAllText(exe + SelfUpdater.OldSuffix, "v1");
        File.WriteAllText(exe + SelfUpdater.OldSuffix + "-1", "v0");
        File.WriteAllText(Path.Combine(_dir, "SalochiLauncher.exe.older.txt"), "not ours");
        File.WriteAllText(exe + ".old-before-upgrade", "the player's own copy");
        await SelfUpdater.CleanUpAsync(exe);
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix));
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix + "-1"));
        Assert.True(File.Exists(Path.Combine(_dir, "SalochiLauncher.exe.older.txt")));
        Assert.True(File.Exists(exe + ".old-before-upgrade")); // only names the updater makes
    }

    [Fact]
    public void FinishUpdate_OnlyReplacesASalochiLauncher()
    {
        string self = Path.Combine(_dir, "new", "SalochiLauncher.exe");
        string notes = Path.Combine(_dir, "notes.txt"), fake = Path.Combine(_dir, "Other.exe");
        File.WriteAllText(notes, "keep me");
        File.WriteAllText(fake, "not a launcher");

        Assert.NotNull(SelfUpdater.TargetProblem("relative\\SalochiLauncher.exe", self));
        Assert.NotNull(SelfUpdater.TargetProblem(notes, self));
        Assert.NotNull(SelfUpdater.TargetProblem(fake, self));      // no "Salochi Launcher" version resource
        Assert.NotNull(SelfUpdater.TargetProblem(Path.Combine(_dir, "missing.exe"), self));

        // a crafted --finish-update does nothing at all
        Assert.True(SelfUpdater.TryFinishUpdate(["--finish-update", notes]));
        Assert.Equal("keep me", File.ReadAllText(notes));
        Assert.False(File.Exists(notes + SelfUpdater.OldSuffix));
    }

    [Fact]
    public void Restore_PutsTheOldExeBack()
    {
        string exe = Path.Combine(_dir, "SalochiLauncher.exe"), fresh = Path.Combine(_dir, "new.exe");
        File.WriteAllText(exe, "v1");
        File.WriteAllText(fresh, "v2");
        SelfUpdater.Swap(exe, fresh);
        Assert.Equal("v2", File.ReadAllText(exe));
        SelfUpdater.Restore(exe);
        Assert.Equal("v1", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix));
    }

    [Fact]
    public void Sync_LeavesInstalledVersionsAloneWhenUpdatesAreOff()
    {
        var core = new ModEntry { Id = "core", File = "OTH.Core.dll", Sha256 = Sha("core v2") };
        var bug = new ModEntry { Id = "bajabug", File = "OTH.BajaBug.dll", Sha256 = Sha("bug") };
        var manifest = new Manifest { Mods = [core, bug] };
        File.WriteAllText(Path.Combine(_dir, "OTH.Core.dll"), "core v1");

        Assert.Equal(["core"], PluginSync.Outdated(manifest, _dir).Select(m => m.Id));
        var off = PluginSync.Plan(_dir, [core, bug], manifest.Mods, updateInstalled: false);
        Assert.Equal(["bajabug"], off.Install.Select(m => m.Id)); // only the missing one
        Assert.Equal(["core"], off.Keep.Select(m => m.Id));
        Assert.Equal(["core", "bajabug"], PluginSync.Plan(_dir, [core, bug], manifest.Mods).Install.Select(m => m.Id));
    }
}
