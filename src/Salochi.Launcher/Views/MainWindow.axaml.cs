using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Salochi.Launcher.ViewModels;

namespace Salochi.Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is not MainViewModel vm) return;
        vm.PickFolder = async title =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        };
        await vm.RefreshAsync();
        await vm.AfterStartAsync();
    }
}
