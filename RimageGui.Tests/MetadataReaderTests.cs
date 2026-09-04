using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.Core;

namespace RimageGui.Tests
{
    [TestClass]
    public class MetadataReaderSpecs
    {
        [TestMethod]
        public void ParsesRimageMetadata_IntoAnOutputMap()
        {
            var path = Path.GetTempFileName();
            try
            {
                // The camelCase shape rimage's --metadata writes.
                File.WriteAllText(path,
                    @"{""inputSize"":10,""outputSize"":5,""totalImages"":2," +
                    @"""images"":[{""input"":""C:\\t\\a.jpg"",""output"":""C:\\t\\a_new.jpg""," +
                    @"""inputSize"":10,""outputSize"":5}," +
                    @"{""input"":""C:\\t\\b.png"",""output"":""C:\\t\\b.jxl""}]}");

                var map = MetadataReader.LoadOutputMap(path);

                Assert.IsNotNull(map);
                Assert.AreEqual(2, map.Count);
                Assert.AreEqual("C:\\t\\a_new.jpg", map["c:\\t\\a.jpg"]);
                Assert.AreEqual("C:\\t\\b.jxl", map["c:\\t\\b.png"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void MissingOrCorruptMetadata_ReturnsEmptyMap()
        {
            Assert.AreEqual(0, MetadataReader.LoadOutputMap(Path.Combine(Path.GetTempPath(), "no-such-metadata.json")).Count);

            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{ not json at all ][");
                Assert.AreEqual(0, MetadataReader.LoadOutputMap(path).Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void EntriesWithoutPaths_AreSkipped()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path,
                    @"{""images"":[{""input"":"""",""output"":""C:\\t\\x.jpg""}," +
                    @"{""input"":""C:\\t\\a.jpg"",""output"":""""}," +
                    @"{""input"":""C:\\t\\ok.jpg"",""output"":""C:\\t\\ok.webp""}]}");

                var map = MetadataReader.LoadOutputMap(path);

                CollectionAssert.AreEqual(new[] { "c:\\t\\ok.jpg" }, (List<string>)new List<string>(map.Keys));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
