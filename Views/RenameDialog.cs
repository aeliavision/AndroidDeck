using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace VcfEditor.Views;

public sealed class RenameDialog : AppDialogWindow
{
    private readonly TextBox _input;
    private readonly TextBlock _validation;

    public string NewName => _input.Text.Trim();

    public RenameDialog(string currentName)
    {
        Title = "Rename";
        Width = 420;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var title = new TextBlock { Text = "Rename item" };
        ApplyDialogStyle(title, "Dialog.Title");
        panel.Children.Add(title);
        var body = new TextBlock { Text = "Enter the new name." };
        ApplyDialogStyle(body, "Dialog.Body");
        panel.Children.Add(body);

        _input = new TextBox { Text = currentName ?? string.Empty, MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_input, "New item name");
        _input.TextChanged += (_, _) => ClearInlineValidation(_input, _validation);
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter && Accept()) e.Handled = true; };
        panel.Children.Add(_input);

        _validation = CreateValidationSummary();
        panel.Children.Add(_validation);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var rename = new Button { Content = "Rename", MinWidth = 100, IsDefault = true };
        rename.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(rename);
        panel.Children.Add(actions);
        Content = panel;
    }

    private bool Accept()
    {
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            SetInlineValidation(_input, _validation, "A new name is required.");
            return false;
        }
        DialogResult = true;
        return true;
    }
}
