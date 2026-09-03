using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimageGui.Core
{
    public sealed class BackendException : Exception
    {
        public BackendException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Materialises the rimage backend on disk and proves it is the build this
    /// GUI was written against.
    /// </summary>
    /// <remarks>
    /// The executable is carried as an embedded resource in Release builds and
    /// unpacked into the per-user cache. Every launch re-hashes the cached copy
    /// against the embedded bytes, so a truncated, tampered, or stale extraction
    /// is replaced rather than executed. The version probe then runs once per
    /// process: rimage's flags change between releases, and silently driving an
    /// unexpected build is how batches lose files.
    /// </remarks>
    public static class BackendExtractor
    {
        /// <summary>The only rimage build whose CLI surface this GUI targets.</summary>
        public const string ExpectedVersion = "rimage 0.13.0-1";

        private const string ResourceName = "RimageGui.rimage.exe";

        private static readonly object Gate = new object();
        private static bool _versionVerified;

        public static string Architecture => Environment.Is64BitProcess ? "x64" : "x86";

        /// <summary>
        /// Extracts (if needed) and verifies the backend. Safe to call repeatedly;
        /// the version probe is memoised, the extraction is not, so an executable
        /// deleted underneath the app is restored.
        /// </summary>
        public static Task<string> PrepareAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var path = Extract();

                lock (Gate)
                {
                    if (_versionVerified)
                    {
                        return path;
                    }
                }

                VerifyVersion(path);

                lock (Gate)
                {
                    _versionVerified = true;
                }

                return path;
            }, token);
        }

        private static string Extract()
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mikachu2333", "RimageGUI", "cache",
                AssemblyVersion(), Architecture);

            Directory.CreateDirectory(cacheDirectory);
            var target = Path.Combine(cacheDirectory, "rimage.exe");

            var expected = ComputeSourceHash();

            if (File.Exists(target) && HashesEqual(FileHash(target), expected))
            {
                return target;
            }

            var temporary = Path.Combine(
                cacheDirectory,
                "rimage-" + Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var source = OpenBackendStream())
                using (var destination = new FileStream(
                           temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    source.CopyTo(destination);
                    destination.Flush(true);
                }

                if (!HashesEqual(FileHash(temporary), expected))
                {
                    throw new BackendException("backend hash verification failed after extraction");
                }

                Publish(temporary, target, expected);
            }
            catch (Exception)
            {
                TryDelete(temporary);
                throw;
            }

            if (!File.Exists(target) || !HashesEqual(FileHash(target), expected))
            {
                throw new BackendException("backend hash verification failed");
            }

            return target;
        }

        /// <summary>
        /// Moves the freshly written copy into place. A concurrent instance may
        /// have published an identical file first, or may be holding the old one
        /// open; both are fine as long as the bytes on disk match.
        /// </summary>
        private static void Publish(string temporary, string target, byte[] expected)
        {
            try
            {
                if (File.Exists(target))
                {
                    File.Replace(temporary, target, null, true);
                }
                else
                {
                    File.Move(temporary, target);
                }

                return;
            }
            catch (IOException)
            {
                if (File.Exists(target) && HashesEqual(FileHash(target), expected))
                {
                    TryDelete(temporary);
                    return;
                }

                throw;
            }
            catch (UnauthorizedAccessException)
            {
                if (File.Exists(target) && HashesEqual(FileHash(target), expected))
                {
                    TryDelete(temporary);
                    return;
                }

                throw;
            }
        }

        private static void VerifyVersion(string path)
        {
            var startInfo = new ProcessStartInfo(path, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            string reported;
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new BackendException("the rimage backend could not be started");
                }

                reported = process.StandardOutput.ReadToEnd().Trim();
                process.StandardError.ReadToEnd();

                if (!process.WaitForExit(15000))
                {
                    TryKill(process);
                    throw new BackendException("the rimage backend did not answer --version");
                }

                if (process.ExitCode != 0)
                {
                    throw new BackendException(
                        "the rimage backend exited with code " + process.ExitCode + " for --version");
                }
            }

            if (!string.Equals(reported, ExpectedVersion, StringComparison.Ordinal))
            {
                throw new BackendException(
                    "unsupported rimage version: expected \"" + ExpectedVersion +
                    "\" but found \"" + reported + "\"");
            }
        }

        /// <summary>
        /// Opens the backend bytes: the embedded resource in Release builds, or
        /// the repository's res/ copy during development so inner-loop builds do
        /// not have to re-embed 25 MB on every compile.
        /// </summary>
        private static Stream OpenBackendStream()
        {
#if EMBED_BACKEND
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                throw new BackendException("the embedded rimage resource is missing from this build");
            }

            return stream;
#else
            var path = LocateDevelopmentBackend();
            if (path == null)
            {
                throw new BackendException(
                    "this build does not embed rimage and res\\rimage_" + Architecture +
                    ".exe was not found; build with -p:Platform=x64 -c Release to embed it");
            }

            return File.OpenRead(path);
#endif
        }

#if !EMBED_BACKEND
        private static string LocateDevelopmentBackend()
        {
            var name = "rimage_" + Architecture + ".exe";
            var directory = new DirectoryInfo(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".");

            // bin\<platform>\<config>\net48 sits four levels below the project,
            // which itself sits two levels below the repository root.
            for (var depth = 0; depth < 8 && directory != null; depth++)
            {
                var candidate = Path.Combine(directory.FullName, "res", name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }
#endif

        private static byte[] ComputeSourceHash()
        {
            using (var stream = OpenBackendStream())
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(stream);
            }
        }

        private static byte[] FileHash(string path)
        {
            try
            {
                using (var stream = new FileStream(
                           path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
                using (var sha = SHA256.Create())
                {
                    return sha.ComputeHash(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool HashesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var difference = 0;
            for (var index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static string AssemblyVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // A leftover .tmp in the cache is harmless.
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }
}
