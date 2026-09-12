using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;
using RimageGui.I18n;

namespace RimageGui.Tests
{
    /// <summary>
    /// Pins the contract the GUI depends on for telling failures apart: rimage's
    /// exit codes, and the stderr lines that name a file and carry a slug.
    /// </summary>
    [TestClass]
    public class RimageDiagnosticsSpecs
    {
        [TestMethod]
        public void ExitCodes_MapOntoTheDocumentedSet()
        {
            Assert.AreEqual(RimageExitCode.Success, RimageExitCodes.Classify(0));
            Assert.AreEqual(RimageExitCode.Usage, RimageExitCodes.Classify(2));
            Assert.AreEqual(RimageExitCode.Input, RimageExitCodes.Classify(3));
            Assert.AreEqual(RimageExitCode.Output, RimageExitCodes.Classify(4));
            Assert.AreEqual(RimageExitCode.Partial, RimageExitCodes.Classify(5));
            Assert.AreEqual(RimageExitCode.Panic, RimageExitCodes.Classify(101));
            Assert.AreEqual(RimageExitCode.Abort, RimageExitCodes.Classify(134));
            Assert.AreEqual(RimageExitCode.Unexpected, RimageExitCodes.Classify(1));
            Assert.AreEqual(RimageExitCode.Unexpected, RimageExitCodes.Classify(-1));
        }

        [TestMethod]
        public void OnlyCleanNoOps_AreSafeToRetryInFull()
        {
            // 3 and 4 guarantee nothing was written; 5 guarantees something was.
            Assert.IsTrue(RimageExitCodes.IsCleanNoOp(RimageExitCode.Input));
            Assert.IsTrue(RimageExitCodes.IsCleanNoOp(RimageExitCode.Output));
            Assert.IsFalse(RimageExitCodes.IsCleanNoOp(RimageExitCode.Partial));
            Assert.IsFalse(RimageExitCodes.IsCleanNoOp(RimageExitCode.Success));
        }

        [TestMethod]
        public void Usage_Panic_AndUnknown_StopTheRun()
        {
            // These repeat for every later chunk, so respawning is pointless.
            Assert.IsTrue(RimageExitCodes.IsFatal(RimageExitCode.Usage));
            Assert.IsTrue(RimageExitCodes.IsFatal(RimageExitCode.Panic));
            Assert.IsTrue(RimageExitCodes.IsFatal(RimageExitCode.Abort));
            Assert.IsTrue(RimageExitCodes.IsFatal(RimageExitCode.Unexpected));
            Assert.IsFalse(RimageExitCodes.IsFatal(RimageExitCode.Input));
            Assert.IsFalse(RimageExitCodes.IsFatal(RimageExitCode.Output));
            Assert.IsFalse(RimageExitCodes.IsFatal(RimageExitCode.Partial));
        }

        [TestMethod]
        public void Metadata_IsOnlyExpectedWhenSomethingSucceeded()
        {
            Assert.IsTrue(RimageExitCodes.CarriesMetadata(RimageExitCode.Success));
            Assert.IsTrue(RimageExitCodes.CarriesMetadata(RimageExitCode.Partial));
            Assert.IsFalse(RimageExitCodes.CarriesMetadata(RimageExitCode.Input));
            Assert.IsFalse(RimageExitCodes.CarriesMetadata(RimageExitCode.Output));
        }

        [TestMethod]
        public void Parse_ExtractsSlugPathAndHint()
        {
            var root = Path.Combine(Path.GetTempPath(), "rimage-gui-parse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var good = Path.Combine(root, "good.jpg");
                var bad = Path.Combine(root, "bad.jpg");
                File.WriteAllText(bad, "not an image");

                var lines = new[]
                {
                    $"[ERROR rimage::error] input: {Path.GetFullPath(bad)} is a jpeg file, but no decoder for this format is implemented [input.unsupported-format]",
                    "[ERROR rimage::error]   hint: convert the file to a supported format before optimizing it",
                    "[ERROR rimage] 1/2 file(s) failed (1 reading, 0 writing). Run with debug logging for details."
                };

                var errors = RimageErrors.Parse(lines, new[] { good, bad });

                Assert.AreEqual(1, errors.Count, "only the per-file error line is an error");
                Assert.AreEqual(PathUtil.Key(bad), PathUtil.Key(errors[0].Path));
                Assert.AreEqual("input.unsupported-format", errors[0].Slug);
                Assert.IsTrue(errors[0].Message.StartsWith("is a jpeg file", StringComparison.Ordinal),
                    "the path should be stripped off the reason");
                Assert.IsFalse(errors[0].Message.Contains(Path.GetFileName(bad)),
                    "the reason should not repeat the file name");
                Assert.AreEqual("convert the file to a supported format before optimizing it", errors[0].Hint);
            }
            finally
            {
                TryDelete(root);
            }
        }

        [TestMethod]
        public void Parse_AttributesASingleFileBatch_WithoutAPathMatch()
        {
            // A one-file invocation owns every error rimage reported, which is
            // what makes per-file chunking produce per-file reasons.
            var lines = new[]
            {
                "[ERROR rimage::error] input: something unexpected happened [output.encode]"
            };

            var errors = RimageErrors.Parse(lines, new[] { @"C:\only\one.png" });

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(@"C:\only\one.png", errors[0].Path);
        }

        [TestMethod]
        public void Parse_LeavesAmbiguousFailuresUnattributed()
        {
            var lines = new[]
            {
                "[ERROR rimage::error] input: photo.jpg cannot be read [input.open]"
            };

            var errors = RimageErrors.Parse(
                lines,
                new[] { @"C:\a\photo.jpg", @"C:\b\photo.jpg" });

            Assert.AreEqual(1, errors.Count);
            Assert.IsNull(errors[0].Path, "two same-named inputs must not be blamed arbitrarily");
        }

        [TestMethod]
        public void Parse_SkipsNonErrorOutput()
        {
            var errors = RimageErrors.Parse(
                new[] { "[ERROR rimage] 2/2 file(s) failed (2 reading, 0 writing).", string.Empty },
                new[] { @"C:\a.png" });

            Assert.AreEqual(0, errors.Count);
        }

        [TestMethod]
        public void Describe_UsesTheLocalizedSlugText()
        {
            var error = new BackendError
            {
                Slug = "input.size-limit",
                Message = "raw wording from rimage",
                Hint = "shrink the image"
            };

            var text = RimageErrors.Describe(error);

            Assert.IsTrue(text.StartsWith(Loc.I["ErrSlugInputSizeLimit"], StringComparison.Ordinal));
            Assert.IsTrue(text.IndexOf("shrink the image", StringComparison.Ordinal) >= 0, "the hint is worth keeping");
            Assert.IsFalse(text.IndexOf("raw wording", StringComparison.Ordinal) >= 0, "the slug text replaces it");
        }

        [TestMethod]
        public void Describe_FallsBackToRimageWording_ForUnknownSlugs()
        {
            var error = new BackendError { Slug = "brand.new.slug", Message = "some brand new reason" };

            Assert.AreEqual("some brand new reason", RimageErrors.Describe(error));
        }

        [TestMethod]
        public void EverySlug_HasALocalizedDescription()
        {
            var slugs = new[]
            {
                "input.open", "input.decode", "input.unsupported-format", "input.size-limit",
                "input.invalid-resize", "input.configuration",
                "output.io", "output.encode", "output.size-limit", "output.out-of-space"
            };

            foreach (var slug in slugs)
            {
                var text = RimageErrors.Describe(new BackendError { Slug = slug, Message = "x" });
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), slug + " has no description");
                Assert.IsFalse(text.StartsWith("!", StringComparison.Ordinal), slug + " resolves to a missing key");
            }
        }

        [TestMethod]
        public void ExitCodeDescriptions_ResolveInEveryLanguage()
        {
            foreach (var language in Strings.Languages)
            {
                Loc.I.Current = language;
                try
                {
                    Assert.IsFalse(RimageErrors.DescribeExit(RimageExitCode.Partial).StartsWith("!", StringComparison.Ordinal));
                    Assert.IsFalse(RimageErrors.DescribeExit(RimageExitCode.Input).StartsWith("!", StringComparison.Ordinal));
                    Assert.IsFalse(RimageErrors.DescribeExit(RimageExitCode.Output).StartsWith("!", StringComparison.Ordinal));
                    Assert.IsFalse(RimageErrors.DescribeFatal(RimageExitCode.Usage).StartsWith("!", StringComparison.Ordinal));
                    Assert.IsFalse(RimageErrors.DescribeFatal(RimageExitCode.Panic).StartsWith("!", StringComparison.Ordinal));
                }
                finally
                {
                    Loc.I.Current = Language.System;
                }
            }
        }

        private static void TryDelete(string root)
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
