using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using static GrabLog.GrabLogMod;

namespace GrabLog;

// Every callback is guarded: a failed diagnostic must never interrupt game code.
// No prefix skips a method; no patch modifies arguments or __result.
[HarmonyPatch(typeof(HVRGrabbable), nameof(HVRGrabbable.Start))]
internal static class ItemStart
{
    static void Postfix(HVRGrabbable __instance) => Safe("item start", () => { if (Active) Register(__instance); });
}

// IL2CPP inlines UpdateGrabInputs into Update in this game build. The next actual
// call is CheckGrabControlSwap, after all four input fields are written and before
// toggle/hover/grab processing. Patching UpdateGrabInputs alone misses normal input.
[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.CheckGrabControlSwap))]
internal static class Inputs
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(HVRHandGrabber __instance) => Safe("input", () => Input(__instance));
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Update))]
internal static class HandUpdate
{
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRHandGrabber __instance) => Safe("hand update", () => Observe(__instance));
}

[HarmonyPatch]
internal static class Prompts
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "EnableGrabIndicator", "DisableGrabIndicator", "EnableDynamicIndicator", "DisableDynamicIndicator", "UpdateGrabIndicator", "UpdateTriggerGrabIndicator" })
            yield return AccessTools.Method(typeof(HVRHandGrabber), name);
    }
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRHandGrabber __instance) => Safe("prompt", () => Prompt(__instance));
}

[HarmonyPatch]
internal static class Predicates
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "CanHover", "CanGrab", "CheckLineOfSight" })
            yield return AccessTools.Method(typeof(HVRHandGrabber), name, new[] { typeof(HVRGrabbable) });
    }
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRHandGrabber __instance, HVRGrabbable __0, bool __result, MethodBase __originalMethod)
        => Safe("predicate", () => Predicate(__instance, __0, __originalMethod.Name, __result));
}

[HarmonyPatch(typeof(HVRGrabbable), nameof(HVRGrabbable.GrabPointValid))]
internal static class PointValidation
{
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRGrabbable __instance, HVRHandGrabber __0, HVRPosableGrabPoint __1, GrabpointFilter __2, bool __result)
        => Safe("point validation", () =>
        {
            if (Active && Alive(__1)) Predicate(__0, __instance, "GrabPointValid:" + Id(__1.transform) + ":" + (int)__2, __result);
        });
}

[HarmonyPatch]
internal static class HoverEvents
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(HVRHandGrabber), "OnHoverEnter");
        yield return AccessTools.Method(typeof(HVRHandGrabber), "OnHoverExit");
    }
    static void Postfix(HVRHandGrabber __instance, HVRGrabbable __0, MethodBase __originalMethod)
        => Safe("hover event", () => Event(__originalMethod.Name == "OnHoverEnter" ? "hover_enter" : "hover_exit", __instance, __0));
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.OnGrabbed))]
internal static class Grabbed
{
    static void Postfix(HVRHandGrabber __instance) => Safe("grabbed", () =>
    {
        if (!Active) return;
        State(__instance).GotObject = true;
        Event("grabbed", __instance, __instance.GrabbedTarget);
    });
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.CheckGrab))]
internal static class CheckGrab
{
    static void Prefix(HVRHandGrabber __instance) => Safe("check grab before", () =>
    {
        if (!Active) return;
        if (__instance.IsGripGrabActivated || __instance.IsTriggerGrabActivated) Event("check_grab_before", __instance);
    });
    static void Postfix(HVRHandGrabber __instance) => Safe("check grab after", () =>
    {
        if (!Active) return;
        if (__instance.IsGripGrabActivated || __instance.IsTriggerGrabActivated) Event("check_grab_after", __instance, __instance.GrabbedTarget);
    });
}

[HarmonyPatch(typeof(HVRGrabberBase), nameof(HVRGrabberBase.TryGrab), new[] { typeof(HVRGrabbable), typeof(bool) })]
internal static class TryGrab
{
    static void Prefix(HVRGrabberBase __instance, HVRGrabbable __0, bool __1) => Safe("try grab before", () =>
    {
        if (!Active) return;
        Event("try_grab_before", __instance.TryCast<HVRHandGrabber>(), __0, new { force = __1 });
    });
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRGrabberBase __instance, HVRGrabbable __0, bool __1, bool __result) => Safe("try grab result", () =>
    {
        if (!Active) return;
        var h = __instance.TryCast<HVRHandGrabber>();
        if (!Alive(h)) return;
        if (__result) State(h).GotObject = true;
        Event("try_grab_result", h, __0, new { force = __1, result = __result });
    });
}

[HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.OnReleased))]
internal static class Release
{
    static void Prefix(HVRHandGrabber __instance, HVRGrabbable __0) => Safe("release before", () => Event("release_before", __instance, __0));
    [HarmonyPriority(Priority.Last)]
    static void Postfix(HVRHandGrabber __instance, HVRGrabbable __0) => Safe("release after", () => Released(__instance, __0));
}
