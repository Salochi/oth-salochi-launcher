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
    public void Doorstop_AddsMissingKey()
    {
        Assert.Null(DoorstopConfig.ReadEnabled("[General]\ntarget_assembly = x\n"));
        Assert.False(DoorstopConfig.ReadEnabled(DoorstopConfig.WriteEnabled("[General]\ntarget_assembly = x\n", false)));
        Assert.True(DoorstopConfig.ReadEnabled(DoorstopConfig.WriteEnabled("", true)));
    }
}
