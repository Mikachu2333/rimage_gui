using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;
using RimageGui.Models;

namespace RimageGui.Tests
{
    [TestClass]
    public class ValidatorTests
    {
        private static ProcessingOptions Base() => new ProcessingOptions
        {
            Format = OutputFormat.MozJpeg,
            Quality = 85,
            Suffix = "_new",
            OutputMode = OutputMode.OriginalDir,
            Threads = 4
        };

        private static ValidationResult Validate(ProcessingOptions options, params string[] files)
        {
            return Validator.ValidateJob(new JobSpec(files, options));
        }

        [TestMethod]
        public void EmptyJob_Fails()
        {
            var result = Validate(Base());
            Assert.AreEqual("ErrorNoFiles", result.MessageKey);
        }

        [TestMethod]
        public void ValidJob_Passes()
        {
            var result = Validate(Base(), "C:\\in\\a.jpg");
            Assert.IsTrue(result.IsValid, result.MessageKey);
        }

        [TestMethod]
        public void QualityRange_IsCheckedOnlyForLossyFormats()
        {
            var lossy = Base();
            lossy.Quality = 0;
            Assert.AreEqual("ErrorQuality", Validate(lossy, "C:\\in\\a.jpg").MessageKey);

            var lossless = Base();
            lossless.Format = OutputFormat.Png;
            lossless.Quality = 0; // ignored by rimage, so the GUI must not reject it
            Assert.IsTrue(Validate(lossless, "C:\\in\\a.png").IsValid);
        }

        [TestMethod]
        public void DitheringRequiresQuantization()
        {
            var options = Base();
            options.Quantization = null;
            options.Dithering = 50;
            Assert.AreEqual("ErrorDithering", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.Quantization = 90;
            Assert.IsTrue(Validate(options, "C:\\in\\a.jpg").IsValid);
        }

        [TestMethod]
        public void InvalidSuffix_IsRejected()
        {
            foreach (var suffix in new[] { "a/b", "a\\b", "a:b", "a*b", "a?b", "a\"b", "a<b", "a>b", "a|b", "con", "aux", "end.", "end " })
            {
                var options = Base();
                options.Suffix = suffix;
                Assert.AreEqual("ErrorSuffix", Validate(options, "C:\\in\\a.jpg").MessageKey, suffix);
            }
        }

        [TestMethod]
        public void BackupAndSuffix_AreMutuallyExclusive()
        {
            var options = Base();
            options.OriginalPolicy = OriginalPolicy.Backup;
            Assert.AreEqual("ErrorBackupSuffixConflict", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.Suffix = null;
            Assert.IsTrue(Validate(options, "C:\\in\\a.jpg").IsValid);
        }

        [TestMethod]
        public void SelectedDirectory_MustExist()
        {
            var options = Base();
            options.OutputMode = OutputMode.SelectedDir;
            options.OutputDirectory = null;
            Assert.AreEqual("ErrorOutputDirectory", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.OutputDirectory = Path.Combine(Path.GetTempPath(), "rimage-gui-tests-missing-" + Guid.NewGuid().ToString("N"));
            Assert.AreEqual("ErrorOutputDirectoryMissing", Validate(options, "C:\\in\\a.jpg").MessageKey);
        }

        [TestMethod]
        public void ThreadsRange_IsChecked_OnlyWhenManual()
        {
            var options = Base();
            options.Threads = 0;
            Assert.AreEqual("ErrorThreads", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.Threads = 257;
            Assert.AreEqual("ErrorThreads", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.Threads = null;
            Assert.IsTrue(Validate(options, "C:\\in\\a.jpg").IsValid);
        }

        [TestMethod]
        public void InvalidResizeArgs_AreRejected()
        {
            var options = Base();
            options.ResizeMode = ResizeMode.Classic;
            options.ResizeArgs = "banana";
            Assert.AreEqual("ErrorResize", Validate(options, "C:\\in\\a.jpg").MessageKey);

            options.ResizeMode = ResizeMode.Bounds;
            options.BoundValue = 0;
            Assert.AreEqual("ErrorSizeBounds", Validate(options, "C:\\in\\a.jpg").MessageKey);
        }

        [TestMethod]
        public void DuplicateOutputs_AreRejected()
        {
            var options = Base();
            options.Suffix = null;
            options.OutputMode = OutputMode.SelectedDir;
            options.OutputDirectory = Path.GetTempPath();

            var result = Validate(options, "C:\\one\\a.jpg", "C:\\two\\a.jpg");
            Assert.AreEqual("ErrorDuplicateOutput", result.MessageKey);
        }

        [TestMethod]
        public void OutputClobberingAnotherInput_IsRejected()
        {
            var options = Base();
            options.Format = OutputFormat.Jpeg;
            options.Suffix = null;

            // a.png predicts to a.jpg, which is another input in the batch.
            // a.png must come first so the collision is not reported as a
            // duplicate output instead.
            var result = Validate(options, "C:\\in\\a.png", "C:\\in\\a.jpg");
            Assert.AreEqual("ErrorOutputOverwritesInput", result.MessageKey);
        }

        [TestMethod]
        public void DeletePolicy_RejectsInPlaceOutput()
        {
            var options = Base();
            options.Format = OutputFormat.Jpeg;
            options.Suffix = null;
            options.OriginalPolicy = OriginalPolicy.DeleteAfterVerifiedSuccess;

            var result = Validate(options, "C:\\in\\a.jpg");
            Assert.AreEqual("ErrorUnsafeDelete", result.MessageKey);
        }

        [TestMethod]
        public void PreserveStructure_SkipsFlatCollisionCheck()
        {
            var options = Base();
            options.OutputMode = OutputMode.SelectedDir;
            options.OutputDirectory = Path.GetTempPath();
            options.PreserveStructure = true;

            var result = Validate(options, "C:\\one\\a.jpg", "C:\\two\\a.jpg");
            Assert.IsTrue(result.IsValid, result.MessageKey);
        }

        [TestMethod]
        public void PredictedOutputPath_FollowsFormatAndSuffix()
        {
            var options = Base();

            Assert.AreEqual("C:\\in\\a_new.jpg", Validator.PredictedOutputPath("C:\\in\\a.jpg", options));
            options.Format = OutputFormat.JpegXl;
            Assert.AreEqual("C:\\in\\a_new.jxl", Validator.PredictedOutputPath("C:\\in\\a.jpg", options));
            options.Format = OutputFormat.OxiPng;
            Assert.AreEqual("C:\\in\\a_new.png", Validator.PredictedOutputPath("C:\\in\\a.jpg", options));
            options.Suffix = null;
            Assert.AreEqual("C:\\in\\a.png", Validator.PredictedOutputPath("C:\\in\\a.jpg", options));

            options.OutputMode = OutputMode.SelectedDir;
            options.OutputDirectory = "C:\\out";
            Assert.AreEqual("C:\\out\\a.png", Validator.PredictedOutputPath("C:\\in\\a.jpg", options));
        }

        [TestMethod]
        public void SafeToDelete_IsConservative()
        {
            Assert.IsFalse(Validator.SafeToDelete("C:\\in\\a.jpg", null, cancelled: false));
            Assert.IsFalse(Validator.SafeToDelete("C:\\in\\a.jpg", "C:\\in\\a.jpg", cancelled: false));
            Assert.IsFalse(Validator.SafeToDelete("C:\\in\\a.jpg", "C:\\nowhere\\a.jpg", cancelled: false));
            Assert.IsFalse(Validator.SafeToDelete("C:\\in\\a.jpg", "C:\\in\\a.jpg", cancelled: true));

            var existing = Path.GetTempFileName();
            try
            {
                // SafeToDelete requires a non-empty output, and GetTempFileName
                // creates an empty file, so give it real content first.
                File.WriteAllText(existing, "real output");
                Assert.IsTrue(Validator.SafeToDelete("C:\\in\\a.jpg", existing, cancelled: false));

                File.WriteAllText(existing, string.Empty);
                Assert.IsFalse(Validator.SafeToDelete("C:\\in\\a.jpg", existing, cancelled: false));
            }
            finally
            {
                File.Delete(existing);
            }
        }

        [TestMethod]
        public void ResizeArgs_AreNormalized()
        {
            Assert.IsTrue(Validator.NormalizeResizeArg("720x", out var widthOnly));
            Assert.AreEqual("720w", widthOnly);

            Assert.IsTrue(Validator.NormalizeResizeArg("720x_", out var legacy));
            Assert.AreEqual("720w", legacy);

            Assert.IsTrue(Validator.NormalizeResizeArg("1920X1080", out var fixedSize));
            Assert.AreEqual("1920x1080", fixedSize);

            Assert.IsTrue(Validator.NormalizeResizeArg("@0.5", out var factor));
            Assert.AreEqual("@0.5", factor);

            Assert.IsTrue(Validator.NormalizeResizeArg("150%", out var percent));
            Assert.AreEqual("150%", percent);

            Assert.IsTrue(Validator.NormalizeResizeArg("1000L", out var longest));
            Assert.AreEqual("1000l", longest);

            Assert.IsTrue(Validator.NormalizeResizeArg("500S", out var shortest));
            Assert.AreEqual("500s", shortest);

            Assert.IsFalse(Validator.NormalizeResizeArg("banana", out _));
            Assert.IsFalse(Validator.NormalizeResizeArg("0", out _));
            Assert.IsFalse(Validator.NormalizeResizeArg("1.5w", out _));
            Assert.IsFalse(Validator.NormalizeResizeArg("", out _));
            Assert.IsFalse(Validator.NormalizeResizeArg(null, out _));

            Assert.IsTrue(Validator.SplitResizeArgs("@1.5 50%", out var chain));
            CollectionAssert.AreEqual(new[] { "@1.5", "50%" }, chain);

            Assert.IsFalse(Validator.SplitResizeArgs("   ", out _));
            Assert.IsFalse(Validator.SplitResizeArgs("@1.5 oops", out _));
        }
    }
}
