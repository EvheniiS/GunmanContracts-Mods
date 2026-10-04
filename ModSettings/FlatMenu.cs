using System;
using Il2CppInfimaGames.LowPolyShooterPack;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace ModSettings
{
    // The flat-screen version of the board: an IMGUI window in the middle of the screen, used with the mouse.
    // Same pages, same buttons, same rules as the VR board.
    //
    // Flat mode runs the Low Poly Shooter Pack controller (InfimaGames Character), which ignores look and fire input
    // while its own cursorLocked flag is off. So the menu clears that flag while it is open (the game then shows the
    // cursor itself) and locks it again on close. Walking still works; the game is not paused.
    internal static class FlatMenu
    {
        const int Rows = 10;

        public static bool IsOpen { get; private set; }
        static Character unlocked;           // the controller whose cursor we unlocked
        static bool savedCursor, savedVisible;
        static CursorLockMode savedLock;
        static string status = "";
        static float statusUntil;
        // Section list: a 3-column grid of every section in place of the rows; perPage depends on the screen size.
        static bool listOpen;
        static int listPage, perPage = 30;
        static int ListPages => Math.Max(1, (Pages.All.Count + perPage - 1) / perPage);

        static GUIStyle label, title, desc, button, popText;
        static int styleSize;

        public static void Toggle(string how)
        {
            if (IsOpen) { Close(); ModSettingsMod.Dbg($"flat menu closed: {how}"); return; }
            if (!Pages.Load()) return;
            IsOpen = true;
            listOpen = false;
            unlocked = null;
            savedCursor = false;
            var c = FindCharacter();
            if (c != null)
            {
                // Its own menus (pause, gun editing) handle the cursor themselves; leave it alone then.
                if (c.cursorLocked && !c.menuShown && !c.editingGun)
                {
                    c.cursorLocked = false;
                    c.UpdateCursorState();
                    unlocked = c;
                }
            }
            else
            {
                savedCursor = true;
                savedLock = Cursor.lockState;
                savedVisible = Cursor.visible;
            }
            ModSettingsMod.Dbg($"flat menu opened: {how}{(c != null ? "" : " (no flat controller - main menu?)")}");
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Pages.PopupChoice(PopupButton.Later);   // closed with the popup still up: nothing decided
            try
            {
                if (unlocked != null && Panel.Alive(unlocked) && !unlocked.menuShown) unlocked.ManualLockCursor();
                else if (savedCursor) { Cursor.lockState = savedLock; Cursor.visible = savedVisible; }
            }
            catch (Exception e) { ModSettingsMod.Log.Warning($"flat menu: cursor restore: {e.Message}"); }
            unlocked = null;
            if (Pages.SaveAt > 0) Pages.SaveNow();
        }

        // Every frame while open: keep the cursor free (a click can make the game lock it again) and scroll with the wheel.
        public static void Update()
        {
            if (!IsOpen) return;
            if (unlocked != null && Panel.Alive(unlocked))
            {
                if (unlocked.cursorLocked && !unlocked.menuShown) { unlocked.cursorLocked = false; unlocked.UpdateCursorState(); }
            }
            else if (savedCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            var m = Mouse.current;
            if (m != null && Pages.All.Count > 0)
            {
                float y = m.scroll.ReadValue().y;
                if (y != 0 && listOpen) listPage = Math.Clamp(listPage + (y > 0 ? -1 : 1), 0, ListPages - 1);
                else if (y != 0) Pages.Cur.Scroll += y > 0 ? -1 : 1;
            }
            if (statusUntil > 0 && Time.unscaledTime > statusUntil) { statusUntil = 0; status = ""; }
        }

        public static bool HasController() => FindCharacter() != null;

        static Character FindCharacter()
        {
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<Character>(), FindObjectsSortMode.None))
            {
                var c = o.TryCast<Character>();
                if (c != null && c.isActiveAndEnabled) return c;
            }
            return null;
        }

        static void Status(string s) { status = s; statusUntil = Time.unscaledTime + 1.5f; }

        // ---- drawing (called from MelonMod.OnGUI) -------------------------------------------------------

        public static void Draw()
        {
            if (!IsOpen || Pages.All.Count == 0) return;
            float u = Mathf.Max(Screen.height / 1080f, 0.6f);
            Styles(u);
            if (DefaultChanges.Popup != null) { DrawPopup(u); return; }

            float w = 1040 * u, h = 720 * u, pad = 14 * u, row = 36 * u, gap = 6 * u;
            var win = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(win, new Color(0.06f, 0.06f, 0.07f, 0.94f));

            var pg = Pages.Cur;
            int maxScroll = Math.Max(0, (pg.Settings.Count - 1) / Rows);
            pg.Scroll = Math.Clamp(pg.Scroll, 0, maxScroll);

            // Header: < title (n/N) [List] > ... X   (the title opens the section list too)
            float x0 = win.x + pad, y = win.y + pad, inner = w - 2 * pad, listW = 90 * u;
            float nextX = x0 + inner - 2 * row - gap * 3;
            if (Btn(new Rect(x0, y, row, row), "<")) { TurnOrPage(-1); return; }
            string head = !listOpen ? $"{pg.Title}   ({Pages.Current + 1}/{Pages.All.Count})"
                : ListPages > 1 ? $"Sections   ({listPage + 1}/{ListPages})" : $"Sections   ({Pages.All.Count})";
            if (GUI.Button(new Rect(x0 + row + gap, y, nextX - listW - 3 * gap - (x0 + row), row), head, title)) { ToggleList(); return; }
            if (Btn(new Rect(nextX - listW - gap, y, listW, row), listOpen ? "Back" : "List")) { ToggleList(); return; }
            if (Btn(new Rect(nextX, y, row, row), ">")) { TurnOrPage(1); return; }
            if (Btn(new Rect(x0 + inner - row, y, row, row), "X", new Color(0.6f, 0.15f, 0.15f))) { Close(); ModSettingsMod.Dbg("flat menu closed: X"); return; }
            y += row + 2 * gap;

            if (listOpen) { DrawList(x0, y, inner, win.yMax - pad, row, gap); return; }

            // Rows: name | value | -big -small +small +big (numbers) or < > (choices)
            float resetW = 85 * u;
            float nameW = inner * 0.36f, valW = inner * 0.18f, stepW = (inner - nameW - valW - resetW - 6 * gap) / 4;
            for (int r = 0; r < Rows; r++, y += row + gap)
            {
                int idx = pg.Scroll * Rows + r;
                if (idx >= pg.Settings.Count) continue;
                var s = pg.Settings[idx];
                float x = x0;
                if (pg.Updates) { UpdateRow(s, x0, y, inner, nameW, valW, stepW, resetW, row, gap); continue; }

                var bg = s == pg.Selected ? new Color(0.45f, 0.1f, 0.1f) : new Color(0.2f, 0.2f, 0.23f);
                var txt = s.IsDefault ? Color.white : new Color(1f, 0.82f, 0.35f);
                bool newDefault = DefaultChanges.Find(s.Entry)?.Waiting == true;
                if (Btn(new Rect(x, y, nameW, row), " " + s.Name + (s.Restart ? " *" : "") + (s.IsDefault ? " [default]" : " [changed]") + (newDefault ? " [new default]" : ""), bg, txt, left: true))
                    pg.Selected = pg.Selected == s ? null : s;
                x += nameW + gap;

                var vr = new Rect(x, y, valW, row);
                if (s.Kind == Kind.Bool)
                {
                    bool on = (bool)s.Entry.BoxedValue;
                    if (Btn(vr, s.ValueText(), on ? new Color(0.15f, 0.55f, 0.2f) : new Color(0.45f, 0.12f, 0.12f))) Status(Pages.Change(s, 1));
                }
                else
                {
                    if (s.Kind == Kind.Color && ColorUtility.TryParseHtmlString(s.ValueText(), out var col)) Fill(vr, col);
                    var old = GUI.contentColor;
                    GUI.contentColor = s.Kind == Kind.ReadOnly ? Color.gray : Color.white;
                    GUI.Label(vr, s.ValueText(), label);
                    GUI.contentColor = old;
                }
                x += valW + gap;

                switch (s.Kind)
                {
                    case Kind.Int:
                    case Kind.Float:
                        double[] amount = { s.Big, s.Small, s.Small, s.Big };
                        int[] dir = { -2, -1, 1, 2 };
                        for (int k = 0; k < 4; k++)
                            if (Btn(new Rect(x + k * (stepW + gap), y, stepW, row), Choices.StepLabel(amount[k], Math.Sign(dir[k]))))
                                Status(Pages.Change(s, dir[k]));
                        break;
                    case Kind.Enum:
                    case Kind.Choice:
                    case Kind.Color:
                        if (Btn(new Rect(x + stepW + gap, y, stepW, row), "<")) Status(Pages.Change(s, -1));
                        if (Btn(new Rect(x + 2 * (stepW + gap), y, stepW, row), ">")) Status(Pages.Change(s, 1));
                        break;
                }
                var rr = new Rect(x0 + inner - resetW, y, resetW, row);
                if (s.IsDefault || !s.CanReset) GUI.Label(rr, s.IsDefault ? "Default" : "Managed", label);
                else if (Btn(rr, "Reset", fg: new Color(1f, 0.82f, 0.35f))) Status(Pages.Reset(s));
            }

            // Footer: Up  n/N  Down   status   Reset section
            float bw = 90 * u;
            if (pg.Scroll > 0 && Btn(new Rect(x0, y, bw, row), "Up")) pg.Scroll--;
            if (maxScroll > 0) GUI.Label(new Rect(x0 + bw + gap, y, bw, row), $"{pg.Scroll + 1}/{maxScroll + 1}", label);
            if (pg.Scroll < maxScroll && Btn(new Rect(x0 + 2 * (bw + gap), y, bw, row), "Down")) pg.Scroll++;
            GUI.Label(new Rect(x0 + 3 * (bw + gap), y, inner - 5 * bw, row), status, label);
            var sel = pg.Selected;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && Pages.CanResetSection;
            if (Btn(new Rect(x0 + inner - 1.8f * bw, y, 1.8f * bw, row), pg.Updates ? "Use all new" : "Reset section"))
                Status(Pages.ResetSection());
            GUI.enabled = wasEnabled;
            y += row + 2 * gap;

            string text = pg.Updates ? DefaultChanges.Help(false) : sel == null
                ? "Click a setting's name to read what it does. Changes apply at once and are saved. * = needs a game restart. Mouse wheel scrolls. Ctrl+"
                  + ModSettingsMod.OpenKeyName + " closes."
                : $"{sel.Name} = {sel.ValueText()}   (default {sel.Entry.GetDefaultValueAsString()})\n{sel.Entry.Description}" +
                  (sel.Restart ? "\nRestart the game to apply." : "") +
                  (sel.Kind == Kind.ReadOnly ? "\nEdit this one in UserData/MelonPreferences.cfg." : "");
            GUI.Label(new Rect(x0, y, inner, win.yMax - pad - y), text, desc);
        }

        // The Mods updated popup (see UpdatePopup): a smaller window with a blue frame, shown instead of the menu until answered.
        static void DrawPopup(float u)
        {
            var p = DefaultChanges.Popup;
            float w = 620 * u, h = 340 * u, pad = 16 * u, row = 40 * u, gap = 12 * u, edge = 3 * u, head = 46 * u;
            var win = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(new Rect(win.x - edge, win.y - edge, w + 2 * edge, h + 2 * edge), new Color(0.45f, 0.85f, 1f));
            Fill(win, new Color(0.06f, 0.06f, 0.07f));
            var top = new Rect(win.x, win.y, w, head);
            Fill(top, new Color(0.1f, 0.3f, 0.4f));
            GUI.Label(top, p.Title, title);
            GUI.Label(new Rect(win.x + pad, top.yMax + pad, w - 2 * pad, h - head - row - 3 * pad), string.Join("\n", p.Lines), popText);
            int n = p.Buttons.Count;
            float bw = n >= 4 ? 130 * u : 170 * u, x = win.x + (w - (n * bw + (n - 1) * gap)) / 2, y = win.yMax - pad - row;
            for (int k = 0; k < n; k++, x += bw + gap)
            {
                var b = p.Buttons[k];
                bool later = b == PopupButton.Later;
                if (Btn(new Rect(x, y, bw, row), UpdatePopup.Label(b), k == 0 ? new Color(0.1f, 0.3f, 0.4f) : later ? new Color(0.13f, 0.13f, 0.15f) : null,
                        later ? Color.gray : null))
                {
                    Status(Pages.PopupChoice(b));
                    return;
                }
            }
        }

        // A row of the Updated defaults page (see Panel.UpdateRow): name (opens its section) | value | Keep | Use new / Revert.
        static void UpdateRow(Setting s, float x0, float y, float inner, float nameW, float valW, float stepW, float resetW, float row, float gap)
        {
            var it = DefaultChanges.Find(s.Entry);
            bool waiting = it != null && it.Waiting;
            var tint = !waiting ? Color.gray : it.Auto ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.82f, 0.35f);   // blue = moved to the new default, yellow = yours kept
            // No step buttons on this page: the name takes their room. name | value | Keep | Use / Undo
            float wide = inner - valW - stepW - resetW - 3 * gap;
            if (Btn(new Rect(x0, y, wide, row), $" {Page.CategoryTitle(s.Entry.Category)} · {s.Name}: {(it == null ? "" : DefaultChanges.Explain(it))}",
                    new Color(0.2f, 0.2f, 0.23f), tint, left: true))
                Pages.JumpTo(s, Rows);
            float x = x0 + wide + gap;
            var vr = new Rect(x, y, valW, row);
            if (s.Kind == Kind.Color && ColorUtility.TryParseHtmlString(s.ValueText(), out var col)) Fill(vr, col);
            GUI.Label(vr, s.ValueText(), label);
            x += valW + gap;
            if (waiting && !it.Auto && Btn(new Rect(x, y, stepW, row), DefaultChanges.KeepLabel(it), fg: tint)) Status(Pages.Decide(it, "keep"));
            var rr = new Rect(x0 + inner - resetW, y, resetW, row);
            if (!waiting) GUI.Label(rr, "Done", label);
            else if (Btn(rr, it.Auto ? "Undo" : DefaultChanges.UseLabel(it), fg: tint)) Status(Pages.Decide(it, it.Auto ? "revert" : "use new"));
        }

        // Every section as a button, filled column by column; yellow = has changed values, red = the current one.
        static void DrawList(float x0, float y0, float inner, float bottom, float row, float gap)
        {
            const int cols = 3;
            int rows = Math.Max(1, (int)((bottom - y0 + gap) / (row + gap)));
            perPage = cols * rows;
            listPage = Math.Clamp(listPage, 0, ListPages - 1);
            float colW = (inner - (cols - 1) * gap) / cols;
            for (int k = 0; k < perPage; k++)
            {
                int idx = listPage * perPage + k;
                if (idx >= Pages.All.Count) break;
                var pg = Pages.All[idx];
                var r = new Rect(x0 + k / rows * (colW + gap), y0 + k % rows * (row + gap), colW, row);
                var bg = idx == Pages.Current ? new Color(0.45f, 0.1f, 0.1f) : pg.Updates && DefaultChanges.AskWaiting ? new Color(0.1f, 0.3f, 0.4f) : new Color(0.2f, 0.2f, 0.23f);   // blue = waits for your answer
                if (Btn(r, " " + pg.Title, bg, pg.HasChanges ? new Color(1f, 0.82f, 0.35f) : Color.white, left: true))
                {
                    Pages.Jump(idx);
                    listOpen = false;
                    ModSettingsMod.Dbg($"flat list: {pg.Title}");
                    return;
                }
            }
        }

        static void ToggleList()
        {
            listOpen = !listOpen;
            if (listOpen) listPage = Pages.Current / perPage;
        }

        // < / >: page the list while it is open and has more than one page; otherwise switch section.
        static void TurnOrPage(int dir)
        {
            if (listOpen && ListPages > 1) listPage = ((listPage + dir) % ListPages + ListPages) % ListPages;
            else { listOpen = false; Pages.Turn(dir); }
        }

        static void Styles(float u)
        {
            int size = Mathf.RoundToInt(19 * u);
            if (label != null && styleSize == size) return;
            styleSize = size;
            label = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 1.25f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            desc = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 0.9f), alignment = TextAnchor.UpperLeft };
            popText = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.UpperLeft, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = size };
        }

        static GUIStyle leftButton;

        static bool Btn(Rect r, string text, Color? bg = null, Color? fg = null, bool left = false)
        {
            if (left && (leftButton == null || leftButton.fontSize != button.fontSize))
                leftButton = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft };
            Color ob = GUI.backgroundColor, oc = GUI.contentColor;
            if (bg.HasValue) GUI.backgroundColor = bg.Value * 2.2f; // the default skin's button texture is dark grey
            if (fg.HasValue) GUI.contentColor = fg.Value;
            bool hit = GUI.Button(r, text, left ? leftButton : button);
            GUI.backgroundColor = ob; GUI.contentColor = oc;
            return hit;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
