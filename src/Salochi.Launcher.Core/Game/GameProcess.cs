using System.Diagnostics;

namespace Salochi.Launcher.Core.Game;

public static class GameProcess
{
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameLocation.ExeName));
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    /// <summary>Starts the game through Steam (overlay, friends and Steam networking keep working).</summary>
    public static void LaunchThroughSteam() =>
        Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamLibrary.AppId) { UseShellExecute = true })?.Dispose();

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
