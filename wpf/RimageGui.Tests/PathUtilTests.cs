using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    [TestClass]
    public class PathUtilSpecs
    {
        [TestMethod]
        public void PlainArguments_StayUnquoted()
        {
            Assert.AreEqual("plain", PathUtil.QuoteArgument("plain"));
            Assert.AreEqual("--quality", PathUtil.QuoteArgument("--quality"));
            Assert.AreEqual("C:\\out", PathUtil.QuoteArgument("C:\\out"));
        }

        [TestMethod]
        public void ArgumentsWithSpaces_AreQuoted()
        {
            Assert.AreEqual("\"with space\"", PathUtil.QuoteArgument("with space"));
        }

        [TestMethod]
        public void TrailingBackslash_OnlyMattersInsideQuotes()
        {
            // Unquoted, a trailing backslash is literal to CommandLineToArgvW;
            // it only escapes the closing quote once the argument is quoted,
            // which is the documented rimage crash with `-d "D:\out\"`.
            Assert.AreEqual("C:\\out\\", PathUtil.QuoteArgument("C:\\out\\"));

            // Quoted because of the space, so the trailing backslash doubles.
            Assert.AreEqual("\"C:\\my out\\\\\"", PathUtil.QuoteArgument("C:\\my out\\"));
        }

        [TestMethod]
        public void EmbeddedQuotes_AreEscaped()
        {
            Assert.AreEqual("\"a\\\"b\"", PathUtil.QuoteArgument("a\"b"));
        }

        [TestMethod]
        public void NullArgument_BecomesEmptyQuotes()
        {
            Assert.AreEqual("\"\"", PathUtil.QuoteArgument(null));
        }

        [TestMethod]
        public void BuildArgumentString_JoinsWithSpaces()
        {
            var line = PathUtil.BuildArgumentString(new[] { "mozjpeg", "--quality", "85", "my file.jpg" });
            Assert.AreEqual("mozjpeg --quality 85 \"my file.jpg\"", line);
        }

        [TestMethod]
        public void DisplayCommandLine_QuotesOnlyWhatNeedsIt()
        {
            // A space-free executable path stays unquoted.
            var line = PathUtil.DisplayCommandLine(@"C:\tools\rimage.exe", new[] { "--quality", "85" });
            Assert.AreEqual(@"C:\tools\rimage.exe --quality 85", line);

            var spaced = PathUtil.DisplayCommandLine(@"C:\my tools\rimage.exe", new[] { "--quality", "85" });
            Assert.AreEqual("\"C:\\my tools\\rimage.exe\" --quality 85", spaced);
        }

        [TestMethod]
        public void NormalizeDirectory_StripsTrailingSeparators_ButKeepsRoots()
        {
            Assert.AreEqual("C:\\out", PathUtil.NormalizeDirectory(@"C:\out\"));
            Assert.AreEqual("C:\\out", PathUtil.NormalizeDirectory(@"C:\out\\"));
            Assert.AreEqual("C:\\", PathUtil.NormalizeDirectory(@"C:\"));
            Assert.AreEqual(string.Empty, PathUtil.NormalizeDirectory(null));
            Assert.AreEqual(string.Empty, PathUtil.NormalizeDirectory("   "));
        }

        [TestMethod]
        public void Key_IsCaseInsensitive_AndSeparatorAgnostic()
        {
            Assert.AreEqual(PathUtil.Key(@"C:\A\B.JPG"), PathUtil.Key(@"c:\a\b.jpg"));
            Assert.AreEqual(PathUtil.Key(@"C:\A\B"), PathUtil.Key(@"C:/A/B"));
            Assert.AreEqual(PathUtil.Key(@"C:\A\B"), PathUtil.Key(@"C:\A\B\"));
            Assert.AreEqual(string.Empty, PathUtil.Key(null));
        }

        [TestMethod]
        public void Shorten_ElidesTheMiddle()
        {
            Assert.AreEqual("short.png", PathUtil.Shorten("short.png", 64));
            Assert.AreEqual(string.Empty, PathUtil.Shorten(null));

            var longPath = @"C:\" + new string('a', 100) + ".png";
            var shortened = PathUtil.Shorten(longPath, 20);
            Assert.AreEqual(20, shortened.Length);
            StringAssert.Contains(shortened, "…");
        }
    }
}
