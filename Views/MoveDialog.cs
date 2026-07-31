using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace VcfEditor.Views;

public sealed class MoveDialog : AppDialogWindow
{
    private readonly TextBox _input;
    private readonly TextBlock _validation;

    public string DestinationPath => _input.Text.Trim();

    public MoveDialog(string defaultDestinationPath)
    {
        Title = "Move";
        Width = 520;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var title = new TextBlock { Text = "Move selected items" };
        ApplyDialogStyle(title, "Dialog.Title");
        panel.Children.Add(title);
        var body = new TextBlock { Text = "Enter the destination path on the phone." };
        ApplyDialogStyle(body, "Dialog.Body");
        panel.Children.Add(body);

        _input = new TextBox { Text = defaultDestinationPath ?? string.Empty, MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_input, "Destination path on phone");
        _input.TextChanged += (_, _) => ClearInlineValidation(_input, _validation);
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter && Accept()) e.Handled = true; };
        panel.Children.Add(_input);

        _validation = CreateValidationSummary();
        panel.Children.Add(_validation);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var move = new Button { Content = "Move", MinWidth = 100, IsDefault = true };
        move.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(move);
        panel.Children.Add(actions);
        Content = panel;
    }

    private bool Accept()
    {
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            SetInlineValidation(_input, _validation, "Destination path is required.");
            return false;
        }
        if (!_input.Text.TrimStart().StartsWith('/'))
        {
            SetInlineValidation(_input, _validation, "Use an absolute phone path beginning with '/'.");
            return false;
        }
        DialogResult = true;
        return true;
    }
}
