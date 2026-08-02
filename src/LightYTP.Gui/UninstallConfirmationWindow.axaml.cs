using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LightYTP.Gui;

public sealed partial class UninstallConfirmationWindow : Window
{
    public UninstallConfirmationWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnUninstallClick(object? sender, RoutedEventArgs e) => Close(true);
}
