using System;
using Il2CppHurricaneVR.Framework.Core.Player;
using MelonLoader;
using UnityEngine;

namespace VRHolsterCustomization
{
    // The game's belt (HVRPlayerWaist.FollowPlayer) turns toward the head only past a yaw gap (WaistAngleThreshold),
    // then at WaistSpeed, plus on snap turns. BeltFollowsHead gives it the head's yaw instead, eased: stick turns
    // (measured around HVRPlayerController.HandleRotation) apply at once, head turns past the dead zone ease in over
    // BeltTurnSeconds, and the last few degrees drift in over BeltCenterSeconds.
    // The PlayerController's own yaw follows the head (0.3.10 log: belt 0 deg off with a 45 deg dead zone), so it is
    // no reference for the body; the belt's heading is kept in world space.
    static class BeltFollow
    {
        static MelonPreferences_Entry<bool> enabled;
        static MelonPreferences_Entry<int> deadZone;
        static MelonPreferences_Entry<float> turnSeconds, centerSeconds;
        static bool reported, warned, haveHead, haveBelt;
        static float beltYaw, headYaw, pendingTurn, turnSum, gapSum, gapMax;
        static int frames;

        internal static bool Active => enabled != null && (enabled.Value || VRHolsterCustomizationMod.DebugOn);

        internal static void Init(MelonPreferences_Category c)
        {
            enabled = c.CreateEntry("BeltFollowsHead", true, display_name: "Belt follows head",
                description: "Hip and knife holsters turn with your head, so they stay in front of where you look. Off = the game's belt, which turns only after you look far enough to one side.");
            deadZone = c.CreateEntry("BeltDeadZoneDeg", 10, display_name: "Belt follow: dead zone (deg)",
                description: "How far you can turn your head before the belt follows quickly. Inside it the belt only drifts (Belt follow: center time). 0 = every head turn moves it.");
            turnSeconds = c.CreateEntry("BeltTurnSeconds", 0.1f, display_name: "Belt follow: turn time (s)",
                description: "How quickly the belt catches up once your head is past the dead zone; higher is slower. 0 = instantly. Snap and smooth turns always move it at once.");
            centerSeconds = c.CreateEntry("BeltCenterSeconds", 1f, display_name: "Belt follow: center time (s)",
                description: "How slowly the belt lines up with your head inside the dead zone, so it faces where you keep looking. 0 = never; it stays at the edge of the dead zone.");
            enabled.OnEntryValueChanged.Subscribe((_, on) =>
            {
                Report(0);
                haveBelt = false;
                if (VRHolsterCustomizationMod.DebugOn) VRHolsterCustomizationMod.Log.Msg($"belt follows head: {(on ? "on" : "off")}");
            });
            deadZone.OnEntryValueChanged.Subscribe((_, _) => Report(300));
            turnSeconds.OnEntryValueChanged.Subscribe((_, _) => Report(300));
            centerSeconds.OnEntryValueChanged.Subscribe((_, _) => Report(300));
        }

        internal static void Scene()
        {
            Report(0);
            reported = haveHead = haveBelt = false;
            pendingTurn = 0f;
        }

        // One line per scene, toggle or tuned setting (if it ran 300+ frames): the belt's final yaw against the head's.
        internal static void Report(int minFrames = 0)
        {
            if (frames > 0 && frames >= minFrames && VRHolsterCustomizationMod.DebugOn)
                VRHolsterCustomizationMod.Log.Msg($"belt vs head yaw: avg {gapSum / frames:0.#} deg, max {gapMax:0} deg over {frames} frames, stick turns {turnSum:0} deg ({Mode()})");
            frames = 0;
            gapSum = gapMax = turnSum = 0f;
        }

        static string Mode() => enabled.Value
            ? $"follow on: dead zone {deadZone.Value} deg, turn {turnSeconds.Value:0.##} s, center {centerSeconds.Value:0.##} s"
            : "game belt";

        static float Ease(float dt, float seconds) => seconds <= 0f ? 1f : 1f - Mathf.Exp(-dt / seconds);

        internal static void RigTurned(float degrees)
        {
            if (Mathf.Abs(degrees) < 0.001f) return;
            pendingTurn += degrees;
            turnSum += Mathf.Abs(degrees);
        }

        internal static void AfterGame(HVRPlayerWaist waist)
        {
            bool on = enabled.Value, debug = VRHolsterCustomizationMod.DebugOn;
            if (!on && !debug) return;
            var head = waist.Camera;
            if (!VRHolsterCustomizationMod.Alive(head)) return;
            var belt = waist.transform;
            if (debug && !reported)
            {
                reported = true;
                VRHolsterCustomizationMod.Log.Msg($"game belt: turns past {waist.WaistAngleThreshold:0} deg at {waist.WaistSpeed:0.#} deg/s, CameraAngleThreshold {waist.CameraAngleThreshold:0}; {Mode()}");
            }
            float turn = pendingTurn;
            pendingTurn = 0f;
            var f = head.forward;
            // Within ~6 degrees of straight down or up the forward vector has no reliable yaw: keep the last one.
            if (f.x * f.x + f.z * f.z >= 0.01f)
            {
                headYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                haveHead = true;
            }
            else if (!haveHead) return;
            else headYaw += turn;
            if (on)
            {
                // Starts from where the game's belt is, so switching on eases in instead of jumping.
                if (!haveBelt) beltYaw = belt.eulerAngles.y;
                else beltYaw += turn;
                haveBelt = true;
                float dt = Time.deltaTime, gap = Mathf.DeltaAngle(beltYaw, headYaw), zone = Mathf.Max(0, deadZone.Value);
                float inner = Mathf.Clamp(gap, -zone, zone);
                beltYaw += (gap - inner) * Ease(dt, turnSeconds.Value);
                if (centerSeconds.Value > 0f) beltYaw += inner * Ease(dt, centerSeconds.Value);
                beltYaw = Mathf.Repeat(beltYaw, 360f);
                belt.rotation = Quaternion.Euler(0f, beltYaw, 0f);
            }
            if (debug)
            {
                float miss = Mathf.Abs(Mathf.DeltaAngle(belt.eulerAngles.y, headYaw));
                frames++;
                gapSum += miss;
                if (miss > gapMax) gapMax = miss;
            }
        }

        internal static void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            VRHolsterCustomizationMod.Log.Warning($"belt follow: {e.Message}");
        }
    }

    // Runs right after the game's own belt update (Update, while the game is started and not paused).
    [HarmonyLib.HarmonyPatch(typeof(HVRPlayerWaist), nameof(HVRPlayerWaist.FollowPlayer))]
    static class BeltFollowPatch
    {
        static void Postfix(HVRPlayerWaist __instance)
        {
            try { BeltFollow.AfterGame(__instance); }
            catch (Exception e) { BeltFollow.Warn(e); }
        }
    }

    // Snap and smooth turns rotate the controller inside HandleRotation (mouse turning too, flat mode only).
    [HarmonyLib.HarmonyPatch(typeof(HVRPlayerController), nameof(HVRPlayerController.HandleRotation))]
    static class BeltRigTurnPatch
    {
        static float before;
        static bool armed;

        static void Prefix(HVRPlayerController __instance)
        {
            try
            {
                armed = BeltFollow.Active;
                if (armed) before = __instance.transform.eulerAngles.y;
            }
            catch (Exception e) { armed = false; BeltFollow.Warn(e); }
        }

        static void Postfix(HVRPlayerController __instance)
        {
            if (!armed) return;
            try { BeltFollow.RigTurned(Mathf.DeltaAngle(before, __instance.transform.eulerAngles.y)); }
            catch (Exception e) { BeltFollow.Warn(e); }
        }
    }
}
