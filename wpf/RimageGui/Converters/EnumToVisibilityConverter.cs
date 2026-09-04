using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimageGui.Converters
{
    /// <summary>Matches an enum value and yields Visible only on a match.</summary>
    public sealed class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return Visibility.Collapsed;
            }

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
