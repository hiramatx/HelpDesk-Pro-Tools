using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HelpDesk_Pro_Tools.Views;

public partial class AddRemoteDesktopWindow : Window
{
    public AddRemoteDesktopWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UserInput.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
