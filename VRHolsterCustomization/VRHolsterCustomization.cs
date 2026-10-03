using System;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(VRHolsterCustomization.VRHolsterCustomizationMod), "VR Holster Customization", "0.3.5", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]
[assembly: MelonAdditionalDependencies("ModSettings")]

namespace VRHolsterCustomization
{
    public class VRHolsterCustomizationMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        static MelonPreferences_Entry<bool> debug;
        internal static bool DebugOn => debug != null && debug.Value;
        internal static bool Alive(UnityEngine.Object o)
        {
            try { return o != null && !o.WasCollected && o; } catch { return false; }
        }
        internal static string V(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("VRHolsters", "VR Holster Customization");
            debug = c.CreateEntry("DebugLog", false, description: "Log holster in/out events and final holster positions.");
            Holsters.Init();
            VanillaHolsters.Init();
            KnifeReturn.Init();
            Log.Msg("loaded");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            HolsterLog.Scene();
            Holsters.Scene();
            BackBlades.Scene();
            VanillaHolsters.Scene();
        }

        public override void OnUpdate()
        {
            try { HolsterLog.Tick(); Dock.Tick(); Holsters.Tick(); BackBlades.Tick(); VanillaHolsters.Tick(); }
            catch (Exception e) { Log.Error($"update: {e}"); }
        }

        public override void OnLateUpdate()
        {
            try { VanillaHolsters.ApplyPositions(); }
            catch (Exception e) { VanillaHolsters.EndAdjustment(); Log.Error($"positions: {e.Message}"); }
        }

        public override void OnDeinitializeMelon() => VanillaHolsters.EndAdjustment();
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRGrabberBase), nameof(HVRGrabberBase.GrabGrabbable))]
    static class HolsterGrabPatch
    {
        // Let Weapon Framework record a wall take before Dock unparents the item.
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Low)]
        static bool Prefix(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            try
            {
                if (Holsters.BlocksGrab(grabber, grabbable)) return false;
                Dock.BeforeGrab(grabbable);
                Holsters.BeforeGrab(grabbable);
                BackBlades.OnGrab(grabber, grabbable);
            }
            catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"grab: {e.Message}"); }
            return true;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2Cpp.ANBGameLogic), nameof(Il2Cpp.ANBGameLogic.resetPlayerLoadout))]
    static class BackLoadoutResetPatch
    {
        static void Postfix(Il2Cpp.ANBGameLogic __instance)
        {
            try { Holsters.PlayerLoadoutReset(__instance); }
            catch (Exception e) { VRHolsterCustomizationMod.Log.Error($"checkpoint back holsters: {e}"); }
        }
    }
}
