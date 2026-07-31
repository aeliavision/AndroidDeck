using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VcfEditor.Behaviors;

/// <summary>Opens a button's context menu from a normal left click.</summary>
public static class ButtonContextMenuBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ButtonContextMenuBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Button button) return;
        button.Click -= OnButtonClick;
        if (args.NewValue is true)
            button.Click += OnButtonClick;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } contextMenu } button) return;
        contextMenu.PlacementTarget = button;
        contextMenu.Placement = PlacementMode.Bottom;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }
}
