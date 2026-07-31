using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VcfEditor.Helpers
{
    /// <summary>
    /// Converts a bool to a GridLength.
    /// True  → GridLength(380) — show the preview panel at 380px
    /// False → GridLength(0)   — collapse the preview panel
    /// </summary>
    [ValueConversion(typeof(bool), typeof(GridLength))]
    public sealed class BoolToPreviewWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var show = value is bool b && b;
            return show ? new GridLength(380) : new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }
}
