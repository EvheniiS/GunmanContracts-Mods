using HarmonyLib;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using static GrabFix.GrabFixMod;

namespace GrabFix;

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.CheckGrabControlSwap))]
internal static class InputPatch
{
    static void Prefix(HVRHandGrabber __instance) => Safe("input", () => Input(__instance));
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.CanHover))]
internal static class HoverPatch
{
    // Run before Grab Log's Priority.Last observer so its result reflects the fix.
    [HarmonyPriority(Priority.Normal)]
    static void Postfix(HVRHandGrabber __instance, HVRGrabbable __0, ref bool __result)
    {
        if (__result || !Active) return;
        bool allowed = false;
        Safe("buffered hover", () => allowed = AllowBufferedHover(__instance, __0));
        if (allowed) __result = true;
    }
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Update))]
internal static class HandPatch
{
    static void Postfix(HVRHandGrabber __instance) => Safe("local assist", () => AfterHand(__instance));
}

[HarmonyPatch(typeof(HVRForceGrabber), nameof(HVRForceGrabber.Update))]
internal static class ForcePatch
{
    static void Postfix(HVRForceGrabber __instance) => Safe("distance assist", () => AfterForce(__instance));
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.OnReleased))]
internal static class ReleasePatch
{
    static void Postfix(HVRHandGrabber __instance, HVRGrabbable __0) => Safe("release", () => OnRelease(__instance, __0));
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.OnGrabbed))]
internal static class GrabbedPatch
{
    static void Postfix(HVRHandGrabber __instance) => Safe("grabbed", () =>
    {
        if (Active) Register(__instance).Intent.Consume();
    });
}
