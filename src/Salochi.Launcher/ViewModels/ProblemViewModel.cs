using CommunityToolkit.Mvvm.Input;

namespace Salochi.Launcher.ViewModels;

/// <summary>
/// A problem with the setup, listed on the Game page: what's wrong, what it means, and (when the launcher can fix it)
/// one button that does. <see cref="Blocks"/> problems keep Play greyed out until they're fixed.
/// </summary>
public sealed class ProblemViewModel(string title, string body, bool blocks, string? actionLabel = null, IAsyncRelayCommand? action = null) : ViewModelBase
{
    public string Title { get; } = title;
    public string Body { get; } = body;
    public bool Blocks { get; } = blocks;
    public string? ActionLabel { get; } = actionLabel;
    public IAsyncRelayCommand? Action { get; } = action;
    public bool HasAction => Action != null;
}
