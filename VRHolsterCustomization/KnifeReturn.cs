using System;
using Il2Cpp;
using MelonLoader;

namespace VRHolsterCustomization
{
    // How long a thrown or dropped knife (katana) takes to come back to its holster, wall spot or back slot (0.3.5).
    //
    // Game (il2cpp_tools, Oct 3 2026): ANBKnife.releaseKnife sets autoReturnAfterCurrent = ANBGameLogic.autoReturnKnifeAfter
    // on every release; ANBKnife.checkAutoReturn counts it down while the knife is neither held nor socketed (stuck in an
    // enemy counts as neither) and then calls returnKnife(). So the timer starts at release, not at impact: a short
    // release delay would recall a long throw in mid-air. Hence the separate "after a hit" and "after a kill" timers,
    // which start when the knife sticks in an enemy (ANBGameLogic.StabEnemyFinal) or kills one (ANBKnife.registerKnifeKill, called
    // from ANBBasicNPC.TakeDamage / TakeKnifeSlashDamage).
    //
    // All three only ever shorten a timer the game has armed (> 0), so the game's own auto-return option still rules.
    internal static class KnifeReturn
    {
        static MelonPreferences_Entry<float> AfterRelease, AfterHit, AfterKill;
        static bool loggedGame;

        internal static void Init()
        {
            var c = MelonPreferences.CreateCategory("VRHolsters_KnifeReturn", "VR Holster Customization: knife return");
            AfterRelease = c.CreateEntry("AfterReleaseSeconds", 0f, description: "Seconds before a thrown or dropped knife or katana returns, counted from the moment you let go. 0 = the game's own delay. Below about 1 s a long throw returns before it lands.");
            AfterHit = c.CreateEntry("AfterHitSeconds", 0f, description: "Seconds a thrown knife or katana stays in an enemy it sticks in before it returns. 0 = off (the release delay applies).");
            AfterKill = c.CreateEntry("AfterKillSeconds", 0f, description: "Seconds before a thrown knife or katana returns after it kills an enemy. 0 = off (the hit or release delay applies).");
        }

        internal static void Released(ANBKnife k)
        {
            if (!VRHolsterCustomizationMod.Alive(k) || k.isArrow || k.autoReturnAfterCurrent <= 0f) return;
            if (!loggedGame && VRHolsterCustomizationMod.DebugOn)
            {
                loggedGame = true;
                VRHolsterCustomizationMod.Log.Msg($"knife return: the game's delay is {k.autoReturnAfterCurrent:0.##} s");
            }
            if (AfterRelease.Value > 0f) k.autoReturnAfterCurrent = AfterRelease.Value;
        }

        internal static void Hit(ANBKnife k) => Shorten(k, AfterHit.Value, "hit");
        internal static void Killed(ANBKnife k) => Shorten(k, AfterKill.Value, "kill");

        static void Shorten(ANBKnife k, float to, string why)
        {
            if (to <= 0f || !VRHolsterCustomizationMod.Alive(k) || k.isArrow || k.isHeld || k.autoReturnAfterCurrent <= to) return;
            k.autoReturnAfterCurrent = to;
            if (VRHolsterCustomizationMod.DebugOn) VRHolsterCustomizationMod.Log.Msg($"knife return: '{k.gameObject.name}' {why}, back in {to:0.##} s");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.releaseKnife))]
    static class KnifeReleaseTimerPatch
    {
        static void Postfix(ANBKnife __instance)
        {
            try { KnifeReturn.Released(__instance); } catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"knife release: {e.Message}"); }
        }
    }

    // The enemy-stab end point (ANBKnife.stabEnemy -> ANBGameLogic.StabEnemyFinal); Throw Assist proved it fires for
    // thrown knives (14/14 stabs).
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.StabEnemyFinal))]
    static class KnifeStabTimerPatch
    {
        static void Postfix(ANBKnife knifeScript)
        {
            try { KnifeReturn.Hit(knifeScript); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.registerKnifeKill))]
    static class KnifeKillTimerPatch
    {
        static void Postfix(ANBKnife __instance)
        {
            try { KnifeReturn.Killed(__instance); } catch { }
        }
    }
}
