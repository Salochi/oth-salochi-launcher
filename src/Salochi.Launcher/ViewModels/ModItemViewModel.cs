using CommunityToolkit.Mvvm.ComponentModel;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.ViewModels;

/// <summary>One row in the mod list.</summary>
public partial class ModItemViewModel : ViewModelBase
{
    public ModItemViewModel(ModEntry entry, bool picked, string status)
    {
        Entry = entry;
        IsPicked = entry.Required || picked;
        Status = status;
    }

    public ModEntry Entry { get; }
    public string Name => Entry.Name;
    public string Version => "v" + Entry.Version;
    public string Description => Entry.Description;
    public string? Credits => Entry.Credits;
    public bool HasCredits => !string.IsNullOrWhiteSpace(Entry.Credits);
    public bool CanToggle => !Entry.Required;
    public bool IsNetworked => Entry.Networked;
    public string LockedHint => Entry.Required ? "Always on: the other mods need it." : "";

    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    /// <summary>"Installed", "Update available", "Will download"…</summary>
    [ObservableProperty]
    public partial string Status { get; set; }

    /// <summary>Raised when the player ticks or unticks the row.</summary>
    public event Action? PickedChanged;

    partial void OnIsPickedChanged(bool value) => PickedChanged?.Invoke();
}
