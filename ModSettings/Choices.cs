using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModSettings
{
    // Pure helpers (no Unity, no MelonLoader), so Tests/ can run them outside the game.
    internal static class Choices
    {
        // String settings store choices as plain text. Our mods list the options in the description, in one of these
        // shapes: "TipFirst (...), Natural (...) or SpinEnd (...)", "Knuckles = ..., KnucklesReverse = ...",
        // "(X, Y or None)", "Down (default) or Up", "FlipX / FlipY".
        static readonly Regex WordParen = new(@"\b([A-Z][A-Za-z0-9]*)\s*\(");
        static readonly Regex WordEquals = new(@"\b([A-Z][A-Za-z0-9]*)\s*=\s");
        static readonly Regex ParenList = new(@"\(([^()]*\bor\b[^()]*)\)");
        static readonly Regex OrPair = new(@"\b([A-Z][A-Za-z0-9]*)(?:\s*\([^)]*\))?\s+or\s+([A-Z][A-Za-z0-9]*)\b");
        static readonly Regex SlashPair = new(@"\b([A-Z][A-Za-z0-9]+)\s*/\s*([A-Z][A-Za-z0-9]+)\b");
        static readonly Regex Token = new(@"^[A-Z][A-Za-z0-9]*$");
        static readonly Regex Hex = new(@"^#[0-9A-Fa-f]{6}$");

        // The options for a string setting, or null when the description doesn't list options that include the
        // current value (then the setting is shown read-only).
        public static List<string> FromDescription(string desc, string current)
        {
            if (string.IsNullOrEmpty(desc) || string.IsNullOrEmpty(current)) return null;
            var found = new List<string>();
            void Add(string s)
            {
                s = s.Trim();
                if (Token.IsMatch(s) && !found.Exists(x => x.Equals(s, StringComparison.OrdinalIgnoreCase))) found.Add(s);
            }
            foreach (Match m in WordParen.Matches(desc)) Add(m.Groups[1].Value);
            foreach (Match m in WordEquals.Matches(desc)) Add(m.Groups[1].Value);
            foreach (Match m in ParenList.Matches(desc))
                foreach (var part in Regex.Split(m.Groups[1].Value, @",|\bor\b|/")) Add(part);
            foreach (Match m in OrPair.Matches(desc)) { Add(m.Groups[1].Value); Add(m.Groups[2].Value); }
            foreach (Match m in SlashPair.Matches(desc)) { Add(m.Groups[1].Value); Add(m.Groups[2].Value); }
            if (found.Count < 2 || !found.Exists(x => x.Equals(current, StringComparison.OrdinalIgnoreCase))) return null;
            return found;
        }

        public static bool IsColor(string s) => s != null && Hex.IsMatch(s.Trim());
        public static bool IsColorSetting(string value, string defaultValue, string description) => IsColor(value) ||
            (string.Equals(defaultValue, "Default", StringComparison.OrdinalIgnoreCase) &&
             description != null && description.Contains("#RRGGBB", StringComparison.OrdinalIgnoreCase));

        public static readonly string[] Palette =
        {
            "#8A0F0F", "#B01818", "#5A080A", "#414141", "#202020", "#C0C0C0", "#1A2340", "#3A4A20", "#4A3020",
        };

        // Next/previous entry of a list, starting from the current value (or the first entry if it isn't in it).
        public static string Cycle(IList<string> list, string current, int dir)
        {
            int i = -1;
            for (int k = 0; k < list.Count; k++)
                if (string.Equals(list[k], current?.Trim(), StringComparison.OrdinalIgnoreCase)) { i = k; break; }
            if (i < 0) return list[dir > 0 ? 0 : list.Count - 1];
            return list[((i + dir) % list.Count + list.Count) % list.Count];
        }

        // Step sizes for a number, from its default value so they don't jump when the value crosses a decade:
        // default 18 -> 1 / 10, 0.6 -> 0.01 / 0.1, 0.04 -> 0.001 / 0.01, 0 -> 0.01 / 0.1.
        public static (double small, double big) Steps(double def, double current, bool integer)
        {
            if (integer) return (1, 10);
            double b = Math.Abs(def) > 1e-9 ? Math.Abs(def) : Math.Abs(current) > 1e-9 ? Math.Abs(current) : 0.1;
            double decade = Math.Pow(10, Math.Floor(Math.Log10(b)));
            return (decade / 10, decade);
        }

        // value + step, rounded to the step's precision (no 0.30000001).
        public static double Add(double value, double step)
        {
            int dec = Math.Max(0, (int)-Math.Floor(Math.Log10(Math.Abs(step)) + 1e-9));
            return Math.Round(value + step, Math.Min(dec + 1, 12));
        }

        public static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        public static string StepLabel(double step, int sign) => (sign > 0 ? "+" : "-") + Num(step);

        public static bool NeedsRestart(string desc) =>
            desc != null && desc.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool ManagedByMod(string desc) =>
            desc != null && (desc.StartsWith("Managed by the mod", StringComparison.OrdinalIgnoreCase) ||
                             desc.IndexOf("Written by the mod", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
