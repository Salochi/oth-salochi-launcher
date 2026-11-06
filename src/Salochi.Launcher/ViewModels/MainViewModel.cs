using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Salochi.Launcher.Core;
using Salochi.Launcher.Core.Game;
using Salochi.Launcher.Core.Mods;
using Salochi.Launcher.Core.Updates;

namespace Salochi.Launcher.ViewModels;

public enum Page { Cars, Features, Game }

public partial class MainViewModel : ViewModelBase
{
    /// <summary>Where every release's files are: "latest" always points at the newest release.</summary>
    public const string ReleaseBase = "https://github.com/Salochi/oth-salochi-launcher/releases/latest/download/";
    /// <summary>The published mod list.</summary>
    public const string ManifestUrl = ReleaseBase + "manifest.json";
    /// <summary>The newest launcher build (see <see cref="LauncherRelease"/>).</summary>
    public const string LauncherUrl = ReleaseBase + "launcher.json";

    private static readonly HttpClient Http = CreateHttp();

    private readonly LauncherSettings _settings;
    private readonly string? _manifestArg;
    private Manifest? _manifest;
    private GameLocation? _game;
    private IReadOnlyList<PluginIssue> _issues = [];
    /// <summary>Why the mod list didn't load, kept so later checks of the game folder list it again.</summary>
    private ProblemViewModel? _listProblem;
    private LauncherRelease? _launcherRelease;
    private bool _selfUpdateTried;
    private static readonly Version Current = LauncherRelease.Normalize(typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0));

    public MainViewModel() : this(null) { }

    public MainViewModel(string? manifestArg)
    {
        _manifestArg = manifestArg;
        _settings = LauncherSettings.Load();
    }

    /// <summary>Does everything but start the game ("--no-launch", for testing).</summary>
    public bool NoLaunch { get; init; }

    /// <summary>The command line, passed on when the launcher restarts after updating itself.</summary>
    public string[] Args { get; init; } = [];

    /// <summary>The version this launcher replaced, when it was started by its own update ("--updated-from").</summary>
    public string? UpdatedFrom { get; init; }

    /// <summary>Why the last update didn't go in, when the new launcher couldn't take the old one's place ("--update-failed").</summary>
    public string? UpdateFailed { get; init; }

    /// <summary>Set by the app: closes the launcher (after it started its updated self).</summary>
    public Action? Shutdown { get; set; }

    /// <summary>Set by the window: asks the player for a folder.</summary>
    public Func<string, Task<string?>>? PickFolder { get; set; }

    // ---------------------------------------------------------------- pages

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCars), nameof(IsFeatures), nameof(IsGame))]
    public partial Page Page { get; set; } = Page.Cars;

    public bool IsCars => Page == Page.Cars;
    public bool IsFeatures => Page == Page.Features;
    public bool IsGame => Page == Page.Game;

    [RelayCommand] private void Show(Page page) => Page = page;

    public ObservableCollection<ModItemViewModel> Cars { get; } = [];
    public ObservableCollection<ModItemViewModel> Features { get; } = [];
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems), nameof(ProblemBadge))]
    public partial int ProblemCount { get; set; }

    public bool HasProblems => ProblemCount > 0;
    public string ProblemBadge => ProblemCount.ToString();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCars), nameof(HasFeatures), nameof(ListMissing))]
    public partial bool ListLoaded { get; set; }

    public bool HasCars => Cars.Count > 0;
    public bool HasFeatures => Features.Count > 0;
    public bool ListMissing => !ListLoaded && !IsBusy;

    // ---------------------------------------------------------------- the Game page

    [ObservableProperty] public partial string GameFolder { get; set; } = "Looking for Over The Hill…";
    [ObservableProperty] public partial string GameBuild { get; set; } = "";
    [ObservableProperty] public partial string LoaderState { get; set; } = "";
    [ObservableProperty] public partial string SteamMode { get; set; } = "";
    [ObservableProperty] public partial string CoreState { get; set; } = "";
    [ObservableProperty] public partial string? CoreCredits { get; set; }
    [ObservableProperty] public partial string ListState { get; set; } = "";

    // ---------------------------------------------------------------- updates (on the Game page)

    public string LauncherVersion => "version " + Current.ToString(3);
    [ObservableProperty] public partial string LauncherUpdateText { get; set; } = "";
    [ObservableProperty] public partial bool HasLauncherUpdate { get; set; }
    [ObservableProperty] public partial bool CanUpdateLauncher { get; set; }
    [ObservableProperty] public partial string ModUpdatesText { get; set; } = "";
    [ObservableProperty] public partial bool HasModUpdates { get; set; }

    public bool AutoUpdateLauncher
    {
        get => _settings.AutoUpdateLauncher;
        set
        {
            if (_settings.AutoUpdateLauncher == value) return;
            _settings.AutoUpdateLauncher = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoUpdateMods
    {
        get => _settings.AutoUpdateMods;
        set
        {
            if (_settings.AutoUpdateMods == value) return;
            _settings.AutoUpdateMods = value;
            _settings.Save();
            OnPropertyChanged();
            BuildModList(); // the tiles' "Version x is available" / "Updates when you play"
            _ = LoadImagesAsync();
            UpdateState();
        }
    }

    // ---------------------------------------------------------------- the footer

    [ObservableProperty] public partial string Summary { get; set; } = "";
    /// <summary>Why Play is greyed out, in a sentence; null when it isn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked))]
    public partial string? BlockReason { get; set; }
    public bool IsBlocked => BlockReason != null && !IsBusy;

    /// <summary>What the last action did, or why it failed; cleared by the next one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }
    [ObservableProperty] public partial bool MessageIsError { get; set; }
    public bool HasMessage => Message != null && !IsBusy;

    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsBlocked), nameof(HasMessage), nameof(ListMissing))]
    public partial bool IsBusy { get; set; } = true;
    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    public partial bool CanPlay { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayWithoutModsCommand))]
    public partial bool CanPlayWithoutMods { get; set; }

    // ---------------------------------------------------------------- loading

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Checking the game and the mod list…";
        Problems.Clear();
        _issues = [];
        _listProblem = null;
        try
        {
            _game = LocateGame();
            _manifest = await LoadManifestAsync();
            if (await CheckLauncherAsync()) return; // restarting into the new version
            ListLoaded = _manifest != null;
            BuildModList();
            DescribeGame();
            if (_settings.AutoUpdateMods) await UpdateModsQuietlyAsync();
            DescribeModUpdates();
            await LoadImagesAsync();
        }
        catch (Exception e)
        {
            LauncherLog.Error("refresh", e);
            Say("Something went wrong while checking the game: " + LauncherLog.Describe(e) + ".", error: true);
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            UpdateState();
        }
    }

    private GameLocation? LocateGame()
    {
        if (_settings.GameRoot != null)
        {
            var picked = SteamLibrary.FromFolder(_settings.GameRoot);
            if (picked.LooksValid) return picked;
        }
        var found = SteamLibrary.FindGame();
        return found is { LooksValid: true } ? found : null;
    }

    // ---------------------------------------------------------------- updates

    /// <summary>
    /// Checks launcher.json. With a newer build out and automatic updates on, installs it and restarts (true: the
    /// launcher is closing). Otherwise it's offered on the Game page.
    /// </summary>
    private async Task<bool> CheckLauncherAsync()
    {
        HasLauncherUpdate = false;
        try { _launcherRelease = await LoadLauncherReleaseAsync(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ManifestException or IOException)
        {
            LauncherLog.Error("launcher.json", e);
            LauncherUpdateText = "Couldn't check for a newer launcher: " + LauncherLog.Describe(e) + ".";
            return false;
        }
        if (_launcherRelease == null || !_launcherRelease.IsNewerThan(Current))
        {
            LauncherUpdateText = "Up to date.";
            return false;
        }

        HasLauncherUpdate = true;
        CanUpdateLauncher = SelfUpdater.CurrentExe() != null;
        string v = _launcherRelease.ParsedVersion.ToString(3);
        LauncherUpdateText = CanUpdateLauncher
            ? $"Version {v} is available. {_launcherRelease.Notes}".TrimEnd()
            : $"Version {v} is available. This is a development build, which doesn't update itself.";
        // Once per run, and never right after an update: a release that still looks older can't loop.
        if (_settings.AutoUpdateLauncher && CanUpdateLauncher && !_selfUpdateTried && UpdatedFrom == null && UpdateFailed == null)
            return await InstallLauncherUpdateAsync();
        if (!_settings.AutoUpdateLauncher)
            Say($"Salochi Launcher {v} is available. Update it from the Game page.");
        return false;
    }

    private async Task<LauncherRelease?> LoadLauncherReleaseAsync()
    {
        string? local = _manifestArg ?? _settings.ManifestOverride;
        if (local != null)
        {
            // next to a local manifest, when one was made with it
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(local))!, "launcher.json");
            if (!File.Exists(path)) return null;
            byte[] bytes = await File.ReadAllBytesAsync(path);
            if (File.Exists(path + ".sig") && !ReleaseKey.Verify(bytes, await File.ReadAllTextAsync(path + ".sig")))
                throw new ManifestException("launcher.json doesn't match its signature");
            return LauncherRelease.Parse(System.Text.Encoding.UTF8.GetString(bytes), new Uri(path));
        }
        using var resp = await Http.GetAsync(LauncherUrl);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no launcher published yet
        resp.EnsureSuccessStatusCode();
        byte[] data = await resp.Content.ReadAsByteArrayAsync();
        if (!ReleaseKey.Verify(data, await Http.GetStringAsync(LauncherUrl + ".sig")))
            throw new ManifestException("launcher.json on GitHub isn't signed by Salochi");
        return LauncherRelease.Parse(System.Text.Encoding.UTF8.GetString(data), new Uri(LauncherUrl));
    }

    [RelayCommand]
    private async Task UpdateLauncherAsync()
    {
        Message = null;
        IsBusy = true;
        UpdateState();
        try { await InstallLauncherUpdateAsync(); }
        finally
        {
            IsBusy = false;
            StatusText = "";
            UpdateState();
        }
    }

    /// <summary>Downloads the new launcher, swaps it in and starts it. True when the launcher is now closing.</summary>
    private async Task<bool> InstallLauncherUpdateAsync()
    {
        string? exe = SelfUpdater.CurrentExe();
        if (_launcherRelease == null || exe == null) return false;
        _selfUpdateTried = true;
        string v = _launcherRelease.ParsedVersion.ToString(3);
        try
        {
            StatusText = $"Updating the launcher to version {v}…";
            Progress = 0;
            string fresh = await SelfUpdater.DownloadAsync(_launcherRelease, LauncherSettings.DataDir, Http, new Progress<double>(p => Progress = p), CancellationToken.None);
            LauncherLog.Info($"updating {exe} from {Current.ToString(3)} to {v}: handing over to {fresh}");
            SelfUpdater.HandOver(fresh, exe, Current.ToString(3), Args);
            Shutdown?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            LauncherLog.Error("launcher update", e);
            Say($"The launcher couldn't update itself to version {v}: {LauncherLog.Describe(e)}. It keeps running as version {Current.ToString(3)}.", error: true);
            return false;
        }
    }

    /// <summary>Runs once the window is up: tidies after an update and says it happened.</summary>
    public async Task AfterStartAsync()
    {
        if (SelfUpdater.CurrentExe() is { } exe) await SelfUpdater.CleanUpAsync(exe);
        if (UpdateFailed != null)
            Say($"The launcher couldn't update itself: {UpdateFailed.TrimEnd('.')}. It tries again next time it opens.", error: true);
        else if (UpdatedFrom != null && !MessageIsError)
            Say($"The launcher updated itself to version {Current.ToString(3)}." + (Message != null ? " " + Message : ""));
    }

    /// <summary>Installs newer versions of the mods in the game folder, when the game is closed and nothing blocks
    /// it, and says which ones it updated.</summary>
    private async Task UpdateModsQuietlyAsync()
    {
        string? target = OurFolder();
        if (_manifest == null || target == null || !Directory.Exists(target)) return;
        if (Problems.Any(p => p.Blocks) || GameProcess.IsRunning()) return;
        var outdated = PluginSync.Outdated(_manifest, target);
        if (outdated.Count == 0) return;
        try
        {
            await InstallUpdatesAsync(outdated);
            Say("Updated " + JoinNames(outdated.Select(m => $"{m.Name} to version {m.Version}")) + ".");
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or ManifestException)
        {
            LauncherLog.Error("mod updates", e);
            Say("Mod updates couldn't be installed: " + LauncherLog.Describe(e) + ". Play tries again.", error: true);
        }
        BuildModList();
    }

    private async Task InstallUpdatesAsync(IReadOnlyList<ModEntry> mods)
    {
        var store = Store();
        for (int i = 0; i < mods.Count; i++)
        {
            StatusText = $"Updating {mods[i].Name}…";
            int n = i;
            await store.EnsureAsync(_manifest!, mods[i], new Progress<double>(p => Progress = (n + p) / mods.Count), CancellationToken.None);
        }
        string target = OurFolder()!;
        PluginSync.Apply(target, PluginSync.Plan(target, mods, mods), store.PathOf);
    }

    [RelayCommand]
    private async Task UpdateModsAsync()
    {
        string? target = OurFolder();
        if (_manifest == null || target == null || !EnsureClosed()) return;
        var outdated = PluginSync.Outdated(_manifest, target);
        string done = "Updated " + JoinNames(outdated.Select(m => $"{m.Name} to version {m.Version}")) + ".";
        await RunAsync(_ => InstallUpdatesAsync(outdated), done, launches: false);
    }

    private void DescribeModUpdates()
    {
        string? target = OurFolder();
        var outdated = _manifest != null && target != null && Directory.Exists(target) ? PluginSync.Outdated(_manifest, target) : [];
        HasModUpdates = outdated.Count > 0;
        ModUpdatesText = outdated.Count == 0
            ? "Your mods are up to date."
            : $"Updates are available for {JoinNames(outdated.Select(m => m.Name))}.";
    }

    private static string JoinNames(IEnumerable<string> names)
    {
        var l = names.ToList();
        return l.Count switch
        {
            0 => "",
            1 => l[0],
            _ => string.Join(", ", l.Take(l.Count - 1)) + " and " + l[^1],
        };
    }

    // ---------------------------------------------------------------- the mod list

    private async Task<Manifest?> LoadManifestAsync()
    {
        string? local = _manifestArg ?? _settings.ManifestOverride;
        try
        {
            if (local != null)
            {
                string full = Path.GetFullPath(local);
                byte[] bytes = await File.ReadAllBytesAsync(full);
                string sigPath = full + ".sig";
                bool signed = File.Exists(sigPath);
                if (signed && !ReleaseKey.Verify(bytes, await File.ReadAllTextAsync(sigPath)))
                    throw new ManifestException($"{full} doesn't match its signature");
                var m = Manifest.Parse(System.Text.Encoding.UTF8.GetString(bytes), new Uri(full));
                ListState = signed ? $"A local list, signed by Salochi: {full}" : $"A local list without a signature: {full}";
                return m;
            }
            byte[] data = await Http.GetByteArrayAsync(ManifestUrl);
            string sig = await Http.GetStringAsync(ManifestUrl + ".sig");
            if (!ReleaseKey.Verify(data, sig))
                throw new ManifestException("the mod list on GitHub isn't signed by Salochi, so the launcher won't use it");
            ListState = "Up to date, signed by Salochi";
            return Manifest.Parse(System.Text.Encoding.UTF8.GetString(data), new Uri(ManifestUrl));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            ListState = "GitHub couldn't be reached";
            _listProblem = new ProblemViewModel("The mod list couldn't be downloaded",
                "Check your internet connection. Until the list loads, mods can't be installed or updated, but Play without mods still works.",
                blocks: true, "Try again", RefreshCommand);
        }
        catch (Exception e) when (e is ManifestException or IOException)
        {
            ListState = "Couldn't be read";
            _listProblem = new ProblemViewModel("The mod list couldn't be read", Capitalize(e.Message) + ".", blocks: true, "Try again", RefreshCommand);
        }
        return null;
    }

    private void BuildModList()
    {
        Cars.Clear();
        Features.Clear();
        OnPropertyChanged(nameof(HasCars));
        OnPropertyChanged(nameof(HasFeatures));
        if (_manifest == null) return;

        // The game folder is the record of what's picked: Core's in-game installer writes there too.
        string? target = OurFolder();
        var installed = target != null && Directory.Exists(target) ? PluginSync.InstalledIds(_manifest, target) : null;
        var store = Store();
        foreach (var mod in _manifest.Mods.Where(m => m.Kind != ModKind.Core))
        {
            bool picked = installed != null
                ? installed.Contains(mod.Id, StringComparer.OrdinalIgnoreCase)
                : _settings.Picked == null || _settings.Picked.Contains(mod.Id, StringComparer.OrdinalIgnoreCase);
            var item = new ModItemViewModel(mod, picked, null);
            item.Status = StatusOf(item, store, target, _settings.AutoUpdateMods);
            item.PickedChanged += () => OnPicked(item);
            (mod.Kind == ModKind.Car ? Cars : Features).Add(item);
        }
        OnPropertyChanged(nameof(HasCars));
        OnPropertyChanged(nameof(HasFeatures));
    }

    private async Task LoadImagesAsync()
    {
        if (_manifest == null) return;
        var store = Store();
        foreach (var item in Cars.Concat(Features))
        {
            string? path = await store.EnsureImageAsync(_manifest, item.Entry, CancellationToken.None);
            if (path == null) continue;
            try { item.Image = new Bitmap(path); }
            catch (Exception) { /* a broken picture: the tile shows the car's name only */ }
        }
    }

    /// <summary>What Play will do about this mod, when it's more than "nothing".</summary>
    private static string? StatusOf(ModItemViewModel item, ModStore store, string? target, bool autoUpdate)
    {
        if (!item.IsPicked) return null;
        string? installed = target != null ? Path.Combine(target, item.Entry.File) : null;
        if (installed != null && File.Exists(installed))
            return PluginSync.Plan(target!, [item.Entry]).Install.Count == 0 ? null
                : autoUpdate ? "Updates when you play" : $"Version {item.Entry.Version} is available";
        return store.Has(item.Entry) ? "Installs when you play" : "Downloads when you play";
    }

    private void OnPicked(ModItemViewModel item)
    {
        // Ticking a mod ticks what it needs; unticking one unticks what needs it.
        var all = AllMods().ToList();
        if (item.IsPicked)
        {
            foreach (var need in item.Entry.Requires)
                if (all.FirstOrDefault(m => m.Entry.Id == need) is { IsPicked: false } dep) dep.IsPicked = true;
        }
        else
        {
            foreach (var other in all.Where(m => m.IsPicked && m.Entry.Requires.Contains(item.Entry.Id, StringComparer.OrdinalIgnoreCase)))
                other.IsPicked = false;
        }
        item.Status = StatusOf(item, Store(), OurFolder(), _settings.AutoUpdateMods);
        _settings.Picked = all.Where(m => m.IsPicked).Select(m => m.Entry.Id).ToList();
        _settings.Save();
        UpdateState();
    }

    private IEnumerable<ModItemViewModel> AllMods() => Cars.Concat(Features);

    private void DescribeGame()
    {
        if (_listProblem != null) Problems.Add(_listProblem);
        var core = _manifest?.Mods.FirstOrDefault(m => m.Kind == ModKind.Core);
        CoreState = core != null ? $"Version {core.Version}. Always on: every other mod runs on it." : "Not known until the mod list loads.";
        CoreCredits = core?.Credits;

        if (_game == null)
        {
            GameFolder = "Not found in your Steam libraries";
            GameBuild = "";
            LoaderState = "";
            SteamMode = "";
            Problem("Over The Hill wasn't found",
                "The launcher looks through your Steam libraries. If the game is installed somewhere else, pick its folder.",
                blocks: true, "Pick the game folder", ChooseGameFolderCommand);
            return;
        }

        var game = _game;
        GameFolder = game.Root;
        bool tested = _manifest is not { TestedGameBuilds.Count: > 0 } || game.BuildId == null || _manifest.TestedGameBuilds.Contains(game.BuildId);
        GameBuild = game.BuildId == null ? "Unknown build" : tested ? $"Build {game.BuildId}" : $"Build {game.BuildId}, newer than the mods were tested on";
        if (!tested)
            Problem("Over The Hill has updated",
                $"These mods were tested on an earlier version of the game. They may not work until they're updated. If the game crashes, use Play without mods.",
                blocks: false);

        if (!BepInExInstall.IsInstalled(game))
        {
            LoaderState = $"Not installed yet. Play installs BepInEx {_manifest?.BepInEx?.Version}".TrimEnd() + ".";
            return;
        }
        LoaderState = "BepInEx is installed";

        var ourHashes = new HashSet<string>(_manifest?.Mods.Select(m => m.Sha256) ?? [], StringComparer.OrdinalIgnoreCase);
        var ourFiles = new HashSet<string>(_manifest?.Mods.Where(m => m.Kind != ModKind.Core).Select(m => m.File) ?? [], StringComparer.OrdinalIgnoreCase);
        _issues = PluginScanner.Issues(PluginScanner.Scan(game.PluginsDir, ourHashes, ourFiles));
        foreach (var issue in _issues)
        {
            string files = string.Join(", ", issue.Files.Select(f => f.RelativePath));
            var move = new AsyncRelayCommand(() => MoveAsideAsync(issue));
            switch (issue.Kind)
            {
                case PluginIssueKind.ForeignCore:
                    Problem("The original OverTheHillPlus is installed",
                        $"Salochi mods run on their own version of the OverTheHillPlus Core. With both installed only one of them loads, and the game may crash. Moving the original into a backup folder fixes this, and nothing is deleted. Found: {files}.",
                        blocks: true, "Move to backup", move);
                    break;
                case PluginIssueKind.LooseSalochiCopies:
                    Problem("Old copies of Salochi mods are installed",
                        $"They would load alongside the launcher's copies. Moving them into a backup folder fixes this. Found: {files}.",
                        blocks: true, "Move to backup", move);
                    break;
                case PluginIssueKind.OverTheHillPlusMods:
                    Problem("Other OTH mods are installed",
                        $"The launcher didn't install these and leaves them alone. Mods made for the original OverTheHillPlus may not work with Salochi Core. Found: {files}.",
                        blocks: false);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- actions

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (_game == null || _manifest == null || !EnsureClosed()) return;
        var game = _game;
        var manifest = _manifest;
        await RunAsync(async ct =>
        {
            if (!BepInExInstall.IsInstalled(game))
            {
                StatusText = "Installing the mod loader…";
                await BepInExInstall.InstallAsync(game, manifest, LauncherSettings.DataDir, Http, new Progress<double>(p => Progress = p), ct);
            }

            var mods = PluginSync.Resolve(manifest, AllMods().Where(m => m.IsPicked).Select(m => m.Entry.Id));
            var store = Store();
            var needed = PluginSync.Plan(OurFolder()!, mods, manifest.Mods, _settings.AutoUpdateMods).Install;
            for (int i = 0; i < mods.Count; i++)
            {
                if (store.Has(mods[i]) || !needed.Contains(mods[i])) continue;
                StatusText = $"Downloading {mods[i].Name}…";
                int n = i;
                await store.EnsureAsync(manifest, mods[i], new Progress<double>(p => Progress = (n + p) / mods.Count), ct);
            }

            StatusText = "Installing mods…";
            string target = OurFolder()!;
            PluginSync.Apply(target, PluginSync.Plan(target, mods, manifest.Mods, _settings.AutoUpdateMods), store.PathOf);
            DoorstopConfig.Write(game.Root, true);

            StatusText = "Starting Over The Hill…";
            if (!NoLaunch) GameProcess.LaunchThroughSteam();
        }, done: "Over The Hill is starting with your mods.");
    }

    [RelayCommand(CanExecute = nameof(CanPlayWithoutMods))]
    private async Task PlayWithoutModsAsync()
    {
        if (_game == null || !EnsureClosed()) return;
        var game = _game;
        await RunAsync(_ =>
        {
            if (DoorstopConfig.Read(game.Root) != null) DoorstopConfig.Write(game.Root, false);
            StatusText = "Starting Over The Hill without mods…";
            if (!NoLaunch) GameProcess.LaunchThroughSteam();
            return Task.CompletedTask;
        }, done: "Over The Hill is starting without mods. Your mods stay installed for next time.");
    }

    private async Task MoveAsideAsync(PluginIssue issue)
    {
        if (_game == null || !EnsureClosed()) return;
        string backup;
        try { backup = PluginScanner.MoveAside(_game.BepInExDir, issue.Files, DateTime.Now); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say("The files couldn't be moved: " + e.Message, error: true);
            return;
        }
        await RefreshAsync();
        Say($"Moved {issue.Files.Count} file{(issue.Files.Count == 1 ? "" : "s")} to {backup}. To undo, move them back into BepInEx\\plugins.");
    }

    [RelayCommand]
    private async Task ChooseGameFolderAsync()
    {
        if (PickFolder == null) return;
        string? dir = await PickFolder("Pick the Over The Hill folder");
        if (dir == null) return;
        if (!File.Exists(Path.Combine(dir, GameLocation.ExeName)))
        {
            Say($"That folder doesn't have \"{GameLocation.ExeName}\" in it. Pick the folder the game is installed in.", error: true);
            return;
        }
        _settings.GameRoot = dir;
        _settings.Save();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (_game != null) GameProcess.OpenFolder(_game.Root);
    }

    [RelayCommand]
    private void OpenLog()
    {
        if (_game == null) return;
        string log = Path.Combine(_game.BepInExDir, "LogOutput.log");
        GameProcess.OpenFolder(File.Exists(log) ? log : _game.BepInExDir);
    }

    [RelayCommand] private void DismissMessage() => Message = null;

    // ---------------------------------------------------------------- helpers

    private async Task RunAsync(Func<CancellationToken, Task> work, string done, bool launches = true)
    {
        Message = null;
        IsBusy = true;
        UpdateState();
        Progress = 0;
        bool ok = false;
        try
        {
            await work(CancellationToken.None);
            if (launches) await Task.Delay(1500); // leave "Starting…" up while Steam picks the launch up
            ok = true;
        }
        catch (Exception e)
        {
            LauncherLog.Error("action", e);
            Say(Capitalize(LauncherLog.Describe(e)) + ".", error: true);
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            Progress = 0;
            BuildModList();
            Problems.Clear();
            DescribeGame();
            DescribeModUpdates();
            await LoadImagesAsync();
            UpdateState();
            if (ok) Say(done);
        }
    }

    private bool EnsureClosed()
    {
        if (!GameProcess.IsRunning()) return true;
        Say("Close Over The Hill first. Windows doesn't let the launcher change mod files while the game is running.", error: true);
        return false;
    }

    private void UpdateState()
    {
        ProblemCount = Problems.Count;
        var blocking = Problems.FirstOrDefault(p => p.Blocks);
        BlockReason = blocking == null ? null : Problems.Count(p => p.Blocks) == 1
            ? $"{blocking.Title}, so mods can't start yet."
            : $"{Problems.Count(p => p.Blocks)} problems need fixing before you can play with mods.";
        CanPlay = !IsBusy && _game != null && _manifest != null && blocking == null;
        CanPlayWithoutMods = !IsBusy && _game != null;
        OnPropertyChanged(nameof(ListMissing));

        var on = AllMods().Where(m => m.IsPicked).Select(m => m.Name).ToList();
        Summary = on.Count switch
        {
            0 => "Salochi Core only",
            1 => $"With {on[0]}",
            2 => $"With {on[0]} and {on[1]}",
            _ => $"With {string.Join(", ", on.Take(on.Count - 1))} and {on[^1]}",
        };
        SteamMode = _game == null ? "" : DoorstopConfig.Read(_game.Root) switch
        {
            null => "without mods, until the mod loader is installed",
            true => "with mods",
            false => "without mods",
        };
    }

    private void Problem(string title, string body, bool blocks, string? actionLabel = null, IAsyncRelayCommand? action = null) =>
        Problems.Add(new ProblemViewModel(title, body, blocks, actionLabel, action));

    private void Say(string text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
    }

    private string? OurFolder() => _game != null ? Path.Combine(_game.PluginsDir, PluginScanner.LauncherFolder) : null;

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static ModStore Store() => new(LauncherSettings.DataDir, Http);

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SalochiLauncher/0.1");
        return http;
    }
}
