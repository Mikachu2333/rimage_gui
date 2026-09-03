using System.Collections.Generic;

namespace RimageGui.Models
{
    /// <summary>
    /// A fully resolved, validated-ready description of one conversion run.
    /// The GUI edits its own view-model state and materialises this snapshot on
    /// Start, so later edits cannot mutate a job that is already running.
    /// </summary>
    public sealed class ProcessingOptions
    {
        public OutputFormat Format { get; set; } = OutputFormat.MozJpeg;

        public int Quality { get; set; } = 85;

        /// <summary>Palette reduction, or <c>null</c> when the box is unchecked.</summary>
        public int? Quantization { get; set; }

        /// <summary>Dither strength; requires <see cref="Quantization"/>.</summary>
        public int? Dithering { get; set; }

        /// <summary>Output-name suffix, or <c>null</c> when the box is unchecked.</summary>
        public string Suffix { get; set; }

        public OutputMode OutputMode { get; set; } = OutputMode.OriginalDir;

        public string OutputDirectory { get; set; }

        /// <summary>Maps to rimage's <c>-r</c>; only meaningful with a selected directory.</summary>
        public bool PreserveStructure { get; set; }

        public OriginalPolicy OriginalPolicy { get; set; } = OriginalPolicy.Keep;

        public ResizeMode ResizeMode { get; set; } = ResizeMode.None;

        /// <summary>Raw chained resize argument used when <see cref="ResizeMode"/> is Classic.</summary>
        public string ResizeArgs { get; set; }

        public ResizeFilter Filter { get; set; } = ResizeFilter.Lanczos3;

        public BoundDirection BoundDirection { get; set; } = BoundDirection.Maximum;

        public BoundEdge BoundEdge { get; set; } = BoundEdge.Longest;

        public int BoundValue { get; set; } = 1920;

        /// <summary>Manual <c>--threads</c> override; <c>null</c> derives it from the CPU count.</summary>
        public int? Threads { get; set; }

        /// <summary>Runs rimage without a visible console window.</summary>
        public bool Hidden { get; set; } = true;

        public ProcessingOptions Clone() => (ProcessingOptions)MemberwiseClone();
    }

    public sealed class JobSpec
    {
        public JobSpec(IReadOnlyList<string> files, ProcessingOptions options)
        {
            Files = files;
            Options = options;
        }

        public IReadOnlyList<string> Files { get; }

        public ProcessingOptions Options { get; }
    }
}
