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
        /// Extensions rimage 0.13 can decode. Encode-only formats are absent on
        /// purpose: offering an input the backend will reject only produces
        /// failures late, after the user has already queued a batch.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp", ".avif", ".bmp",
                ".tif", ".tiff", ".psd", ".qoi", ".jxl", ".hdr", ".ppm", ".ff"
            };

        public static string FileDialogFilter =>
            "Images|*.jpg;*.jpeg;*.png;*.webp;*.avif;*.bmp;*.tif;*.tiff;*.psd;*.qoi;*.jxl;*.hdr;*.ppm;*.ff|" +
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

        /// <summary>
        /// Expands a mix of files and folders into absolute image paths.
        /// Folders are walked depth-first; unreadable subtrees are skipped rather
        /// than aborting a scan the user cannot otherwise complete.
        /// </summary>
        /// <param name="onProgress">Called with the running count, for status text.</param>
        public static List<string> Collect(
            IEnumerable<string> roots,
            Action<int> onProgress,
            CancellationToken token)
        {
            var found = new List<string>();
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
                    else if (File.Exists(root) && IsSupported(root))
                    {
                        Add(found, seen, Path.GetFullPath(root), onProgress);
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

            return found;
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
