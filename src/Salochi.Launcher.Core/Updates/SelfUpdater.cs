using System.Diagnostics;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.Core.Updates;

/// <summary>
/// Replaces the launcher with a newer build, in two steps. A single-file app reads parts of itself from its own exe
/// while it runs, so the exe can't be swapped under a running launcher:
/// <list type="number">
/// <item>The running launcher downloads the new exe into its store, starts that copy with
/// <c>--finish-update &lt;exe&gt; --wait-pid &lt;pid&gt;</c>, and exits.</item>
/// <item>The new copy (<see cref="TryFinishUpdate"/>, before any UI) waits for the old process to exit, puts itself in
/// place of the old exe (keeping it as <c>&lt;exe&gt;.old</c> until the next start), and starts it.</item>
/// </list>
/// </summary>
public static class SelfUpdater
{
    public const string OldSuffix = ".old";
    public const string FinishArg = "--finish-update";
    public const string WaitPidArg = "--wait-pid";
    public const string UpdatedFromArg = "--updated-from";
    public const string UpdateFailedArg = "--update-failed";

    /// <summary>
    /// The exe to replace, or null when this isn't a published single-file build (running from a build folder or
    /// through <c>dotnet run</c>, there's no one exe to swap, so development builds never update themselves).
    /// </summary>
    public static string? CurrentExe()
    {
        // A single-file app's assemblies have no location on disk.
        if (!string.IsNullOrEmpty(typeof(SelfUpdater).Assembly.Location)) return null;
        string? exe = Environment.ProcessPath;
        return exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : null;
    }

    /// <summary>Downloads the release into the store (verified) and returns its path.</summary>
    public static async Task<string> DownloadAsync(LauncherRelease release, string storeRoot, HttpClient http, IProgress<double>? progress, CancellationToken ct)
    {
        string dest = Path.Combine(storeRoot, "launcher", release.ParsedVersion.ToString(3), "SalochiLauncher.exe");
        if (File.Exists(dest) && string.Equals(FileUtil.Sha256(dest), release.Sha256, StringComparison.OrdinalIgnoreCase)) return dest;
        await ModStore.DownloadVerifiedAsync(http, release.DownloadUri(), dest, release.Sha256, release.Size, progress, ct);
        return dest;
    }

    /// <summary>Step 1: starts the downloaded exe to finish the update. The caller exits right after.</summary>
    public static void HandOver(string newExe, string currentExe, string currentVersion, IEnumerable<string> args)
    {
        Start(newExe, new[] { FinishArg, currentExe, WaitPidArg, Environment.ProcessId.ToString() }
            .Concat(WithoutUpdateArgs(args))
            .Concat([UpdatedFromArg, currentVersion]));
    }

    /// <summary>
    /// Step 2, called first thing in Main. Returns false when this run isn't finishing an update (start normally);
    /// true when it did its part and this process should exit.
    /// </summary>
    public static bool TryFinishUpdate(string[] args, Action<string>? log = null)
    {
        int i = Array.IndexOf(args, FinishArg);
        if (i < 0 || i + 1 >= args.Length) return false;
        string target = args[i + 1];
        string? self = Environment.ProcessPath;
        var rest = WithoutUpdateArgs(args, keepUpdatedFrom: true).ToList();
        try
        {
            if (self == null) throw new InvalidOperationException("the new launcher can't tell where it is");
            int w = Array.IndexOf(args, WaitPidArg);
            if (w >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out int pid)) WaitForExit(pid);
            Swap(target, self);
            log?.Invoke($"finished the update of {target}");
        }
        catch (Exception e)
        {
            // Start the old launcher anyway, and tell it the update failed so it doesn't try again this run.
            log?.Invoke($"couldn't finish the update of {target}: {e}");
            rest = rest.Where((a, k) => a != UpdatedFromArg && (k == 0 || rest[k - 1] != UpdatedFromArg)).ToList();
            rest.AddRange([UpdateFailedArg, string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message]);
        }
        Start(target, rest);
        return true;
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in place of <paramref name="currentExe"/>, keeping the old one as
    /// <c>&lt;exe&gt;.old</c> (or <c>.old-&lt;n&gt;</c> when an earlier one is still locked). If the copy fails, the old exe
    /// gets its name back.
    /// </summary>
    public static void Swap(string currentExe, string newExe)
    {
        string old = FreeOldName(currentExe);
        File.Move(currentExe, old);
        try
        {
            File.Copy(newExe, currentExe);
        }
        catch
        {
            if (File.Exists(currentExe)) File.Delete(currentExe);
            File.Move(old, currentExe);
            throw;
        }
    }

    /// <summary>A name to move the old exe to: &lt;exe&gt;.old, deleting an earlier one; when that one can't go (a
    /// launcher still running from it), &lt;exe&gt;.old-&lt;n&gt;.</summary>
    private static string FreeOldName(string currentExe)
    {
        string old = currentExe + OldSuffix;
        try
        {
            if (File.Exists(old)) File.Delete(old);
            return old;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            for (int n = 1; ; n++)
                if (!File.Exists($"{old}-{n}")) return $"{old}-{n}";
        }
    }

    /// <summary>Deletes the copies updates left behind (retrying each for a moment, else leaving it for next time).</summary>
    public static async Task CleanUpAsync(string currentExe)
    {
        string dir = Path.GetDirectoryName(currentExe)!;
        string name = Path.GetFileName(currentExe) + OldSuffix;
        foreach (var old in Directory.EnumerateFiles(dir, name + "*").Where(f => Path.GetFileName(f) == name || Path.GetFileName(f).StartsWith(name + "-")).ToList())
        {
            for (int i = 0; i < 10 && File.Exists(old); i++)
            {
                try { File.Delete(old); break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(500); }
            }
        }
    }

    /// <summary>The arguments without the update bookkeeping (so it never piles up over several updates).</summary>
    internal static IEnumerable<string> WithoutUpdateArgs(IEnumerable<string> args, bool keepUpdatedFrom = false)
    {
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            string a = list[i];
            bool takesValue = a is FinishArg or WaitPidArg or UpdateFailedArg || (a == UpdatedFromArg && !keepUpdatedFrom);
            if (takesValue) { i++; continue; }
            yield return a;
        }
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(TimeSpan.FromSeconds(30))) throw new TimeoutException("the old launcher didn't close within 30 seconds");
        }
        catch (ArgumentException) { /* already gone */ }
    }

    private static void Start(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }
}
