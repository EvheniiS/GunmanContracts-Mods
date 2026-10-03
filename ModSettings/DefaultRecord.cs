using System;
using System.Collections.Generic;
using System.Text;

namespace ModSettings
{
    enum DefaultChange { Record, Same, AlreadyNew, AutoUpdate, Ask }

    // The file behind DefaultChanges (UserData/ModSettings_defaults.txt): the default each setting had when the player
    // last saw it, plus every mod's version. One "key<TAB>value" per line, versions as "@Mod<TAB>1.2.0", so it can be
    // read and edited by hand. No MelonLoader types here, so the tests can run it.
    internal static class DefaultRecord
    {
        // known = the recorded default (null = never seen); all three are the entries' TOML strings.
        public static DefaultChange Decide(string known, string newDefault, string value)
        {
            if (known == null) return DefaultChange.Record;
            if (known == newDefault) return DefaultChange.Same;
            if (value == newDefault) return DefaultChange.AlreadyNew;
            return value == known ? DefaultChange.AutoUpdate : DefaultChange.Ask;
        }

        public static void Parse(IEnumerable<string> lines, Dictionary<string, string> defaults, Dictionary<string, string> versions)
        {
            foreach (var raw in lines)
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                string key = Unescape(raw[..tab]), val = Unescape(raw[(tab + 1)..]);
                if (key[0] == '@') { if (key.Length > 1) versions[key[1..]] = val; }
                else defaults[key] = val;
            }
        }

        public static List<string> Format(Dictionary<string, string> defaults, Dictionary<string, string> versions)
        {
            var lines = new List<string>
            {
                "# Mod Settings: the default each setting had when you last saw it, and each mod's version.",
                "# When a mod update changes a default, the board shows it. Delete this file to start over.",
            };
            var keys = new List<string>(versions.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) lines.Add(Escape("@" + k) + "\t" + Escape(versions[k]));
            keys = new List<string>(defaults.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) lines.Add(Escape(k) + "\t" + Escape(defaults[k]));
            return lines;
        }

        public static string Escape(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0) return s;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
                sb.Append(c switch { '\\' => "\\\\", '\t' => "\\t", '\n' => "\\n", '\r' => "\\r", _ => c.ToString() });
            return sb.ToString();
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                sb.Append(n switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => n });
            }
            return sb.ToString();
        }
    }
}
