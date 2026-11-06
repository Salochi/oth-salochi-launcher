using Salochi.Launcher.Core.Game;

namespace Salochi.Launcher.Tests;

public class GameTests
{
    [Fact]
    public void LibraryFolders_ReadsEveryPath()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "C:\\Program Files (x86)\\Steam"
                    "apps" { "228980" "1" }
                }
                // a comment
                "1"
                {
                    "path"      "E:\\SteamLibrary"
                    "apps" { "2929250" "123" }
                }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"], SteamLibrary.ParseLibraryFolders(vdf));
    }

    [Fact]
    public void AppManifest_GivesInstallDirAndBuild()
    {
        var state = Vdf.Parse("\"AppState\"\n{\n\t\"appid\"\t\t\"2929250\"\n\t\"installdir\"\t\t\"over the hill\"\n\t\"buildid\"\t\t\"26326013\"\n}").Block("appstate");
        Assert.Equal("over the hill", state?.Value("installdir"));
        Assert.Equal("26326013", state?.Value("BUILDID"));
    }

    private const string Ini = "# General options for Unity Doorstop\r\n[General]\r\n\r\n# Enable Doorstop?\r\nenabled = true\r\n\r\ntarget_assembly = BepInEx\\core\\BepInEx.Unity.IL2CPP.dll\r\n[UnityMono]\r\nenabled = true\r\n";

    [Fact]
    public void Doorstop_TogglesOnlyGeneralEnabled()
    {
        Assert.True(DoorstopConfig.ReadEnabled(Ini));
        string off = DoorstopConfig.WriteEnabled(Ini, false);
        Assert.False(DoorstopConfig.ReadEnabled(off));
        Assert.Contains("[UnityMono]\r\nenabled = true", off); // another section's key is left alone
        Assert.Equal(Ini, DoorstopConfig.WriteEnabled(off, true)); // the round trip changes nothing else
    }

    [Fact]
    public void FirstStart_IsPendingUntilBepInExHasPreparedTheGame()
    {
        string root = Directory.CreateTempSubdirectory("salochi-game-").FullName;
        try
        {
            var game = new GameLocation(root, null);
            Assert.True(BepInExInstall.FirstStartPending(game));
            Directory.CreateDirectory(Path.Combine(game.BepInExDir, "interop"));
            Assert.True(BepInExInstall.FirstStartPending(game)); // the folder alone isn't enough
            File.WriteAllText(Path.Combine(game.BepInExDir, "interop", "Assembly-CSharp.dll"), "");
            Assert.False(BepInExInstall.FirstStartPending(game));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LoaderInstall_BacksUpWhatItReplacesAndKeepsTheDoorstopChoice()
    {
        string dir = Directory.CreateTempSubdirectory("salochi-loader-").FullName;
        try
        {
            string game = Path.Combine(dir, "game"), zip = Path.Combine(dir, "bepinex.zip");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "winhttp.dll"), "another loader");
            File.WriteAllText(Path.Combine(game, "doorstop_config.ini"), "[General]\nenabled = false\n");
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                Add(z, "winhttp.dll", "doorstop");
                Add(z, "doorstop_config.ini", "[General]\nenabled = true\n");
                Add(z, "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "bepinex");
            }

            string? backup = BepInExInstall.Extract(zip, game, new DateTime(2026, 11, 6, 12, 0, 0));

            Assert.Equal("doorstop", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
            Assert.Equal("another loader", File.ReadAllText(Path.Combine(backup!, "winhttp.dll")));
            Assert.False(DoorstopConfig.Read(game)); // the player's choice stays
            Assert.True(File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")));
            Assert.False(Directory.Exists(Path.Combine(game, ".salochi-loader-staging")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoaderInstall_RefusesEntriesOutsideTheGameAndChangesNothing()
    {
        string dir = Directory.CreateTempSubdirectory("salochi-loader-").FullName;
        try
        {
            string game = Path.Combine(dir, "game"), zip = Path.Combine(dir, "bad.zip");
            Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "winhttp.dll"), "mine");
            using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                Add(z, "winhttp.dll", "theirs");
                Add(z, "../escape.dll", "x");
            }
            Assert.Throws<IOException>(() => BepInExInstall.Extract(zip, game, DateTime.Now));
            Assert.Equal("mine", File.ReadAllText(Path.Combine(game, "winhttp.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "escape.dll")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void Add(System.IO.Compression.ZipArchive z, string name, string text)
    {
        using var w = new StreamWriter(z.CreateEntry(name).Open());
        w.Write(text);
    }

    [Fact]
    public void Doorstop_AddsMissingKey()
    {
        Assert.Null(DoorstopConfig.ReadEnabled("[General]\ntarget_assembly = x\n"));
        Assert.False(DoorstopConfig.ReadEnabled(DoorstopConfig.WriteEnabled("[General]\ntarget_assembly = x\n", false)));
        Assert.True(DoorstopConfig.ReadEnabled(DoorstopConfig.WriteEnabled("", true)));
    }
}
