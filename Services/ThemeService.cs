using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Windows;
using Microsoft.Win32;
using VcfEditor.Models;

namespace VcfEditor.Services;

public sealed class ThemeService : IThemeService
{
    internal const string LightPalettePath = "Themes/Generated.Colors.Light.xaml";
    internal const string DarkPalettePath = "Themes/Generated.Colors.Dark.xaml";

    private readonly Application _application;

    public ThemeService(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;

    public void Apply(AppTheme theme)
    {
        var resolvedTheme = ResolveTheme(theme);
        var palettePath = resolvedTheme == AppTheme.Dark ? DarkPalettePath : LightPalettePath;
        var paletteUri = new Uri(palettePath, UriKind.Relative);
        var dictionaries = _application.Resources.MergedDictionaries;
        var palette = dictionaries.FirstOrDefault(dictionary => IsGeneratedPalette(dictionary.Source));

        if (palette is null)
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = paletteUri });
        }
        else if (palette.Source != paletteUri)
        {
            palette.Source = paletteUri;
        }

        CurrentTheme = resolvedTheme;
    }

    private static AppTheme ResolveTheme(AppTheme requestedTheme)
    {
        if (SystemParameters.HighContrast || requestedTheme == AppTheme.HighContrast)
            return AppTheme.HighContrast;

        if (requestedTheme != AppTheme.System)
            return requestedTheme;

        return SystemUsesDarkTheme() ? AppTheme.Dark : AppTheme.Light;
    }

    private static bool SystemUsesDarkTheme()
    {
        const string personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(personalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsGeneratedPalette(Uri? source)
    {
        var original = source?.OriginalString;
        return string.Equals(original, LightPalettePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(original, DarkPalettePath, StringComparison.OrdinalIgnoreCase);
    }
}
