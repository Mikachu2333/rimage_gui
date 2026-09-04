using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Models;

namespace RimageGui.Tests
{
    /// <summary>
    /// The CLI names and predicted extensions must match rimage 0.13 exactly:
    /// the name is the sub-command word, the extension drives pre-flight
    /// collision checks and the fallback output path.
    /// </summary>
    [TestClass]
    public class FormatInfoSpecs
    {
        [TestMethod]
        public void AllFormats_AreListed()
        {
            CollectionAssert.AllItemsAreUnique(FormatInfo.All.ToList());
            Assert.AreEqual(7, FormatInfo.All.Count);
        }

        [TestMethod]
        public void CliNames_MatchRimageSubcommands()
        {
            CollectionAssert.AreEqual(
                new[] { "mozjpeg", "jpeg", "oxipng", "png", "webp", "avif", "jpeg_xl" },
                FormatInfo.All.Select(format => format.CliName()).ToArray());
        }

        [TestMethod]
        public void Extensions_MatchRimageOutputs()
        {
            var expected = new[] { "jpg", "jpg", "png", "png", "webp", "avif", "jxl" };

            for (var index = 0; index < FormatInfo.All.Count; index++)
            {
                Assert.AreEqual(expected[index], FormatInfo.All[index].Extension(),
                    FormatInfo.All[index].ToString());
            }
        }

        [TestMethod]
        public void QualitySupport_FollowsRimageFlags()
        {
            var lossy = new[] { OutputFormat.MozJpeg, OutputFormat.Jpeg, OutputFormat.WebP, OutputFormat.Avif };
            var lossless = new[] { OutputFormat.OxiPng, OutputFormat.Png, OutputFormat.JpegXl };

            foreach (var format in lossy)
            {
                Assert.IsTrue(format.SupportsQuality(), format.ToString());
            }

            foreach (var format in lossless)
            {
                Assert.IsFalse(format.SupportsQuality(), format.ToString());
            }
        }

        [TestMethod]
        public void SwitchableLossless_IsWebPOnly()
        {
            foreach (var format in FormatInfo.All)
            {
                Assert.AreEqual(format == OutputFormat.WebP, format.SupportsLossless(),
                    format.ToString());
            }
        }
    }
}
