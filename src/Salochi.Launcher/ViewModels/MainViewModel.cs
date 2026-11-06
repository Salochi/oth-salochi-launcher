using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Salochi.Launcher.Core;
using Salochi.Launcher.Core.Game;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>The published mod list. Each release attaches manifest.json; "latest" always points at the newest.</summary>
    public const string ManifestUrl = "https://github.com/Salochi/oth-salochi-launcher/releases/latest/download/manifest.json";

    private static readonly HttpClient Http = CreateHttp();

    private readonly LauncherSettings _settings;
    private readonly string? _manifestArg;
    private Manifest? _manifest;
    private GameLocation? _game;
    private IReadOnlyList<PluginIssue> _issues = [];

    public MainViewModel() : this(null) { }

    public MainViewModel(string? manifestArg)
    {
        _manifestArg = manifestArg;
        _settings = LauncherSettings.Load();
    }

    /// <summary>Set by the window: asks the player for a folder.</summary>
    public Func<string, Task<string?>>? PickFolder { get; set; }

    public ObservableCollection<NoticeViewModel> Notices { get; } = [];
    public ObservableCollection<ModItemViewModel> CoreMods { get; } = [];
    public ObservableCollection<ModItemViewModel> FeatureMods { get; } = [];
    public ObservableCollection<ModItemViewModel> CarMods { get; } = [];

    [ObservableProperty] public partial string GameSummary { get; set; } = "Looking for Over The Hill…";
    [ObservableProperty] public partial string LaunchMode { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; } = true;
    [ObservableProperty] public partial bool HasMods { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand), nameof(PlayVanillaCommand))]
    public partial bool CanPlay { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayVanillaCommand))]
    public partial bool CanPlayVanilla { get; set; }

    public bool HasFeatures => FeatureMods.Count > 0;
    public bool HasCars => CarMods.Count > 0;

    // ---------------------------------------------------------------- loading

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        Notices.Clear();
        StatusText = "Checking the game and the mod list…";
        try
        {
            _game = LocateGame();
            if (_game == null)
            {
                GameSummary = "Over The Hill wasn't found";
                Notice(NoticeLevel.Error, "Over The Hill wasn't found",
                    "The launcher looks through your Steam libraries. If the game is installed somewhere else, pick its folder.",
                    "Pick game folder…", ChooseGameFolderCommand);
            }
            else
            {
                GameSummary = _game.BuildId != null ? $"{_game.Root}  ·  build {_game.BuildId}" : _game.Root;
            }

            _manifest = await LoadManifestAsync();
            BuildModList();
            if (_game != null) ScanGame();
        }
        catch (Exception e)
        {
            Notice(NoticeLevel.Error, "Something went wrong", e.Message);
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

    private async Task<Manifest?> LoadManifestAsync()
    {
        string? local = _manifestArg ?? _settings.ManifestOverride;
        try
        {
            if (local != null)
            {
                string full = Path.GetFullPath(local);
                var m = Manifest.Parse(await File.ReadAllTextAsync(full), new Uri(full));
                Notice(NoticeLevel.Info, "Using a local mod list", full);
                return m;
            }
            string json = await Http.GetStringAsync(ManifestUrl);
            return Manifest.Parse(json, new Uri(ManifestUrl));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Notice(NoticeLevel.Warning, "Couldn't reach GitHub",
                "The mod list couldn't be downloaded, so nothing can be installed or updated right now. Play vanilla still works.",
                "Try again", RefreshCommand);
        }
        catch (Exception e) when (e is ManifestException or IOException)
        {
            Notice(NoticeLevel.Error, "The mod list couldn't be read", e.Message);
        }
        return null;
    }

    private void BuildModList()
    {
        CoreMods.Clear();
        FeatureMods.Clear();
        CarMods.Clear();
        HasMods = _manifest is { Mods.Count: > 0 };
        if (_manifest == null) return;

        var store = Store();
        string? target = _game != null ? Path.Combine(_game.PluginsDir, PluginScanner.LauncherFolder) : null;
        foreach (var mod in _manifest.Mods)
        {
            bool picked = _settings.Picked == null || _settings.Picked.Contains(mod.Id, StringComparer.OrdinalIgnoreCase);
            var item = new ModItemViewModel(mod, picked, StatusOf(mod, store, target));
            item.PickedChanged += () => OnPicked(item);
            (mod.Kind switch { ModKind.Core => CoreMods, ModKind.Car => CarMods, _ => FeatureMods }).Add(item);
        }
        OnPropertyChanged(nameof(HasFeatures));
        OnPropertyChanged(nameof(HasCars));
    }

    private static string StatusOf(ModEntry mod, ModStore store, string? target)
    {
        string? installed = target != null ? Path.Combine(target, mod.File) : null;
        if (installed != null && File.Exists(installed))
            return PluginSync.Plan(target!, [mod]).Install.Count == 0 ? "Installed" : "Update ready";
        return store.Has(mod) ? "Downloaded" : "Will download";
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
        _settings.Picked = all.Where(m => m.IsPicked && !m.Entry.Required).Select(m => m.Entry.Id).ToList();
        _settings.Save();
    }

    private IEnumerable<ModItemViewModel> AllMods() => CoreMods.Concat(FeatureMods).Concat(CarMods);

    private void ScanGame()
    {
        var game = _game!;
        if (_manifest is { TestedGameBuilds.Count: > 0 } && game.BuildId != null && !_manifest.TestedGameBuilds.Contains(game.BuildId))
            Notice(NoticeLevel.Warning, "Over The Hill has updated",
                $"These mods were tested on an earlier version of the game (you have build {game.BuildId}). They may not work until they're updated. If the game crashes, use Play vanilla.");

        if (!BepInExInstall.IsInstalled(game))
        {
            Notice(NoticeLevel.Info, "The mod loader will be installed",
                $"Mods run on BepInEx {_manifest?.BepInEx?.Version ?? ""}. The launcher installs it into the game folder the first time you press Play. Play vanilla turns it off again.");
            return;
        }

        var ourHashes = new HashSet<string>(_manifest?.Mods.Select(m => m.Sha256) ?? [], StringComparer.OrdinalIgnoreCase);
        var ourFiles = new HashSet<string>(_manifest?.Mods.Where(m => m.Kind != ModKind.Core).Select(m => m.File) ?? [], StringComparer.OrdinalIgnoreCase);
        _issues = PluginScanner.Issues(PluginScanner.Scan(game.PluginsDir, ourHashes, ourFiles));
        foreach (var issue in _issues)
        {
            string files = string.Join(", ", issue.Files.Select(f => f.RelativePath));
            switch (issue.Kind)
            {
                case PluginIssueKind.ForeignCore:
                    Notice(NoticeLevel.Error, "The original OverTheHillPlus is installed",
                        $"Salochi mods run on their own version of the OverTheHillPlus Core. With both installed, only one loads, and the game may crash or lose features. The launcher can move the original into a backup folder. Nothing is deleted. ({files})",
                        "Move it aside", MoveAsideCommand);
                    break;
                case PluginIssueKind.LooseSalochiCopies:
                    Notice(NoticeLevel.Error, "Old copies of Salochi mods found",
                        $"These would load next to the launcher's copies: {files}. The launcher can move them into a backup folder.",
                        "Move them aside", MoveAsideCommand);
                    break;
                case PluginIssueKind.OverTheHillPlusMods:
                    Notice(NoticeLevel.Warning, "Other OTH mods are installed",
                        $"The launcher didn't install these and leaves them alone: {files}. Mods made for the original OverTheHillPlus may not work with Salochi Core.");
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- actions

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (_game == null || _manifest == null) return;
        if (!EnsureClosed()) return;
        await RunAsync(async ct =>
        {
            var game = _game;
            if (!BepInExInstall.IsInstalled(game))
            {
                StatusText = "Installing the mod loader…";
                await BepInExInstall.InstallAsync(game, _manifest, LauncherSettings.DataDir, Http, new Progress<double>(p => Progress = p), ct);
            }

            var picked = AllMods().Where(m => m.IsPicked).Select(m => m.Entry.Id);
            var mods = PluginSync.Resolve(_manifest, picked);
            var store = Store();
            for (int i = 0; i < mods.Count; i++)
            {
                if (store.Has(mods[i])) continue;
                StatusText = $"Downloading {mods[i].Name}…";
                int n = i;
                await store.EnsureAsync(_manifest, mods[i], new Progress<double>(p => Progress = (n + p) / mods.Count), ct);
            }

            StatusText = "Installing mods…";
            string target = Path.Combine(game.PluginsDir, PluginScanner.LauncherFolder);
            PluginSync.Apply(target, PluginSync.Plan(target, mods), store.PathOf);
            DoorstopConfig.Write(game.Root, true);

            StatusText = "Starting Over The Hill…";
            GameProcess.LaunchThroughSteam();
        });
    }

    [RelayCommand(CanExecute = nameof(CanPlayVanilla))]
    private async Task PlayVanillaAsync()
    {
        if (_game == null) return;
        if (!EnsureClosed()) return;
        await RunAsync(ct =>
        {
            if (DoorstopConfig.Read(_game.Root) != null) DoorstopConfig.Write(_game.Root, false);
            StatusText = "Starting Over The Hill without mods…";
            GameProcess.LaunchThroughSteam();
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private async Task MoveAsideAsync()
    {
        if (_game == null) return;
        if (!EnsureClosed()) return;
        var files = _issues.Where(i => PluginScanner.Blocks(i.Kind)).SelectMany(i => i.Files).ToList();
        string backup = PluginScanner.MoveAside(_game.BepInExDir, files, DateTime.Now);
        await RefreshAsync();
        Notice(NoticeLevel.Info, "Moved aside", $"{files.Count} file(s) are in {backup}. Move them back into BepInEx\\plugins to undo.");
    }

    [RelayCommand]
    private async Task ChooseGameFolderAsync()
    {
        if (PickFolder == null) return;
        string? dir = await PickFolder("Pick the Over The Hill folder");
        if (dir == null) return;
        if (!File.Exists(Path.Combine(dir, GameLocation.ExeName)))
        {
            Notice(NoticeLevel.Error, "That isn't the game folder", $"There's no \"{GameLocation.ExeName}\" in {dir}.");
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

    // ---------------------------------------------------------------- helpers

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        IsBusy = true;
        UpdateState();
        Progress = 0;
        try
        {
            await work(CancellationToken.None);
            await Task.Delay(1500); // leave "Starting…" up while Steam picks the launch up
        }
        catch (Exception e)
        {
            Notice(NoticeLevel.Error, "That didn't work", e.Message);
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            Progress = 0;
            BuildModList();
            UpdateState();
        }
    }

    private bool EnsureClosed()
    {
        if (!GameProcess.IsRunning()) return true;
        Notice(NoticeLevel.Error, "Over The Hill is running", "Close the game first: Windows doesn't let the launcher change mod files while the game has them open.");
        return false;
    }

    private void UpdateState()
    {
        bool blocked = _issues.Any(i => PluginScanner.Blocks(i.Kind));
        CanPlay = !IsBusy && _game != null && _manifest != null && !blocked;
        CanPlayVanilla = !IsBusy && _game != null;
        LaunchMode = _game == null ? "" : DoorstopConfig.Read(_game.Root) switch
        {
            null => "Mod loader not installed",
            true => "Steam launches modded",
            false => "Steam launches vanilla",
        };
    }

    private void Notice(NoticeLevel level, string title, string body, string? actionLabel = null, IAsyncRelayCommand? action = null) =>
        Notices.Add(new NoticeViewModel(level, title, body, actionLabel, action));

    private static ModStore Store() => new(LauncherSettings.DataDir, Http);

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SalochiLauncher/0.1");
        return http;
    }
}
