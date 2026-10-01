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
        if (!Active) return;
        if (__result)
        {
            // The widened local sphere is the actual physical trigger collider, so it can make
            // a docked item (holstered, socketed, wall-mounted) natively hoverable - and so
            // grabbable on grip press - well beyond arm's reach. Demand native closeness for
            // those; the widened reach is only meant for a loose item's own recovery.
            bool native = true;
            if (Docked(__0)) Safe("docked hover", () => native = Register(__instance).Geometry.NativeContains(__0));
            if (!native) __result = false;
            // Enemy bodies (standing or ragdolled) need the palm right on them.
            else
            {
                bool reachable = true;
                Safe("enemy hover", () => reachable = !EnemyPart(__0) || EnemyReachable(__instance, __0));
                if (!reachable) __result = false;
            }
            return;
        }
        bool allowed = false;
        Safe("buffered hover", () => allowed = AllowBufferedHover(__instance, __0));
        if (allowed) __result = true;
    }
}

// HVRForceGrabber has its own, separate CanHover - widening its distance capsules (both the
// native one via DistanceGrabWidth and, in Both mode, the palm-aimed copy at a different
// position/rotation) can make a docked item hoverable from across the body, e.g. the off-hand
// club while the palm happens to face across the belt. Same fix as the local sphere above.
[HarmonyPatch(typeof(HVRForceGrabber), nameof(HVRForceGrabber.CanHover))]
internal static class ForceHoverPatch
{
    static void Postfix(HVRForceGrabber __instance, HVRGrabbable __0, ref bool __result)
    {
        if (!Active || !__result) return;
        var h = __instance.HandGrabber;
        if (!Alive(h)) return;
        // Enemies are never distance-grabbed: only the palm-close local grab applies to them.
        if (Limit(EnemyRadius.Value, .10f, 0, .25f) > 0)
        {
            bool enemy = false;
            Safe("enemy force hover", () => enemy = EnemyPart(__0));
            if (enemy) { __result = false; return; }
        }
        if (!Docked(__0)) return;
        bool native = true;
        Safe("docked force hover", () => native = Register(h).Geometry.NativeContainsFar(__0));
        if (!native) __result = false;
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
