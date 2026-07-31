using System;

namespace VcfEditor.Navigation;

public static class ShellLayoutPolicy
{
    public const double ExpandedMinimumWidth = 1200;
    public const double CompactMinimumWidth = 900;

    public static ShellLayoutMode GetDefaultMode(double windowWidth)
    {
        ValidateWidth(windowWidth);

        if (windowWidth >= ExpandedMinimumWidth)
            return ShellLayoutMode.Expanded;

        return windowWidth >= CompactMinimumWidth
            ? ShellLayoutMode.Compact
            : ShellLayoutMode.Overlay;
    }

    public static ShellLayoutMode GetEffectiveMode(
        double windowWidth,
        ShellLayoutMode preferredDesktopMode)
    {
        ValidateWidth(windowWidth);
        if (preferredDesktopMode == ShellLayoutMode.Overlay)
            throw new ArgumentOutOfRangeException(nameof(preferredDesktopMode));

        return GetDefaultMode(windowWidth) switch
        {
            ShellLayoutMode.Overlay => ShellLayoutMode.Overlay,
            ShellLayoutMode.Compact => ShellLayoutMode.Compact,
            _ => preferredDesktopMode
        };
    }

    private static void ValidateWidth(double windowWidth)
    {
        if (!double.IsFinite(windowWidth))
            throw new ArgumentOutOfRangeException(nameof(windowWidth), windowWidth, "Window width must be finite.");
        ArgumentOutOfRangeException.ThrowIfNegative(windowWidth);
    }
}
