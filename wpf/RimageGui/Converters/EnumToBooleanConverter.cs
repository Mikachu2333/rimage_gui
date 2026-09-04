using System;
using System.Globalization;
using System.Windows.Data;

namespace RimageGui.Converters
{
    /// <summary>
    /// Binds a group of segmented buttons or radio buttons to a single enum
    /// property. <c>ConverterParameter</c> names the enum member the element
    /// represents.
    /// </summary>
    public sealed class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
            {
                return false;
            }

            return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Only the button being checked should write; the one being unchecked
            // fires too, and letting it through would clobber the new selection.
            if (!(value is bool isChecked) || !isChecked || parameter == null)
            {
                return Binding.DoNothing;
            }

            try
            {
                return Enum.Parse(targetType, parameter.ToString());
            }
            catch (Exception)
            {
                return Binding.DoNothing;
            }
        }
    }
}
