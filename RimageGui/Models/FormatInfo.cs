using System;
using System.Collections.Generic;

namespace RimageGui.Models
{
    public static class FormatInfo
    {
        public static IReadOnlyList<OutputFormat> All { get; } =
            new[]
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
                case OutputFormat.MozJpeg:
                {
                    return "mozjpeg";
                }

                case OutputFormat.Jpeg:
                {
                    return "jpeg";
                }

                case OutputFormat.OxiPng:
                {
                    return "oxipng";
                }

                case OutputFormat.Png:
                {
                    return "png";
                }

                case OutputFormat.WebP:
                {
                    return "webp";
                }

                case OutputFormat.Avif:
                {
                    return "avif";
                }

                case OutputFormat.JpegXl:
                {
                    return "jpeg_xl";
                }

                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        public static string Extension(this OutputFormat format)
        {
            switch (format)
            {
                case OutputFormat.MozJpeg:
                case OutputFormat.Jpeg:
                {
                    return "jpg";
                }

                case OutputFormat.OxiPng:
                case OutputFormat.Png:
                {
                    return "png";
                }

                case OutputFormat.WebP:
                {
                    return "webp";
                }

                case OutputFormat.Avif:
                {
                    return "avif";
                }

                case OutputFormat.JpegXl:
                {
                    return "jxl";
                }

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
                case OutputFormat.Avif:
                {
                    return true;
                }

                default:
                {
                    return false;
                }
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
}
