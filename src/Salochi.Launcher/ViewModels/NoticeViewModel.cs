using CommunityToolkit.Mvvm.Input;

namespace Salochi.Launcher.ViewModels;

public enum NoticeLevel { Info, Warning, Error }

/// <summary>A banner above the mod list: something the player should know, maybe with one action that fixes it.</summary>
public sealed class NoticeViewModel(NoticeLevel level, string title, string body, string? actionLabel = null, IAsyncRelayCommand? action = null) : ViewModelBase
{
    public NoticeLevel Level { get; } = level;
    public string Title { get; } = title;
    public string Body { get; } = body;
    public string? ActionLabel { get; } = actionLabel;
    public IAsyncRelayCommand? Action { get; } = action;
    public bool HasAction => Action != null;
    public bool IsError => Level == NoticeLevel.Error;
    public bool IsWarning => Level == NoticeLevel.Warning;
    public bool IsInfo => Level == NoticeLevel.Info;
}
