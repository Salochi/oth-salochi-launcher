using System.Diagnostics;
using System.Text.RegularExpressions;
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
    /// <summary>The product name in the launcher exe's version resource (Directory.Build.props).</summary>
    public const string ProductName = "Salochi Launcher";
    private const string NewSuffix = ".new";

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
        // Only ever replace a Salochi Launcher: a shortcut with a crafted --finish-update must not overwrite other files.
        string? refusal = TargetProblem(target, self);
        if (refusal != null)
        {
            log?.Invoke($"refused --finish-update {target}: {refusal}");
            return true; // do nothing at all, and show no window
        }
        var rest = WithoutUpdateArgs(args, keepUpdatedFrom: true).ToList();
        bool swapped = false;
        try
        {
            int w = Array.IndexOf(args, WaitPidArg);
            if (w >= 0 && w + 1 < args.Length && int.TryParse(args[w + 1], out int pid)) WaitForExit(pid, target);
            Swap(target, self!);
            swapped = true;
            log?.Invoke($"finished the update of {target}");
            Start(target, rest);
            return true;
        }
        catch (Exception e)
        {
            log?.Invoke($"couldn't finish the update of {target}: {e}");
            // the new build won't start: put the old one back before starting it
            if (swapped)
            {
                try { Restore(target); }
                catch (Exception r) { log?.Invoke($"couldn't put the old launcher back: {r}"); }
            }
            // Start the old launcher, and tell it the update failed so it doesn't try again this run.
            rest = rest.Where((a, k) => a != UpdatedFromArg && (k == 0 || rest[k - 1] != UpdatedFromArg)).ToList();
            rest.AddRange([UpdateFailedArg, string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message]);
            try { Start(target, rest); }
            catch (Exception s2) { log?.Invoke($"couldn't start {target}: {s2}"); }
            return true;
        }
    }

    /// <summary>Why <paramref name="target"/> can't be the exe an update replaces, or null when it can: an absolute
    /// path to an existing .exe whose version resource says Salochi Launcher, and not the running exe itself.</summary>
    internal static string? TargetProblem(string target, string? self)
    {
        if (self == null) return "the new launcher can't tell where it is";
        if (!Path.IsPathFullyQualified(target)) return "not an absolute path";
        if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "not an .exe";
        if (!File.Exists(target)) return "no such file";
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase)) return "that's this exe";
        string? product;
        try { product = FileVersionInfo.GetVersionInfo(target).ProductName; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "unreadable"; }
        return product == ProductName ? null : $"not a Salochi Launcher (product \"{product}\")";
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in place of <paramref name="currentExe"/>, keeping the old one as
    /// <c>&lt;exe&gt;.old</c> (or <c>.old-&lt;n&gt;</c> when an earlier one is still locked). The new exe is first copied
    /// next to the old one and checked, so the swap itself is two renames on one disk: an interruption never leaves a
    /// half-copied launcher under the real name. If the second rename fails, the old exe gets its name back.
    /// </summary>
    public static void Swap(string currentExe, string newExe)
    {
        string fresh = currentExe + NewSuffix;
        File.Copy(newExe, fresh, overwrite: true);
        if (new FileInfo(fresh).Length != new FileInfo(newExe).Length || FileUtil.Sha256(fresh) != FileUtil.Sha256(newExe))
        {
            File.Delete(fresh);
            throw new IOException("the copy of the new launcher doesn't match the download");
        }
        string old = FreeOldName(currentExe);
        File.Move(currentExe, old);
        try
        {
            File.Move(fresh, currentExe);
        }
        catch
        {
            File.Move(old, currentExe);
            try { File.Delete(fresh); } catch (IOException) { }
            throw;
        }
        File.WriteAllText(currentExe + ".previous", old); // where Restore finds it, if the new one won't start
    }

    /// <summary>Puts the exe <see cref="Swap"/> set aside back under its name (the new one is deleted).</summary>
    public static void Restore(string currentExe)
    {
        string note = currentExe + ".previous";
        string old = File.Exists(note) ? File.ReadAllText(note).Trim() : currentExe + OldSuffix;
        if (!File.Exists(old)) throw new FileNotFoundException("the old launcher isn't there any more", old);
        File.Move(old, currentExe, overwrite: true);
        File.Delete(note);
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
        string name = Path.GetFileName(currentExe);
        // only what Swap makes: <exe>.old, <exe>.old-<n>, <exe>.new, <exe>.previous
        var ours = new Regex("^" + Regex.Escape(name) + @"(\.old(-\d+)?|\.new|\.previous)$", RegexOptions.IgnoreCase);
        foreach (var old in Directory.EnumerateFiles(dir, name + ".*").Where(f => ours.IsMatch(Path.GetFileName(f))).ToList())
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

    /// <summary>Waits for the old launcher to exit. A process with that id that isn't running the target exe is some
    /// other program that got the id after the launcher exited: nothing to wait for.</summary>
    private static void WaitForExit(int pid, string target)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = null;
            try { path = p.MainModule?.FileName; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            if (path != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
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
