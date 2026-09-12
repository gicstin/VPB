using System;

namespace VPB.src.util
{
    internal static class VpbDiagnosticLogLevel
    {
        public const string Normal = "Normal";
        public const string Extra = "Extra";
        public const string Full = "Full";

        public static string FromFlags(bool extraBundleOn, bool fullBundleOn)
        {
            if (fullBundleOn) return Full;
            if (extraBundleOn) return Extra;
            return Normal;
        }

        public static string Normalize(string level)
        {
            if (string.IsNullOrEmpty(level)) return Normal;
            if (string.Equals(level, Extra, StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "Detailed", StringComparison.OrdinalIgnoreCase))
                return Extra;
            if (string.Equals(level, Full, StringComparison.OrdinalIgnoreCase)
                || string.Equals(level, "Everything", StringComparison.OrdinalIgnoreCase))
                return Full;
            return Normal;
        }

        public static void Decode(string level, out bool extraBundle, out bool fullBundle)
        {
            string n = Normalize(level);
            fullBundle = string.Equals(n, Full, StringComparison.OrdinalIgnoreCase);
            extraBundle = fullBundle || string.Equals(n, Extra, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsElevated(string level)
        {
            return !string.Equals(Normalize(level), Normal, StringComparison.OrdinalIgnoreCase);
        }
    }
}
