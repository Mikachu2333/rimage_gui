using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimageGui.I18n;
using RimageGui.Models;

namespace RimageGui.Tests
{
    /// <summary>
    /// Guards the i18n workflow: the shipped catalogs stay in sync, and every
    /// key the app actually references (XAML, view models, validators, dynamic
    /// families) resolves in every registered language — which is what makes
    /// adding a new language file safe.
    /// </summary>
    [TestClass]
    public class StringsSpecs
    {
        [TestMethod]
        public void ShippedCatalogs_HaveIdenticalKeySets()
        {
            var zh = Strings.CatalogFor(Language.Chinese).Keys;
            var en = Strings.CatalogFor(Language.English).Keys;

            var missingInEnglish = zh.Except(en).ToList();
            var missingInChinese = en.Except(zh).ToList();

            Assert.AreEqual(0, missingInEnglish.Count, "keys missing from English: " + string.Join(", ", missingInEnglish));
            Assert.AreEqual(0, missingInChinese.Count, "keys missing from Chinese: " + string.Join(", ", missingInChinese));
        }

        [TestMethod]
        public void NoCatalogValue_IsEmpty()
        {
            foreach (var language in Strings.Languages)
            {
                foreach (var pair in Strings.CatalogFor(language))
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(pair.Value),
                        language + "/" + pair.Key + " is empty");
                }
            }
        }

        [TestMethod]
        public void UnknownKey_SurfacesTheMarker()
        {
            Assert.AreEqual("!NoSuchKeyAnywhere", Strings.Get(Language.Chinese, "NoSuchKeyAnywhere"));
            Assert.AreEqual(string.Empty, Strings.Get(Language.Chinese, null));
        }

        [TestMethod]
        public void CatalogFor_System_IsRejected()
        {
            Assert.ThrowsException<ArgumentException>(() => Strings.CatalogFor(Language.System));
        }

        [TestMethod]
        public void EveryKeyUsedByTheApp_ResolvesInEveryLanguage()
        {
            var used = CollectUsedKeys().ToHashSet(StringComparer.Ordinal);
            Assert.IsTrue(used.Count > 100, "the scanner found suspiciously few keys: " + used.Count);

            foreach (var language in Strings.Languages)
            {
                var catalog = Strings.CatalogFor(language);
                var missing = used.Where(key => !catalog.ContainsKey(key)).ToList();
                Assert.AreEqual(0, missing.Count,
                    language + " is missing keys: " + string.Join(", ", missing));
            }
        }

        [TestMethod]
        public void SystemLanguage_ResolvesToAShippedLanguage()
        {
            Assert.IsTrue(Strings.Languages.Contains(Strings.Effective(Language.System)));
        }

        /// <summary>
        /// Walks the app sources and collects every localization key that is
        /// referenced statically, plus the dynamic families built by
        /// concatenation ("FormatHint" + format, "Status" + status).
        /// </summary>
        private static IEnumerable<string> CollectUsedKeys()
        {
            var root = LocateProjectRoot();
            var appRoot = Directory.Exists(Path.Combine(root, "RimageGui"))
                ? Path.Combine(root, "RimageGui")
                : Path.Combine(root, "wpf");
            var sources = Directory
                .EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !path.Contains("RimageGui.Tests"))
                .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var patterns = new[]
            {
                new Regex(@"\{loc:Loc\s+([A-Za-z0-9_]+)\s*\}"),
                new Regex(@"Loc\.I\[\s*""([^""]+)""\s*\]"),
                new Regex(@"\.Fail\(\s*""([A-Za-z0-9_]+)""\s*\)"),
            };

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in sources)
            {
                foreach (var line in File.ReadLines(file))
                {
                    foreach (var pattern in patterns)
                    {
                        foreach (Match match in pattern.Matches(line))
                        {
                            used.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            // Keys built by concatenation cannot be scanned as literals; the
            // families below are the ones the code composes at runtime.
            foreach (var format in FormatInfo.All)
            {
                used.Add("FormatHint" + format);
            }

            foreach (FileStatus status in Enum.GetValues(typeof(FileStatus)))
            {
                used.Add("Status" + status);
            }

            return used;
        }

        private static string LocateProjectRoot()
        {
            // CI can point the scanner at a copied source tree that has no
            // bin/obj walking path from the test output.
            var overridden = Environment.GetEnvironmentVariable("RIMAGEGUI_REPO_ROOT");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return Path.GetFullPath(overridden);
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; depth < 10 && directory != null; depth++)
            {
                var marker = Path.Combine(directory.FullName, "RimageGui", "RimageGui.csproj");
                if (File.Exists(marker))
                {
                    return directory.FullName;
                }

                marker = Path.Combine(directory.FullName, "wpf", "RimageGui", "RimageGui.csproj");
                if (File.Exists(marker))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("the repository root (RimageGui/RimageGui.csproj) was not found");
            return null;
        }
    }
}
