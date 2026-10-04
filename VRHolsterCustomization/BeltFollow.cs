using System;
using Il2CppHurricaneVR.Framework.Core.Player;
using MelonLoader;
using UnityEngine;

namespace VRHolsterCustomization
{
    // The game's belt (HVRPlayerWaist.FollowPlayer) turns toward the head only past a yaw gap (WaistAngleThreshold),
    // then at WaistSpeed, plus on snap turns. BeltFollowsHead gives it the head's yaw every frame instead.
    static class BeltFollow
    {
        static MelonPreferences_Entry<bool> enabled;
        static bool reported, warned, haveYaw;
        static float lastYaw, gapSum, gapMax;
        static int frames;

        internal static void Init(MelonPreferences_Category c)
        {
            enabled = c.CreateEntry("BeltFollowsHead", false, display_name: "Belt follows head",
                description: "Hip and knife holsters turn with your head every frame, so they stay in front of where you look. Off = the game's belt, which turns only after you look far enough to one side.");
            enabled.OnEntryValueChanged.Subscribe((_, on) =>
            {
                Report();
                if (VRHolsterCustomizationMod.DebugOn) VRHolsterCustomizationMod.Log.Msg($"belt follows head: {(on ? "on" : "off")}");
            });
        }

        internal static void Scene()
        {
            Report();
            reported = haveYaw = false;
        }

        // One line per scene (or per toggle): how far the belt's yaw was from the head's before this mod set it.
        internal static void Report()
        {
            if (frames > 0 && VRHolsterCustomizationMod.DebugOn)
                VRHolsterCustomizationMod.Log.Msg($"belt vs head yaw: avg {gapSum / frames:0.#} deg, max {gapMax:0} deg over {frames} frames ({(enabled.Value ? "follow on" : "game belt")})");
            frames = 0;
            gapSum = gapMax = 0;
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
                VRHolsterCustomizationMod.Log.Msg($"game belt: turns past {waist.WaistAngleThreshold:0} deg at {waist.WaistSpeed:0.#} deg/s, CameraAngleThreshold {waist.CameraAngleThreshold:0}; follow {(on ? "on" : "off")}");
            }
            var f = head.forward;
            // Within ~6 degrees of straight down or up the forward vector has no reliable yaw: hold the last one.
            if (f.x * f.x + f.z * f.z < 0.01f)
            {
                if (on && haveYaw) belt.rotation = Quaternion.Euler(0f, lastYaw, 0f);
                return;
            }
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (debug)
            {
                float gap = Mathf.Abs(Mathf.DeltaAngle(belt.eulerAngles.y, yaw));
                frames++;
                gapSum += gap;
                if (gap > gapMax) gapMax = gap;
            }
            if (!on) return;
            lastYaw = yaw;
            haveYaw = true;
            belt.rotation = Quaternion.Euler(0f, yaw, 0f);
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
}
