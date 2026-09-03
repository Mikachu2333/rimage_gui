using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RimageGui.I18n;
using RimageGui.Models;

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

    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool flag) || !flag;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            !(value is bool flag) || !flag;
    }

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

    public sealed class FileStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is FileStatus status))
            {
                return string.Empty;
            }

            switch (status)
            {
                case FileStatus.Running: return Loc.I["StatusRunning"];
                case FileStatus.Done: return Loc.I["StatusDone"];
                case FileStatus.Failed: return Loc.I["StatusFailed"];
                case FileStatus.Skipped: return Loc.I["StatusSkipped"];
                default: return Loc.I["StatusPending"];
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

    /// <summary>
    /// Colours the status column. Resolved from the live application resources
    /// so the palette swap reaches these values too.
    /// </summary>
    public sealed class FileStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var key = "TextSecondary";
            if (value is FileStatus status)
            {
                switch (status)
                {
                    case FileStatus.Done:
                        key = "StatusDone";
                        break;
                    case FileStatus.Failed:
                        key = "StatusFailed";
                        break;
                    case FileStatus.Skipped:
                        key = "StatusMuted";
                        break;
                    case FileStatus.Running:
                        key = "AccentBackground";
                        break;
                }
            }

            return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }

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
                case OutputFormat format: return format.CliName() + " (" + format.Extension() + ")";
                case ResizeFilter filter: return filter.CliName();
                default: return value?.ToString() ?? string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
