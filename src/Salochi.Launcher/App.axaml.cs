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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(ManifestArg(desktop.Args ?? [])),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>"--manifest &lt;path&gt;" reads a local mod list instead of the published one (for testing builds).</summary>
    private static string? ManifestArg(string[] args)
    {
        int i = Array.IndexOf(args, "--manifest");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
