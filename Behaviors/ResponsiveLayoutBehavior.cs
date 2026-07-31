using System;
using System.Windows;
using VcfEditor.Models;

namespace VcfEditor.Behaviors;

/// <summary>
/// Publishes a semantic width class for a view without coupling view models to WPF size events.
/// Compact is below 900 px, medium is 900-1199 px, and expanded starts at 1200 px.
/// </summary>
public static class ResponsiveLayoutBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ResponsiveLayoutBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyKey ModePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Mode",
        typeof(ResponsiveLayoutMode),
        typeof(ResponsiveLayoutBehavior),
        new FrameworkPropertyMetadata(ResponsiveLayoutMode.Expanded, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ModeProperty = ModePropertyKey.DependencyProperty;

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static ResponsiveLayoutMode GetMode(DependencyObject element) =>
        (ResponsiveLayoutMode)element.GetValue(ModeProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if ((bool)args.NewValue)
        {
            element.Loaded += OnElementSizeChanged;
            element.SizeChanged += OnElementSizeChanged;
            UpdateMode(element);
        }
        else
        {
            element.Loaded -= OnElementSizeChanged;
            element.SizeChanged -= OnElementSizeChanged;
        }
    }

    private static void OnElementSizeChanged(object sender, EventArgs args)
    {
        if (sender is FrameworkElement element)
            UpdateMode(element);
    }

    internal static ResponsiveLayoutMode ResolveMode(double width) => width switch
    {
        > 0 and < 900 => ResponsiveLayoutMode.Compact,
        >= 900 and < 1200 => ResponsiveLayoutMode.Medium,
        _ => ResponsiveLayoutMode.Expanded
    };

    private static void UpdateMode(FrameworkElement element)
    {
        element.SetValue(ModePropertyKey, ResolveMode(element.ActualWidth));
    }
}
