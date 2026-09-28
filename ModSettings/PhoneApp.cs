using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModSettings
{
    // A "Mod Settings" tile on the game's phone, in an empty slot of the middle row of the home screen.
    //
    // The home screen (ANBSmartphone.mainApp) holds app tiles: ANBInterfaceButton, pressed by the fingertip trigger,
    // whose buttonPush plays the click and fires onButtonPush. ANBSmartphone.allButtons (List<GameObject>) is what
    // toggleInput(held) switches on/off together, so the tile joins that list and shows/hides with the others.
    //
    // The tile is a copy of the Game Options tile, put in the right-hand empty slot of the middle row: made under an
    // inactive holder so nothing in it wakes before it's cleaned (the Awake-singleton trap), its localisation component
    // removed so it keeps our label, and its actions emptied, so the press only opens our board (a patch on
    // buttonPush) and never the game's pause menu.
    internal static class PhoneApp
    {
        const string TileName = "ModSettingsPhoneTile";
        static GameObject tile;
        static IntPtr tileButton;
        static bool dumped;
        static Transform centre, fallback;             // home slot (App_5) and where the tile goes while a mission uses it
        static Vector3 tilePos, tileScale;
        static Quaternion tileRot;
        static float nextCheck;

        public static bool IsOurs(ANBInterfaceButton b) => b != null && b.Pointer == tileButton && tileButton != IntPtr.Zero;

        public static bool Ready => Panel.Alive(tile);

        // Returns true once the tile exists (or there's nothing to do).
        public static bool TryAdd()
        {
            if (Panel.Alive(tile)) return true;
            ANBSmartphone phone = null;
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<ANBSmartphone>(), FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var p = o.TryCast<ANBSmartphone>();
                if (p != null && Panel.Alive(p.mainApp)) { phone = p; break; }
            }
            if (phone == null) return false;
            try { Add(phone); }
            catch (Exception e) { ModSettingsMod.Log.Warning($"phone tile failed: {e}"); }
            return true; // don't retry every second after a failure
        }

        static void Add(ANBSmartphone phone)
        {
            var main = phone.mainApp.transform;
            if (ModSettingsMod.DebugLog.Value && !dumped) { dumped = true; Dump(phone); }

            // The home screen is a 3x3 GridLayoutGroup ("Apps") of slot containers App_1..App_9 (fill order = reading
            // order on the screen). Each slot has a dark Button_Back tile; filled slots also hold a Button_* with an
            // ANBInterfaceButton. App_4 and App_6 are empty. App_5, the centre, holds the mission Data Breach buttons,
            // hidden until a mission needs them: the tile lives there and moves to App_4 (left, away from Game Options
            // below App_6) while one of them is showing. Local x is mirrored on this screen: never place by geometry.
            Transform grid = null;
            foreach (var g in main.GetComponentsInChildren<GridLayoutGroup>(true)) { grid = g.transform; break; }
            if (grid == null) { ModSettingsMod.Log.Warning("phone tile: no app grid on the home screen"); return; }

            Transform template = null;
            centre = fallback = null;
            for (int i = 0; i < grid.childCount; i++)
            {
                var s = grid.GetChild(i);
                var old = s.Find(TileName);
                if (old != null) Object.Destroy(old.gameObject);
                var b = FirstButton(s);
                if (b != null && b.transform.parent == s) template = b.transform; // the last filled slot's button = Game Options
                if (b == null && fallback == null && i < grid.childCount - 3) fallback = s; // the first empty slot above the bottom row
            }
            if (grid.childCount >= 3) centre = grid.GetChild(grid.childCount / 2);
            if (template == null || (centre == null && fallback == null)) { ModSettingsMod.Log.Warning($"phone tile: {(template == null ? "no tile to copy" : "no slot")} in '{grid.name}'"); return; }
            fallback ??= centre;
            centre ??= fallback;
            var slot = CentreBusy() ? fallback : centre;
            var holder = new GameObject("ModSettingsTileHolder");
            holder.SetActive(false);
            var clone = Object.Instantiate(template.gameObject, holder.transform);
            clone.name = TileName;
            foreach (var c in clone.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetIl2CppType().Name.Contains("Language")) Object.DestroyImmediate(c);
            foreach (var t in clone.GetComponentsInChildren<TextMeshProUGUI>(true)) t.text = "Mod Settings";
            var btn = clone.GetComponent<ANBInterfaceButton>();
            // Nothing of the original tile's action may survive: its own events, the UI Button's click, the EventTrigger.
            btn.onButtonPush = new ANBInterfaceButton.ANBInterfaceButtonPushEvent();
            btn.onButtonPushToggleOn = new ANBInterfaceButton.ANBInterfaceButtonPushEvent();
            btn.onButtonPushToggleOff = new ANBInterfaceButton.ANBInterfaceButtonPushEvent();
            btn.isToggleButton = false;
            btn.locked = false;
            var uiButton = clone.GetComponent<Button>();
            if (uiButton != null) uiButton.onClick = new Button.ButtonClickedEvent();
            var trigger = clone.GetComponent<EventTrigger>();
            if (trigger != null) trigger.triggers.Clear();
            tileButton = btn.Pointer;

            tilePos = template.localPosition; tileRot = template.localRotation; tileScale = template.localScale;
            Place(clone.transform, slot);
            clone.SetActive(template.gameObject.activeSelf);
            Object.Destroy(holder);
            phone.allButtons.Add(clone);
            tile = clone;
            ModSettingsMod.Log.Msg($"phone tile added in '{slot.name}' (copy of '{template.name}' from '{template.parent.name}')");
        }

        // Keep the tile in the centre unless a mission button is showing there. Checked 4 times a second.
        public static void Update()
        {
            if (!Panel.Alive(tile) || !Panel.Alive(centre) || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            var want = CentreBusy() ? fallback : centre;
            if (!Panel.Alive(want) || tile.transform.parent == want) return;
            Place(tile.transform, want);
            ModSettingsMod.Dbg($"phone tile moved to '{want.name}'{(want == centre ? " (centre free again)" : " (a mission app is using the centre)")}");
        }

        static void Place(Transform t, Transform slot)
        {
            t.SetParent(slot, false);
            t.localPosition = tilePos; t.localRotation = tileRot; t.localScale = tileScale;
            t.SetAsLastSibling(); // drawn over the slot's dark Button_Back
        }

        // A holder in the centre slot (BreachHolder / BreachHolder_1) switched on = a mission is using the slot. Judged
        // by the holder's own switch, not the button's: the phone turns buttons on/off whenever it's picked up or put
        // away, while the holders stay off outside missions (the centre shows empty with the phone in hand).
        static bool CentreBusy()
        {
            if (!Panel.Alive(centre)) return false;
            for (int i = 0; i < centre.childCount; i++)
            {
                var c = centre.GetChild(i);
                if (c.name == TileName || !c.gameObject.activeSelf) continue;
                if (c.GetComponentInChildren<ANBInterfaceButton>(true) != null) return true;
            }
            return false;
        }

        static ANBInterfaceButton FirstButton(Transform slot)
        {
            foreach (var b in slot.GetComponentsInChildren<ANBInterfaceButton>(true))
                if (b != null && b.gameObject.name != TileName) return b;
            return null;
        }

        // One-off layout dump for debugging, to its own file (not the MelonLoader log).
        static void Dump(ANBSmartphone phone)
        {
            try
            {
                var main = phone.mainApp.transform;
                var sb = new StringBuilder($"phone '{phone.name}', mainApp '{main.name}'\n");
                void Walk(Transform t, int d)
                {
                    if (d > 6) return;
                    var p = main.InverseTransformPoint(t.position);
                    var comps = new List<string>();
                    foreach (var c in t.GetComponents<Component>())
                    {
                        var n = c.GetIl2CppType().Name;
                        if (n != "Transform" && n != "RectTransform" && n != "CanvasRenderer") comps.Add(n);
                    }
                    var tmp = t.GetComponent<TextMeshProUGUI>();
                    sb.Append(new string(' ', d * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)")
                      .Append($" @({p.x:0.###},{p.y:0.###}) [{string.Join(",", comps)}]")
                      .Append(tmp != null ? $" \"{tmp.text}\"" : "").Append('\n');
                    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1);
                }
                Walk(main, 0);
                if (Panel.Alive(phone.dataBreachAppButton)) sb.Append($"dataBreachAppButton '{phone.dataBreachAppButton.name}' @{main.InverseTransformPoint(phone.dataBreachAppButton.transform.position)}\n");
                if (Panel.Alive(phone.codeAppButton)) sb.Append($"codeAppButton '{phone.codeAppButton.name}' @{main.InverseTransformPoint(phone.codeAppButton.transform.position)}\n");
                File.WriteAllText(Path.Combine(MelonEnvironment.UserDataDirectory, "ModSettings_phone.txt"), sb.ToString());
            }
            catch (Exception e) { ModSettingsMod.Log.Warning($"phone dump: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBInterfaceButton), nameof(ANBInterfaceButton.buttonPush))]
    internal static class PhoneTilePushPatch
    {
        static void Postfix(ANBInterfaceButton __instance)
        {
            try { if (PhoneApp.IsOurs(__instance)) ModSettingsMod.OpenFromPhone(); }
            catch (Exception e) { ModSettingsMod.Log.Warning($"phone tile: {e.Message}"); }
        }
    }
}
