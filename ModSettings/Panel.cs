using System;
using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ModSettings
{
    // The floating settings board. Built once from quads + TextMeshPro, then shown/hidden. Panel space: metres,
    // +X right, +Y up, +Z away from the player (the quads' visible side faces -Z). No colliders anywhere: the
    // fingertips and pointer rays are tested against button rectangles in panel space; no physics colliders.
    internal static class Panel
    {
        const float W = 0.72f, H = 0.56f, RowStep = 0.052f, Btn = 0.042f;
        const int Rows = 6;
        const float PressDepth = -0.015f, PressBack = 0.03f, HoverDepth = -0.07f;

        sealed class Button
        {
            public GameObject Go, Quad;
            public Material Mat;
            public TextMeshPro Text;
            public Vector2 Center, Half;
            public Action OnPress;
            public Color Base;
            public bool Interactive = true;
            public float FlashUntil;
        }

        sealed class Poker { public bool WasIn, Trigger, Grip; public float LastPress, LastDepth; public Button Hover; }

        static GameObject root;
        static Shader flat;
        static TMP_FontAsset font;
        static readonly List<Button> buttons = new();
        static Button title, prevCat, nextCat, close, up, down, pageText, reset, status, desc;
        static readonly Button[] labels = new Button[Rows], values = new Button[Rows];
        static readonly Button[] resets = new Button[Rows];
        static readonly Button[,] steps = new Button[Rows, 4];
        static readonly Poker[] pokers = { new(), new() };
        static float nextValueRefresh, statusUntil;
        static int dragging = -1;
        static Vector3 dragOffset;
        static Quaternion dragRotation;

        static readonly Color Bg = new(0.07f, 0.07f, 0.08f), BtnCol = new(0.22f, 0.22f, 0.25f), LabelCol = new(0.13f, 0.13f, 0.15f),
            SelCol = new(0.32f, 0.08f, 0.08f), FlashCol = new(0.75f, 0.2f, 0.2f), OnCol = new(0.12f, 0.42f, 0.16f),
            OffCol = new(0.32f, 0.1f, 0.1f), Changed = new(1f, 0.82f, 0.35f), Grey = new(0.6f, 0.6f, 0.6f);

        public static bool IsOpen => Alive(root) && root.activeSelf;

        public static void Open(Transform head, float distance, float scale, float below)
        {
            if (!Alive(root) && !Build()) return;
            if (!Pages.Load()) return;

            // In front of the eyes, a little below them, facing the eyes (panel +Z points away from the player).
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
            fwd.Normalize();
            var pos = head.position + fwd * distance + Vector3.down * (below * Mathf.Max(distance / 0.4f, 0.5f));
            // Ride on the player rig (the head's parent), so stick locomotion and snap turns carry the board along;
            // only walking away in the room moves you off it.
            root.transform.SetParent(head.parent, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.position, Vector3.up));
            float parentScale = head.parent != null ? head.parent.lossyScale.x : 1f;
            root.transform.localScale = Vector3.one * (Mathf.Clamp(scale, 0.5f, 2f) / Mathf.Max(parentScale, 1e-3f));
            dragging = -1;
            foreach (var p in pokers) { p.WasIn = p.Trigger = p.Grip = true; p.LastDepth = float.PositiveInfinity; p.Hover = null; }
            root.SetActive(true);
            Refresh();
        }

        public static void Close()
        {
            dragging = -1;
            VRInteraction.Close();
            if (Alive(root))
            {
                root.SetActive(false);
                root.transform.SetParent(null, false);
                Object.DontDestroyOnLoad(root);
            }
            if (Pages.SaveAt > 0) Pages.SaveNow();
        }

        // tips[i] = fingertip of hand i (null if not found); hands[i] for haptics.
        public static void Update(Transform head, IList<Transform> tips, IList<HVRHandGrabber> hands)
        {
            if (!IsOpen) return;
            float now = Time.unscaledTime;
            // A floor-height board should stay open while the player stands and adjusts holsters.
            if (head != null && Vector3.Distance(head.position, root.transform.position) > 5f) { Close(); ModSettingsMod.Dbg("closed: walked away"); return; }

            for (int i = 0; i < pokers.Length; i++)
            {
                var p = pokers[i];
                var tip = i < tips.Count ? tips[i] : null;
                var hand = i < hands.Count ? hands[i] : null;
                bool free = VRInteraction.Free(hand);
                bool trigger = free && hand.Controller != null && hand.Controller.TriggerButtonState.Active;
                bool grip = free && hand.Controller != null && hand.Controller.GripButtonState.Active;
                if (!Alive(tip) || !free)
                {
                    if (dragging == i) dragging = -1;
                    p.WasIn = p.Trigger = p.Grip = true; p.Hover = null;
                    p.LastDepth = float.PositiveInfinity;
                    VRInteraction.Show(i, hand, false, Vector3.zero, Vector3.zero, flat);
                    continue;
                }
                var anchor = Alive(hand.TrackedController) ? hand.TrackedController : hand.transform;
                if (dragging == i)
                {
                    if (grip)
                        root.transform.SetPositionAndRotation(anchor.TransformPoint(dragOffset), anchor.rotation * dragRotation);
                    else dragging = -1;
                    p.WasIn = true; p.Trigger = trigger; p.Grip = grip; p.Hover = null;
                    p.LastDepth = float.PositiveInfinity;
                    VRInteraction.Show(i, hand, false, Vector3.zero, Vector3.zero, flat);
                    continue;
                }
                var local = root.transform.InverseTransformPoint(tip.position);
                var direction = root.transform.InverseTransformDirection(VRInteraction.Direction(hand));
                bool aimed = PointerGeometry.Hit(local.x, local.y, local.z, direction.x, direction.y, direction.z,
                    root.transform.lossyScale.x, W, H, out float hitX, out float hitY);
                var hit = new Vector3(hitX, hitY, 0);
                bool near = local.z > -0.14f && local.z < PressBack && Mathf.Abs(local.x) < W / 2 && Mathf.Abs(local.y) < H / 2;
                bool handle = (near && local.y > H / 2 - 0.038f) || (aimed && hit.y > H / 2 - 0.038f);
                if (dragging < 0 && handle && grip && !p.Grip)
                {
                    dragging = i;
                    dragOffset = anchor.InverseTransformPoint(root.transform.position);
                    dragRotation = Quaternion.Inverse(anchor.rotation) * root.transform.rotation;
                    p.Grip = true; p.Trigger = trigger; p.WasIn = true; p.Hover = null;
                    p.LastDepth = float.PositiveInfinity;
                    hand.Controller?.Vibrate(0.3f, 0.03f, 160f);
                    VRInteraction.Close();
                    continue;
                }
                VRInteraction.Show(i, hand, dragging < 0 && (aimed || near), tip.position,
                    aimed ? root.transform.TransformPoint(new Vector3(hit.x, hit.y, -0.003f)) : tip.position, flat);
                var b = Hit(new Vector2(local.x, local.y));
                bool touching = local.z > PressDepth && local.z < PressBack && Mathf.Abs(local.x) < W / 2 && Mathf.Abs(local.y) < H / 2;
                bool isIn = b != null && touching;
                p.Hover = b != null && local.z > HoverDepth && local.z < PressBack ? b : null;
                var rayButton = aimed ? Hit(new Vector2(hit.x, hit.y)) : null;
                bool rayPress = !near && rayButton != null && trigger && !p.Trigger;
                if (!near && aimed) p.Hover = rayButton;
                // Press only when the fingertip comes in from the front, not when it slides sideways across the board.
                if (dragging < 0 && !grip && ((isIn && !p.WasIn && p.LastDepth <= PressDepth) || rayPress) && now - p.LastPress > 0.2f)
                {
                    if (rayPress) b = rayButton;
                    p.LastPress = now;
                    b.FlashUntil = now + 0.15f;
                    try { var h = i < hands.Count ? hands[i] : null; if (Alive(h)) h.Controller?.Vibrate(0.3f, 0.03f, 160f); } catch { }
                    try { b.OnPress?.Invoke(); } catch (Exception e) { ModSettingsMod.Log.Warning($"button: {e.Message}"); }
                    if (!IsOpen) return;
                    // A page change/reset must not make another finger press newly positioned content.
                    foreach (var other in pokers) other.WasIn = true;
                }
                p.WasIn = touching;
                p.LastDepth = local.z;
                p.Trigger = trigger; p.Grip = grip;
            }

            foreach (var b in buttons)
            {
                if (!b.Interactive || !b.Go.activeSelf) continue;
                bool hover = pokers[0].Hover == b || pokers[1].Hover == b;
                var c = now < b.FlashUntil ? FlashCol : hover ? b.Base + new Color(0.1f, 0.1f, 0.1f) : b.Base;
                b.Mat.SetColor("_BaseColor", c); b.Mat.SetColor("_Color", c);
            }

            if (now >= nextValueRefresh) { nextValueRefresh = now + 0.5f; Refresh(); } // values changed by keys or the cfg file
            if (statusUntil > 0 && now > statusUntil) { statusUntil = 0; status.Text.SetIfChanged(""); }
        }

        static Button Hit(Vector2 p)
        {
            foreach (var b in buttons)
                if (b.Interactive && b.OnPress != null && b.Go.activeSelf &&
                    Mathf.Abs(p.x - b.Center.x) <= b.Half.x && Mathf.Abs(p.y - b.Center.y) <= b.Half.y) return b;
            return null;
        }

        // ---- content ------------------------------------------------------------------------------

        static void Refresh()
        {
            var pg = Pages.Cur;
            int maxScroll = Math.Max(0, (pg.Settings.Count - 1) / Rows);
            pg.Scroll = Math.Clamp(pg.Scroll, 0, maxScroll);
            title.Text.SetIfChanged($"{pg.Title}  <size=70%>({Pages.Current + 1}/{Pages.All.Count})</size>");

            for (int r = 0; r < Rows; r++)
            {
                int idx = pg.Scroll * Rows + r;
                var s = idx < pg.Settings.Count ? pg.Settings[idx] : null;
                labels[r].Go.SetActive(s != null);
                values[r].Go.SetActive(s != null);
                resets[r].Go.SetActive(s != null);
                for (int k = 0; k < 4; k++) steps[r, k].Go.SetActive(false);
                if (s == null) continue;

                labels[r].Text.SetIfChanged(s.Name + (s.Restart ? " *" : "") +
                    $"\n<size=65%>{(s.IsDefault ? "DEFAULT" : "CHANGED")} · default: {s.Entry.GetDefaultValueAsString()}</size>");
                labels[r].Text.color = s.IsDefault ? Color.white : Changed;
                SetBase(labels[r], s == pg.Selected ? SelCol : LabelCol);
                resets[r].Text.SetIfChanged(s.IsDefault ? "Default" : s.CanReset ? "Reset" : "Managed");
                resets[r].Interactive = !s.IsDefault && s.CanReset;
                resets[r].Text.color = s.IsDefault ? Grey : Changed;
                SetBase(resets[r], s.IsDefault ? LabelCol : BtnCol);

                var v = values[r];
                v.Text.SetIfChanged(s.ValueText());
                v.Text.color = s.Kind == Kind.ReadOnly ? Grey : Color.white;
                v.Interactive = s.Kind == Kind.Bool;
                v.Quad.SetActive(s.Kind == Kind.Bool || s.Kind == Kind.Color);
                if (s.Kind == Kind.Bool) SetBase(v, (bool)s.Entry.BoxedValue ? OnCol : OffCol);
                else if (s.Kind == Kind.Color && ColorUtility.TryParseHtmlString(s.ValueText(), out var col)) SetBase(v, col);
                else if (s.Kind == Kind.Color) SetBase(v, LabelCol);

                switch (s.Kind)
                {
                    case Kind.Int:
                    case Kind.Float:
                        SetStep(r, 0, Choices.StepLabel(s.Big, -1)); SetStep(r, 1, Choices.StepLabel(s.Small, -1));
                        SetStep(r, 2, Choices.StepLabel(s.Small, 1)); SetStep(r, 3, Choices.StepLabel(s.Big, 1));
                        break;
                    case Kind.Enum:
                    case Kind.Choice:
                    case Kind.Color:
                        SetStep(r, 1, "<"); SetStep(r, 2, ">");
                        break;
                }
            }

            pageText.Text.SetIfChanged(maxScroll > 0 ? $"{pg.Scroll + 1}/{maxScroll + 1}" : "");
            up.Go.SetActive(pg.Scroll > 0);
            down.Go.SetActive(pg.Scroll < maxScroll);
            var sel = pg.Selected;
            reset.Go.SetActive(true);
            reset.Interactive = Pages.CanResetSection;
            reset.Text.color = reset.Interactive ? Color.white : Grey;
            SetBase(reset, reset.Interactive ? BtnCol : LabelCol);
            desc.Text.SetIfChanged(sel == null
                ? "Point + trigger or poke to adjust. Grip the top bar to move / tilt this board. Yellow = changed; Reset restores that row. Select a name for help. * = restart required."
                : $"<b>{sel.Name}</b> = {sel.ValueText()}  <color=#999999>(default {sel.Entry.GetDefaultValueAsString()})</color>\n{sel.Entry.Description}" +
                  (sel.Restart ? "\n<color=#FFD060>Restart the game to apply.</color>" : "") +
                  (sel.Kind == Kind.ReadOnly ? "\n<color=#999999>Edit this one in UserData/MelonPreferences.cfg.</color>" : ""));
        }

        static void SetStep(int r, int k, string text)
        {
            var b = steps[r, k];
            b.Go.SetActive(true);
            b.Text.SetIfChanged(text);
        }

        static void Step(int r, int dir)
        {
            var pg = Pages.Cur;
            int idx = pg.Scroll * Rows + r;
            if (idx >= pg.Settings.Count) return;
            Status(Pages.Change(pg.Settings[idx], dir));
            Refresh();
        }

        static void Select(int r)
        {
            var pg = Pages.Cur;
            int idx = pg.Scroll * Rows + r;
            if (idx < pg.Settings.Count) pg.Selected = pg.Selected == pg.Settings[idx] ? null : pg.Settings[idx];
            Refresh();
        }

        static void SetIfChanged(this TextMeshPro t, string text) { if (t.text != text) t.text = text; }

        static void Status(string text) { status.Text.SetIfChanged(text); statusUntil = Time.unscaledTime + 1.5f; }

        // ---- construction -------------------------------------------------------------------------

        static bool Build()
        {
            font = FindFont();
            flat = FindShader();
            if (font == null || flat == null)
            {
                ModSettingsMod.Log.Warning($"can't build the menu: font {(font != null ? "ok" : "missing")}, shader {(flat != null ? "ok" : "missing")}");
                return false;
            }
            root = new GameObject("ModSettingsPanel");
            Object.DontDestroyOnLoad(root);
            root.SetActive(false);
            buttons.Clear();

            var bg = Quad(root.transform, Vector2.zero, new Vector2(W, H), 0.002f, out _);
            SetColor(bg, Bg);

            Make(new Vector2(0, H / 2 - 0.019f), new Vector2(W - 0.02f, 0.03f),
                "GRIP HERE TO MOVE / TILT", 0.12f, BtnCol, null);
            float top = H / 2 - 0.07f;
            prevCat = Make(new Vector2(-W / 2 + 0.03f, top), new Vector2(Btn, Btn), "<", 0.2f, BtnCol, () => { Pages.Turn(-1); Refresh(); });
            title = Make(new Vector2(-0.025f, top), new Vector2(0.50f, Btn), "", 0.22f, Bg, null, TextAlignmentOptions.Center, false);
            nextCat = Make(new Vector2(W / 2 - 0.085f, top), new Vector2(Btn, Btn), ">", 0.2f, BtnCol, () => { Pages.Turn(1); Refresh(); });
            close = Make(new Vector2(W / 2 - 0.03f, top), new Vector2(Btn, Btn), "X", 0.2f, OffCol, () => { Close(); ModSettingsMod.Dbg("closed: X"); });

            float[] stepX = { -0.073f, -0.023f, 0.132f, 0.182f };
            int[] stepDir = { -2, -1, 1, 2 };
            for (int r = 0; r < Rows; r++)
            {
                int row = r;
                float y = top - 0.05f - r * RowStep;
                labels[r] = Make(new Vector2(-0.23f, y), new Vector2(0.245f, Btn), "", 0.16f, LabelCol, () => Select(row), TextAlignmentOptions.MidlineLeft);
                values[r] = Make(new Vector2(0.055f, y), new Vector2(0.09f, Btn), "", 0.16f, BtnCol, () => Step(row, 1));
                resets[r] = Make(new Vector2(0.28f, y), new Vector2(0.12f, Btn), "", 0.15f, BtnCol, () =>
                {
                    int idx = Pages.Cur.Scroll * Rows + row;
                    if (idx < Pages.Cur.Settings.Count) Status(Pages.Reset(Pages.Cur.Settings[idx]));
                    Refresh();
                });
                for (int k = 0; k < 4; k++)
                {
                    int dir = stepDir[k];
                    steps[r, k] = Make(new Vector2(stepX[k], y), new Vector2(Btn + 0.004f, Btn), "", 0.15f, BtnCol, () => Step(row, dir));
                }
            }

            float nav = top - 0.05f - Rows * RowStep;
            up = Make(new Vector2(-W / 2 + 0.045f, nav), new Vector2(0.06f, Btn), "Up", 0.16f, BtnCol, () => { Pages.Cur.Scroll--; Refresh(); });
            pageText = Make(new Vector2(-0.115f, nav), new Vector2(0.07f, Btn), "", 0.15f, Bg, null, TextAlignmentOptions.Center, false);
            down = Make(new Vector2(-0.035f, nav), new Vector2(0.06f, Btn), "Down", 0.16f, BtnCol, () => { Pages.Cur.Scroll++; Refresh(); });
            status = Make(new Vector2(0.065f, nav), new Vector2(0.11f, Btn), "", 0.13f, Bg, null, TextAlignmentOptions.Center, false);
            reset = Make(new Vector2(0.245f, nav), new Vector2(0.20f, Btn), "Reset section", 0.16f, BtnCol, () =>
            {
                Status(Pages.ResetSection());
                Refresh();
            });

            float descTop = nav - Btn / 2 - 0.006f, descH = descTop + H / 2 - 0.008f;
            desc = Make(new Vector2(0, descTop - descH / 2), new Vector2(W - 0.03f, descH), "", 0.12f, Bg, null, TextAlignmentOptions.TopLeft, false, true);
            desc.Text.fontSizeMin = 0.07f;

            ModSettingsMod.Log.Msg($"menu built: font '{font.name}', shader '{flat.name}'");
            return true;
        }

        static Button Make(Vector2 c, Vector2 size, string text, float fontSize, Color col, Action onPress,
                           TextAlignmentOptions align = TextAlignmentOptions.Center, bool interactive = true, bool wrap = false)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(c.x, c.y, 0);
            var quad = Quad(go.transform, Vector2.zero, size, 0, out var mat);
            var b = new Button { Go = go, Quad = quad, Mat = mat, Center = c, Half = size / 2, OnPress = onPress, Base = col, Interactive = interactive && onPress != null };
            SetColor(quad, col);
            if (!b.Interactive && col == Bg) quad.SetActive(false);

            var tgo = new GameObject("Text");
            var t = tgo.AddComponent<TextMeshPro>(); // turns the Transform into a RectTransform
            tgo.transform.SetParent(go.transform, false);
            tgo.transform.localPosition = new Vector3(0, 0, -0.002f);
            tgo.transform.localRotation = Quaternion.identity;
            t.font = font;
            t.rectTransform.sizeDelta = size - new Vector2(0.006f, 0.002f);
            t.enableAutoSizing = true;
            t.fontSizeMax = fontSize;
            t.fontSizeMin = fontSize * 0.5f;
            t.alignment = align;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            t.richText = true;
            t.color = Color.white;
            t.text = text;
            var tr = t.GetComponent<MeshRenderer>();
            if (tr != null) { tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; }
            b.Text = t;
            buttons.Add(b);
            return b;
        }

        static GameObject Quad(Transform parent, Vector2 c, Vector2 size, float z, out Material mat)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            q.name = "Quad";
            q.transform.SetParent(parent, false);
            q.transform.localPosition = new Vector3(c.x, c.y, z);
            q.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = q.GetComponent<MeshRenderer>();
            mat = new Material(flat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        static void SetColor(GameObject quad, Color c)
        {
            var m = quad.GetComponent<MeshRenderer>().sharedMaterial;
            m.SetColor("_BaseColor", c); m.SetColor("_Color", c);
        }

        static void SetBase(Button b, Color c) { b.Base = c; SetColor(b.Quad, c); }

        static TMP_FontAsset FindFont()
        {
            try { var f = TMP_Settings.defaultFontAsset; if (f != null) return f; } catch { }
            TMP_FontAsset any = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>()))
            {
                var f = o.TryCast<TMP_FontAsset>();
                if (f == null) continue;
                if (f.name.Contains("LiberationSans")) return f;
                any ??= f;
            }
            return any;
        }

        static Shader FindShader()
        {
            foreach (var name in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Universal Render Pipeline/Lit" })
            {
                var s = Shader.Find(name);
                if (s != null) return s;
            }
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>()))
            {
                var s = o.TryCast<Shader>();
                if (s != null && s.name == "Universal Render Pipeline/Unlit") return s;
            }
            return null;
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }
    }
}
