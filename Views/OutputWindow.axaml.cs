using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelpDesk_Pro_Tools.ViewModels;

namespace HelpDesk_Pro_Tools.Views;

public partial class OutputWindow : Window
{
    public OutputWindow()
    {
        InitializeComponent();

        // Keep the log scrolled to the newest line (pure view behaviour).
        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as OutputViewModel)?.Stop();
        base.OnClosed(e);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
