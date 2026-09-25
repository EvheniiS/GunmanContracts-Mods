using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using UnityEngine;
using static BetterBow.BetterBowMod;

namespace BetterBow
{
    // ---- Explosive barrels: one arrow detonates them ----------------------------------------------
    // Arrows damage breakables through ANBKnife.stabEnemy -> ANBBreakable.hit with the raw
    // knifeDamage (bullets and explosions get a multiplier, arrows get none), and a barrel breaks only
    // at health <= 0: damage 100 against health 250 = three arrows. hit() is called synchronously
    // from inside stabEnemy, so a flag set around stabEnemy tells hit() the damage comes from an
    // arrow. Only explosive, destructible breakables are touched.
    internal static class ArrowStab
    {
        internal static bool Active;
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.stabEnemy))]
    internal static class StabEnemyPatch
    {
        static void Prefix(ANBKnife __instance, Il2CppHurricaneVR.Framework.Core.Stabbing.StabArgs args)
        {
            try
            {
                ArrowStab.Active = __instance != null && __instance.isArrow;
                if (U.Dbg && __instance != null && __instance.Pointer == Quiver.CarriedKnife)
                    Log.Msg($"carried quiver arrow stabbed (damage {__instance.knifeDamage})");
                ThrowAssist.BeforeStab(__instance, args);
                ThrowAssist.StabStart(__instance, args);
            }
            catch { ArrowStab.Active = false; }
        }

        static void Postfix(ANBKnife __instance)
        {
            ArrowStab.Active = false;
            try { ThrowAssist.AfterStab(__instance); ThrowAssist.StabEnd(__instance); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBreakable), nameof(ANBBreakable.hit))]
    internal static class BreakableHitPatch
    {
        static void Prefix(ANBBreakable __instance, ref float bulletDamage, bool isBullet, bool isExplosion)
        {
            if (!ArrowStab.Active || isBullet || isExplosion) return;
            if (Settings.ExplosiveBarrels == null || !Settings.ExplosiveBarrels.Value) return;
            try
            {
                if (!__instance.explosionOnBreak || __instance.indistructable) return;
                // hitAfterTime does health -= damage and breaks at health <= 0.
                float needed = __instance.health + 1f;
                if (bulletDamage >= needed) return;
                if (U.Dbg) Log.Msg($"arrow hit explosive barrel: damage {bulletDamage:0.#} -> {needed:0.#} (health {__instance.health:0.#})");
                bulletDamage = needed;
            }
            catch (Exception e) { Log?.Warning($"barrel patch skipped: {e.Message}"); }
        }
    }

    // ---- Draw power: full speed without the last few centimetres of pull ----------------------------
    // HVRBowBase.ArrowShootCall sets Tension (= nock distance / StringLimit, 0..1) and
    // _shootSpeed = SpeedCurve.Evaluate(Tension) * Speed, then calls ShootArrow, which only does
    // arrow.velocity = direction * _shootSpeed. The compound bow prefab has StringLimit 0.40 m,
    // ShootThreshold 0.20 m, Speed 50 and a SpeedCurve of keys (0,0 slope 0) -> (1,1 slope 2), which
    // is exactly t^2: speed = 50 * tension^2, so 90% of the pull gives 81% speed and about 66% range.
    // Recompute _shootSpeed here from the same Tension, rescaled so FullDrawAt counts as full draw.
    [HarmonyLib.HarmonyPatch(typeof(HVRPhysicsBow), nameof(HVRPhysicsBow.ShootArrow))]
    internal static class ShootArrowSpeedPatch
    {
        static void Prefix(HVRPhysicsBow __instance)
        {
            if (Settings.FullDrawAt == null || Settings.ArrowSpeed == null) return;
            float fullAt = Math.Clamp(Settings.FullDrawAt.Value, 0.5f, 1f);
            float mult = Math.Clamp(Settings.ArrowSpeed.Value, 0.5f, 2f);
            if (fullAt >= 1f && mult == 1f) return;
            try
            {
                var curve = __instance.SpeedCurve;
                if (curve == null) return;
                float tension = Math.Clamp(__instance.Tension, 0f, 1f);
                float t = Math.Min(1f, tension / fullAt);
                float before = __instance._shootSpeed;
                float speed = curve.Evaluate(t) * __instance.Speed * mult;
                __instance._shootSpeed = speed;
                if (U.Dbg) Log.Msg($"arrow shot: pull {tension:P0} -> {t:P0}, speed {before:0.#} -> {speed:0.#} m/s");
            }
            catch (Exception e) { Log?.Warning($"arrow speed patch skipped: {e.Message}"); }
        }
    }

    // ---- Doors: arrows breach them like a gunshot ---------------------------------------------------
    // Door breaching is ANBGameLogic.TryKickDoor(pos, dir): gated by useVRDoorKick, it raycasts
    // doorKickDistance along dir on doorKickMask, and if the hit collider has an ANBDoorKicker it
    // calls kicker.doorScript.playerKickDoor() (and the linked kicker's). Its only callers are gun
    // code: ANBHVRGunBase.OnShoot (VR), Character.OnTryFire and ANBFpsInteraction.Interact (flat).
    // So call it for the bow too, at the same moment a gun does - when the shot is released - from
    // the nocked arrow along the shot direction. Same range, mask and gate as the pistol.
    [HarmonyLib.HarmonyPatch(typeof(HVRPhysicsBow), nameof(HVRPhysicsBow.ShootArrow))]
    internal static class ShootArrowDoorPatch
    {
        static void Prefix(HVRPhysicsBow __instance, Vector3 direction)
        {
            if (Settings.BreachDoors == null || !Settings.BreachDoors.Value) return;
            try
            {
                var game = ANBStaticGameManager.ANBmain;
                var arrow = __instance.Arrow;
                if (game == null || arrow == null) return;
                float len = MathF.Sqrt(direction.x * direction.x + direction.y * direction.y + direction.z * direction.z);
                if (len < 1e-4f) return;
                var dir = new Vector3(direction.x / len, direction.y / len, direction.z / len);
                bool kicked = game.TryKickDoor(arrow.transform.position, dir);
                if (kicked || U.Dbg)
                    Log.Msg(kicked ? "arrow breached a door"
                        : $"arrow shot: no door mark in range (door kick {(game.useVRDoorKick ? "on" : "OFF")}, range {game.doorKickDistance:0.#} m)");
            }
            catch (Exception e) { Log?.Warning($"door breach check skipped: {e.Message}"); }
        }
    }
}
