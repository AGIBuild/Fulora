using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class ConsoleView : UserControl
{
    public ConsoleView()
    {
        InitializeComponent();
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (string.IsNullOrEmpty(vm.SharedLog)) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is not { } clipboard) return;

        await clipboard.SetTextAsync(vm.SharedLog);
    }
}
