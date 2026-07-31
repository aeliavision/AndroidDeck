using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace VcfEditor.Views;

public enum AppMessageKind
{
    Information,
    Warning,
    Error,
    Confirmation
}

/// <summary>A themed, accessible replacement for native message boxes.</summary>
public sealed class AppMessageDialog : AppDialogWindow
{
    public AppMessageDialog(string title, string message, AppMessageKind kind, bool showConfirmationActions = false)
    {
        Title = title;
        Width = 500;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var titleBlock = new TextBlock { Text = title };
        ApplyDialogStyle(titleBlock, "Dialog.Title");
        panel.Children.Add(titleBlock);

        var banner = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 18) };
        banner.SetResourceReference(StyleProperty, kind switch
        {
            AppMessageKind.Warning => "StatusBanner.Warning",
            AppMessageKind.Error => "StatusBanner.Error",
            _ => "StatusBanner"
        });
        var messageBlock = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(messageBlock, message);
        banner.Child = messageBlock;
        panel.Children.Add(banner);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (showConfirmationActions)
        {
            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 90,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true,
                IsDefault = true
            };
            var confirm = new Button { Content = "Confirm", MinWidth = 100 };
            ApplyDialogStyle(confirm, "Dialog.PrimaryAction");
            confirm.IsDefault = false;
            confirm.Click += (_, _) => DialogResult = true;
            actions.Children.Add(cancel);
            actions.Children.Add(confirm);
        }
        else
        {
            var close = new Button { Content = "Close", MinWidth = 90, IsCancel = true, IsDefault = true };
            actions.Children.Add(close);
        }

        panel.Children.Add(actions);
        Content = panel;
    }
}
