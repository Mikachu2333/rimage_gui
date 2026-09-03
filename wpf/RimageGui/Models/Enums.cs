using System;

namespace RimageGui.Models
{
    /// <summary>
    /// The encoders the GUI offers. Niche intermediate formats (qoi, ppm,
    /// farbfeld) are deliberately not offered even though rimage supports them.
    /// <see cref="CliName"/> is the literal command word; <see cref="Extension"/>
    /// must match what rimage actually writes, because the output path is
    /// predicted from it before the job starts.
    /// </summary>
    public enum OutputFormat
    {
        MozJpeg,
        Jpeg,
        OxiPng,
        Png,
        WebP,
        Avif,
        JpegXl
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
            OutputFormat.JpegXl
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
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>
        /// Whether the codec accepts <c>--quality</c>. The lossless encoders
        /// (oxipng, png, jpeg_xl) reject it, so the GUI disables the field.
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

        /// <summary>
        /// Whether the codec exposes a switchable lossless mode. rimage 0.13
        /// defines a <c>--lossless</c> flag only for WebP; the other lossless
        /// codecs are always lossless and carry no flag at all.
        /// </summary>
        public static bool SupportsLossless(this OutputFormat format)
        {
            return format == OutputFormat.WebP;
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
