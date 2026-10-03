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
    // The list is the "Updated defaults" page, which the board opens on while anything waits for a decision.
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
        static readonly Dictionary<string, string> known = new(), versions = new();
        static bool loaded, shown;

        static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "ModSettings_defaults.txt");

        public static bool AnyWaiting => Items.Exists(i => i.Waiting);
        public static bool AskWaiting => Items.Exists(i => i.Waiting && !i.Auto);
        public static Item Find(MelonPreferences_Entry e) => e == null ? null : Items.Find(i => i.Entry == e);

        public static string Help =>
            (VersionChanges.Count > 0 ? "Updated: " + string.Join(", ", VersionChanges) + "\n" : "") +
            "Mod updates changed these defaults. Settings you had left at the old default now use the new one (Revert undoes it). " +
            "Settings you had changed keep your value until you choose Use new or Keep. Select a name to adjust it in its own section.";

        // Open the board on the page while a decision is waiting, and once per session after automatic updates.
        public static bool ShowOnOpen()
        {
            bool show = AskWaiting || (AnyWaiting && !shown);
            if (show) shown = true;
            return show;
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
                    if (File.Exists(FilePath)) DefaultRecord.Parse(File.ReadAllLines(FilePath), known, versions);
                    else first = true;
                    dirty = ScanVersions() || first;
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
                        known.TryGetValue(key, out var was);
                        string def = e.GetDefaultValueAsString(), val = e.GetValueAsString();
                        switch (DefaultRecord.Decide(was, def, val))
                        {
                            case DefaultChange.Record:
                                recorded++;
                                goto case DefaultChange.AlreadyNew;
                            case DefaultChange.AlreadyNew:
                                known[key] = def; dirty = true;
                                break;
                            case DefaultChange.AutoUpdate:
                                var old = e.BoxedValue;
                                e.ResetToDefault();
                                known[key] = def; dirty = saveCfg = true;
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

        static bool ScanVersions()
        {
            bool changed = false;
            foreach (var m in MelonMod.RegisteredMelons)
            {
                if (m?.Info == null) continue;
                string name = m.Info.Name, ver = m.Info.Version;
                if (versions.TryGetValue(name, out var was) && was == ver) continue;
                if (was != null) VersionChanges.Add($"{name} {was} -> {ver}");
                versions[name] = ver;
                changed = true;
            }
            return changed;
        }

        // ---- decisions (the board logs them and schedules the cfg save) --------------------------------

        public static void UseNew(Item i) { i.Entry.ResetToDefault(); Decide(i, "new default"); }
        public static void Keep(Item i) => Decide(i, "kept yours");

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
            try { File.WriteAllLines(FilePath, DefaultRecord.Format(known, versions)); }
            catch (Exception e) { ModSettingsMod.Log.Warning($"can't write {FilePath}: {e.Message}"); }
        }
    }
}
