using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using VcfEditor.Services;

namespace VcfEditor.Views;

public sealed class PasswordDialog : AppDialogWindow
{
    private readonly PasswordBox _password;
    private readonly PasswordBox? _confirm;
    private readonly TextBlock _validation;

    public string Password { get; private set; } = string.Empty;

    public PasswordDialog(string title, bool requireConfirm, IDialogService dialogService)
    {
        System.ArgumentNullException.ThrowIfNull(dialogService);
        Title = title;
        Width = 440;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var titleBlock = new TextBlock { Text = title };
        ApplyDialogStyle(titleBlock, "Dialog.Title");
        panel.Children.Add(titleBlock);
        var body = new TextBlock
        {
            Text = requireConfirm
                ? "Choose a password and confirm it. The password is not stored."
                : "Enter the password used to encrypt this backup."
        };
        ApplyDialogStyle(body, "Dialog.Body");
        panel.Children.Add(body);

        panel.Children.Add(new TextBlock { Text = "Password", Margin = new Thickness(0, 0, 0, 4) });
        _password = new PasswordBox { MinHeight = 38, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(_password, "Password");
        _password.PasswordChanged += (_, _) => ClearInlineValidation(_password, _validation);
        panel.Children.Add(_password);

        if (requireConfirm)
        {
            panel.Children.Add(new TextBlock { Text = "Confirm password", Margin = new Thickness(0, 0, 0, 4) });
            _confirm = new PasswordBox { MinHeight = 38, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetName(_confirm, "Confirm password");
            _confirm.PasswordChanged += (_, _) => ClearInlineValidation(_confirm, _validation);
            _confirm.KeyDown += (_, e) => { if (e.Key == Key.Enter && Accept()) e.Handled = true; };
            panel.Children.Add(_confirm);
        }

        _validation = CreateValidationSummary();
        panel.Children.Add(_validation);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        ok.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        panel.Children.Add(actions);
        Content = panel;
    }

    private bool Accept()
    {
        if (string.IsNullOrEmpty(_password.Password))
        {
            SetInlineValidation(_password, _validation, "Password cannot be empty.");
            return false;
        }
        if (_confirm is not null && _password.Password != _confirm.Password)
        {
            SetInlineValidation(_confirm, _validation, "Passwords do not match.");
            return false;
        }
        Password = _password.Password;
        DialogResult = true;
        return true;
    }
}
