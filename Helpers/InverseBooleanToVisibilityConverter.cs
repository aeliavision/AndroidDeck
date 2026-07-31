using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VcfEditor.Helpers
{
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Safe two-way support for bindings that accidentally call ConvertBack.
            // Visible means the source boolean should be false because this is an inverse converter.
            return value is Visibility visibility && visibility != Visibility.Visible;
        }
    }
}
