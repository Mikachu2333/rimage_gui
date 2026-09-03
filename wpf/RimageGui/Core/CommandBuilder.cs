using System;
using System.Collections.Generic;
using System.Globalization;
using RimageGui.Models;

namespace RimageGui.Core
{
    public static class CommandBuilder
    {
        /// <summary>
        /// Builds one rimage invocation for a batch of inputs.
        /// </summary>
        /// <remarks>
        /// Inputs always travel through a file named exactly <c>file.list</c>
        /// rather than as positional arguments: rimage reads one path per line
        /// from it, which keeps the command line short enough for batches of any
        /// size. <c>--metadata</c> makes rimage record the input/output pair it
        /// actually produced, which is the only reliable way to learn the real
        /// output path — predicting it is a fallback, not the source of truth.
        /// </remarks>
        public static List<string> BuildArgs(ProcessingOptions options, string fileList, string metadata)
        {
            var invariant = CultureInfo.InvariantCulture;
            var args = new List<string> { options.Format.CliName() };

            if (options.Format.SupportsQuality())
            {
                args.Add("--quality");
                args.Add(options.Quality.ToString(invariant));
            }

            if (options.OutputMode == OutputMode.SelectedDir &&
                !string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                args.Add("--directory");
                args.Add(PathUtil.NormalizeDirectory(options.OutputDirectory));

                if (options.PreserveStructure)
                {
                    // Mirrors each input's relative folder under --directory.
                    // The runner sets the working directory to the inputs' common
                    // root so "relative" means what the user expects.
                    args.Add("-r");
                }
            }

            if (!string.IsNullOrEmpty(options.Suffix))
            {
                args.Add("--suffix");
                args.Add(options.Suffix);
            }

            if (options.OriginalPolicy == OriginalPolicy.Backup)
            {
                args.Add("--backup");
            }

            if (options.Quantization.HasValue)
            {
                args.Add("--quantization");
                args.Add(options.Quantization.Value.ToString(invariant));

                // rimage only honours --dithering alongside --quantization.
                if (options.Dithering.HasValue)
                {
                    args.Add("--dithering");
                    args.Add(options.Dithering.Value.ToString(invariant));
                }
            }

            AppendResizeArgs(args, options);

            args.Add("--threads");
            args.Add(ResolveThreads(options).ToString(invariant));
            args.Add("--no-progress");
            args.Add("--metadata");
            args.Add(metadata);
            args.Add(fileList);

            return args;
        }

        /// <summary>
        /// Worker count handed to rimage: one below the logical CPU count so the
        /// UI thread keeps a core, with a floor of one.
        /// </summary>
        public static int ResolveThreads(ProcessingOptions options)
        {
            if (options.Threads.HasValue)
            {
                return Math.Max(1, options.Threads.Value);
            }

            return Math.Max(1, Environment.ProcessorCount - 1);
        }

        private static void AppendResizeArgs(List<string> args, ProcessingOptions options)
        {
            switch (options.ResizeMode)
            {
                case ResizeMode.None:
                    return;

                case ResizeMode.Classic:
                {
                    if (!Validator.SplitResizeArgs(options.ResizeArgs, out var steps))
                    {
                        // Unreachable for a validated job; refuse to emit a
                        // half-built resize chain rather than guess.
                        return;
                    }

                    foreach (var step in steps)
                    {
                        args.Add("--resize");
                        args.Add(step);
                    }

                    args.Add("--filter");
                    args.Add(options.Filter.CliName());
                    return;
                }

                case ResizeMode.Bounds:
                {
                    var edge = options.BoundEdge == BoundEdge.Longest ? "l" : "s";
                    args.Add("--resize");
                    args.Add(options.BoundValue.ToString(CultureInfo.InvariantCulture) + edge);
                    // A maximum may only shrink; a minimum may only grow. rimage
                    // skips any step the direction flag forbids.
                    args.Add(options.BoundDirection == BoundDirection.Maximum
                        ? "--reduce-only"
                        : "--enlarge-only");
                    args.Add("--filter");
                    args.Add(options.Filter.CliName());
                    return;
                }
            }
        }
    }
}
