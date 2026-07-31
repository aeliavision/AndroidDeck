using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace VcfEditor.Views;

public sealed class NewFolderDialog : AppDialogWindow
{
    private readonly TextBox _input;
    private readonly TextBlock _validation;

    public string FolderName => _input.Text.Trim();

    public NewFolderDialog()
    {
        Title = "New folder";
        Width = 420;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var title = new TextBlock { Text = "Create a folder" };
        ApplyDialogStyle(title, "Dialog.Title");
        panel.Children.Add(title);

        var body = new TextBlock { Text = "Enter a folder name." };
        ApplyDialogStyle(body, "Dialog.Body");
        panel.Children.Add(body);

        _input = new TextBox { MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_input, "Folder name");
        _input.TextChanged += (_, _) => ClearInlineValidation(_input, _validation);
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Accept()) e.Handled = true;
        };
        panel.Children.Add(_input);

        _validation = CreateValidationSummary();
        panel.Children.Add(_validation);
        panel.Children.Add(CreateActions("Create", Accept));
        Content = panel;
    }

    private bool Accept()
    {
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            SetInlineValidation(_input, _validation, "Folder name is required.");
            return false;
        }
        DialogResult = true;
        return true;
    }

    private static StackPanel CreateActions(string primaryText, System.Func<bool> accept)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var primary = new Button { Content = primaryText, MinWidth = 100, IsDefault = true };
        primary.Click += (_, _) => accept();
        actions.Children.Add(cancel);
        actions.Children.Add(primary);
        return actions;
    }
}
