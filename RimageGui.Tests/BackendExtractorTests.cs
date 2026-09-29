using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    /// <summary>
    /// Exercises the real release path: unpacking the backend into the per-user
    /// cache, accepting it while it reports the pinned version, replacing it when it
    /// does not, and reclaiming temporary files an interrupted run left behind.
    /// </summary>
    /// <remarks>
    /// These tests write to the same cache the application uses
    /// (<c>%LocalAppData%\rimage_gui</c>). That is deliberate — it is the code
    /// under test — and safe because extraction is idempotent and hash-checked,
    /// so a concurrent application instance ends up with identical bytes.
    /// </remarks>
    [TestClass]
    public class BackendExtractorSpecs
    {
        private static string LocateBackend()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 10 && directory != null; depth++)
            {
                var candidate = Path.Combine(
                    directory.FullName, "res", $"rimage_{BackendExtractor.Architecture}.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string CacheTarget()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "rimage_gui",
                "rimage.exe");
        }

        /// <summary>
        /// Skips, or fails under CI, when this run has no backend of its own
        /// architecture to release. The cache is shared, so an x86 test run must
        /// never leave an x86 backend in the path the x64 application will use; a
        /// backend of the right architecture always exists in the repository.
        /// </summary>
        private static bool BackendSourceIsAvailable()
        {
            if (LocateBackend() != null)
            {
                return true;
            }

            var requireBackend =
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIMAGEGUI_REQUIRE_BACKEND"));
            if (requireBackend)
            {
                Assert.Fail(
                    $"res/rimage_{BackendExtractor.Architecture}.exe is missing; CI requires it");
            }

            Assert.Inconclusive(
                $"res/rimage_{BackendExtractor.Architecture}.exe is missing next to the test output");
            return false;
        }

        /// <summary>
        /// Proves the published file is a working executable, not just a file whose
        /// resource claims to be one. The application itself never launches the
        /// backend, so this runs the check the startup path deliberately skips.
        /// </summary>
        private static bool BackendIsRunnable(string path)
        {
            using (var probe = Process.Start(new ProcessStartInfo(path, "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            }))
            {
                var stdout = probe.StandardOutput.ReadToEnd();
                return
                    probe.WaitForExit(15000) &&
                    probe.ExitCode == 0 &&
                    stdout.Trim() == "rimage " + BackendExtractor.ExpectedProductVersion;
            }
        }

        [TestMethod]
        public void PrepareAsync_PublishesThePinnedBackendIntoTheCache()
        {
            if (!BackendSourceIsAvailable())
            {
                return;
            }

            // Start from something that cannot be mistaken for the pinned build, so
            // the release path runs instead of the "already current" shortcut.
            var target = CacheTarget();
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target, new byte[1024]);

            var path = BackendExtractor.PrepareAsync().GetAwaiter().GetResult();

            Assert.AreEqual(target, path);
            Assert.IsTrue(File.Exists(path), "the backend was not written to the per-user cache");
            Assert.AreEqual(
                BackendExtractor.ExpectedProductVersion,
                FileVersionResource.ReadProductVersion(path));
            Assert.IsTrue(BackendIsRunnable(path), "the published backend is not runnable");
        }

        /// <summary>
        /// The whole decision is one string comparison: anything that does not report
        /// the pinned version is replaced, including a file whose version resource
        /// cannot be read at all.
        /// </summary>
        [TestMethod]
        public void PrepareAsync_ReplacesAStaleBackend()
        {
            if (!BackendSourceIsAvailable())
            {
                return;
            }

            BackendExtractor.PrepareAsync().GetAwaiter().GetResult();

            var target = CacheTarget();
            var intact = File.ReadAllBytes(target);

            try
            {
                // Stand in for a previous GUI version: a file that can pass neither
                // the SHA-256 check nor the version comparison.
                File.WriteAllBytes(target, new byte[Math.Min(intact.Length, 4096)]);
                Assert.IsNull(
                    FileVersionResource.ReadProductVersion(target),
                    "the stale stand-in unexpectedly reports a product version");

                var path = BackendExtractor.PrepareAsync().GetAwaiter().GetResult();

                Assert.AreEqual(target, path);
                CollectionAssert.AreEqual(
                    intact,
                    File.ReadAllBytes(path),
                    "the stale cache entry was not replaced by the embedded backend");
            }
            finally
            {
                File.WriteAllBytes(target, intact);
            }
        }

        [TestMethod]
        public void PrepareAsync_ReclaimsTemporaryFilesLeftByAnInterruptedRun()
        {
            if (!BackendSourceIsAvailable())
            {
                return;
            }

            var target = CacheTarget();
            var directory = Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);

            // A run killed mid copy never reaches its own cleanup, leaving a 25 MB
            // temporary behind. Force an extraction, which is when it must be swept.
            var abandoned = Path.Combine(
                directory, $"rimage-{Process.GetCurrentProcess().Id}-abandoned.tmp");
            File.WriteAllBytes(abandoned, new byte[1024]);
            File.WriteAllBytes(target, new byte[1024]);

            try
            {
                BackendExtractor.PrepareAsync().GetAwaiter().GetResult();

                Assert.IsFalse(File.Exists(abandoned), "the abandoned temporary file was not reclaimed");
                Assert.IsFalse(
                    Directory.EnumerateFiles(directory, "rimage-*.tmp").Any(),
                    "temporaries are still lying around after an extraction");
            }
            finally
            {
                if (File.Exists(abandoned))
                {
                    File.Delete(abandoned);
                }
            }
        }

        [TestMethod]
        public void PrepareAsync_ReclaimsTemporariesEvenWhenNothingIsExtracted()
        {
            if (!BackendSourceIsAvailable())
            {
                return;
            }

            // Leave the cache current, so the next start takes the fast path and
            // never reaches the extraction that would otherwise do the sweeping.
            var target = BackendExtractor.PrepareAsync().GetAwaiter().GetResult();
            var directory = Path.GetDirectoryName(target);

            var abandoned = Path.Combine(
                directory, $"rimage-{Process.GetCurrentProcess().Id}-leftover.tmp");
            File.WriteAllBytes(abandoned, new byte[1024]);

            try
            {
                var path = BackendExtractor.PrepareAsync().GetAwaiter().GetResult();

                Assert.AreEqual(target, path);
                Assert.IsFalse(
                    File.Exists(abandoned),
                    "a leftover temporary survived a start that had nothing to extract");
            }
            finally
            {
                if (File.Exists(abandoned))
                {
                    File.Delete(abandoned);
                }
            }
        }
    }
}
