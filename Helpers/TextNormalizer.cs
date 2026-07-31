using System.Text.RegularExpressions;

namespace VcfEditor.Helpers
{
    public static partial class TextNormalizer
    {
        // This eliminates runtime regex compilation overhead and produces faster, allocation-free matchers.
        [GeneratedRegex("[أإآ]")]
        private static partial Regex AlefRegex();

        [GeneratedRegex("[\u064B-\u065F]")]
        private static partial Regex DiacriticsRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Collapse repeated whitespace using the source-generated regex.
            var normalized = WhitespaceRegex().Replace(input.Trim(), " ").ToLowerInvariant();

            // Normalize Alef: replace أ, إ, آ with ا
            normalized = AlefRegex().Replace(normalized, "ا");

            // Normalize Yeh: map ى to ي for broader matching
            normalized = normalized.Replace("ى", "ي");

            // Normalize Teh Marbuta: replace ة with ه
            normalized = normalized.Replace("ة", "ه");

            // Remove Tatweel (Kashida)
            normalized = normalized.Replace("ـ", "");

            // Remove Diacritics (Tashkeel) — Fatha, Damma, Kasra, etc.
            normalized = DiacriticsRegex().Replace(normalized, "");

            return normalized;
        }
    }
}
