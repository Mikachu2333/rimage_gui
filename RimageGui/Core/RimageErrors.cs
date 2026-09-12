using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RimageGui.I18n;

namespace RimageGui.Core
{
    /// <summary>One failure rimage reported on stderr, attributed to an input when possible.</summary>
    public sealed class BackendError
    {
        /// <summary>
        /// The input this failure belongs to, or null when the line named no
        /// file the caller recognises.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The stable machine-readable kind rimage prints in brackets
        /// (<c>RimageError::kind()</c>), e.g. <c>input.decode</c>.
        /// </summary>
        public string Slug { get; set; }

        /// <summary>The reason text with the file path already stripped off.</summary>
        public string Message { get; set; }

        /// <summary>The follow-up <c>hint:</c> line rimage prints, when it printed one.</summary>
        public string Hint { get; set; }
    }

    /// <summary>
    /// Turns rimage's stderr into per-file failures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>--metadata</c> JSON only ever lists successes, so failures have to
    /// come from the log. rimage prints one line per failed file
    /// (<c>[ERROR rimage::error] input: &lt;path&gt; &lt;reason&gt; [slug]</c>)
    /// optionally followed by a <c>hint:</c> line, and the bracketed slug is the
    /// stable part worth classifying — the prose before it is free to change.
    /// </para>
    /// <para>
    /// <c>--quiet</c> silences stdout but not these lines, so parsing them is
    /// what makes a quiet run still explain itself.
    /// </para>
    /// </remarks>
    public static class RimageErrors
    {
        private static readonly Regex ErrorLine = new Regex(
            @"^\[ERROR rimage::error\]\s+(?:input|output):\s*(?<body>.*?)\s*\[(?<slug>[A-Za-z0-9._-]+)\]\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex HintLine = new Regex(
            @"^\[ERROR rimage::error\]\s+hint:\s*(?<hint>\S.*?)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Parses captured output lines and attributes each failure to one of
        /// <paramref name="candidates"/>.
        /// </summary>
        /// <param name="lines">Captured stdout/stderr lines, in order.</param>
        /// <param name="candidates">
        /// The inputs that were handed to this invocation. A single candidate
        /// owns every failure rimage reported, because it is the only file
        /// involved.
        /// </param>
        public static List<BackendError> Parse(IEnumerable<string> lines, IReadOnlyList<string> candidates)
        {
            var errors = new List<BackendError>();
            if (lines == null)
            {
                return errors;
            }

            BackendError pending = null;

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var line = raw.TrimEnd();

                var error = ErrorLine.Match(line);
                if (error.Success)
                {
                    var body = error.Groups["body"].Value;
                    var owner = MatchCandidate(body, candidates);
                    pending = new BackendError
                    {
                        Path = owner,
                        Slug = error.Groups["slug"].Value,
                        Message = Strip(body, owner)
                    };
                    errors.Add(pending);
                    continue;
                }

                var hint = HintLine.Match(line);
                if (hint.Success && pending != null && string.IsNullOrEmpty(pending.Hint))
                {
                    pending.Hint = hint.Groups["hint"].Value;
                }
            }

            return errors;
        }

        /// <summary>Localised one-line description of a failure, including its hint.</summary>
        public static string Describe(BackendError error)
        {
            if (error == null)
            {
                return string.Empty;
            }

            var text = DescribeSlug(error.Slug);
            if (string.IsNullOrEmpty(text))
            {
                text = error.Message ?? string.Empty;
            }

            if (!string.IsNullOrEmpty(error.Hint))
            {
                text = $"{text} {Loc.I["ErrHint"]}{error.Hint}";
            }

            return text.Trim();
        }

        /// <summary>
        /// Reason used when a file produced no output and rimage named no error
        /// for it, so the exit code is all the caller has.
        /// </summary>
        public static string DescribeExit(RimageExitCode code)
        {
            switch (code)
            {
                case RimageExitCode.Success:
                    return Loc.I["ErrSuccessNoOutput"];
                case RimageExitCode.Partial:
                    return Loc.I["ErrExitPartial"];
                case RimageExitCode.Input:
                    return Loc.I["ErrExitInput"];
                case RimageExitCode.Output:
                    return Loc.I["ErrExitOutput"];
                default:
                    return Loc.I["ErrFileFailed"];
            }
        }

        /// <summary>Reason used when a code aborts the whole run.</summary>
        public static string DescribeFatal(RimageExitCode code)
        {
            switch (code)
            {
                case RimageExitCode.Usage:
                    return Loc.I["ErrExitUsage"];
                case RimageExitCode.Panic:
                    return Loc.I["ErrExitPanic"];
                case RimageExitCode.Abort:
                    return Loc.I["ErrExitAbort"];
                default:
                    return Loc.I["ErrExitUnknown"];
            }
        }

        /// <summary>
        /// Localised text for a slug. Unrecognised slugs return null so the
        /// caller can fall back to rimage's own wording.
        /// </summary>
        private static string DescribeSlug(string slug)
        {
            switch (slug)
            {
                case "input.open":
                    return Loc.I["ErrSlugInputOpen"];
                case "input.decode":
                    return Loc.I["ErrSlugInputDecode"];
                case "input.unsupported-format":
                    return Loc.I["ErrSlugInputUnsupportedFormat"];
                case "input.size-limit":
                    return Loc.I["ErrSlugInputSizeLimit"];
                case "input.invalid-resize":
                    return Loc.I["ErrSlugInputInvalidResize"];
                case "input.configuration":
                    return Loc.I["ErrSlugInputConfiguration"];
                case "output.io":
                    return Loc.I["ErrSlugOutputIo"];
                case "output.encode":
                    return Loc.I["ErrSlugOutputEncode"];
                case "output.size-limit":
                    return Loc.I["ErrSlugOutputSizeLimit"];
                case "output.out-of-space":
                    return Loc.I["ErrSlugOutputOutOfSpace"];
                default:
                    return null;
            }
        }

        /// <summary>
        /// Finds which candidate a line is about. The path rimage prints is
        /// fully resolved, so both the raw and the canonical form are tried; the
        /// longest match wins because one input's directory can be a prefix of
        /// another input.
        /// </summary>
        private static string MatchCandidate(string body, IReadOnlyList<string> candidates)
        {
            if (string.IsNullOrEmpty(body) || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var best = LongestPrefix(body, candidates);
            if (best != null)
            {
                return best;
            }

            // A one-file batch owns every error rimage reported for it; there is
            // no other file the message could be about.
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            return UniqueNameMatch(body, candidates);
        }

        private static string LongestPrefix(string body, IReadOnlyList<string> candidates)
        {
            string best = null;
            var bestLength = -1;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                foreach (var form in Forms(candidate))
                {
                    if (form.Length <= bestLength || !body.StartsWith(form, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // The reason starts right after the path, so a longer
                    // candidate that merely shares the prefix must not match.
                    if (body.Length > form.Length && !IsBoundary(body[form.Length]))
                    {
                        continue;
                    }

                    best = candidate;
                    bestLength = form.Length;
                }
            }

            return best;
        }

        private static bool IsBoundary(char value)
        {
            return value == ' ' || value == ':' || value == ',' || value == '"';
        }

        private static string[] Forms(string candidate)
        {
            string full;
            try
            {
                full = System.IO.Path.GetFullPath(candidate);
            }
            catch (Exception)
            {
                full = null;
            }

            if (full != null && !string.Equals(full, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { full, candidate };
            }

            return new[] { candidate };
        }

        /// <summary>
        /// Last resort: a file name that appears in only one candidate. Two
        /// candidates with the same name in different folders stay unattributed
        /// rather than blamed on the wrong file.
        /// </summary>
        private static string UniqueNameMatch(string body, IReadOnlyList<string> candidates)
        {
            string found = null;
            foreach (var candidate in candidates)
            {
                string name;
                try
                {
                    name = System.IO.Path.GetFileName(candidate);
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name) ||
                    body.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = candidate;
            }

            return found;
        }

        /// <summary>Removes the path from the front of a reason so the row is not repeating itself.</summary>
        private static string Strip(string body, string owner)
        {
            if (string.IsNullOrEmpty(owner))
            {
                return body.Trim();
            }

            foreach (var form in Forms(owner))
            {
                if (body.StartsWith(form, StringComparison.OrdinalIgnoreCase))
                {
                    return body.Substring(form.Length).Trim();
                }
            }

            return body.Trim();
        }
    }
}
