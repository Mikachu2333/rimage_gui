using System;
using System.Globalization;
using System.Windows.Data;
using RimageGui.Models;

namespace RimageGui.Converters
{
    /// <summary>Renders an enum with its rimage CLI spelling, which is what the
    /// dropdowns should show — users compare it against rimage's own docs.
    /// Output formats additionally carry the extension rimage writes, e.g.
    /// "mozjpeg (jpg)", so the resulting file names are no surprise.</summary>
    public sealed class CliNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case OutputFormat format:
                {
                    return $"{format.CliName()} ({format.Extension()})";
                }

                case ResizeFilter filter:
                {
                    return filter.CliName();
                }

                default:
                {
                    return value?.ToString() ?? string.Empty;
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
