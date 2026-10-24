using System;
using Avalonia.Controls;

namespace HelpDesk_Pro_Tools.Views;

/// <summary>Startup login box (see App and LoginViewModel).</summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Straight to the password when the user name is already filled in.
        if (string.IsNullOrEmpty(UserInput.Text)) UserInput.Focus();
        else PasswordInput.Focus();
    }
}
