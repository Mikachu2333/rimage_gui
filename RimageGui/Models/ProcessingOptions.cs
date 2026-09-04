namespace RimageGui.Models
{
    /// <summary>
    /// A fully resolved, validated-ready description of one conversion run.
    /// The GUI edits its own view-model state and materialises this snapshot on
    /// Start, so later edits cannot mutate a job that is already running.
    /// </summary>
    public sealed class ProcessingOptions
    {
        /// <summary>
        /// Single source for the GUI's initial option values. The XAML tooltip
        /// texts still list the rounded numbers, so they must be kept in sync by
        /// hand when these constants change.
        /// </summary>
        public static class Defaults
        {
            public const OutputFormat Format = OutputFormat.MozJpeg;
            public const int Quality = 85;
            public const int Quantization = 90;
            public const int Dithering = 90;
            public const string Suffix = "_new";
            public const string ResizeArgs = "1920l";
            public const int BoundValue = 1920;
            public const ResizeFilter Filter = ResizeFilter.Lanczos3;
            public const int Threads = 4;
        }

        public OutputFormat Format { get; set; } = Defaults.Format;

        public int Quality { get; set; } = Defaults.Quality;

        /// <summary>Palette reduction, or <c>null</c> when the box is unchecked.</summary>
        public int? Quantization { get; set; }

        /// <summary>Dither strength; requires <see cref="Quantization"/>.</summary>
        public int? Dithering { get; set; }

        /// <summary>Output-name suffix, or <c>null</c> when the box is unchecked.</summary>
        public string Suffix { get; set; } = Defaults.Suffix;

        public OutputMode OutputMode { get; set; } = OutputMode.OriginalDir;

        public string OutputDirectory { get; set; }

        /// <summary>Maps to rimage's <c>-r</c>; only meaningful with a selected directory.</summary>
        public bool PreserveStructure { get; set; }

        public OriginalPolicy OriginalPolicy { get; set; } = OriginalPolicy.Keep;

        public ResizeMode ResizeMode { get; set; } = ResizeMode.None;

        /// <summary>Raw chained resize argument used when <see cref="ResizeMode"/> is Classic.</summary>
        public string ResizeArgs { get; set; } = Defaults.ResizeArgs;

        public ResizeFilter Filter { get; set; } = Defaults.Filter;

        public BoundDirection BoundDirection { get; set; } = BoundDirection.Maximum;

        public BoundEdge BoundEdge { get; set; } = BoundEdge.Longest;

        public int BoundValue { get; set; } = Defaults.BoundValue;

        /// <summary>Manual <c>--threads</c> override; <c>null</c> derives it from the CPU count.</summary>
        public int? Threads { get; set; }

        /// <summary>Runs rimage without a visible console window.</summary>
        public bool HideBackendWindow { get; set; } = true;
    }
}
