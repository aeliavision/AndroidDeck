using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace VcfEditor.Views;

public sealed class TextInputDialog : AppDialogWindow
{
    private readonly TextBox _text;
    private readonly TextBlock _validation;

    public string Value { get; private set; } = string.Empty;

    public TextInputDialog(string title, string prompt, string initialValue = "")
    {
        Title = title;
        Width = 480;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var titleBlock = new TextBlock { Text = title };
        ApplyDialogStyle(titleBlock, "Dialog.Title");
        panel.Children.Add(titleBlock);
        var promptBlock = new TextBlock { Text = prompt };
        ApplyDialogStyle(promptBlock, "Dialog.Body");
        panel.Children.Add(promptBlock);

        _text = new TextBox { Text = initialValue, MinHeight = 38, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(_text, prompt);
        _text.TextChanged += (_, _) => ClearInlineValidation(_text, _validation);
        _text.KeyDown += (_, e) => { if (e.Key == Key.Enter && Accept()) e.Handled = true; };
        panel.Children.Add(_text);
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
        if (string.IsNullOrWhiteSpace(_text.Text))
        {
            SetInlineValidation(_text, _validation, "A value is required.");
            return false;
        }
        Value = _text.Text.Trim();
        DialogResult = true;
        return true;
    }
}
