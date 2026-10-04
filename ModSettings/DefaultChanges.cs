using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace ModSettings
{
    // Default changes from mod updates. MelonLoader writes every value into MelonPreferences.cfg, untouched ones too,
    // so a new default in a mod update never reaches existing players on its own. This remembers the default each
    // setting had (DefaultRecord) and, when an update changes it:
    //  - value still the old default (never touched) -> moved to the new default at start, listed with Revert;
    //  - value changed by the player -> left alone and listed with Use new / Keep until they decide.
    // The first board open of a session shows a small popup (UpdatePopup) that sums it up; the full list is the
    // "Updated defaults" page (first in List, opened by Review).
    internal static class DefaultChanges
    {
        internal sealed class Item
        {
            public string Key, OldDefault;
            public MelonPreferences_Entry Entry;
            public object OldValue; // auto-updated: the value before, for Revert
            public bool Auto;       // moved to the new default automatically
            public string Done;     // null = no decision yet
            public bool Waiting => Done == null;
        }

        public static readonly List<Item> Items = new();          // this session's
        public static readonly List<string> VersionChanges = new(); // "Daredevil 1.0.0 -> 1.1.0"
        public static readonly List<string> Notices = new();        // e.g. "Defaults Test: settings were reset", until the board has shown them
        const string NoticeKey = "~notice";
        static readonly Dictionary<string, string> known = new(), versions = new();
        static bool loaded, popupShown;

        public static UpdatePopup Popup;   // non-null while the popup is up (the board waits behind it)

        static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "ModSettings_defaults.txt");

        public static bool AnyWaiting => Items.Exists(i => i.Waiting);
        public static bool AskWaiting => Items.Exists(i => i.Waiting && !i.Auto);
        public static Item Find(MelonPreferences_Entry e) => e == null ? null : Items.Find(i => i.Entry == e);

        // The words on the Updated defaults page, shared by the VR board (rich text) and the flat menu (plain).
        public static string Help(bool rich)
        {
            string blue = rich ? "<color=#73D9FF>Blue</color>" : "Blue", yellow = rich ? "<color=#FFD15A>Yellow</color>" : "Yellow";
            string b0 = rich ? "<b>" : "", b1 = rich ? "</b>" : "";
            var sb = new System.Text.StringBuilder();
            if (Notices.Count > 0) sb.Append(b0).Append(string.Join("\n", Notices)).Append(b1).Append('\n');
            if (Items.Count == 0) return sb.ToString().TrimEnd('\n');
            if (VersionChanges.Count > 0) sb.Append(b0).Append("Updated: ").Append(string.Join(", ", VersionChanges)).Append(b1).Append('\n');
            if (Items.Exists(i => !i.Auto)) sb.Append(yellow).Append(": you changed it, your value was kept. Keep it, or Use the new default.  ");
            if (Items.Exists(i => i.Auto)) sb.Append(blue).Append(": you never changed it, it moved to the new default. Undo = old value.");
            sb.Append('\n').Append(AskWaiting ? "Press a name to open it in its own section. Undecided rows are asked again next start."
                                              : "All decided. This page goes away next start.");
            return sb.ToString();
        }

        // Row texts. Plain words: what the author changed, what you had, what happened.
        public static string Explain(Item i)
        {
            string old = i.OldDefault, nw = i.Entry.GetDefaultValueAsString(), val = i.Entry.GetValueAsString();
            if (i.Waiting)
                return i.Auto ? $"default {old} -> {nw} (you never changed it)"
                              : $"default {old} -> {nw} (yours kept: {val})";
            return $"default {old} -> {nw} · " + i.Done switch
            {
                "new default" => $"now uses {nw}",
                "kept yours" => $"kept yours ({val})",
                "reverted" => $"back to your old value ({val})",
                _ => $"you changed it ({val})",
            };
        }

        // Button text: "Keep 2", "Use 1"; just the verb when the value would not fit.
        public static string Verb(string verb, string value) => verb.Length + 1 + value.Length <= 7 ? verb + " " + value : verb;   // 7 chars fit the button
        public static string KeepLabel(Item i) => Verb("Keep", i.Entry.GetValueAsString());
        public static string UseLabel(Item i) => Verb("Use", i.Entry.GetDefaultValueAsString());

        // First board open of the session with something to tell: put the popup up. Once per session; an undecided
        // setting is asked again at the next start (Scan lists it again), a shown notice is not.
        public static void OpenPopup()
        {
            if (popupShown || !(AnyWaiting || Notices.Count > 0)) return;
            popupShown = true;
            int auto = Items.FindAll(i => i.Waiting && i.Auto).Count, ask = Items.FindAll(i => i.Waiting && !i.Auto).Count;
            var sections = new List<(string Section, int Auto, int Ask)>();   // per board section, so the popup names the mods
            foreach (bool a in new[] { false, true })   // same order as the page: customised first
                foreach (var i in Items)
                {
                    if (!i.Waiting || i.Auto != a) continue;
                    string title = Page.CategoryTitle(i.Entry.Category);
                    int k = sections.FindIndex(s => s.Section == title);
                    if (k < 0) { sections.Add((title, 0, 0)); k = sections.Count - 1; }
                    var s = sections[k];
                    sections[k] = (title, s.Auto + (a ? 1 : 0), s.Ask + (a ? 0 : 1));
                }
            Popup = UpdatePopup.Build(VersionChanges, sections, Notices);
            ModSettingsMod.Log.Msg($"update popup: shown ({auto} auto, {ask} ask, {Notices.Count} notices)");
            if (Notices.Count > 0 && versions.Remove(NoticeKey)) Save();   // seen: stop carrying it to the next start
        }

        // At start (all mods have made their entries) and on every board open (for entries made later).
        public static void Scan()
        {
            try
            {
                bool first = false, dirty = false, saveCfg = false;
                if (!loaded)
                {
                    loaded = true;
                    first = !DefaultRecord.Load(FilePath, known, versions);
                    dirty = ScanVersions() || first;
                    dirty |= ScanResets(first);
                }
                int recorded = 0, auto = 0, ask = 0;
                foreach (var cat in MelonPreferences.Categories)
                {
                    if (cat == null || cat.IsHidden) continue;
                    foreach (var e in cat.Entries)
                    {
                        if (e == null || e.IsHidden || Choices.ManagedByMod(e.Description)) continue;
                        string key = cat.Identifier + "." + e.Identifier;
                        if (Items.Exists(i => i.Key == key)) continue;
                        string def = e.GetDefaultValueAsString(), val = e.GetValueAsString();
                        switch (DefaultRecord.Apply(known, key, def, val, out var was))
                        {
                            case DefaultChange.Record:
                                recorded++; dirty = true;
                                break;
                            case DefaultChange.AlreadyNew:
                                dirty = true;
                                break;
                            case DefaultChange.AutoUpdate:
                                var old = e.BoxedValue;
                                e.ResetToDefault();
                                dirty = saveCfg = true;
                                Items.Add(new Item { Key = key, OldDefault = was, Entry = e, OldValue = old, Auto = true });
                                ModSettingsMod.Log.Msg($"{key}: default {was} -> {def}; you had the old default, so it now uses the new one");
                                auto++;
                                break;
                            case DefaultChange.Ask:
                                Items.Add(new Item { Key = key, OldDefault = was, Entry = e });
                                ModSettingsMod.Log.Msg($"{key}: default {was} -> {def}; yours ({val}) kept until you decide on the board");
                                ask++;
                                break;
                        }
                    }
                }
                if (saveCfg) Pages.SaveNow();
                if (dirty) Save();
                if (first) ModSettingsMod.Log.Msg($"recorded the defaults of {recorded} settings; later default changes from mod updates will be shown");
                else if (auto + ask > 0)
                    ModSettingsMod.Log.Msg($"default changes: {auto} updated automatically, {ask} need a decision (open Mod Settings)"
                        + (VersionChanges.Count > 0 ? "; updated: " + string.Join(", ", VersionChanges) : ""));
            }
            catch (Exception ex) { ModSettingsMod.Log.Warning($"default changes: {ex.GetType().Name}: {ex.Message}"); }
        }

        // Counts the settings the player has changed, per category; compares with what the last session saved.
        static Dictionary<string, int> CountChanged()
        {
            var counts = new Dictionary<string, int>();
            foreach (var cat in MelonPreferences.Categories)
            {
                if (cat == null || cat.IsHidden) continue;
                int n = 0, total = 0;
                foreach (var e in cat.Entries)
                {
                    if (e == null || e.IsHidden || Choices.ManagedByMod(e.Description)) continue;
                    total++;
                    if (e.GetValueAsString() != e.GetDefaultValueAsString()) n++;
                }
                if (total > 0) counts[cat.Identifier] = n;
            }
            return counts;
        }

        static bool ScanResets(bool first)
        {
            var now = CountChanged();
            if (!first)
            {
                if (versions.TryGetValue(NoticeKey, out var carried) && !string.IsNullOrEmpty(carried)) Notices.Add(carried);   // not seen on the board yet
                var reset = DefaultRecord.ResetCategories(versions, now);
                if (reset.Count > 0)
                {
                    var names = new List<string>();
                    foreach (var id in reset)
                    {
                        string title = id;
                        foreach (var cat in MelonPreferences.Categories) if (cat != null && cat.Identifier == id) title = Page.CategoryTitle(cat);
                        names.Add(title);
                    }
                    string list = names.Count <= 6 ? string.Join(", ", names) : string.Join(", ", names.GetRange(0, 6)) + $" and {names.Count - 6} more";
                    string msg = $"Settings were reset: {list}. Everything you had changed there is back at its default (the settings file was removed or edited).";
                    Notices.Add(msg);
                    versions[NoticeKey] = string.IsNullOrEmpty(carried) ? msg : carried + "\n" + msg;
                    ModSettingsMod.Log.Warning(msg);
                }
            }
            return StoreCounts(now) | (Notices.Count > 0);
        }

        static bool StoreCounts(Dictionary<string, int> counts)
        {
            bool changed = false;
            foreach (var kv in counts)
            {
                string key = DefaultRecord.CustomKey(kv.Key), val = kv.Value.ToString();
                if (versions.TryGetValue(key, out var was) && was == val) continue;
                versions[key] = val; changed = true;
            }
            return changed;
        }

        // At quit: remember how many settings the player has changed per category (see ScanResets).
        public static void SaveCounts()
        {
            try { if (loaded && StoreCounts(CountChanged())) Save(); }
            catch (Exception ex) { ModSettingsMod.Log.Warning($"saving change counts: {ex.GetType().Name}: {ex.Message}"); }
        }

        static bool ScanVersions()
        {
            var mods = new List<(string, string)>();
            foreach (var m in MelonMod.RegisteredMelons)
                if (m?.Info != null) mods.Add((m.Info.Name, m.Info.Version));
            return DefaultRecord.Versions(versions, mods, VersionChanges);
        }

        // ---- decisions (the board logs them and schedules the cfg save) --------------------------------

        public static void UseNew(Item i) { i.Entry.ResetToDefault(); Decide(i, "new default"); }
        public static void Keep(Item i) => Decide(i, "kept yours");

        // The same decision for every customised setting still waiting (popup Use new / Keep mine, page Use all new).
        // Returns their keys.
        public static List<string> DecideAll(bool useNew)
        {
            var keys = new List<string>();
            foreach (var i in Items)
            {
                if (!i.Waiting || i.Auto) continue;
                if (useNew) i.Entry.ResetToDefault();
                known[i.Key] = i.Entry.GetDefaultValueAsString();
                i.Done = useNew ? "new default" : "kept yours";
                keys.Add(i.Key);
            }
            if (keys.Count > 0) Save();
            return keys;
        }

        // Back to the value from before the automatic update; it is then simply a changed value.
        public static void Revert(Item i)
        {
            i.Entry.BoxedValue = i.OldValue;
            i.Done = "reverted";
        }

        // A waiting setting changed anywhere on the board (steps, Reset) counts as the player's decision.
        public static void Touched(MelonPreferences_Entry e)
        {
            var i = Find(e);
            if (i == null || !i.Waiting) return;
            if (i.Auto) { i.Done = "changed"; return; }
            Decide(i, e.GetValueAsString() == e.GetDefaultValueAsString() ? "new default" : "changed");
        }

        static void Decide(Item i, string done)
        {
            known[i.Key] = i.Entry.GetDefaultValueAsString();
            i.Done = done;
            Save();
        }

        static void Save()
        {
            try { DefaultRecord.Save(FilePath, known, versions); }
            catch (Exception e) { ModSettingsMod.Log.Warning($"can't write {FilePath}: {e.Message}"); }
        }
    }
}
