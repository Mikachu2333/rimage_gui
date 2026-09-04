using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RimageGui.Converters
{
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>Collapses when true instead of when false.</summary>
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;

            // A shared converter instance is used from many bindings, so the
            // inversion is also accepted per-binding via ConverterParameter.
            var invert = Invert ||
                         string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
