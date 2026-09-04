using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RimageGui.Models;

namespace RimageGui.Converters
{
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
}
