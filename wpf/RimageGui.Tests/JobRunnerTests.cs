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
