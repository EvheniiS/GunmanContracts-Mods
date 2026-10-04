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
        public MelonPreferences_Category Cat; // null on the Updated defaults page
        public List<Setting> Settings = new();
        public int Scroll;
        public Setting Selected;

        public bool Updates => Cat == null;
        public string Key => Updates ? "@updates" : Cat.Identifier;
        public string Title => Updates ? "Updated defaults" : CategoryTitle(Cat);
        public bool HasChanges => Updates ? DefaultChanges.AnyWaiting : Settings.Exists(s => s.CanReset && !s.IsDefault);

        public static string CategoryTitle(MelonPreferences_Category c) => string.IsNullOrEmpty(c.DisplayName) ? c.Identifier : c.DisplayName;
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
            DefaultChanges.Scan();
            var old = Current < All.Count ? All[Current].Key : null;
            var keep = new Dictionary<string, Page>();
            foreach (var p in All) keep[p.Key] = p;
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
            // Default changes from mod updates come first, while there are any this session: your customised settings
            // (the ones with a question) first, then the ones that moved by themselves.
            if (DefaultChanges.Items.Count > 0 || DefaultChanges.Notices.Count > 0)
            {
                var up = new Page();
                foreach (bool auto in new[] { false, true })
                    foreach (var it in DefaultChanges.Items)
                        if (it.Auto == auto)
                            try { up.Settings.Add(new Setting(it.Entry)); } catch (Exception ex) { ModSettingsMod.Dbg($"skipped {it.Key}: {ex.Message}"); }
                if (keep.TryGetValue(up.Key, out var was)) up.Scroll = was.Scroll;
                if (up.Settings.Count > 0 || DefaultChanges.Notices.Count > 0) All.Insert(0, up);
            }
            int i = All.FindIndex(p => p.Key == old);
            if (i >= 0) Current = i;
            // Nothing left to answer: reopen on a mod's section, not the list of changes (it stays first in List).
            if (Current == 0 && All.Count > 1 && All[0].Updates && !DefaultChanges.AskWaiting) Current = 1;
            DefaultChanges.OpenPopup();
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
            DefaultChanges.Touched(s.Entry);
            ModSettingsMod.Log.Msg($"{Key(s)}: {before} -> {s.ValueText()}");
            return s.Restart ? "restart to apply" : "applied";
        }

        public static string Reset(Setting s)
        {
            if (s == null || !s.CanReset || s.IsDefault) return "";
            string before = s.ValueText();
            s.Entry.ResetToDefault();
            DefaultChanges.Touched(s.Entry);
            SaveAt = Time.unscaledTime + 1f;
            ModSettingsMod.Log.Msg($"{Key(s)}: {before} -> {s.ValueText()} (default)");
            return "reset";
        }

        public static bool CanResetSection => Cur.Updates ? DefaultChanges.AskWaiting : Cur.Settings.Exists(s => s.CanReset && !s.IsDefault);

        // The complete category, including settings on other scroll pages. Preserve mod-managed state.
        // On the Updated defaults page: Use new for every setting still waiting for a decision.
        public static string ResetSection()
        {
            if (Cur.Updates)
            {
                var keys = DefaultChanges.DecideAll(true);
                if (keys.Count == 0) return "nothing waiting";
                SaveAt = Time.unscaledTime + 1f;
                ModSettingsMod.Log.Msg($"use all new ({keys.Count}: {string.Join(", ", keys)})");
                return $"{keys.Count} set to new";
            }
            int changed = 0, failed = 0;
            foreach (var s in Cur.Settings)
            {
                if (!s.CanReset || s.IsDefault) continue;
                try { Reset(s); changed++; }
                catch (Exception e)
                {
                    failed++;
                    ModSettingsMod.Log.Warning($"reset {Key(s)}: {e.Message}");
                }
            }
            return failed > 0 ? $"reset {changed}; failed {failed}" : changed > 0 ? "section reset" : "all default";
        }

        // A row of the Updated defaults page: "use new", "keep" or "revert".
        public static string Decide(DefaultChanges.Item it, string what)
        {
            if (it == null || !it.Waiting) return "";
            string before = it.Entry.GetValueAsString();
            if (what == "use new") DefaultChanges.UseNew(it);
            else if (what == "keep") DefaultChanges.Keep(it);
            else if (what == "revert") DefaultChanges.Revert(it);
            else return "";
            SaveAt = Time.unscaledTime + 1f;
            ModSettingsMod.Log.Msg($"{it.Key}: {before} -> {it.Entry.GetValueAsString()} ({what})");
            return it.Done;
        }

        // A button of the Mods updated popup (closing the board while it is up counts as Later). Returns the status text.
        public static string PopupChoice(PopupButton b)
        {
            if (DefaultChanges.Popup == null) return "";
            DefaultChanges.Popup = null;
            string what = UpdatePopup.Label(b).ToLowerInvariant(), status = "";
            if (b == PopupButton.UseNew || b == PopupButton.KeepMine)
            {
                var keys = DefaultChanges.DecideAll(b == PopupButton.UseNew);
                if (keys.Count > 0) what += $" ({keys.Count}: {string.Join(", ", keys)})";
                if (b == PopupButton.UseNew) SaveAt = Time.unscaledTime + 1f;
                status = b == PopupButton.UseNew ? $"{keys.Count} set to new" : $"kept {keys.Count}";
            }
            bool page = All.Count > 0 && All[0].Updates;
            if (b == PopupButton.Review && page) Current = 0;
            else if (page && Current == 0 && All.Count > 1) Current = 1;   // answered: start on a mod's section, not the list of changes
            ModSettingsMod.Log.Msg("update popup: " + what);
            return status;
        }

        // Opens a setting's own section with it selected (from the Updated defaults page).
        public static void JumpTo(Setting s, int rows)
        {
            int i = All.FindIndex(p => !p.Updates && p.Cat == s.Entry.Category);
            if (i < 0) return;
            Current = i;
            var pg = All[i];
            int idx = pg.Settings.FindIndex(x => x.Entry == s.Entry);
            if (idx < 0) return;
            pg.Selected = pg.Settings[idx];
            pg.Scroll = idx / rows;
        }

        static string Key(Setting s) => s.Entry.Category.Identifier + "." + s.Entry.Identifier;

        public static void SaveNow()
        {
            SaveAt = -1;
            try { MelonPreferences.Save(); } catch (Exception e) { ModSettingsMod.Log.Warning($"save failed: {e.Message}"); }
        }
    }
}
