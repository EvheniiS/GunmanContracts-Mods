using System;
using System.Collections.Generic;
using System.IO;
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

        // One setting at start: what to do with it, with the record updated to match. Ask leaves the old default recorded,
        // so the setting is asked again at every start until the player decides. recorded = the default remembered before.
        public static DefaultChange Apply(Dictionary<string, string> known, string key, string newDefault, string value, out string recorded)
        {
            known.TryGetValue(key, out recorded);
            var d = Decide(recorded, newDefault, value);
            if (d is DefaultChange.Record or DefaultChange.AlreadyNew or DefaultChange.AutoUpdate) known[key] = newDefault;
            return d;
        }

        // Every loaded mod's version against the recorded one. New versions and new mods are recorded; only a changed
        // version is listed ("Daredevil 1.0.0 -> 1.1.0"). Mods no longer installed stay recorded. Returns whether anything changed.
        public static bool Versions(Dictionary<string, string> recorded, IEnumerable<(string Name, string Version)> mods, List<string> changes)
        {
            bool changed = false;
            foreach (var (name, ver) in mods)
            {
                if (recorded.TryGetValue(name, out var was) && was == ver) continue;
                if (was != null) changes.Add($"{name} {was} -> {ver}");
                recorded[name] = ver;
                changed = true;
            }
            return changed;
        }

        // The file itself. Load returns false when there is none yet: the first run, which only records.
        public static bool Load(string path, Dictionary<string, string> defaults, Dictionary<string, string> versions)
        {
            if (!File.Exists(path)) return false;
            Parse(File.ReadAllLines(path), defaults, versions);
            return true;
        }

        public static void Save(string path, Dictionary<string, string> defaults, Dictionary<string, string> versions) =>
            File.WriteAllLines(path, Format(defaults, versions));

        // Settings-file removal detection. The record keeps, per category, how many settings the player had changed when
        // the game last ran ("~custom.<Category>" in the versions slot). A category that had at least minBefore changed
        // settings and now has none was reset (the settings file was removed or edited back). Returns those categories.
        public static List<string> ResetCategories(Dictionary<string, string> recorded, Dictionary<string, int> now, int minBefore = 2)
        {
            var reset = new List<string>();
            foreach (var kv in now)
                if (kv.Value == 0 && recorded.TryGetValue(CustomKey(kv.Key), out var was) && int.TryParse(was, out var n) && n >= minBefore)
                    reset.Add(kv.Key);
            reset.Sort(StringComparer.OrdinalIgnoreCase);
            return reset;
        }

        public static string CustomKey(string category) => "~custom." + category;

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
