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

        static GUIStyle label, title, desc, button;
        static int styleSize;

        public static void Toggle(string how)
        {
            if (IsOpen) { Close(); ModSettingsMod.Dbg($"flat menu closed: {how}"); return; }
            if (!Pages.Load()) return;
            IsOpen = true;
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
                if (y != 0) Pages.Cur.Scroll += y > 0 ? -1 : 1;
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

            float w = 1040 * u, h = 720 * u, pad = 14 * u, row = 36 * u, gap = 6 * u;
            var win = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            Fill(win, new Color(0.06f, 0.06f, 0.07f, 0.94f));

            var pg = Pages.Cur;
            int maxScroll = Math.Max(0, (pg.Settings.Count - 1) / Rows);
            pg.Scroll = Math.Clamp(pg.Scroll, 0, maxScroll);

            // Header: < title (n/N) > ... X
            float x0 = win.x + pad, y = win.y + pad, inner = w - 2 * pad;
            if (Btn(new Rect(x0, y, row, row), "<")) { Pages.Turn(-1); return; }
            GUI.Label(new Rect(x0 + row + gap, y, inner - 3 * row - 3 * gap, row), $"{pg.Title}   ({Pages.Current + 1}/{Pages.All.Count})", title);
            if (Btn(new Rect(x0 + inner - 2 * row - gap * 3, y, row, row), ">")) { Pages.Turn(1); return; }
            if (Btn(new Rect(x0 + inner - row, y, row, row), "X", new Color(0.6f, 0.15f, 0.15f))) { Close(); ModSettingsMod.Dbg("flat menu closed: X"); return; }
            y += row + 2 * gap;

            // Rows: name | value | -big -small +small +big (numbers) or < > (choices)
            float resetW = 85 * u;
            float nameW = inner * 0.36f, valW = inner * 0.18f, stepW = (inner - nameW - valW - resetW - 6 * gap) / 4;
            for (int r = 0; r < Rows; r++, y += row + gap)
            {
                int idx = pg.Scroll * Rows + r;
                if (idx >= pg.Settings.Count) continue;
                var s = pg.Settings[idx];
                float x = x0;

                var bg = s == pg.Selected ? new Color(0.45f, 0.1f, 0.1f) : new Color(0.2f, 0.2f, 0.23f);
                var txt = s.IsDefault ? Color.white : new Color(1f, 0.82f, 0.35f);
                if (Btn(new Rect(x, y, nameW, row), " " + s.Name + (s.Restart ? " *" : "") + (s.IsDefault ? " [default]" : " [changed]"), bg, txt, left: true))
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
            if (Btn(new Rect(x0 + inner - 1.8f * bw, y, 1.8f * bw, row), "Reset section"))
                Status(Pages.ResetSection());
            GUI.enabled = wasEnabled;
            y += row + 2 * gap;

            string text = sel == null
                ? "Click a setting's name to read what it does. Changes apply at once and are saved. * = needs a game restart. Mouse wheel scrolls. Ctrl+"
                  + ModSettingsMod.OpenKeyName + " closes."
                : $"{sel.Name} = {sel.ValueText()}   (default {sel.Entry.GetDefaultValueAsString()})\n{sel.Entry.Description}" +
                  (sel.Restart ? "\nRestart the game to apply." : "") +
                  (sel.Kind == Kind.ReadOnly ? "\nEdit this one in UserData/MelonPreferences.cfg." : "");
            GUI.Label(new Rect(x0, y, inner, win.yMax - pad - y), text, desc);
        }

        static void Styles(float u)
        {
            int size = Mathf.RoundToInt(19 * u);
            if (label != null && styleSize == size) return;
            styleSize = size;
            label = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 1.25f), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            desc = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size * 0.9f), alignment = TextAnchor.UpperLeft };
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
