using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HelpDesk_Pro_Tools.Services;
using HelpDesk_Pro_Tools.ViewModels;

namespace HelpDesk_Pro_Tools.Views;

/// <summary>Modal login box. ShowDialog returns the <see cref="AppLogin"/>, or null when closed without logging in.</summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is LoginViewModel vm)
            vm.LoggedIn += login => Close(login);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Straight to the password when the user name is already filled in.
        if (string.IsNullOrEmpty(UserInput.Text)) UserInput.Focus();
        else PasswordInput.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(null);
}
