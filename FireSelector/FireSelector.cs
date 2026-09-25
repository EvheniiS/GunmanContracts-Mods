using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(FireSelector.FireSelectorMod), "Fire Selector", "1.0.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace FireSelector
{
    // Automatic guns get a fire selector: hold A / X on the SUPPORT hand (the one not holding the
    // grip - on the foregrip or free) to cycle Automatic -> 3-round burst -> single shot.
    //
    // How the game does it (GameAssembly.dll): HurricaneVR's HVRGunBase already implements all three
    // modes through its FireType field (GunFireType: 0 Single, 1 ThreeRoundBurst, 2 Automatic), and
    // only three framework methods read it - none of them overridden by the game's ANBHVRGunBase:
    // - TriggerPulled: Single fires one shot; otherwise it sets IsFiring and RoundsFired = 0.
    // - Shoot: RoundsFired++, and a burst stops at exactly 3.
    // - TriggerReleased: only Automatic stops on release, so a burst finishes if you let go.
    // UpdateShooting keeps shooting every Cooldown while IsFiring. So the mod only writes FireType.
    //
    // Buttons: A / X on the GUN hand is the magazine release (HVRANBAmmoReleaseAction, polled only for
    // the hands on the gun's main grabbable) and stays untouched. The support hand's A / X goes through
    // ANBHVRControllerInputs.checkVRButtonTaps: on the foregrip (OnHandFrontalStabilizerGrabbed puts the
    // gun in that hand's slot too) it is ALSO a magazine release, or the flashlight toggle on guns with
    // the laser/light attachment (AT2); with a free hand it does nothing. The game acts on the press,
    // so the mod holds a selector press back: a tap replays the game's own action on release, a hold
    // switches the mode instead.
    public class FireSelectorMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, Haptics, RememberModes, DebugLog;
        internal static MelonPreferences_Entry<float> HoldSeconds;
        internal static MelonPreferences_Entry<string> SavedModes;

        // Filled by a Harmony postfix on HVRHandGrabber.Start, plus one search per scene as a safety net.
        internal static readonly List<HVRHandGrabber> Hands = new();

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double Now => Clock.Elapsed.TotalSeconds;

        // One per support hand (left, right).
        class Press
        {
            public bool WasDown, Switched;
            public double At;
            public ANBHVRGunBase Gun;         // the gun a press started on, if it was a selector press
            public Tap Held;                  // the game's own A action, held back until release
        }
        static readonly Press[] Presses = { new Press(), new Press() };

        // The arguments of a held-back checkVRButtonTaps call, to replay it on a short tap.
        internal class Tap { public ANBHVRControllerInputs Inputs; public ANBHVRGunBase Gun, OtherGun; public string Side; }
        internal static bool Replaying;

        class GunInfo { public GunFireType Original; public string Key; public bool Selectable; }
        static readonly Dictionary<IntPtr, GunInfo> Guns = new();
        static readonly Dictionary<string, GunFireType> Chosen = new();

        struct Pulse { public double At; public HVRHandGrabber Hand; public float Amp, Dur; }
        static readonly List<Pulse> Pulses = new();

        double _fallbackScanAt = -1;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("FireSelector", "Fire Selector");
            Enabled = c.CreateEntry("Enabled", true, description: "Hold A / X on the support hand to switch an automatic gun between automatic, 3-round burst and single shot.");
            HoldSeconds = c.CreateEntry("HoldSeconds", 0.35f, description: "How long to hold A / X before the mode switches. A shorter press still toggles the flashlight on guns that have one.");
            Haptics = c.CreateEntry("Haptics", true, description: "Vibrate the support hand on a switch: 1 pulse = single, 3 pulses = burst, a long buzz = automatic.");
            RememberModes = c.CreateEntry("RememberModes", true, description: "Each gun type keeps the mode you picked, also after a restart.");
            SavedModes = c.CreateEntry("SavedModes", "", description: "The remembered modes (weapon=mode;...). Written by the mod.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log each gun you hold (its original mode and fire rate) and every selector press.");
            LoadSaved();
            LoggerInstance.Msg($"loaded - hold A / X on the support hand for {HoldSeconds.Value:0.##} s to switch fire mode.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Prune();
            foreach (var p in Presses) { p.Gun = null; p.Held = null; p.Switched = false; }
            Pulses.Clear();
            Guns.Clear();                     // keyed by pointer, which the GC can hand to another object
            _fallbackScanAt = Now + 3.0;
        }

        public override void OnUpdate()
        {
            if (_fallbackScanAt >= 0 && Now >= _fallbackScanAt) { _fallbackScanAt = -1; FallbackScan(); }
            if (!Enabled.Value) return;
            try
            {
                Prune();
                var game = ANBStaticGameManager.ANBmain;
                if (!Alive(game)) return;
                ApplyChosen(game.rightHandGun);
                ApplyChosen(game.leftHandGun);
                foreach (var hand in Hands) UpdateSupportHand(hand);
                RunPulses();
            }
            catch (Exception e) { Log.Warning($"update: {e.GetType().Name}: {e.Message}"); }
        }

        // ---- the selector press -------------------------------------------------------------------

        static void UpdateSupportHand(HVRHandGrabber hand)
        {
            var ctrl = hand.Controller;
            if (ctrl == null) return;
            var p = Presses[hand.IsLeftHand ? 0 : 1];
            bool down = ctrl.PrimaryButtonState.Active;

            if (down && !p.WasDown)
            {
                p.At = Now; p.Switched = false;
                p.Gun = GunForSupportHand(hand);
                if (DebugLog.Value && p.Gun != null)
                    Log.Msg($"{Side(hand)} A/X pressed on {Describe(p.Gun)}{(p.Held != null ? " (game's A action held back)" : "")}");
            }
            else if (down && p.Gun != null && !p.Switched && Now - p.At >= HoldSeconds.Value)
            {
                // Still the same gun, and this hand is still free or on it.
                var gun = GunForSupportHand(hand);
                if (gun != null && gun.Pointer == p.Gun.Pointer) { Cycle(gun, hand); p.Switched = true; }
                else p.Gun = null;
            }
            else if (!down && p.WasDown)
            {
                if (p.Held != null && !p.Switched) Replay(p.Held);
                p.Gun = null; p.Held = null;
            }
            p.WasDown = down;
        }

        // The automatic gun held in the OTHER hand's grip, if this hand is free or on that same gun
        // (foregrip / stabilizer). Null otherwise - a hand holding a knife, a magazine, the phone or
        // an arrow keeps A / X for itself.
        internal static ANBHVRGunBase GunForSupportHand(HVRHandGrabber support)
        {
            var game = ANBStaticGameManager.ANBmain;
            if (!Alive(game) || game.Paused) return null;
            var gun = support.IsLeftHand ? game.rightHandGun : game.leftHandGun;
            if (!Alive(gun) || !GunInfoOf(gun).Selectable) return null;

            HVRHandGrabber gripHand = null;
            foreach (var h in Hands) if (h.IsLeftHand != support.IsLeftHand) gripHand = h;
            if (!Alive(gripHand) || !(Holds(gripHand, gun.Grabbable) || Holds(gripHand, gun.myGrabbable))) return null;

            var held = support.GrabbedTarget;
            if (!Alive(held)) return gun;                                // free hand
            if (Holds(support, gun.StabilizerGrabbable) || Holds(support, gun.myStabilizerGrabbable)) return gun;   // foregrip
            var anb = held.TryCast<ANBHVRGrabbable>();
            if (anb != null && anb.isMag) return null;
            var owner = held.GetComponentInParent<ANBHVRGunBase>();
            return Alive(owner) && owner.Pointer == gun.Pointer ? gun : null;
        }

        static bool Holds(HVRHandGrabber hand, HVRGrabbable g)
        {
            var t = hand.GrabbedTarget;
            return Alive(t) && Alive(g) && t.Pointer == g.Pointer;
        }

        static void Cycle(ANBHVRGunBase gun, HVRHandGrabber hand)
        {
            var next = gun.FireType switch
            {
                GunFireType.Automatic => GunFireType.ThreeRoundBurst,
                GunFireType.ThreeRoundBurst => GunFireType.Single,
                _ => GunFireType.Automatic,
            };
            SetMode(gun, next);
            var info = GunInfoOf(gun);
            Chosen[info.Key] = next;
            if (RememberModes.Value) Save();
            Log.Msg($"{info.Key}: fire mode -> {Name(next)}");
            if (Haptics.Value) Buzz(hand, next);
        }

        // Also stops a shot sequence in progress: UpdateShooting keeps firing while IsFiring, and only
        // Automatic clears it on trigger release (a burst only at exactly 3 rounds), so switching
        // mid-fire could otherwise empty the magazine.
        static void SetMode(ANBHVRGunBase gun, GunFireType mode)
        {
            gun.FireType = mode;
            gun.IsFiring = false;
            gun.RoundsFired = 0;
        }

        // Guns are pooled and reset by the game (scene loads, the gun wall), which puts the prefab's
        // mode back - so the picked mode is re-applied to whatever gun is in hand.
        static void ApplyChosen(ANBHVRGunBase gun)
        {
            if (!Alive(gun)) return;
            var info = GunInfoOf(gun);
            if (!info.Selectable || !Chosen.TryGetValue(info.Key, out var mode) || gun.FireType == mode) return;
            SetMode(gun, mode);
            if (DebugLog.Value) Log.Msg($"{info.Key}: back to {Name(mode)}");
        }

        static GunInfo GunInfoOf(ANBHVRGunBase gun)
        {
            if (Guns.TryGetValue(gun.Pointer, out var info)) return info;
            string key = null;
            try { key = gun.ANBwpt?.WeaponID; } catch { }
            if (string.IsNullOrEmpty(key)) key = gun.name.Replace("(Clone)", "").Trim();
            info = new GunInfo
            {
                Original = gun.FireType,
                Key = key,
                // A remembered mode means it was automatic when first seen - it may be set to single now.
                Selectable = (gun.FireType == GunFireType.Automatic || Chosen.ContainsKey(key)) && !gun.isBow && !gun.isShotgun,
            };
            Guns[gun.Pointer] = info;
            if (DebugLog.Value)
                Log.Msg($"gun '{gun.name}' ({key}): {Name(info.Original)}, cooldown {gun.Cooldown:0.###} s " +
                        $"({(gun.Cooldown > 0 ? 60f / gun.Cooldown : 0):0} rounds/min){(info.Selectable ? " - selector available" : "")}");
            return info;
        }

        static string Describe(ANBHVRGunBase gun) => $"{GunInfoOf(gun).Key} ({Name(gun.FireType)})";
        static string Name(GunFireType m) => m switch
        {
            GunFireType.Single => "single",
            GunFireType.ThreeRoundBurst => "3-round burst",
            _ => "automatic",
        };

        // ---- the game's own support-hand A action: held back on a selector press, replayed on a tap ----

        // Prefix on checkVRButtonTaps(buttonA, buttonB, Gun, otherGun, side). Gun = this side's slot,
        // otherGun = the other side's. The game: if otherGun is two-handed with the light attachment,
        // A toggles its flashlight; otherwise A calls Gun.ReleaseAmmo (Gun is the same gun when this
        // hand is on the foregrip, null when it's free).
        internal static void BeforeButtonTaps(ANBHVRControllerInputs inputs, ref bool buttonA, ANBHVRGunBase gun, ANBHVRGunBase otherGun, string side)
        {
            if (Replaying || !buttonA || !Enabled.Value || !Alive(otherGun)) return;
            bool left = side == "Left";
            foreach (var h in Hands)
            {
                if (h.IsLeftHand != left) continue;
                var selected = GunForSupportHand(h);
                if (selected == null || selected.Pointer != otherGun.Pointer) return;
                Presses[left ? 0 : 1].Held = new Tap { Inputs = inputs, Gun = gun, OtherGun = otherGun, Side = side };
                buttonA = false;
                return;
            }
        }

        static void Replay(Tap t)
        {
            if (!Alive(t.Inputs)) return;
            Replaying = true;
            try
            {
                t.Inputs.checkVRButtonTaps(true, false, Alive(t.Gun) ? t.Gun : null, Alive(t.OtherGun) ? t.OtherGun : null, t.Side);
                if (DebugLog.Value) Log.Msg($"{t.Side} A/X short tap - game action replayed ({(Alive(t.Gun) ? "mag release / flashlight" : "flashlight or nothing")})");
            }
            catch (Exception e) { Log.Warning($"replay: {e.Message}"); }
            finally { Replaying = false; }
        }

        // ---- haptics ----------------------------------------------------------------------------------

        static void Buzz(HVRHandGrabber hand, GunFireType mode)
        {
            double t = Now;
            if (mode == GunFireType.Automatic) Pulses.Add(new Pulse { At = t, Hand = hand, Amp = 0.6f, Dur = 0.25f });
            else
                for (int i = 0; i < (mode == GunFireType.ThreeRoundBurst ? 3 : 1); i++)
                    Pulses.Add(new Pulse { At = t + i * 0.1, Hand = hand, Amp = 0.7f, Dur = 0.04f });
        }

        static void RunPulses()
        {
            for (int i = 0; i < Pulses.Count; i++)
            {
                if (Pulses[i].At > Now) continue;
                var p = Pulses[i];
                Pulses.RemoveAt(i--);
                try { if (Alive(p.Hand)) p.Hand.Controller?.Vibrate(p.Amp, p.Dur, 160f); } catch { }
            }
        }

        // ---- remembered modes ---------------------------------------------------------------------------

        static void LoadSaved()
        {
            foreach (var part in (SavedModes.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.LastIndexOf('=');
                if (eq > 0 && Enum.TryParse<GunFireType>(part[(eq + 1)..], out var m)) Chosen[part[..eq]] = m;
            }
        }

        static void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in Chosen) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            SavedModes.Value = sb.ToString();
            MelonPreferences.Save();
        }

        // ---- plumbing ----------------------------------------------------------------------------------

        void FallbackScan()
        {
            int before = Hands.Count;
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None))
                Register(o.TryCast<HVRHandGrabber>());
            if (DebugLog.Value) Log.Msg($"scene check: {Hands.Count} hand(s){(Hands.Count > before ? $" - {Hands.Count - before} missed by registration" : "")}");
        }

        internal static void Register(HVRHandGrabber h)
        {
            if (!Alive(h)) return;
            foreach (var x in Hands) if (x.Pointer == h.Pointer) return;
            Hands.Add(h);
        }

        static void Prune()
        {
            for (int i = 0; i < Hands.Count; i++) if (!Alive(Hands[i])) Hands.RemoveAt(i--);
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }

        static string Side(HVRHandGrabber h) => h.IsLeftHand ? "left" : "right";
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Start))]
    internal static class HandStartPatch
    {
        static void Postfix(HVRHandGrabber __instance)
        {
            try { FireSelectorMod.Register(__instance); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBHVRControllerInputs), nameof(ANBHVRControllerInputs.checkVRButtonTaps))]
    internal static class ButtonTapsPatch
    {
        static void Prefix(ANBHVRControllerInputs __instance, ref bool __0, ANBHVRGunBase __2, ANBHVRGunBase __3, string __4)
        {
            try { FireSelectorMod.BeforeButtonTaps(__instance, ref __0, __2, __3, __4); }
            catch (Exception e) { FireSelectorMod.Log.Warning($"button taps: {e.Message}"); }
        }
    }
}
