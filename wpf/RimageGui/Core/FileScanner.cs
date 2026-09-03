using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace RimageGui.Core
{
    /// <summary>
    /// Collects supported images from dropped or picked paths.
    /// </summary>
    public static class FileScanner
    {
        /// <summary>
        /// Extensions rimage 0.13 can decode and the GUI actually offers,
        /// verified against the shipped backend on real and synthetic samples.
        /// Deliberately absent: encode-only formats that the backend would only
        /// reject late (offering them means failures after the user has already
        /// queued a batch) and the niche intermediate formats qoi/ppm/farbfeld,
        /// which the GUI neither accepts as input nor offers as output.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp", ".avif", ".bmp",
                ".tif", ".tiff", ".psd", ".jxl", ".hdr"
            };

        public static string FileDialogFilter =>
            "Images|*.jpg;*.jpeg;*.png;*.webp;*.avif;*.bmp;*.tif;*.tiff;*.psd;*.jxl;*.hdr|" +
            "All files|*.*";

        public static bool IsSupported(string path)
        {
            try
            {
                return SupportedExtensions.Contains(Path.GetExtension(path) ?? string.Empty);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Outcome of one scan: the accepted paths and how many were filtered out.</summary>
        public sealed class ScanResult
        {
            public List<string> Found { get; } = new List<string>();

            public int Skipped { get; set; }
        }

        /// <summary>
        /// Expands a mix of files and folders into absolute image paths.
        /// Files whose extension is not supported are filtered out and counted;
        /// folders are walked depth-first; unreadable subtrees are skipped rather
        /// than aborting a scan the user cannot otherwise complete.
        /// </summary>
        /// <param name="onProgress">Called with the running count, for status text.</param>
        public static ScanResult Collect(
            IEnumerable<string> roots,
            Action<int> onProgress,
            CancellationToken token)
        {
            var result = new ScanResult();
            var found = result.Found;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                try
                {
                    if (Directory.Exists(root))
                    {
                        pending.Push(Path.GetFullPath(root));
                    }
                    else if (File.Exists(root))
                    {
                        if (IsSupported(root))
                        {
                            Add(found, seen, Path.GetFullPath(root), onProgress);
                        }
                        else
                        {
                            result.Skipped++;
                        }
                    }
                }
                catch (Exception)
                {
                    // Unreachable or malformed root; nothing to contribute.
                }
            }

            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (IsSupported(file))
                        {
                            Add(found, seen, file, onProgress);
                        }
                        else
                        {
                            result.Skipped++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Access denied or the folder vanished mid-scan.
                }

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        pending.Push(child);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Same as above; keep whatever the scan already found.
                }
            }

            return result;
        }

        private static void Add(List<string> found, HashSet<string> seen, string path, Action<int> onProgress)
        {
            if (!seen.Add(PathUtil.Key(path)))
            {
                return;
            }

            found.Add(path);

            // Throttled so a scan of tens of thousands of files does not flood
            // the dispatcher with status updates nobody can read.
            if (found.Count % 256 == 0)
            {
                onProgress?.Invoke(found.Count);
            }
        }
    }
}
