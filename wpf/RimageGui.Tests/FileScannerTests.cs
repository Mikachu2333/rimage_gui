using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    [TestClass]
    public class FileScannerTests
    {
        private string _root;

        [TestInitialize]
        public void Initialize()
        {
            _root = Path.Combine(Path.GetTempPath(), "rimage-gui-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
                // Temp leftovers are cleaned by the OS.
            }
        }

        private string Create(params string[] relativePath)
        {
            var path = Path.Combine(new[] { _root }.Concat(relativePath).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "placeholder");
            return path;
        }

        [TestMethod]
        public void DirectFiles_AreFilteredByExtension()
        {
            var image = Create("a.jpg");
            var text = Create("notes.txt");

            var result = FileScanner.Collect(new[] { image, text }, null, System.Threading.CancellationToken.None);

            CollectionAssert.AreEqual(new[] { image }, result.Found);
            Assert.AreEqual(1, result.Skipped);
        }

        [TestMethod]
        public void Folders_AreWalkedRecursively()
        {
            var nested = Create("nested", "deeper", "d.bmp");
            Create("nested", "e.tiff");
            Create("nested", "ignored.exe");

            var result = FileScanner.Collect(new[] { _root }, null, System.Threading.CancellationToken.None);

            Assert.IsTrue(result.Found.Contains(nested), "recursion missed the nested image");
            Assert.AreEqual(2, result.Found.Count, string.Join(", ", result.Found));
            Assert.AreEqual(1, result.Skipped);
        }

        [TestMethod]
        public void MixedInput_CountsSkipsAndDeduplicates()
        {
            var image = Create("dup.jpg");
            Create("other.png");
            Create("skipme.txt");

            var result = FileScanner.Collect(
                new[] { _root, _root, image, image },
                null,
                System.Threading.CancellationToken.None);

            // The folder root is walked once per occurrence, so the unsupported
            // file is counted twice; the duplicate file arguments add nothing.
            Assert.AreEqual(2, result.Found.Count, string.Join(", ", result.Found));
            Assert.AreEqual(2, result.Skipped);
        }

        [TestMethod]
        public void AllWhitelistedExtensions_AreAccepted()
        {
            // Every entry is empirically verified against the shipped backend.
            // qoi/ppm/farbfeld are deliberately absent: the GUI neither accepts
            // them as input nor offers them as output.
            var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".avif", ".bmp", ".tif", ".tiff", ".psd", ".jxl", ".hdr" };
            foreach (var extension in extensions)
            {
                Assert.IsTrue(FileScanner.IsSupported("x" + extension), extension);
                Assert.IsTrue(FileScanner.IsSupported("X" + extension.ToUpperInvariant()), extension);
            }

            Assert.IsFalse(FileScanner.IsSupported("x.qoi"));
            Assert.IsFalse(FileScanner.IsSupported("x.ppm"));
            Assert.IsFalse(FileScanner.IsSupported("x.ff"));
            Assert.IsFalse(FileScanner.IsSupported("x.txt"));
            Assert.IsFalse(FileScanner.IsSupported("x"));
            Assert.IsFalse(FileScanner.IsSupported(null));
        }

        [TestMethod]
        public void Progress_IsReportedThrottled()
        {
            for (var index = 0; index < 300; index++)
            {
                Create("bulk", "img" + index + ".png");
            }

            var reports = 0;
            var result = FileScanner.Collect(
                new[] { _root },
                _ => reports++,
                System.Threading.CancellationToken.None);

            Assert.AreEqual(300, result.Found.Count);
            Assert.IsTrue(reports > 0 && reports < 300, "throttling failed: " + reports);
        }

        [TestMethod]
        public void MissingPaths_AreIgnored()
        {
            var result = FileScanner.Collect(
                new[] { Path.Combine(_root, "does-not-exist.jpg"), "   ", null },
                null,
                System.Threading.CancellationToken.None);

            Assert.AreEqual(0, result.Found.Count);
            Assert.AreEqual(0, result.Skipped);
        }
    }
}
