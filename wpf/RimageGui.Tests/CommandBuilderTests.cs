using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;
using RimageGui.Models;

namespace RimageGui.Tests
{
    /// <summary>
    /// The command line is the GUI's whole contract with rimage, so every flag
    /// every option can produce is pinned here against rimage 0.13's syntax.
    /// </summary>
    [TestClass]
    public class CommandBuilderTests
    {
        private static ProcessingOptions Base() => new ProcessingOptions
        {
            Format = OutputFormat.MozJpeg,
            Quality = 85,
            Quantization = null,
            Dithering = null,
            Suffix = null,
            OutputMode = OutputMode.OriginalDir,
            OutputDirectory = null,
            PreserveStructure = false,
            OriginalPolicy = OriginalPolicy.Keep,
            ResizeMode = ResizeMode.None,
            ResizeArgs = null,
            Filter = ResizeFilter.Lanczos3,
            Threads = 4,
            Hidden = true
        };

        private static List<string> Build(ProcessingOptions options)
        {
            return CommandBuilder.BuildArgs(options, "file.list", "metadata.json");
        }

        private static bool Contains(IReadOnlyList<string> args, string value)
        {
            return args.Contains(value);
        }

        [TestMethod]
        public void DefaultMozJpegInvocation_IsWellFormed()
        {
            var args = Build(Base());

            Assert.AreEqual("mozjpeg", args[0]);
            CollectionAssert.AreEqual(
                new[] { "--threads", "4", "--no-progress", "--metadata", "metadata.json", "file.list" },
                args.Skip(args.Count - 6).ToList());
            Assert.IsTrue(Contains(args, "--quality"));
            Assert.AreEqual("85", args[args.IndexOf("--quality") + 1]);
        }

        [TestMethod]
        public void LossyFormats_PassQuality()
        {
            foreach (var format in new[] { OutputFormat.MozJpeg, OutputFormat.Jpeg, OutputFormat.WebP, OutputFormat.Avif })
            {
                var options = Base();
                options.Format = format;
                var args = Build(options);

                Assert.IsTrue(Contains(args, "--quality"), format.ToString());
            }
        }

        [TestMethod]
        public void LosslessFormats_OmitQuality()
        {
            foreach (var format in new[] { OutputFormat.OxiPng, OutputFormat.Png, OutputFormat.JpegXl })
            {
                var options = Base();
                options.Format = format;
                var args = Build(options);

                Assert.IsFalse(Contains(args, "--quality"), format.ToString());
                Assert.IsFalse(Contains(args, "--lossless"), format.ToString());
            }
        }

        [TestMethod]
        public void WebPQuality100WithoutQuantization_UsesLossless()
        {
            var options = Base();
            options.Format = OutputFormat.WebP;
            options.Quality = 100;
            var args = Build(options);

            Assert.IsTrue(Contains(args, "--lossless"));
            Assert.IsFalse(Contains(args, "--quality"));
        }

        [TestMethod]
        public void WebPQuality100WithQuantization_KeepsQuality()
        {
            var options = Base();
            options.Format = OutputFormat.WebP;
            options.Quality = 100;
            options.Quantization = 90;
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--lossless"));
            Assert.IsTrue(Contains(args, "--quality"));
            Assert.AreEqual("100", args[args.IndexOf("--quality") + 1]);
            Assert.AreEqual("90", args[args.IndexOf("--quantization") + 1]);
        }

        [TestMethod]
        public void WebPQualityBelow100_KeepsQuality()
        {
            var options = Base();
            options.Format = OutputFormat.WebP;
            options.Quality = 85;
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--lossless"));
            Assert.AreEqual("85", args[args.IndexOf("--quality") + 1]);
        }

        [TestMethod]
        public void MozJpegQuality100_StaysLossy()
        {
            var options = Base();
            options.Quality = 100;
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--lossless"));
            Assert.AreEqual("100", args[args.IndexOf("--quality") + 1]);
        }

        [TestMethod]
        public void Quantization_EmitsValue_AndDitheringFollows()
        {
            var options = Base();
            options.Quantization = 90;
            options.Dithering = 90;
            var args = Build(options);

            Assert.AreEqual("90", args[args.IndexOf("--quantization") + 1]);
            Assert.AreEqual("90", args[args.IndexOf("--dithering") + 1]);
        }

        [TestMethod]
        public void DitheringWithoutQuantization_IsDropped()
        {
            var options = Base();
            options.Dithering = 90;
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--dithering"));
            Assert.IsFalse(Contains(args, "--quantization"));
        }

        [TestMethod]
        public void Suffix_EmitsValue_OnlyWhenSet()
        {
            var with = Base();
            with.Suffix = "_new";
            var withArgs = Build(with);
            Assert.AreEqual("_new", withArgs[withArgs.IndexOf("--suffix") + 1]);

            var without = Base();
            Assert.IsFalse(Contains(Build(without), "--suffix"));
        }

        [TestMethod]
        public void BackupPolicy_EmitsBackupFlag()
        {
            var options = Base();
            options.OriginalPolicy = OriginalPolicy.Backup;
            Assert.IsTrue(Contains(Build(options), "--backup"));

            options.OriginalPolicy = OriginalPolicy.Keep;
            Assert.IsFalse(Contains(Build(options), "--backup"));

            options.OriginalPolicy = OriginalPolicy.DeleteAfterVerifiedSuccess;
            Assert.IsFalse(Contains(Build(options), "--backup"));
        }

        [TestMethod]
        public void SelectedDirectory_IsNormalized_AndPreserveStructureAddsMinusR()
        {
            var options = Base();
            options.OutputMode = OutputMode.SelectedDir;
            options.OutputDirectory = @"C:\out\";
            var args = Build(options);

            Assert.AreEqual("C:\\out", args[args.IndexOf("--directory") + 1]);
            Assert.IsFalse(Contains(args, "-r"));

            options.PreserveStructure = true;
            Assert.IsTrue(Contains(Build(options), "-r"));
        }

        [TestMethod]
        public void OriginalDirectoryMode_OmitsDirectoryFlags()
        {
            var options = Base();
            options.OutputDirectory = @"C:\out";
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--directory"));
            Assert.IsFalse(Contains(args, "-r"));
        }

        [TestMethod]
        public void ClassicResize_ChainsSteps_AndAppendsFilter()
        {
            var options = Base();
            options.ResizeMode = ResizeMode.Classic;
            options.ResizeArgs = "@1.5 50%";
            options.Filter = ResizeFilter.Nearest;
            var args = Build(options);

            var firstResize = args.IndexOf("--resize");
            Assert.AreEqual("@1.5", args[firstResize + 1]);
            Assert.AreEqual("50%", args[firstResize + 3]);
            Assert.AreEqual("nearest", args[args.IndexOf("--filter") + 1]);
        }

        [TestMethod]
        public void InvalidClassicResize_EmitsNothing()
        {
            var options = Base();
            options.ResizeMode = ResizeMode.Classic;
            options.ResizeArgs = "not-a-size";
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--resize"));
            Assert.IsFalse(Contains(args, "--filter"));
        }

        [TestMethod]
        public void BoundsResize_MapsDirectionAndEdge()
        {
            var maximum = Base();
            maximum.ResizeMode = ResizeMode.Bounds;
            maximum.BoundValue = 1024;
            maximum.BoundEdge = BoundEdge.Longest;
            maximum.BoundDirection = BoundDirection.Maximum;
            var maxArgs = Build(maximum);
            Assert.AreEqual("1024l", maxArgs[maxArgs.IndexOf("--resize") + 1]);
            Assert.IsTrue(Contains(maxArgs, "--reduce-only"));

            var minimum = Base();
            minimum.ResizeMode = ResizeMode.Bounds;
            minimum.BoundValue = 1024;
            minimum.BoundEdge = BoundEdge.Shortest;
            minimum.BoundDirection = BoundDirection.Minimum;
            var minArgs = Build(minimum);
            Assert.AreEqual("1024s", minArgs[minArgs.IndexOf("--resize") + 1]);
            Assert.IsTrue(Contains(minArgs, "--enlarge-only"));
        }

        [TestMethod]
        public void NoResize_OmitsFilter()
        {
            var options = Base();
            options.Filter = ResizeFilter.Mitchell;
            var args = Build(options);

            Assert.IsFalse(Contains(args, "--resize"));
            Assert.IsFalse(Contains(args, "--filter"));
        }

        [TestMethod]
        public void Threads_ArePassedThrough_AndAutoResolvesFromCpu()
        {
            var options = Base();
            options.Threads = 8;
            var args = Build(options);
            Assert.AreEqual("8", args[args.IndexOf("--threads") + 1]);

            options.Threads = null;
            var expected = Math.Max(1, Environment.ProcessorCount - 1);
            Assert.AreEqual(expected, CommandBuilder.ResolveThreads(options));

            options.Threads = 0;
            Assert.AreEqual(1, CommandBuilder.ResolveThreads(options));
        }
    }
}
