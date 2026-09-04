using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimageGui.I18n
{
    /// <summary>
    /// The languages the catalog can serve. <see cref="System"/> resolves from
    /// the OS UI culture; every other member must have a map registered in
    /// <see cref="Strings"/> (one partial file per language).
    /// </summary>
    public enum Language
    {
        System,
        Chinese,
        English
    }

    /// <summary>
    /// Per-language string catalog served from memory, so the shipped product
    /// stays a single self-contained executable with no satellite assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each language lives in its own partial file holding one flat
    /// key&nbsp;→&nbsp;value dictionary (<c>Strings.Zh.cs</c>,
    /// <c>Strings.En.cs</c>, …). Keys are shared across languages; a key that a
    /// language has not translated yet falls back to English, then Chinese, so
    /// a new translation can land incrementally without blocking a release.
    /// </para>
    /// <para>
    /// ══ Adding a language ══
    /// 1. Add a member to the <see cref="Language"/> enum.
    /// 2. Add a partial file, e.g. <c>Strings.Ja.cs</c>, with a
    ///    <c>JapaneseMap</c> dictionary (copy any existing one as the skeleton).
    /// 3. Register it in the <see cref="Maps"/> table below and list it in
    ///    <see cref="Languages"/>.
    /// <see cref="Effective"/> maps the OS UI culture to the new language for
    /// <see cref="Language.System"/>.
    /// StringsTests enforces that zh/en stay key-complete, that no value is
    /// empty, and that every key the app actually uses resolves in every
    /// registered language.
    /// </para>
    /// </remarks>
    public static partial class Strings
    {
        /// <summary>Language → values. Registering here is what makes a language available.</summary>
        private static readonly Dictionary<Language, IReadOnlyDictionary<string, string>> Maps =
            new Dictionary<Language, IReadOnlyDictionary<string, string>>();

        /// <summary>Languages a catalog exists for (<see cref="Language.System"/> excluded).</summary>
        public static IReadOnlyCollection<Language> Languages { get; } =
            Array.AsReadOnly(new[] { Language.Chinese, Language.English });

        /// <summary>
        /// Registered inside the explicit static constructor because static
        /// field initializers run before it in every partial file — reading the
        /// per-language maps from a field initializer would capture nulls.
        /// </summary>
        static Strings()
        {
            Maps[Language.Chinese] = ChineseMap;
            Maps[Language.English] = EnglishMap;
        }

        /// <summary>Resolves <see cref="Language.System"/> from the current UI culture.</summary>
        public static Language Effective(Language language)
        {
            if (language != Language.System)
            {
                return language;
            }

            var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase)
                ? Language.Chinese
                : Language.English;
        }

        /// <summary>
        /// Looks a key up for the requested language, falling back to English
        /// and then Chinese so an unfinished translation degrades gracefully.
        /// A key unknown to the whole catalog surfaces as <c>!key</c> instead
        /// of rendering blank.
        /// </summary>
        public static string Get(Language language, string key)
        {
            if (key == null)
            {
                return string.Empty;
            }

            if (TryGet(Effective(language), key, out var value))
            {
                return value;
            }

            if (TryGet(Language.English, key, out value) ||
                TryGet(Language.Chinese, key, out value))
            {
                return value;
            }

            return $"!{key}";
        }

        /// <summary>The exact values registered for one language, no fallback.</summary>
        public static IReadOnlyDictionary<string, string> CatalogFor(Language language)
        {
            if (language == Language.System || !Maps.TryGetValue(language, out var map))
            {
                throw new ArgumentException($"no catalog registered for {language}", nameof(language));
            }

            return map;
        }

        private static bool TryGet(Language language, string key, out string value)
        {
            value = null;
            return language != Language.System
                && Maps.TryGetValue(language, out var map)
                && map.TryGetValue(key, out value);
        }
    }
}
