using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Salochi.Launcher.Core.Mods;

namespace Salochi.Launcher.ViewModels;

/// <summary>One mod the player can switch on or off: a car tile or a feature row.</summary>
public partial class ModItemViewModel : ViewModelBase
{
    public ModItemViewModel(ModEntry entry, bool picked, string? status)
    {
        Entry = entry;
        IsPicked = entry.Required || picked;
        Status = status;
    }

    public ModEntry Entry { get; }
    public string Name => Entry.Name;
    public string Version => "Version " + Entry.Version;
    public string Description => Entry.Description;
    public string? Credits => Entry.Credits;
    public bool HasCredits => !string.IsNullOrWhiteSpace(Entry.Credits);

    [ObservableProperty] public partial bool IsPicked { get; set; }

    /// <summary>A note when the game folder doesn't match the pick yet ("Downloads when you play", "Update ready"),
    /// null when there's nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    public bool HasStatus => Status != null;

    /// <summary>The car's garage silhouette; null until it's loaded, or when the mod has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image != null;

    public event Action? PickedChanged;

    partial void OnIsPickedChanged(bool value) => PickedChanged?.Invoke();
}
