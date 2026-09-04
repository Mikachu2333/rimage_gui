using System;
using System.Globalization;
using System.Windows.Data;
using RimageGui.I18n;
using RimageGui.Models;

namespace RimageGui.Converters
{
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
                case FileStatus.Running:
                {
                    return Loc.I["StatusRunning"];
                }

                case FileStatus.Done:
                {
                    return Loc.I["StatusDone"];
                }

                case FileStatus.Failed:
                {
                    return Loc.I["StatusFailed"];
                }

                case FileStatus.Skipped:
                {
                    return Loc.I["StatusSkipped"];
                }

                default:
                {
                    return Loc.I["StatusPending"];
                }
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
