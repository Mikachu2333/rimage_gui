using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RimageGui.Core
{
    public static class PathUtil
    {
        /// <summary>
        /// Stable comparison key for a path. Windows paths compare
        /// case-insensitively, and normalising first makes <c>a\.\b</c> and
        /// <c>a\b</c> collide the way the file system does.
        /// </summary>
        public static string Key(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // Malformed input still needs a key so validation can report it.
                full = path;
            }

            full = full.TrimEnd('\\', '/');
            return full.ToLowerInvariant();
        }

        /// <summary>
        /// Quotes one argument using the rules <c>CommandLineToArgvW</c> applies,
        /// which is what the MSVC-built rimage uses to split its command line.
        /// </summary>
        /// <remarks>
        /// .NET Framework has no <c>ProcessStartInfo.ArgumentList</c>, so the
        /// command line must be assembled by hand. Backslashes that precede a
        /// quote — including the closing quote — are doubled; without that,
        /// <c>-d "D:\out\"</c> escapes its own terminator and rimage receives a
        /// mangled path. That is the documented rimage crash with trailing
        /// separators, and correct quoting is what actually prevents it.
        /// </remarks>
        public static string QuoteArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            if (argument.Length > 0 && argument.IndexOfAny(NeedsQuoting) < 0)
            {
                return argument;
            }

            var builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                }
                else
                {
                    builder.Append('\\', backslashes);
                }

                backslashes = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private static readonly char[] NeedsQuoting = { ' ', '\t', '"' };

        /// <summary>Joins pre-quoted arguments into a single command line.</summary>
        public static string BuildArgumentString(IEnumerable<string> arguments)
        {
            var builder = new StringBuilder();
            foreach (var argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteArgument(argument));
            }

            return builder.ToString();
        }

        /// <summary>Full command line for logs; copyable straight into a shell.</summary>
        public static string DisplayCommandLine(string executable, IEnumerable<string> arguments)
        {
            var builder = new StringBuilder(QuoteArgument(executable));
            foreach (var argument in arguments)
            {
                builder.Append(' ').Append(QuoteArgument(argument));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Strips trailing separators unless the path is a root such as
        /// <c>C:\</c>. rimage treats <c>D:\out\</c> and <c>D:\out</c> alike, but
        /// the shorter form avoids exercising its trailing-separator edge cases.
        /// Null or whitespace normalizes to <see cref="string.Empty"/>, matching
        /// <see cref="Key"/>.
        /// </summary>
        public static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            var trimmed = directory.Trim();
            try
            {
                var full = Path.GetFullPath(trimmed);
                var root = Path.GetPathRoot(full);
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                {
                    return full;
                }

                return full.TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                return trimmed;
            }
        }

        /// <summary>
        /// Middle-elides a path so long names stay readable in fixed-width UI.
        /// </summary>
        public static string Shorten(string path, int maxLength = 64)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            {
                return path ?? string.Empty;
            }

            var keepLeft = (maxLength - 1) / 2;
            var keepRight = maxLength - keepLeft - 1;
            return $"{path.Substring(0, keepLeft)}…{path.Substring(path.Length - keepRight)}";
        }
    }
}
