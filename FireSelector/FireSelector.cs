using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using Il2CppInfimaGames.LowPolyShooterPack;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(FireSelector.FireSelectorMod), "Fire Selector", "1.2.3", "Evgeeso")]
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
    //
    // Flat (desktop) mode uses the Low Poly Shooter Pack Character, which ignores FireType: Character.Update
    // fires again while the fire button is held only if Weapon.IsAutomatic() (the `automatic` field), and a
    // non-automatic weapon fires once per press from OnTryFire. So flat Single = automatic off, Burst =
    // automatic on until the third Character.Fire, then off until the button is released. Every gamepad
    // button is taken in flat, so the switch is a keyboard key (B is unbound in the game).
    public class FireSelectorMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, AllowBurst, Haptics, RememberModes, DebugLog;
        internal static MelonPreferences_Entry<float> HoldSeconds;
        internal static MelonPreferences_Entry<string> SavedModes, FlatKey;

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
            AllowBurst = c.CreateEntry("AllowBurst", true, description: "Include 3-round burst in the cycle. false = the selector only switches between automatic and single shot.");
            HoldSeconds = c.CreateEntry("HoldSeconds", 0.35f, description: "How long to hold A / X before the mode switches. A shorter press still toggles the flashlight on guns that have one.");
            Haptics = c.CreateEntry("Haptics", true, description: "Vibrate the support hand on a switch: 1 pulse = single, 3 pulses = burst, a long buzz = automatic.");
            RememberModes = c.CreateEntry("RememberModes", true, description: "Each gun type keeps the mode you picked, also after a restart.");
            FlatKey = c.CreateEntry("FlatKey", "B", description: "Flat (desktop) mode: this key cycles the fire mode of the gun in your hands (Input System key name, empty = off). The game does not use B.");
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
            FlatGuns.Clear();
            _burstWeapon = null; _burstShots = 0;
            _fallbackScanAt = Now + 3.0;
        }

        public override void OnUpdate()
        {
            if (_fallbackScanAt >= 0 && Now >= _fallbackScanAt) { _fallbackScanAt = -1; FallbackScan(); }
            if (!Enabled.Value) return;
            try
            {
                Prune();
                UpdateFlat();
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
                GunFireType.Automatic => AllowBurst.Value ? GunFireType.ThreeRoundBurst : GunFireType.Single,
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
            if (!Alive(gun) || gun.FPSGun) return;   // flat guns are handled by the flat code below
            var info = GunInfoOf(gun);
            if (!info.Selectable || !Chosen.TryGetValue(info.Key, out var mode)) return;
            if (mode == GunFireType.ThreeRoundBurst && !AllowBurst.Value) mode = GunFireType.Automatic;   // burst remembered from before it was turned off
            if (gun.FireType == mode) return;
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

        // ---- flat (desktop) mode ----------------------------------------------------------------------

        static Character _character;          // set by the Character.Fire patch, or looked up on a key press
        class FlatInfo { public string Key; public bool Selectable; }
        static readonly Dictionary<IntPtr, FlatInfo> FlatGuns = new();
        static Weapon _burstWeapon;           // the weapon in a burst; automatic is switched off after shot 3
        static int _burstShots, _burstCalls;
        static ANBHVRGunBase _fireGun;        // the HVR gun behind the weapon in the current Fire call
        static float _fireStamp;              // its TimeOfLastShot before the call
        static float _burstShotAt;          // game time, so slow motion does not cut a burst short
        internal static bool FireHeld;        // the real fire button, from Character.OnTryFire (Started / Canceled)
        static bool _keyWasDown;
        static string _toast;
        static double _toastUntil;
        static GUIStyle _toastStyle;

        static void UpdateFlat()
        {
            UpdateBurst();

            // Own edge detection: wasPressedThisFrame stayed true for two frames once (one press, two switches).
            var kb = Keyboard.current;
            if (kb == null || string.IsNullOrEmpty(FlatKey.Value) || !Enum.TryParse<Key>(FlatKey.Value, true, out var key)) return;
            bool down = kb[key].isPressed, pressed = down && !_keyWasDown;
            _keyWasDown = down;
            if (!pressed || kb.ctrlKey.isPressed) return;   // Ctrl + key belongs to other mods' menus
            if (!Alive(_character)) _character = Object.FindFirstObjectByType<Character>();
            if (!Alive(_character) || !_character.cursorLocked || _character.menuShown || _character.editingGun) return;
            var w = FlatWeapon(_character);
            if (w == null) return;
            var info = FlatInfoOf(w);
            if (!info.Selectable) { Toast("This gun has one fire mode"); return; }

            var cur = Chosen.TryGetValue(info.Key, out var m) ? m : (w.automatic ? GunFireType.Automatic : GunFireType.Single);
            if (cur == GunFireType.ThreeRoundBurst && !AllowBurst.Value) cur = GunFireType.Automatic;
            var next = cur switch
            {
                GunFireType.Automatic => AllowBurst.Value ? GunFireType.ThreeRoundBurst : GunFireType.Single,
                GunFireType.ThreeRoundBurst => GunFireType.Single,
                _ => GunFireType.Automatic,
            };
            EndBurst();
            Chosen[info.Key] = next;
            w.automatic = next != GunFireType.Single;
            if (RememberModes.Value) Save();
            Log.Msg($"{info.Key}: fire mode -> {Name(next)} (flat)");
            Toast($"Fire mode: {Name(next)}");
        }

        static Weapon FlatWeapon(Character ch)
        {
            var wb = ch.equippedWeapon;
            return Alive(wb) ? wb.TryCast<Weapon>() : null;
        }

        // Same key as VR (the HVR gun's WeaponID), so a mode picked in one mode carries over to the other.
        static FlatInfo FlatInfoOf(Weapon w)
        {
            if (FlatGuns.TryGetValue(w.Pointer, out var info)) return info;
            ANBFpsWeapons fps = null; ANBHVRGunBase gun = null;
            try { fps = w.ANBfps; gun = Alive(fps) ? fps.HVRgunbase : null; } catch { }
            string key = null;
            try { key = Alive(gun) ? gun.ANBwpt?.WeaponID : null; } catch { }
            if (string.IsNullOrEmpty(key)) key = (Alive(gun) ? gun.name : w.name).Replace("(Clone)", "").Trim();
            bool special = (Alive(gun) && (gun.isBow || gun.isShotgun)) || (Alive(fps) && fps.isShotgun);
            info = new FlatInfo { Key = key, Selectable = (w.automatic || Chosen.ContainsKey(key)) && !special };
            FlatGuns[w.Pointer] = info;
            if (DebugLog.Value)
                Log.Msg($"flat gun '{w.name}' ({key}): {(w.automatic ? "automatic" : "single")}, {w.roundsPerMinutes} rounds/min{(info.Selectable ? " - selector available" : "")}");
            return info;
        }

        // Character.Fire prefix: put the picked mode on the weapon (flat guns are rebuilt on loads and swaps).
        // A remembered Single on a weapon that is still automatic costs nothing: this shot goes out, then
        // Update stops because automatic is now off - exactly one shot.
        internal static void BeforeFlatFire(Character ch)
        {
            if (!Enabled.Value) return;
            _character = ch;
            var w = FlatWeapon(ch);
            if (w == null) return;
            // FPSshoot fakes a trigger pull + release on the HVR gun behind the flat weapon; only Single
            // fires on the pull (Automatic is cancelled by the release: no bullet, Burst fires 3 per shot).
            // ANBFpsWeapons.Init sets it to Single; keep it there.
            _fireGun = null;
            try
            {
                var gun = w.ANBfps?.HVRgunbase;
                if (Alive(gun))
                {
                    if (gun.FireType != GunFireType.Single) SetMode(gun, GunFireType.Single);
                    _fireGun = gun; _fireStamp = gun.TimeOfLastShot;
                }
            }
            catch { }
            var info = FlatInfoOf(w);
            if (!info.Selectable || !Chosen.TryGetValue(info.Key, out var mode)) return;
            if (mode == GunFireType.ThreeRoundBurst && !AllowBurst.Value) mode = GunFireType.Automatic;
            if (mode == GunFireType.Single) w.automatic = false;
            else if (_burstShots == 0) w.automatic = true;
        }

        // Character.Update calls Fire EVERY FRAME while an automatic weapon is held: the rate of fire is the HVR
        // gun's Cooldown, checked in TriggerPulled, which stamps TimeOfLastShot only when a round really goes
        // out. So a burst counts stamp changes, not Fire calls (3 calls = 3 frames = one round).
        internal static void AfterFlatFire(Character ch)
        {
            if (!Enabled.Value || !AllowBurst.Value) return;
            var w = FlatWeapon(ch);
            if (w == null) return;
            var info = FlatInfoOf(w);
            if (!info.Selectable || !Chosen.TryGetValue(info.Key, out var mode) || mode != GunFireType.ThreeRoundBurst) return;
            if (_burstShots == 0) { _burstWeapon = w; _burstShotAt = Time.time; _burstCalls = 0; }
            _burstCalls++;
            if (!Alive(_fireGun) || _fireGun.TimeOfLastShot == _fireStamp) return;   // in cooldown: no round
            _burstShotAt = Time.time;
            if (++_burstShots >= 3)
            {
                w.automatic = false;
                ch.holdingButtonFire = FireHeld;   // hand the flag back to the real button
            }
        }

        // A burst always fires 3 rounds, like HVR's (TriggerReleased doesn't stop a burst): a click is shorter
        // than 3 rounds at 650 rounds/min (0.18 s), so the held flag is kept on until round 3. After round 3 it
        // waits for the button to come up; before it, it gives up when the weapon changes or no round comes for
        // 0.5 s of game time (empty magazine).
        static void UpdateBurst()
        {
            if (_burstWeapon == null) return;
            bool sameWeapon = Alive(_character) && Alive(_burstWeapon) && Alive(_character.equippedWeapon) &&
                              _character.equippedWeapon.Pointer == _burstWeapon.Pointer;
            if (!sameWeapon) { EndBurst(); return; }
            if (_burstShots >= 3) { if (!FireHeld) EndBurst(); return; }
            if (Time.time - _burstShotAt > 0.5) { EndBurst(); return; }
            _character.holdingButtonFire = true;
        }

        static void EndBurst()
        {
            if (_burstWeapon == null) return;
            if (DebugLog.Value) Log.Msg($"burst: {_burstShots} round(s) from {_burstCalls} Fire call(s){(_burstShots < 3 ? " - cut short" : "")}");
            if (Alive(_burstWeapon)) _burstWeapon.automatic = true;
            if (Alive(_character) && _burstShots < 3) _character.holdingButtonFire = FireHeld;
            _burstShots = 0; _burstCalls = 0; _burstWeapon = null;
        }

        static void Toast(string text) { _toast = text; _toastUntil = Now + 1.5; }

        public override void OnGUI()
        {
            if (_toast == null || Now > _toastUntil) return;
            _toastStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, Screen.height * 0.72f, Screen.width, 40);
            var c = GUI.color;
            GUI.color = Color.black; GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), _toast, _toastStyle);
            GUI.color = Color.white; GUI.Label(r, _toast, _toastStyle);
            GUI.color = c;
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

    [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.Fire))]
    internal static class FlatFirePatch
    {
        static void Prefix(Character __instance)
        {
            try { FireSelectorMod.BeforeFlatFire(__instance); }
            catch (Exception e) { FireSelectorMod.Log.Warning($"flat fire: {e.Message}"); }
        }
        static void Postfix(Character __instance)
        {
            try { FireSelectorMod.AfterFlatFire(__instance); }
            catch (Exception e) { FireSelectorMod.Log.Warning($"flat fire: {e.Message}"); }
        }
    }

    // Started sets holdingButtonFire, Canceled clears it: read it back as the real button state.
    [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.OnTryFire))]
    internal static class FlatTryFirePatch
    {
        static void Postfix(Character __instance)
        {
            try { FireSelectorMod.FireHeld = __instance.holdingButtonFire; } catch { }
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
