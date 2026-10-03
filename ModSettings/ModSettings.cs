using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ModSettings.ModSettingsMod), "Mod Settings", "1.2.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace ModSettings
{
    // An in-VR settings board for every MelonLoader mod. It lists every category in MelonPreferences, so any mod that
    // uses MelonPreferences appears without doing anything. A change sets the entry's value (which fires its
    // OnEntryValueChanged) and saves the file a second later. Whether the change takes effect at once is up to each
    // mod: it does if the mod reads the value when it uses it.
    //
    // Open / close: the "Mod Settings" tile on the game's phone (middle row), or Ctrl+M. Press the buttons with either
    // index fingertip. In flat mode (no headset) Ctrl+M opens the same menu on screen instead, used with the mouse.
    public class ModSettingsMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        static MelonPreferences_Entry<string> OpenKey;
        static MelonPreferences_Entry<float> Distance, Scale;
        static MelonPreferences_Entry<bool> PhoneTile;
        internal static MelonPreferences_Entry<bool> DebugLog;

        internal static readonly List<HVRHandGrabber> Hands = new();
        static readonly Transform[] tips = new Transform[2];      // [0] left, [1] right
        static readonly HVRHandGrabber[] tipHands = new HVRHandGrabber[2];
        static float sceneStart, nextSearch, nextTipSearch, lastPhoneOpen = -9;
        static bool phoneDone;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("ModSettings", "Mod Settings");
            PhoneTile = c.CreateEntry("PhoneTile", true, description: "Put a Mod Settings tile in an empty slot of the phone's middle row. Press it to open / close this menu.");
            OpenKey = c.CreateEntry("OpenKey", "M", description: "Keyboard: Ctrl + this key opens / closes the menu (Input System key name).");
            Distance = c.CreateEntry("PanelDistance", 0.4f, description: "How far in front of your eyes the menu opens, in metres. Fingertips reach it up to about 0.6; farther, point and press trigger (the laser reaches 4 m). Raise PanelScale if the text gets too small.");
            Scale = c.CreateEntry("PanelScale", 1f, description: "Menu size (1 = 72 x 56 cm). Applies the next time it opens. Grip the top bar to move or tilt the board.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log opening/closing and which fingertips were found; write the phone's home-screen layout to UserData/ModSettings_phone.txt once.");
            Log.Msg($"loaded - open with Ctrl+{OpenKey.Value}{(PhoneTile.Value ? " or the Mod Settings tile on the phone" : "")}");
        }

        // Every mod has made its entries by now: move untouched settings to defaults that a mod update changed.
        public override void OnLateInitializeMelon() => DefaultChanges.Scan();

        internal static string OpenKeyName => OpenKey.Value;

        // Flat mode: no headset running, or the flat-mode controller is up and no VR hands exist.
        static bool Flat
        {
            get
            {
                try { if (!XRSettings.isDeviceActive) return true; } catch { }
                return Hands.Count == 0 && FlatMenu.HasController();
            }
        }

        internal static void Dbg(string msg) { if (DebugLog.Value) Log.Msg(msg); }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (Panel.IsOpen) Panel.Close();
            FlatMenu.Close();
            tips[0] = tips[1] = null;
            phoneDone = PhoneApp.Ready;
            sceneStart = nextSearch = Time.unscaledTime;
        }

        public override void OnUpdate()
        {
            try
            {
                float now = Time.unscaledTime;
                var cam = Camera.main;
                var head = cam != null ? cam.transform : null;

                var kb = Keyboard.current;
                if (kb != null && kb.ctrlKey.isPressed && Enum.TryParse<Key>(OpenKey.Value, true, out var key) && kb[key].wasPressedThisFrame)
                    Toggle(head, "Ctrl+" + OpenKey.Value);

                if (now >= nextSearch && now - sceneStart < 60f && (Hands.Count == 0 || (PhoneTile.Value && !phoneDone)))
                {
                    nextSearch = now + 1f;
                    FindRig();
                    if (PhoneTile.Value && !phoneDone) phoneDone = PhoneApp.TryAdd();
                }
                for (int i = 0; i < Hands.Count; i++) if (!Panel.Alive(Hands[i])) Hands.RemoveAt(i--);

                if (PhoneTile.Value) PhoneApp.Update();
                if (Panel.IsOpen)
                {
                    if ((!Panel.Alive(tips[0]) || !Panel.Alive(tips[1])) && now >= nextTipSearch) { nextTipSearch = now + 1f; FindTips(); }
                    Panel.Update(head, tips, tipHands);
                }
                FlatMenu.Update();
                if (Pages.SaveAt > 0 && now >= Pages.SaveAt) Pages.SaveNow();
            }
            catch (Exception e) { VRInteraction.Close(); Log.Warning($"update: {e.GetType().Name}: {e.Message}"); }
        }

        public override void OnGUI()
        {
            try { FlatMenu.Draw(); } catch (Exception e) { Log.Warning($"flat menu: {e.GetType().Name}: {e.Message}"); FlatMenu.Close(); }
        }

        public override void OnDeinitializeMelon()
        {
            Panel.Close();
            FlatMenu.Close();
        }

        static void Toggle(Transform head, string how)
        {
            if (FlatMenu.IsOpen || (Flat && how != "phone")) { FlatMenu.Toggle(how); return; }
            if (Panel.IsOpen) { Panel.Close(); Dbg($"closed: {how}"); return; }
            if (head == null) { Log.Warning("no camera - can't place the menu"); return; }
            FindTips();
            // From the phone: the phone is held low in front, so the board opens higher, above it.
            Panel.Open(head, Distance.Value, Scale.Value, how == "phone" ? 0.06f : 0.22f);
            if (Panel.IsOpen) Dbg($"opened: {how}");
        }

        internal static void OpenFromPhone()
        {
            float now = Time.unscaledTime;
            if (now - lastPhoneOpen < 0.5f) return; // one touch can push twice
            lastPhoneOpen = now;
            var cam = Camera.main;
            Toggle(cam != null ? cam.transform : null, "phone");
        }

        // ---- rig lookup ------------------------------------------------------------------------------

        static void FindRig()
        {
            if (Hands.Count == 0)
                foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None))
                    Register(o.TryCast<HVRHandGrabber>());
        }

        internal static void Register(HVRHandGrabber h)
        {
            if (!Panel.Alive(h)) return;
            foreach (var x in Hands) if (x.Pointer == h.Pointer) return;
            Hands.Add(h);
        }

        // The index fingertip bone of each hand: the glove rig names it "LndexNub" / "RndexNub" (sic). Falls back to
        // the last index joint, then to the palm.
        static void FindTips()
        {
            var found = new List<string>();
            foreach (var h in Hands)
            {
                int i = h.IsLeftHand ? 0 : 1;
                tipHands[i] = h;
                var palm = Panel.Alive(h.Palm) ? h.Palm : h.transform;
                var t = BestTip(h.transform, palm.position, h.IsLeftHand) ?? BestTip(h.transform.root, palm.position, h.IsLeftHand);
                tips[i] = t ?? palm;
                found.Add($"{(i == 0 ? "left" : "right")} '{tips[i].name}' {Vector3.Distance(tips[i].position, palm.position) * 100:0} cm from the palm");
            }
            if (found.Count > 0) Dbg("fingertips: " + string.Join(", ", found));
        }

        static Transform BestTip(Transform under, Vector3 palm, bool left)
        {
            Transform best = null;
            int bestRank = 99;
            float bestDist = 0.25f;
            foreach (var t in under.GetComponentsInChildren<Transform>(false))
            {
                var n = t.name;
                int rank = n.EndsWith("ndexNub", StringComparison.OrdinalIgnoreCase) ? 0
                    : n.EndsWith("IndexFinger3", StringComparison.OrdinalIgnoreCase) || n.StartsWith("index_03", StringComparison.OrdinalIgnoreCase) ? 1 : 99;
                if (rank == 99) continue;
                bool nameLeft = n.StartsWith("L") || n.EndsWith("_l"), nameRight = n.StartsWith("R") || n.EndsWith("_r");
                if (left ? nameRight : nameLeft) continue;
                float d = Vector3.Distance(t.position, palm);
                if (d > 0.25f) continue;
                if (rank < bestRank || (rank == bestRank && d < bestDist)) { best = t; bestRank = rank; bestDist = d; }
            }
            return best;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Start))]
    internal static class HandStartPatch
    {
        static void Postfix(HVRHandGrabber __instance)
        {
            try { ModSettingsMod.Register(__instance); } catch { }
        }
    }
}
