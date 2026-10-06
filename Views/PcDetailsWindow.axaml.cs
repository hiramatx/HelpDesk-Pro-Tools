using Avalonia.Controls;

namespace HelpDesk_Pro_Tools.Views;

public partial class PcDetailsWindow : Window
{
    public PcDetailsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FitToScreen();
    }

    /// <summary>Fills the left half of the monitor it opened on (the main window's monitor).</summary>
    private void FitToScreen() =>
        WindowPlacement.SnapToHalf(this, Screens.ScreenFromWindow(this) ?? Screens.Primary, left: true);
}
