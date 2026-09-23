using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HelpDesk_Pro_Tools.Views;

/// <summary>Small themed confirm / error dialog. Returns true from ShowDialog when confirmed.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public static MessageDialog CreateConfirm(string title, string message, string confirmText, bool danger)
    {
        var dialog = new MessageDialog { Title = title };
        dialog.HeadingText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        if (danger)
        {
            dialog.ConfirmButton.Classes.Remove("primary");
            dialog.ConfirmButton.Classes.Add("red");
        }
        return dialog;
    }

    public static MessageDialog CreateError(string title, string message)
    {
        var dialog = new MessageDialog { Title = title };
        dialog.HeadingText.Text = title;
        dialog.MessageText.Text = message;
        dialog.CancelButton.IsVisible = false;
        return dialog;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
