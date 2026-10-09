using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelpDesk_Pro_Tools.Models;
using HelpDesk_Pro_Tools.ViewModels;

namespace HelpDesk_Pro_Tools.Views;

public partial class RemoveTempAdminWindow : Window
{
    public RemoveTempAdminWindow()
    {
        InitializeComponent();
    }

    private void EntryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is RemoveTempAdminViewModel vm)
            vm.SetSelection(EntryList.SelectedItems?.OfType<TempAdminEntry>() ?? Enumerable.Empty<TempAdminEntry>());
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
