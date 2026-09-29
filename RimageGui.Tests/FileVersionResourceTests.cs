using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    /// <summary>
    /// Proves the startup version check reads the backend's version resource
    /// through Windows instead of launching the 25 MB binary, which is what makes
    /// the check cheap on a cold %LocalAppData% copy.
    /// </summary>
    [TestClass]
    public class FileVersionResourceSpecs
    {
        private static string LocateBackend()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 10 && directory != null; depth++)
            {
                var candidate = Path.Combine(directory.FullName, "res", "rimage_x64.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        [TestMethod]
        public void ReadsProductMetadataOfTheRealBackend()
        {
            var backend = LocateBackend();
            if (backend == null)
            {
                var requireBackend =
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RIMAGEGUI_REQUIRE_BACKEND"));
                if (requireBackend)
                {
                    Assert.Fail("res/rimage_x64.exe not found next to the test output; CI requires the backend");
                }

                Assert.Inconclusive("res/rimage_x64.exe not found next to the test output");
            }

            // ProductName identifies the tool, so both fields come from the PE
            // resource rather than from anything this assembly carries.
            Assert.AreEqual("rimage", FileVersionResource.ReadProductName(backend));

            // ProductVersion, not FileVersion, is the field the startup check pins.
            Assert.AreEqual(
                BackendExtractor.ExpectedProductVersion,
                FileVersionResource.ReadProductVersion(backend));
        }

        [TestMethod]
        public void ReadingTheResourceDoesNotLaunchTheExecutable()
        {
            var backend = LocateBackend();
            if (backend == null)
            {
                Assert.Inconclusive("res/rimage_x64.exe not found next to the test output");
            }

            // Warm the file cache first, then measure the read itself.
            FileVersionResource.ReadProductVersion(backend);

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 200; i++)
            {
                FileVersionResource.ReadProductVersion(backend);
            }

            stopwatch.Stop();

            // A launched process costs tens of milliseconds at best and seconds
            // behind an on-access scanner; 200 resource reads must stay far below
            // a single launch.
            Assert.IsTrue(
                stopwatch.ElapsedMilliseconds < 2000,
                $"200 version-resource reads took {stopwatch.ElapsedMilliseconds} ms, which suggests the check is launching the backend again");
        }

        [TestMethod]
        public void UnreadableImagesReportNoVersion()
        {
            Assert.IsNull(FileVersionResource.ReadProductVersion(null));
            Assert.IsNull(FileVersionResource.ReadProductVersion(string.Empty));
            Assert.IsNull(FileVersionResource.ReadProductVersion(
                Path.Combine(Path.GetTempPath(), "rimage-gui-missing-" + Guid.NewGuid().ToString("N") + ".exe")));

            var text = Path.GetTempFileName();
            try
            {
                File.WriteAllText(text, "not a PE image");
                Assert.IsNull(FileVersionResource.ReadProductVersion(text));
            }
            finally
            {
                File.Delete(text);
            }
        }
    }
}
