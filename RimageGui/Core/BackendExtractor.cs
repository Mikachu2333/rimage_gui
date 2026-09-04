using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimageGui.Core
{
    /// <summary>
    /// Materialises the rimage backend on disk and proves it is the build this
    /// GUI was written against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The executable is carried as an embedded resource in Release builds and
    /// unpacked to a flat per-user path, <c>%LocalAppData%\rimage_gui\rimage.exe</c>
    /// — no version or architecture component. Whether the copy already on disk
    /// is usable is decided on every startup by its <c>--version</c> answer
    /// matching <see cref="ExpectedVersion"/>; the constant is bumped by hand
    /// together with the embedded binary. A missing, stale, corrupt, or foreign
    /// copy simply answers wrong (or fails to start) and is replaced by the
    /// embedded one, which also self-heals an x86/x64 mismatch since the wrong
    /// architecture cannot start at all.
    /// </para>
    /// <para>
    /// rimage's flags change between releases, and silently driving an
    /// unexpected build is how batches lose files — hence the strict probe.
    /// </para>
    /// </remarks>
    public static class BackendExtractor
    {
        /// <summary>
        /// The latest rimage build this GUI is written against. Bump by hand
        /// together with the embedded binary; it decides whether the on-disk
        /// copy is current.
        /// </summary>
        public const string ExpectedVersion = "rimage 0.13.0-1";

        private const string ResourceName = "RimageGui.rimage.exe";

        private const int VersionTimeoutMilliseconds = 15000;

        public static string Architecture => Environment.Is64BitProcess ? "x64" : "x86";

        /// <summary>
        /// Ensures the backend on disk answers <see cref="ExpectedVersion"/> and
        /// returns its path. Safe to call repeatedly: a current copy short-circuits
        /// with one version probe, anything else is re-extracted.
        /// </summary>
        public static Task<string> PrepareAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var target = TargetPath();

                if (File.Exists(target) && ProbesAsExpected(target))
                {
                    return target;
                }

                Extract(target);
                VerifyVersion(target);
                return target;
            }, token);
        }

        private static string TargetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "rimage_gui", "rimage.exe");
        }

        private static bool ProbesAsExpected(string path)
        {
            try
            {
                return string.Equals(ProbeVersion(path), ExpectedVersion, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                // Cannot start, timed out, or answered nothing — treat as stale.
                return false;
            }
        }

        /// <summary>Runs <c>rimage --version</c> and returns the reported string.</summary>
        private static string ProbeVersion(string path)
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

                // Start both reads before waiting so a stderr-filled pipe cannot
                // block the stdout read and deadlock the version probe.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(VersionTimeoutMilliseconds))
                {
                    TryKill(process);
                    throw new BackendException("the rimage backend did not answer --version");
                }

                var stdout = stdoutTask.GetAwaiter().GetResult();
                stderrTask.GetAwaiter().GetResult();
                reported = stdout.Trim();

                if (process.ExitCode != 0)
                {
                    throw new BackendException(
                        $"the rimage backend exited with code {process.ExitCode} for --version");
                }
            }

            return reported;
        }

        private static void VerifyVersion(string path)
        {
            var reported = ProbeVersion(path);
            if (!string.Equals(reported, ExpectedVersion, StringComparison.Ordinal))
            {
                throw new BackendException(
                    $"unsupported rimage version: expected \"{ExpectedVersion}\" but found \"{reported}\"");
            }
        }

        /// <summary>
        /// Opens the backend bytes: the embedded resource in Release builds, or
        /// the repository's res/ copy during development so inner-loop builds do
        /// not have to re-embed 25 MB on every compile.
        /// </summary>
        [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
            Justification = "The EMBED_BACKEND path returns a non-FileStream resource stream.")]
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
                    $"this build does not embed rimage and res\\rimage_{Architecture}.exe was not found; build with -p:Platform=x64 -c Release to embed it");
            }

            return File.OpenRead(path);
#endif
        }

#if !EMBED_BACKEND
        private static string LocateDevelopmentBackend()
        {
            var name = $"rimage_{Architecture}.exe";
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

        /// <summary>
        /// Writes the embedded bytes to <paramref name="target"/> through a
        /// temporary file whose contents are hash-checked before publishing.
        /// </summary>
        private static void Extract(string target)
        {
            var directory = Path.GetDirectoryName(target) ?? ".";
            Directory.CreateDirectory(directory);

            var expected = ComputeSourceHash();

            var temporary = Path.Combine(
                directory,
                $"rimage-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}.tmp");

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
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                if (File.Exists(target) && HashesEqual(FileHash(target), expected))
                {
                    TryDelete(temporary);
                    return;
                }

                throw;
            }
        }

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
