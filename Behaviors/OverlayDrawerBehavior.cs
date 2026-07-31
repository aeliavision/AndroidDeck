using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VcfEditor.Behaviors;

public static class OverlayDrawerBehavior
{
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(180));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
        "IsOpen",
        typeof(bool),
        typeof(OverlayDrawerBehavior),
        new PropertyMetadata(false, OnDrawerPropertyChanged));

    public static readonly DependencyProperty DrawerProperty = DependencyProperty.RegisterAttached(
        "Drawer",
        typeof(FrameworkElement),
        typeof(OverlayDrawerBehavior),
        new PropertyMetadata(null, OnDrawerPropertyChanged));

    public static void SetIsOpen(DependencyObject element, bool value)
        => element.SetValue(IsOpenProperty, value);

    public static bool GetIsOpen(DependencyObject element)
        => (bool)element.GetValue(IsOpenProperty);

    public static void SetDrawer(DependencyObject element, FrameworkElement value)
        => element.SetValue(DrawerProperty, value);

    public static FrameworkElement? GetDrawer(DependencyObject element)
        => element.GetValue(DrawerProperty) as FrameworkElement;

    private static void OnDrawerPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is FrameworkElement overlay)
            ApplyState(overlay, GetIsOpen(overlay));
    }

    private static void ApplyState(FrameworkElement overlay, bool isOpen)
    {
        var drawer = GetDrawer(overlay);
        if (drawer is null)
            return;

        var transform = drawer.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            drawer.RenderTransform = transform;
        }

        var closedOffset = -(drawer.ActualWidth > 0 ? drawer.ActualWidth : 296);
        var animate = SystemParameters.ClientAreaAnimation;

        var currentOpacity = overlay.Opacity;
        var currentOffset = transform.X;
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        overlay.Opacity = currentOpacity;
        transform.X = currentOffset;

        if (isOpen)
        {
            overlay.Visibility = Visibility.Visible;
            overlay.IsHitTestVisible = true;

            if (!animate)
            {
                overlay.Opacity = 1;
                transform.X = 0;
                return;
            }

            overlay.Opacity = 0;
            transform.X = closedOffset;
            overlay.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, AnimationDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(closedOffset, 0, AnimationDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            return;
        }

        overlay.IsHitTestVisible = false;
        if (!animate || overlay.Visibility != Visibility.Visible)
        {
            overlay.Opacity = 0;
            transform.X = closedOffset;
            overlay.Visibility = Visibility.Collapsed;
            return;
        }

        var opacityAnimation = new DoubleAnimation(overlay.Opacity, 0, AnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        opacityAnimation.Completed += (_, _) =>
        {
            if (GetIsOpen(overlay)) return;

            overlay.BeginAnimation(UIElement.OpacityProperty, null);
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            overlay.Opacity = 0;
            transform.X = closedOffset;
            overlay.Visibility = Visibility.Collapsed;
        };
        overlay.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(transform.X, closedOffset, AnimationDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            });
    }
}
