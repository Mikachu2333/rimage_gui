using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
    /// — no version or architecture component. Whether the copy already on disk is
    /// usable is decided on every startup by the <c>ProductVersion</c> string in
    /// its own version resource, read straight out of the image by Windows
    /// (<see cref="FileVersionResource"/>) instead of by running it. The expected
    /// value is pinned by <see cref="ExpectedProductVersion"/>, bumped by hand
    /// together with the embedded binary.
    /// </para>
    /// <para>
    /// The comparison is a plain string equality and it is the whole decision: a
    /// copy that does not report exactly that version — including one whose version
    /// resource cannot be read at all, and including a copy of the other
    /// architecture, which can never report it — is replaced by the embedded one.
    /// Nothing here starts the backend; the only launch is the job itself.
    /// </para>
    /// </remarks>
    public static class BackendExtractor
    {
        /// <summary>
        /// The latest rimage build this GUI is written against, as the version
        /// resource spells it (<c>ProductVersion</c>). Bump by hand together with
        /// the embedded binary; it decides whether the on-disk copy is current.
        /// </summary>
        /// <remarks>
        /// The <c>ProductName</c> is deliberately not compared: every rimage build
        /// is named <c>rimage</c>, so the version number is the only field that
        /// distinguishes one release from another.
        /// </remarks>
        public const string ExpectedProductVersion = "0.14.0";

        private const string ResourceName = "RimageGui.rimage.exe";

        /// <summary>
        /// What <c>rimage --version</c> prints for the pinned build, used only when
        /// reporting a mismatch to a user who may check by hand.
        /// </summary>
        private const string VersionDisplayPrefix = "rimage ";

        public static string Architecture => Environment.Is64BitProcess ? "x64" : "x86";

        /// <summary>
        /// Ensures the backend on disk is the build pinned by
        /// <see cref="ExpectedProductVersion"/> and returns its path. Safe to call
        /// repeatedly: a current copy short-circuits after reading one version
        /// resource, anything else is re-extracted.
        /// </summary>
        public static Task<string> PrepareAsync(CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var target = TargetPath();
                var cacheDirectory = Path.GetDirectoryName(target) ?? ".";
                Directory.CreateDirectory(cacheDirectory);

                // Sweep before deciding anything, so a large leftover from a run
                // that was killed mid copy is reclaimed even when this start has
                // nothing to extract.
                DeleteAbandonedTemporaries(cacheDirectory);

                if (File.Exists(target) && FileMatchesExpectedVersion(target))
                {
                    return target;
                }

                Extract(target, cacheDirectory);
                return target;
            }, token);
        }

        private static string TargetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "rimage_gui", "rimage.exe");
        }

        /// <summary>
        /// Whether the image at <paramref name="path"/> reports exactly the pinned
        /// <c>ProductVersion</c>. An unreadable resource reports no version, so it
        /// compares unequal and the copy is replaced — there is no second opinion to
        /// seek, and nothing here starts the backend.
        /// </summary>
        private static bool FileMatchesExpectedVersion(string path)
        {
            return string.Equals(
                FileVersionResource.ReadProductVersion(path),
                ExpectedProductVersion,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms the bytes that are about to be extracted report the pinned
        /// version, so a build whose source and
        /// <see cref="ExpectedProductVersion"/> disagree fails before it can
        /// publish anything. The bytes' identity is proven separately by SHA-256;
        /// this check is only about the version, and it never launches anything.
        /// </summary>
        /// <remarks>
        /// The version of what is already published is not re-read after extraction:
        /// the published bytes are the verified embedded ones, so the next start's
        /// comparison is what decides — and whoever extracts already holds the
        /// pinned build.
        /// </remarks>
        private static void VerifyBackendVersion(string path)
        {
            // No path means there is nothing to compare against — the bytes live in
            // this assembly, which VerifySourceVersion stages for checking instead.
            if (path == null)
            {
                return;
            }

            var reported = FileVersionResource.ReadProductVersion(path);
            if (!string.Equals(reported, ExpectedProductVersion, StringComparison.Ordinal))
            {
                // The bundled backend and the constant above were bumped apart, so
                // the flags this GUI would pass cannot be trusted for these bytes.
                var found = reported == null
                    ? "an executable whose version resource cannot be read"
                    : $"\"{VersionDisplayPrefix}{reported}\"";

                throw new BackendException(
                    $"the bundled rimage backend is not the pinned build: expected " +
                    $"\"{VersionDisplayPrefix}{ExpectedProductVersion}\" but found {found}");
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
        /// <paramref name="directory"/> is the cache directory that already holds
        /// the target and is passed in so the caller can sweep it before deciding
        /// whether extraction is needed at all.
        /// </summary>
        /// <remarks>
        /// The source is checked before anything is written: a build whose embedded
        /// binary and <see cref="ExpectedProductVersion"/> disagree fails here rather
        /// than publishing a backend that every following start would then have to
        /// throw away again. What ends up on disk is the hash-verified source, so its
        /// version needs no second reading.
        /// </remarks>
        private static void Extract(string target, string directory)
        {
            VerifySourceVersion();

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
                // Clean up the partial copy, then let the caller report why the
                // verified backend could not be put in place.
                TryDelete(temporary);
                throw;
            }

            if (!File.Exists(target) || !HashesEqual(FileHash(target), expected))
            {
                throw new BackendException("backend hash verification failed");
            }
        }

        /// <summary>
        /// Checks the version of the bytes that are about to be extracted. When the
        /// backend ships as a file the check reads it in place; when it is embedded
        /// it has no path, so those bytes are written to a temporary first and
        /// removed again — still cheaper than publishing a backend this GUI cannot
        /// drive.
        /// </summary>
        private static void VerifySourceVersion()
        {
            var path = PathOfBackendSource();
            if (path != null)
            {
                VerifyBackendVersion(path);
                return;
            }

            // A directory of this process's own, so the check file cannot collide
            // with a concurrent instance's and is removed wholesale afterwards.
            var staging = Path.Combine(
                Path.GetTempPath(),
                $"rimage-gui-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(staging);
                var temporary = Path.Combine(staging, "rimage.exe");

                using (var source = OpenBackendStream())
                using (var destination = new FileStream(
                           temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    source.CopyTo(destination);
                    destination.Flush(true);
                }

                VerifyBackendVersion(temporary);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        /// <summary>
        /// Path of the backend bytes <see cref="OpenBackendStream"/> would read, for
        /// the version check that runs before they are written. Only the
        /// development layout has a file to inspect; an embedded resource has no
        /// path and is written to a temporary for checking instead.
        /// </summary>
        private static string PathOfBackendSource()
        {
#if EMBED_BACKEND
            return null;
#else
            return LocateDevelopmentBackend();
#endif
        }

        /// <summary>
        /// Removes <c>.tmp</c> files this class left behind. A process killed mid
        /// copy never reaches the cleanup in the catch below, and the file it was
        /// writing is 25 MB — worth reclaiming on the next start rather than
        /// accumulating one per crash. A file another instance is actively writing
        /// is locked against deletion and is simply skipped.
        /// </summary>
        private static void DeleteAbandonedTemporaries(string directory)
        {
            try
            {
                foreach (var candidate in Directory.EnumerateFiles(directory, "rimage-*.tmp"))
                {
                    TryDelete(candidate);
                }
            }
            catch (Exception)
            {
                // Reclaiming space is best effort; extraction must not fail over it.
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

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
                // A leftover staging directory in %TEMP% is harmless.
            }
        }
    }
}
