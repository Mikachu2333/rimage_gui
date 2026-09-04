using System;
using System.Collections.Generic;

namespace RimageGui.Models
{
    public static class FilterInfo
    {
        public static IReadOnlyList<ResizeFilter> All { get; } =
            new[]
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
                case ResizeFilter.Nearest:
                {
                    return "nearest";
                }

                case ResizeFilter.Box:
                {
                    return "box";
                }

                case ResizeFilter.Bilinear:
                {
                    return "bilinear";
                }

                case ResizeFilter.Hamming:
                {
                    return "hamming";
                }

                case ResizeFilter.CatmullRom:
                {
                    return "catmull-rom";
                }

                case ResizeFilter.Mitchell:
                {
                    return "mitchell";
                }

                case ResizeFilter.Lanczos3:
                {
                    return "lanczos3";
                }

                default: throw new ArgumentOutOfRangeException(nameof(filter));
            }
        }
    }
}
