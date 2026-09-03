using System;

namespace RimageGui.Models
{
    /// <summary>
    /// rimage encoder sub-command. <see cref="CliName"/> is the literal command
    /// word; <see cref="Extension"/> must match what rimage actually writes,
    /// because the output path is predicted from it before the job starts.
    /// </summary>
    public enum OutputFormat
    {
        MozJpeg,
        Jpeg,
        OxiPng,
        Png,
        WebP,
        Avif,
        JpegXl,
        Qoi
    }

    public enum ResizeFilter
    {
        Nearest,
        Box,
        Bilinear,
        Hamming,
        CatmullRom,
        Mitchell,
        Lanczos3
    }

    public enum OutputMode
    {
        OriginalDir,
        SelectedDir
    }

    public enum OriginalPolicy
    {
        Keep,
        Backup,
        DeleteAfterVerifiedSuccess
    }

    public enum ResizeMode
    {
        None,
        Classic,
        Bounds
    }

    /// <summary>Which edge a size limit is measured against.</summary>
    public enum BoundEdge
    {
        Longest,
        Shortest
    }

    /// <summary>
    /// rimage can only constrain one direction per invocation, so a limit is
    /// either an upper bound (shrink only) or a lower bound (enlarge only).
    /// </summary>
    public enum BoundDirection
    {
        Maximum,
        Minimum
    }

    public static class FormatInfo
    {
        public static readonly OutputFormat[] All =
        {
            OutputFormat.MozJpeg,
            OutputFormat.Jpeg,
            OutputFormat.OxiPng,
            OutputFormat.Png,
            OutputFormat.WebP,
            OutputFormat.Avif,
            OutputFormat.JpegXl,
            OutputFormat.Qoi
        };

        public static string CliName(this OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.MozJpeg: return "mozjpeg";
                case OutputFormat.Jpeg: return "jpeg";
                case OutputFormat.OxiPng: return "oxipng";
                case OutputFormat.Png: return "png";
                case OutputFormat.WebP: return "webp";
                case OutputFormat.Avif: return "avif";
                case OutputFormat.JpegXl: return "jpeg_xl";
                case OutputFormat.Qoi: return "qoi";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        public static string Extension(this OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.MozJpeg:
                case OutputFormat.Jpeg: return "jpg";
                case OutputFormat.OxiPng:
                case OutputFormat.Png: return "png";
                case OutputFormat.WebP: return "webp";
                case OutputFormat.Avif: return "avif";
                case OutputFormat.JpegXl: return "jxl";
                case OutputFormat.Qoi: return "qoi";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>
        /// Whether the codec accepts <c>--quality</c>. The lossless encoders
        /// (oxipng, png, jpeg_xl, qoi) reject it, so the GUI disables the field.
        /// </summary>
        public static bool SupportsQuality(this OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.MozJpeg:
                case OutputFormat.Jpeg:
                case OutputFormat.WebP:
                case OutputFormat.Avif: return true;
                default: return false;
            }
        }
    }

    public static class FilterInfo
    {
        public static readonly ResizeFilter[] All =
        {
            ResizeFilter.Nearest,
            ResizeFilter.Box,
            ResizeFilter.Bilinear,
            ResizeFilter.Hamming,
            ResizeFilter.CatmullRom,
            ResizeFilter.Mitchell,
            ResizeFilter.Lanczos3
        };

        public static string CliName(this ResizeFilter filter)
        {
            switch (filter)
            {
                case ResizeFilter.Nearest: return "nearest";
                case ResizeFilter.Box: return "box";
                case ResizeFilter.Bilinear: return "bilinear";
                case ResizeFilter.Hamming: return "hamming";
                case ResizeFilter.CatmullRom: return "catmull-rom";
                case ResizeFilter.Mitchell: return "mitchell";
                case ResizeFilter.Lanczos3: return "lanczos3";
                default: throw new ArgumentOutOfRangeException(nameof(filter));
            }
        }
    }
}
