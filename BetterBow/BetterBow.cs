using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using Il2CppHurricaneVR.Framework.Core.Utils;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(BetterBow.BetterBowMod), "Better Bow", "1.1.5", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace BetterBow
{
    // Better Bow = the former Arrow Grab Assist (string grab buffer, arrow spawn retry, explosive
    // barrels, door breach) and Arrow Quiver (draw from the holster, dagger grip) in one mod, plus the
    // knives' throw aim assist for thrown quiver arrows.
    // Each part can be switched off on its own in [BetterBow].
    public class BetterBowMod : MelonMod
    {
        internal static MelonLogger.Instance Log;

        // Filled by Harmony postfixes on Start (LoaderStartPatch / HandStartPatch) - no scene-wide
        // searches, which cost a one-frame hitch every time they ran.
        internal static readonly List<HVRArrowLoader> Loaders = new();
        internal static readonly List<HVRHandGrabber> Hands = new();

        StringGrab _stringGrab;
        Quiver _quiver;
        double _fallbackScanAt = -1;
        bool _warmed;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Settings.Create();
            _stringGrab = new StringGrab();
            _quiver = new Quiver();
            LoggerInstance.Msg("loaded - string grab buffer, quiver, dagger grip, arrow throw assist, one-arrow barrels, arrow door breach, pause/phone hand fix.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            U.Prune(Loaders);
            U.Prune(Hands);
            _stringGrab.OnScene();
            _quiver.OnScene();
            PauseHands.OnScene();
            ThrowAssist.OnScene();
            _fallbackScanAt = U.Now + 3.0;
            if (!_warmed) { _warmed = true; WarmUp(); }
        }

        public override void OnUpdate()
        {
            U.Frame++;
            // Safety net only: one search per scene, in case a Start ran before the patches.
            if (_fallbackScanAt >= 0 && U.Now >= _fallbackScanAt) { _fallbackScanAt = -1; FallbackScan(); }
            for (int i = 0; i < Loaders.Count; i++)
                if (!U.Alive(Loaders[i]) || !U.Alive(Loaders[i].bow)) Loaders.RemoveAt(i--);
            for (int i = 0; i < Hands.Count; i++)
                if (!U.Alive(Hands[i])) Hands.RemoveAt(i--);
            PauseHands.Update();                                        // runs with or without a bow
            if (Loaders.Count == 0) return;                             // no bow in the scene

            _stringGrab.Update();
            _quiver.Update();
        }

        void FallbackScan()
        {
            int before = Loaders.Count + Hands.Count;
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRArrowLoader>(), FindObjectsSortMode.None))
                U.Register(Loaders, o.TryCast<HVRArrowLoader>());
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None))
                U.Register(Hands, o.TryCast<HVRHandGrabber>());
            int found = Loaders.Count + Hands.Count - before;
            if (U.Dbg) Log.Msg($"scene check: {Loaders.Count} arrow loader(s), {Hands.Count} hand(s)" +
                               (found > 0 ? $" - {found} were missed by registration" : ""));
        }

        // First-use costs (JIT of this mod's methods, interop type setup) otherwise land on the first
        // bow grab / draw as a visible hitch (measured 39-55 ms once). Pay them during a scene load.
        void WarmUp()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var t in typeof(BetterBowMod).Assembly.GetTypes())
                    foreach (var m in t.GetMethods(all))
                        if (!m.IsAbstract && !m.ContainsGenericParameters && !t.ContainsGenericParameters)
                            try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
                foreach (var t in new[] { typeof(HVRArrow), typeof(HVRArrowLoader), typeof(HVRPhysicsBow), typeof(HVRBowBase),
                                          typeof(HVRHandGrabber), typeof(HVRGrabbable), typeof(HVRSocket), typeof(HVRShoulderSocket),
                                          typeof(HVRPosableGrabPoint), typeof(HVRPosableHand), typeof(Il2CppHurricaneVR.Framework.Shared.HVRController),
                                          typeof(ANBKnife), typeof(ANBAssistedThrowingObject), typeof(ANBBreakable), typeof(ANBGameLogic), typeof(ANBStaticGameManager), typeof(ANBSmartphone),
                                          typeof(Collider), typeof(Renderer), typeof(Camera) })
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                    Il2CppType.From(t);
                }
            }
            catch (Exception e) { Log.Warning($"warm-up incomplete: {e.Message}"); }
            if (U.Dbg) Log.Msg($"warm-up done in {sw.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    // All settings live in one [BetterBow] section of UserData\MelonPreferences.cfg.
    internal static class Settings
    {
        internal static MelonPreferences_Entry<bool> StringGrab, RetrySpawn, ExplosiveBarrels, BreachDoors,
            Quiver, HeadFallback, Haptics, GripSwitch, ThrowAssist, HandsKeepParentAfterPause, PhoneSettingsRescue, DebugLog;
        internal static MelonPreferences_Entry<float> StringGrabRadius, StringGrabBuffer, RetryWindow,
            QuiverRadius, QuiverBuffer, NockRadius, SlipNockRadius, DropLifetime, DaggerFromNock, ThrowAssistSpeed, ThrowStabReach, ThrowStabBack;
        internal static MelonPreferences_Entry<bool> ThrowAssistAvoidVest;
        internal static MelonPreferences_Entry<string> DaggerTip;

        internal static void Create()
        {
            var c = MelonPreferences.CreateCategory("BetterBow", "Better Bow");

            StringGrab = c.CreateEntry("StringGrabAssist", true, description: "Complete a grip press on the bow string that the game missed (the 'have to grip twice' fix).");
            StringGrabRadius = c.CreateEntry("StringGrabRadius", 0.15f, description: "Metres from your hand to the string within which a missed grip press is completed.");
            StringGrabBuffer = c.CreateEntry("StringGrabBufferSeconds", 0.35f, description: "How long after pressing grip (while still holding it) the string grab can still happen.");
            RetrySpawn = c.CreateEntry("RetryArrowSpawn", true, description: "Spawn the arrow if the string was grabbed while the previous arrow was still being cleared.");
            RetryWindow = c.CreateEntry("RetryWindowSeconds", 0.5f, description: "How long after grabbing the string the arrow-spawn retry is allowed.");

            ExplosiveBarrels = c.CreateEntry("ExplosiveArrowsDetonateBarrels", true, description: "One arrow hit detonates an explosive barrel (the game otherwise needs three).");
            BreachDoors = c.CreateEntry("ArrowsBreachDoors", true, description: "An arrow shot at a door's 'shoot here to burst open door' mark breaches it, like a gunshot.");

            Quiver = c.CreateEntry("Quiver", true, description: "Draw arrows from the holster the bow came from, with the hand that isn't holding the bow.");
            QuiverRadius = c.CreateEntry("QuiverRadius", 0.20f, description: "Metres around the quiver spot within which a grip press draws an arrow.");
            QuiverBuffer = c.CreateEntry("QuiverBufferSeconds", 0.35f, description: "How long after pressing grip (while still holding it) a draw can still happen.");
            NockRadius = c.CreateEntry("NockRadius", 0.15f, description: "Metres from the string at which a carried arrow is nocked.");
            SlipNockRadius = c.CreateEntry("SlipNockRadius", 0.30f, description: "If the arrow slips out of the hand while grip is still held this close to the string, nock it anyway.");
            HeadFallback = c.CreateEntry("UseHeadFallback", true, description: "If the bow was never holstered, put the quiver beside the head on the drawing hand's side.");
            DropLifetime = c.CreateEntry("DroppedArrowLifetime", 10f, description: "Seconds before a dropped quiver arrow is removed (the timer pauses while it is held).");
            Haptics = c.CreateEntry("HapticOnDraw", true, description: "Short controller pulse when an arrow is drawn.");

            GripSwitch = c.CreateEntry("GripSwitch", true, description: "A (right hand) / X (left hand) switches a quiver arrow between the nocking grip and a dagger grip.");
            DaggerFromNock = c.CreateEntry("DaggerGripFromNock", 0.25f, description: "Dagger grip: metres from the nock end towards the tip where the hand holds the arrow.");
            DaggerTip = c.CreateEntry("DaggerTip", "Down", description: "Dagger grip: which way the arrow tip points, Down (default) or Up. The shaft runs along your knuckles either way. With DebugLog on, B / Y flips it in game.");

            ThrowAssist = c.CreateEntry("ThrowAssist", true, description: "A quiver arrow thrown by hand gets the game's knife throw aim assist (needs the game's own assisted throw setting on).");
            ThrowAssistSpeed = c.CreateEntry("ThrowAssistSpeed", 0f, description: "Metres per second a thrown arrow flies to its target. 0 = the same speed as the game's throwing knives.");

            ThrowStabReach = c.CreateEntry("ThrowStabReach", 0.5f, description: "Metres the game's stab check reaches into the enemy when a thrown arrow sticks in. The arrow's own value is tuned for fast bow shots; too short and a thrown arrow sticks in without damage.");
            ThrowStabBack = c.CreateEntry("ThrowStabBack", 0.3f, description: "Metres behind the arrow tip the stab check also looks, for when the tip is already inside the enemy (a check that starts inside a hit zone never sees it).");
            ThrowAssistAvoidVest = c.CreateEntry("ThrowAssistAvoidVest", true, description: "A thrown arrow aims at the head of an enemy wearing a vest (an arrow in armor does almost no damage).");
            HandsKeepParentAfterPause = c.CreateEntry("HandsKeepParentAfterPause", true, description: "After the pause menu closes, put the physics hands back where they were before it opened (fixes hands drifting away when moving with the stick). Pauses are always logged.");
            PhoneSettingsRescue = c.CreateEntry("PhoneSettingsRescue", true, description: "If the phone's Settings button gets stuck (the game's menu switch never finishes), open the menu and unstick it.");

            DebugLog = c.CreateEntry("DebugLog", false, description: "Log grabs, draws, nocks, grip switches, barrel hits and door breaches to the MelonLoader console.");
        }
    }

    // Shared helpers and the frame clock.
    internal static class U
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        internal static double Now => Clock.Elapsed.TotalSeconds;
        internal static long Frame;
        internal static bool Dbg => Settings.DebugLog != null && Settings.DebugLog.Value;

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }

        internal static float Dist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        internal static void Register<T>(List<T> list, T o) where T : Object
        {
            if (!Alive(o)) return;
            foreach (var x in list) if (x.Pointer == o.Pointer) return;
            list.Add(o);
        }

        internal static void Prune<T>(List<T> list) where T : Object
        {
            for (int i = 0; i < list.Count; i++) if (!Alive(list[i])) list.RemoveAt(i--);
        }

        internal static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        internal static string Name(Object o) => Alive(o) ? o.name : "none";
        internal static string Side(HVRHandGrabber h) => h.IsLeftHand ? "left" : "right";
    }

    // Objects register themselves when they start, so the mod never searches the scene.
    [HarmonyLib.HarmonyPatch(typeof(HVRArrowLoader), nameof(HVRArrowLoader.Start))]
    internal static class LoaderStartPatch
    {
        static void Postfix(HVRArrowLoader __instance)
        {
            try { U.Register(BetterBowMod.Loaders, __instance); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Start))]
    internal static class HandStartPatch
    {
        static void Postfix(HVRHandGrabber __instance)
        {
            try { U.Register(BetterBowMod.Hands, __instance); } catch { }
        }
    }
}
