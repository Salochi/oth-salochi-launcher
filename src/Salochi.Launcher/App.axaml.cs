using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Salochi.Launcher.ViewModels;
using Salochi.Launcher.Views;

namespace Salochi.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string[] args = desktop.Args ?? [];
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(Arg(args, "--manifest"))
                {
                    Args = args,
                    UpdatedFrom = Arg(args, "--updated-from"),
                    UpdateFailed = Arg(args, "--update-failed"),
                    NoLaunch = Array.IndexOf(args, "--no-launch") >= 0,
                    Page = Enum.TryParse<Page>(Arg(args, "--page"), true, out var page) ? page : Page.Cars,
                    Shutdown = () => desktop.Shutdown(),
                },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>"--manifest &lt;path&gt;" reads a local mod list instead of the published one (for testing builds);
    /// "--page cars|features|game" opens on that page; "--no-launch" does everything but start the game;
    /// "--updated-from &lt;version&gt;" is added by the launcher when it restarts after updating itself.</summary>
    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
