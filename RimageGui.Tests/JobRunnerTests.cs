using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;
using RimageGui.Models;

namespace RimageGui.Tests
{
    /// <summary>
    /// Integration test: drives the real rimage binary through the runner so
    /// the process plumbing, metadata parsing and per-file resolution are all
    /// exercised together.
    /// </summary>
    [TestClass]
    public class JobRunnerSpecs
    {
        private sealed class SyncProgress : IProgress<JobReport>
        {
            public List<JobReport> Reports { get; } = new List<JobReport>();

            public void Report(JobReport value) => Reports.Add(value);
        }

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
        public async Task RunAsync_ResolvesPerFile_AndCollectsFailures()
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

            var root = Path.Combine(Path.GetTempPath(), "rimage-gui-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var good = Path.Combine(root, "good.bmp");
                using (var bitmap = new Bitmap(8, 8))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Coral);
                    bitmap.Save(good, ImageFormat.Bmp);
                }

                var bad = Path.Combine(root, "bad.bmp");
                File.WriteAllText(bad, "this is not an image");

                var options = new ProcessingOptions
                {
                    Format = OutputFormat.MozJpeg,
                    Quality = 85,
                    Suffix = "_new",
                    OutputMode = OutputMode.OriginalDir,
                    OriginalPolicy = OriginalPolicy.Keep,
                    ResizeMode = ResizeMode.None,
                    Threads = 1,
                    HideBackendWindow = true
                };
                var job = new JobSpec(new[] { good, bad }, options);

                var progress = new SyncProgress();
                var summary = await JobRunner.RunAsync(
                    job, backend, progress, CancellationToken.None);
                var reports = progress.Reports;

                Assert.AreEqual(1, summary.Succeeded);
                Assert.AreEqual(1, summary.Failed);
                Assert.AreEqual(0, summary.Skipped);
                Assert.IsFalse(summary.Cancelled);

                Assert.AreEqual(1, summary.FailedItems.Count);
                Assert.AreEqual(PathUtil.Key(bad), PathUtil.Key(summary.FailedItems[0].Input));
                Assert.IsFalse(string.IsNullOrEmpty(summary.FailedItems[0].Error));

                Assert.IsTrue(File.Exists(Path.Combine(root, "good_new.jpg")), "expected output missing");
                Assert.IsFalse(File.Exists(Path.Combine(root, "bad_new.jpg")), "failed input produced output");

                CollectionAssert.Contains(
                    reports.Select(r => r.Kind).ToList(),
                    JobReportKind.Progress);
                Assert.IsTrue(reports.Any(r => r.Kind == JobReportKind.Log), "command line was not logged");

                // A partial run is not an aborted one: the good file really did
                // land on disk, and exit code 5 says exactly that.
                Assert.IsFalse(summary.Aborted);
                Assert.IsTrue(string.IsNullOrEmpty(summary.FatalError));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (Exception)
                {
                    // Temp leftovers are cleaned by the OS.
                }
            }
        }

        /// <summary>
        /// Exit code 2 is decided before any file is opened, so it would repeat
        /// for every remaining chunk. The run has to stop and say so instead of
        /// respawning the same rejected command.
        /// </summary>
        [TestMethod]
        public async Task RunAsync_AbortsTheRun_WhenRimageRejectsTheArguments()
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

            var root = Path.Combine(Path.GetTempPath(), "rimage-gui-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var inputs = new List<string>();
                for (var index = 0; index < 3; index++)
                {
                    var path = Path.Combine(root, "in" + index + ".bmp");
                    using (var bitmap = new Bitmap(8, 8))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Coral);
                        bitmap.Save(path, ImageFormat.Bmp);
                    }

                    inputs.Add(path);
                }

                // Quality 0 is outside rimage's accepted range. The GUI's own
                // validation would catch it, but the point here is the backend's
                // answer: clap refuses it and nothing is ever opened.
                var options = new ProcessingOptions
                {
                    Format = OutputFormat.MozJpeg,
                    Quality = 0,
                    OutputMode = OutputMode.OriginalDir,
                    OriginalPolicy = OriginalPolicy.Keep,
                    ResizeMode = ResizeMode.None,
                    Threads = 1,
                    HideBackendWindow = true
                };
                var job = new JobSpec(inputs, options);

                var summary = await JobRunner.RunAsync(
                    job, backend, new SyncProgress(), CancellationToken.None);

                Assert.IsTrue(summary.Aborted, "a usage rejection must stop the run");
                Assert.IsFalse(string.IsNullOrEmpty(summary.FatalError));

                // Chunk size is one file for a small batch, so only the first
                // file is charged with the failure and the rest never started.
                Assert.AreEqual(1, summary.Failed);
                Assert.AreEqual(0, summary.Succeeded);
                Assert.AreEqual(2, summary.Skipped);
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (Exception)
                {
                    // Temp leftovers are cleaned by the OS.
                }
            }
        }
    }
}
