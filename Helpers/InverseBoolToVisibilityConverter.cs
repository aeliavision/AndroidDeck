using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VcfEditor.Helpers
{
    /// <summary>Converts bool to Visibility: True → Collapsed, False → Visible.</summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v != Visibility.Visible;
    }
}
