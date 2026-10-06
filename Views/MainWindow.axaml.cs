using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelpDesk_Pro_Tools.ViewModels;

namespace HelpDesk_Pro_Tools.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Enter in the PC box opens PC Details - but not when Enter is just picking
        // an entry from the open history drop-down. Tunnel so we see it before the ComboBox.
        PcComboBox.AddHandler(KeyDownEvent, PcComboBox_KeyDown, RoutingStrategies.Tunnel);

        // Fill the right half of the monitor: once before the first show (avoids a visible jump),
        // then exactly once the real frame size is known.
        WindowPlacement.SnapToHalf(this, Screens.Primary, left: false);
        Opened += (_, _) => WindowPlacement.SnapToHalf(this, Screens.ScreenFromWindow(this) ?? Screens.Primary, left: false);
    }

    private void PcComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PcComboBox.IsDropDownOpen) return;

        if (DataContext is MainViewModel vm && vm.GetPcDetailsCommand.CanExecute(null))
        {
            vm.GetPcDetailsCommand.Execute(null);
            e.Handled = true;
        }
    }
}
