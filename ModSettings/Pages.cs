using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace ModSettings
{
    // One page per MelonPreferences category, shared by the VR board (Panel) and the flat-screen menu (FlatMenu), so
    // both show the same mod, scroll position and selection.
    internal sealed class Page
    {
        public MelonPreferences_Category Cat;
        public List<Setting> Settings = new();
        public int Scroll;
        public Setting Selected;

        public string Title => string.IsNullOrEmpty(Cat.DisplayName) ? Cat.Identifier : Cat.DisplayName;
        public bool HasChanges => Settings.Exists(s => s.CanReset && !s.IsDefault);
    }

    internal static class Pages
    {
        public static readonly List<Page> All = new();
        public static int Current;
        public static float SaveAt = -1;

        public static Page Cur => All[Current];

        // Rebuilds the list from MelonPreferences (mods may have added categories since), keeping each page's scroll
        // and selection. Returns false when there is nothing to show.
        public static bool Load()
        {
            var old = Current < All.Count ? All[Current].Cat?.Identifier : null;
            var keep = new Dictionary<string, Page>();
            foreach (var p in All) keep[p.Cat.Identifier] = p;
            All.Clear();
            foreach (var cat in MelonPreferences.Categories)
            {
                if (cat == null || cat.IsHidden) continue;
                var pg = new Page { Cat = cat };
                foreach (var e in cat.Entries)
                    if (e != null && !e.IsHidden)
                        try { pg.Settings.Add(new Setting(e)); } catch (Exception ex) { ModSettingsMod.Dbg($"skipped {cat.Identifier}.{e.Identifier}: {ex.Message}"); }
                if (pg.Settings.Count == 0) continue;
                if (keep.TryGetValue(cat.Identifier, out var was)) { pg.Scroll = was.Scroll; pg.Selected = pg.Settings.Find(s => s.Entry == was.Selected?.Entry); }
                All.Add(pg);
            }
            // Alphabetical, so the section list is easy to scan and < / > follow the same order.
            All.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            int i = All.FindIndex(p => p.Cat.Identifier == old);
            if (i >= 0) Current = i;
            if (All.Count == 0) { ModSettingsMod.Log.Warning("no mod settings to show"); return false; }
            Current = Math.Clamp(Current, 0, All.Count - 1);
            return true;
        }

        public static void Turn(int dir) => Current = ((Current + dir) % All.Count + All.Count) % All.Count;

        public static void Jump(int i) { if (i >= 0 && i < All.Count) Current = i; }

        // Steps a setting (see Setting.Change), logs it and schedules the save. Returns the status text to show.
        public static string Change(Setting s, int dir)
        {
            var pg = Cur;
            string before = s.ValueText();
            pg.Selected = s;
            if (!s.Change(dir)) return "limit";
            SaveAt = Time.unscaledTime + 1f;
            ModSettingsMod.Log.Msg($"{pg.Cat.Identifier}.{s.Entry.Identifier}: {before} -> {s.ValueText()}");
            return s.Restart ? "restart to apply" : "applied";
        }

        public static string Reset(Setting s)
        {
            if (s == null || !s.CanReset || s.IsDefault) return "";
            string before = s.ValueText();
            s.Entry.ResetToDefault();
            SaveAt = Time.unscaledTime + 1f;
            ModSettingsMod.Log.Msg($"{Cur.Cat.Identifier}.{s.Entry.Identifier}: {before} -> {s.ValueText()} (default)");
            return "reset";
        }

        public static bool CanResetSection => Cur.Settings.Exists(s => s.CanReset && !s.IsDefault);

        // The complete category, including settings on other scroll pages. Preserve mod-managed state.
        public static string ResetSection()
        {
            int changed = 0, failed = 0;
            foreach (var s in Cur.Settings)
            {
                if (!s.CanReset || s.IsDefault) continue;
                try { Reset(s); changed++; }
                catch (Exception e)
                {
                    failed++;
                    ModSettingsMod.Log.Warning($"reset {Cur.Cat.Identifier}.{s.Entry.Identifier}: {e.Message}");
                }
            }
            return failed > 0 ? $"reset {changed}; failed {failed}" : changed > 0 ? "section reset" : "all default";
        }

        public static void SaveNow()
        {
            SaveAt = -1;
            try { MelonPreferences.Save(); } catch (Exception e) { ModSettingsMod.Log.Warning($"save failed: {e.Message}"); }
        }
    }
}
