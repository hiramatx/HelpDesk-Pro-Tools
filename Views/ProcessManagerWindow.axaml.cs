using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HelpDesk_Pro_Tools.Views;

public partial class ProcessManagerWindow : Window
{
    public ProcessManagerWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
