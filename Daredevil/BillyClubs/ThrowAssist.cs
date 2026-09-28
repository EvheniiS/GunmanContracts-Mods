using System;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // Hook on the game's throw assist. The game picks the target (GetClosestAndCenteredTarget: enemies first, then
    // other targets); the mod stops the knife-style MovePosition coroutine it starts and steers the club itself.
    public partial class BillyClubsMod
    {
        static Vector3 PreV, PreW;
        static bool AssistSettingsLogged;

        internal static void BeforeAssist(ANBAssistedThrowingObject ato)
        {
            var k = ClubOf(ato);
            if (k == null || !Alive(k.Rb)) return;
            PreV = k.Rb.linearVelocity; PreW = k.Rb.angularVelocity;
        }

        internal static void AfterAssist(ANBAssistedThrowingObject ato)
        {
            var k = ClubOf(ato);
            if (k == null || !Alive(k.Rb) || k.In != null) return;
            var game = ANBStaticGameManager.ANBmain;
            if (Dbg && !AssistSettingsLogged && game != null)
            {
                AssistSettingsLogged = true;
                Log.Msg($"game throw assist: {(game.assistedThrow ? "on" : "OFF")}, from {game.assistedThrowAtVelocity:0.0} m/s, view {game.assistedThrowViewRadius:0.#} m / {game.assistedThrowViewAngle:0} deg; club search {ato.targetSearchDistanceOverride:0} m, fly {ato.maxFlyDistanceOverride:0} m");
            }
            Transform target = null;
            if (ato.isHoming)
            {
                target = ato.homingTarget;
                ato.StopAllCoroutines();
                ato.isHoming = false;
                ato.homingTarget = null;
                // Its first step already zeroed both.
                k.Rb.linearVelocity = PreV;
                k.Rb.angularVelocity = PreW;
            }
            string why = ato.dontUse ? "disabled on this object"
                : game != null && !game.assistedThrow ? "off in the game settings"
                : game != null && PreV.magnitude < game.assistedThrowAtVelocity ? $"{PreV.magnitude:0.0} m/s is below the game's {game.assistedThrowAtVelocity:0.0}"
                : "no target in view";
            StartFlight(k);
            ApplyAssist(k, target, why);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBAssistedThrowingObject), nameof(ANBAssistedThrowingObject.StartAssistedThrow))]
    static class AssistPatch
    {
        static void Prefix(ANBAssistedThrowingObject __instance)
        {
            try { BillyClubsMod.BeforeAssist(__instance); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] BeforeAssist: {e.Message}"); }
        }

        static void Postfix(ANBAssistedThrowingObject __instance)
        {
            try { BillyClubsMod.AfterAssist(__instance); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] AfterAssist: {e.Message}"); }
        }
    }
}
