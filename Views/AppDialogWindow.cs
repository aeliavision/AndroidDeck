using System;
using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace VcfEditor.Views;

public class AppDialogWindow : Window
{
    public AppDialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, "Dialog.Window");
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += OnLoaded;
    }

    protected static void ApplyDialogStyle(FrameworkElement element, string styleKey)
        => element.SetResourceReference(StyleProperty, styleKey);

    protected static TextBlock CreateValidationSummary()
    {
        var summary = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        summary.SetResourceReference(TextElement.ForegroundProperty, "Brush.Error");
        AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Assertive);
        return summary;
    }

    protected static void SetInlineValidation(Control control, TextBlock? summary, string message)
    {
        if (summary is not null)
        {
            summary.Text = message;
            summary.Visibility = Visibility.Visible;
        }
        control.SetResourceReference(Control.BorderBrushProperty, "Brush.Error");
        AutomationProperties.SetHelpText(control, message);
        control.Focus();
    }

    protected static void ClearInlineValidation(Control control, TextBlock? summary)
    {
        if (summary is not null)
        {
            summary.Text = string.Empty;
            summary.Visibility = Visibility.Collapsed;
        }
        control.ClearValue(Control.BorderBrushProperty);
        AutomationProperties.SetHelpText(control, string.Empty);
    }

    protected static void SetThemeResource(FrameworkElement element, DependencyProperty property, string resourceKey)
        => element.SetResourceReference(property, resourceKey);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Owner is null && Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, this))
            Owner = owner;
        AutomationProperties.SetName(this, Title);
        NormalizeButtons(Content);
        FocusSafestControl(Content);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }

    private static void NormalizeButtons(object? node)
    {
        if (node is Button button && button.ReadLocalValue(StyleProperty) == DependencyProperty.UnsetValue)
        {
            var text = button.Content?.ToString() ?? string.Empty;
            var destructive = text.Contains("delete", StringComparison.OrdinalIgnoreCase)
                || text.Contains("replace", StringComparison.OrdinalIgnoreCase)
                || text.Contains("revoke", StringComparison.OrdinalIgnoreCase);
            ApplyDialogStyle(
                button,
                destructive ? "Dialog.DestructiveAction"
                    : button.IsDefault ? "Dialog.PrimaryAction"
                    : "Dialog.SecondaryAction");
        }

        if (node is not DependencyObject dependencyObject) return;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
            NormalizeButtons(child);
    }

    private static void FocusSafestControl(object? node)
    {
        if (FindFirst<TextBox>(node) is { } textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
            return;
        }
        if (FindFirst<PasswordBox>(node) is { } passwordBox)
        {
            passwordBox.Focus();
            return;
        }
        FindFirst<Button>(node, button => button.IsCancel)?.Focus();
    }

    private static T? FindFirst<T>(object? node, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (node is T candidate && (predicate is null || predicate(candidate)))
            return candidate;
        if (node is not DependencyObject dependencyObject) return null;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
        {
            var found = FindFirst<T>(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }
}
