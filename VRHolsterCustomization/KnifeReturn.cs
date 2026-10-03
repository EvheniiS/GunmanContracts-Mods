using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace VRHolsterCustomization
{
    // How long a thrown or dropped knife (katana) waits before it returns to its holster, wall spot or back slot.
    //
    // Game (il2cpp_tools, Oct 3 2026): ANBKnife.releaseKnife sets autoReturnAfterCurrent = ANBGameLogic.autoReturnKnifeAfter
    // on every release; ANBKnife.checkAutoReturn counts it down while the knife is neither held nor socketed (stuck in an
    // enemy counts as neither) and then calls returnKnife(). One timer from the moment you let go, whatever happens after.
    //
    // 0.3.6: two plain totals instead of 0.3.5's three "shorten" timers (too confusing):
    // - InEnemySeconds: from the moment a thrown knife sticks in an enemy (ANBGameLogic.StabEnemyFinal, proven for thrown
    //   knives by Throw Assist) or kills one (ANBKnife.registerKnifeKill, from ANBBasicNPC.TakeDamage /
    //   TakeKnifeSlashDamage), it returns after exactly this long. Longer than the game's is fine.
    // - OnGroundSeconds: from the moment a released knife comes to rest (on the floor, stuck in a wall), it returns after
    //   exactly this long. While it is still flying the return is held back, so a long throw never returns in mid-air.
    // 0 = the game's own timing. Nothing happens when the game's knife auto-return is off (timer not armed at release).
    internal static class KnifeReturn
    {
        static MelonPreferences_Entry<float> InEnemy, OnGround;
        sealed class Flying { public ANBKnife Knife; public Rigidbody Rb; public float At, Still; }
        static readonly List<Flying> flying = new();
        const float MaxFlight = 10f;    // never hold a knife back longer than this (fell out of the map)
        const float RestSpeed = 0.15f;  // m/s
        const float RestTime = 0.2f;    // s below RestSpeed = at rest
        static bool loggedGame;

        internal static void Init()
        {
            var c = MelonPreferences.CreateCategory("VRHolsters_KnifeReturn", "VR Holster Customization: knife return");
            InEnemy = c.CreateEntry("InEnemySeconds", 0f, description: "How long a thrown knife or katana stays in an enemy (after it sticks in or kills one) before it returns, in seconds. 0 = the game's own timing.");
            OnGround = c.CreateEntry("OnGroundSeconds", 0f, description: "How long a dropped or thrown knife or katana lies where it landed (floor, wall) before it returns, in seconds. 0 = the game's own timing.");
        }

        internal static void Scene()
        {
            flying.Clear();
            loggedGame = false;
        }

        internal static void Released(ANBKnife k)
        {
            if (!VRHolsterCustomizationMod.Alive(k) || k.isArrow || k.autoReturnAfterCurrent <= 0f) return;
            if (!loggedGame && VRHolsterCustomizationMod.DebugOn)
            {
                loggedGame = true;
                VRHolsterCustomizationMod.Log.Msg($"knife return: the game's delay is {k.autoReturnAfterCurrent:0.##} s from release");
            }
            if (OnGround.Value <= 0f) return;
            flying.RemoveAll(f => !VRHolsterCustomizationMod.Alive(f.Knife) || f.Knife.Pointer == k.Pointer);
            flying.Add(new Flying { Knife = k, Rb = k.GetComponent<Rigidbody>(), At = Time.time });
            k.autoReturnAfterCurrent = MaxFlight + 1f; // held back until it lands
        }

        internal static void InEnemyNow(ANBKnife k, string why)
        {
            if (!VRHolsterCustomizationMod.Alive(k) || k.isArrow || k.isHeld) return;
            var f = flying.Find(x => VRHolsterCustomizationMod.Alive(x.Knife) && x.Knife.Pointer == k.Pointer);
            if (f != null) flying.Remove(f);
            if (k.autoReturnAfterCurrent <= 0f) return;
            if (InEnemy.Value > 0f) Set(k, InEnemy.Value, why);
            else if (f != null) Set(k, GameLeft(k, f), why); // undo the flight hold: back to the game's own timing
        }

        internal static void Tick()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                var k = f.Knife;
                if (!VRHolsterCustomizationMod.Alive(k) || k.isHeld || k.autoReturnAfterCurrent <= 0f || Holsters.Holds(k.gameObject) || Socketed(k))
                { flying.RemoveAt(i); continue; }
                float speed = VRHolsterCustomizationMod.Alive(f.Rb) && !f.Rb.isKinematic ? f.Rb.linearVelocity.magnitude : 0f;
                f.Still = speed < RestSpeed ? f.Still + Time.deltaTime : 0f;
                if (f.Still < RestTime && Time.time - f.At < MaxFlight)
                {
                    if (k.autoReturnAfterCurrent < MaxFlight) k.autoReturnAfterCurrent = MaxFlight + 1f; // still flying
                    continue;
                }
                flying.RemoveAt(i);
                Set(k, OnGround.Value, "landed");
            }
        }

        static bool Socketed(ANBKnife k)
        {
            try { var g = k.GrabbableScript; return VRHolsterCustomizationMod.Alive(g) && g.IsSocketed; } catch { return false; }
        }

        // What the game's own timer would have left now.
        static float GameLeft(ANBKnife k, Flying f)
        {
            var game = ANBStaticGameManager.ANBmain;
            float delay = VRHolsterCustomizationMod.Alive(game) ? game.autoReturnKnifeAfter : 0f;
            return Mathf.Max(0.05f, delay - (Time.time - f.At));
        }

        static void Set(ANBKnife k, float seconds, string why)
        {
            k.autoReturnAfterCurrent = seconds;
            if (VRHolsterCustomizationMod.DebugOn) VRHolsterCustomizationMod.Log.Msg($"knife return: '{k.gameObject.name}' {why}, back in {seconds:0.##} s");
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
            try { KnifeReturn.InEnemyNow(knifeScript, "in an enemy"); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.registerKnifeKill))]
    static class KnifeKillTimerPatch
    {
        static void Postfix(ANBKnife __instance)
        {
            try { KnifeReturn.InEnemyNow(__instance, "killed"); } catch { }
        }
    }
}
