using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RimageGui.Models;

namespace RimageGui.Core
{
    public static class Validator
    {
        private static readonly string[] ReservedDeviceNames =
        {
            "con", "prn", "aux", "nul",
            "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
        };

        private static readonly char[] InvalidComponentChars =
            { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        private static readonly char[] ResizeSeparators = { ' ', '\t' };

        /// <summary>
        /// Whether a string is usable as part of a Windows file name. Reserved
        /// device names stay reserved even with an extension (<c>NUL.txt</c>),
        /// and trailing dots or spaces are silently stripped by the shell, so
        /// both are rejected up front.
        /// </summary>
        public static bool IsValidNameComponent(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == "..")
            {
                return false;
            }

            if (value.EndsWith(".", StringComparison.Ordinal) ||
                value.EndsWith(" ", StringComparison.Ordinal))
            {
                return false;
            }

            if (value.IndexOfAny(InvalidComponentChars) >= 0)
            {
                return false;
            }

            if (value.Any(char.IsControl))
            {
                return false;
            }

            var stem = value.Split('.')[0];
            return !ReservedDeviceNames.Any(
                name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase));
        }

        public static ValidationResult ValidateJob(JobSpec job)
        {
            if (job.Files == null || job.Files.Count == 0)
            {
                return ValidationResult.Fail("ErrorNoFiles");
            }

            var options = job.Options;

            if (options.Format.SupportsQuality() && (options.Quality < 1 || options.Quality > 100))
            {
                return ValidationResult.Fail("ErrorQuality");
            }

            if (options.Quantization.HasValue &&
                (options.Quantization.Value < 1 || options.Quantization.Value > 100))
            {
                return ValidationResult.Fail("ErrorQuantization");
            }

            if (options.Dithering.HasValue &&
                (!options.Quantization.HasValue ||
                 options.Dithering.Value < 1 || options.Dithering.Value > 100))
            {
                return ValidationResult.Fail("ErrorDithering");
            }

            if (options.Suffix != null && !IsValidNameComponent(options.Suffix))
            {
                return ValidationResult.Fail("ErrorSuffix");
            }

            // rimage names its backup "<stem>@backup.<ext>"; adding a suffix on
            // top produces a second naming scheme for the same file and the two
            // can collide, so the pair is rejected instead of guessed at.
            if (options.OriginalPolicy == OriginalPolicy.Backup && options.Suffix != null)
            {
                return ValidationResult.Fail("ErrorBackupSuffixConflict");
            }

            if (options.OutputMode == OutputMode.SelectedDir)
            {
                if (string.IsNullOrWhiteSpace(options.OutputDirectory))
                {
                    return ValidationResult.Fail("ErrorOutputDirectory");
                }

                if (!Directory.Exists(options.OutputDirectory))
                {
                    return ValidationResult.Fail("ErrorOutputDirectoryMissing", options.OutputDirectory);
                }
            }

            if (options.Threads.HasValue &&
                (options.Threads.Value < 1 || options.Threads.Value > 256))
            {
                return ValidationResult.Fail("ErrorThreads");
            }

            var resize = ValidateResize(options);
            if (!resize.IsValid)
            {
                return resize;
            }

            return ValidateOutputPaths(job);
        }

        private static ValidationResult ValidateResize(ProcessingOptions options)
        {
            switch (options.ResizeMode)
            {
                case ResizeMode.None:
                    return ValidationResult.Ok;

                case ResizeMode.Classic:
                    return SplitResizeArgs(options.ResizeArgs, out _)
                        ? ValidationResult.Ok
                        : ValidationResult.Fail("ErrorResize");

                case ResizeMode.Bounds:
                    return options.BoundValue > 0
                        ? ValidationResult.Ok
                        : ValidationResult.Fail("ErrorSizeBounds");

                default:
                    return ValidationResult.Ok;
            }
        }

        /// <summary>
        /// Predicts the path rimage 0.13 writes for one input. Used for
        /// pre-flight collision detection and as a fallback when a failed batch
        /// leaves no metadata behind.
        /// </summary>
        public static string PredictedOutputPath(string input, ProcessingOptions options)
        {
            var directory = options.OutputMode == OutputMode.SelectedDir
                ? options.OutputDirectory
                : (Path.GetDirectoryName(input) ?? ".");

            var stem = Path.GetFileNameWithoutExtension(input);
            var name = $"{stem}{options.Suffix ?? string.Empty}.{options.Format.Extension()}";
            return Path.Combine(directory ?? ".", name);
        }

        /// <summary>
        /// Rejects batches whose outputs collide with each other or clobber
        /// another input before any process starts.
        /// </summary>
        /// <remarks>
        /// Skipped when <see cref="ProcessingOptions.PreserveStructure"/> is on:
        /// rimage then mirrors each input's relative folder under the output
        /// directory, so a flat prediction would report collisions that the real
        /// run never produces. Those batches rely on metadata instead.
        /// </remarks>
        public static ValidationResult ValidateOutputPaths(JobSpec job)
        {
            var options = job.Options;
            if (options.OutputMode == OutputMode.SelectedDir && options.PreserveStructure)
            {
                return ValidationResult.Ok;
            }

            var inputKeys = new HashSet<string>(job.Files.Select(PathUtil.Key), StringComparer.Ordinal);
            var outputKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var input in job.Files)
            {
                var output = PredictedOutputPath(input, options);
                var inputKey = PathUtil.Key(input);
                var outputKey = PathUtil.Key(output);

                if (!outputKeys.Add(outputKey))
                {
                    return ValidationResult.Fail("ErrorDuplicateOutput", output);
                }

                if (outputKey == inputKey)
                {
                    // Converting in place is fine unless the original is meant to
                    // be deleted afterwards, which would erase the only copy.
                    if (options.OriginalPolicy == OriginalPolicy.DeleteAfterVerifiedSuccess)
                    {
                        return ValidationResult.Fail("ErrorUnsafeDelete", input);
                    }
                }
                else if (inputKeys.Contains(outputKey))
                {
                    return ValidationResult.Fail("ErrorOutputOverwritesInput", output);
                }
            }

            return ValidationResult.Ok;
        }

        /// <summary>
        /// Splits a whitespace-chained resize argument and normalises each step
        /// to a form rimage accepts. Returns false for a blank chain or any
        /// unrecognised step.
        /// </summary>
        public static bool SplitResizeArgs(string input, out List<string> values)
        {
            values = new List<string>();
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            foreach (var part in input.Split(ResizeSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!NormalizeResizeArg(part, out var normalized))
                {
                    values = null;
                    return false;
                }

                values.Add(normalized);
            }

            return values.Count > 0;
        }

        /// <summary>
        /// Normalises one resize step. Accepts <c>@1.5</c>, <c>150%</c>,
        /// <c>1920x1080</c>, <c>720w</c>/<c>720h</c>, <c>1000l</c>/<c>500s</c>,
        /// plus the legacy <c>720x_</c> and <c>720x</c> spellings which both mean
        /// "anchor the width", and are rewritten to <c>720w</c>.
        /// </summary>
        public static bool NormalizeResizeArg(string input, out string normalized)
        {
            normalized = null;
            var value = (input ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return false;
            }

            var invariant = CultureInfo.InvariantCulture;

            if (value[0] == '@')
            {
                if (double.TryParse(value.Substring(1).Trim(), NumberStyles.Float, invariant, out var factor) &&
                    factor > 0 && !double.IsInfinity(factor))
                {
                    normalized = $"@{factor.ToString(invariant)}";
                    return true;
                }

                return false;
            }

            if (value[value.Length - 1] == '%')
            {
                var body = value.Substring(0, value.Length - 1).Trim();
                if (double.TryParse(body, NumberStyles.Float, invariant, out var percent) &&
                    percent > 0 && !double.IsInfinity(percent))
                {
                    normalized = $"{percent.ToString(invariant)}%";
                    return true;
                }

                return false;
            }

            var lower = value.ToLowerInvariant();
            var marker = lower[lower.Length - 1];
            if (marker == 'w' || marker == 'h' || marker == 'l' || marker == 's')
            {
                return NormalizeSide(lower.Substring(0, lower.Length - 1), marker, out normalized);
            }

            var separator = lower.IndexOf('x');
            if (separator > 0)
            {
                var widthPart = lower.Substring(0, separator).Trim();
                var heightPart = lower.Substring(separator + 1).Trim();

                if (!int.TryParse(widthPart, NumberStyles.Integer, invariant, out var width) || width <= 0)
                {
                    return false;
                }

                // "720x" and "720x_" both anchor the width and let the height follow.
                if (heightPart.Length == 0 || heightPart == "_")
                {
                    normalized = $"{width.ToString(invariant)}w";
                    return true;
                }

                if (!int.TryParse(heightPart, NumberStyles.Integer, invariant, out var height) || height <= 0)
                {
                    return false;
                }

                normalized = $"{width.ToString(invariant)}x{height.ToString(invariant)}";
                return true;
            }

            return false;
        }

        private static bool NormalizeSide(string digits, char marker, out string normalized)
        {
            normalized = null;
            if (!int.TryParse(digits.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
                length <= 0)
            {
                return false;
            }

            normalized = length.ToString(CultureInfo.InvariantCulture) + marker;
            return true;
        }

        /// <summary>
        /// Last gate before an original is deleted. Requires a real, non-empty
        /// output at a path that differs from the input, and no cancellation.
        /// </summary>
        public static bool SafeToDelete(string input, string output, bool cancelled)
        {
            if (cancelled || string.IsNullOrEmpty(output))
            {
                return false;
            }

            try
            {
                var info = new FileInfo(output);
                if (!info.Exists || info.Length == 0)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return PathUtil.Key(input) != PathUtil.Key(output);
        }
    }
}
