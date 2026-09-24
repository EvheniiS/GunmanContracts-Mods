using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppInterop.Runtime;
using UnityEngine;
using static BetterBow.BetterBowMod;

namespace BetterBow
{
    // ---- Thrown quiver arrows get the knives' throw aim assist ---------------------------------------
    // Knife throw assist is ANBKnife.releaseKnife: if allowAssistedThrow, the game's assistedThrow
    // setting is on, the knife has a ThrowScript (ANBAssistedThrowingObject) and it leaves the hand at
    // assistedThrowAtVelocity or faster, it runs
    //   ToggleBodyPartCollisionsExec(0)   (only if ignoreBodyPartsOnThrow: fly past arms/legs)
    //   makeTempStabAll()                 (stab anything, as a shot arrow does)
    //   ThrowScript.StartAssistedThrow()  (pick the enemy closest to the centre of view, then
    //                                      MovePosition at `speed` towards it every FixedUpdate)
    // An arrow is an ANBKnife too, but its prefab has no ThrowScript, so a thrown arrow just falls.
    // This gives it one and runs the same three steps. The target search uses the head camera and the
    // game's assistedThrowView* settings, never the knife, so it works unchanged for an arrow.
    internal static class ThrowAssist
    {
        // Tuning copied from the first real throwing knife that starts (see KnifeStartPatch).
        static bool _haveKnife;
        static float _speed = 10f, _stopDistance = 0.5f, _searchOverride, _flyOverride;
        static bool _ignoreBody, _ignoreLegs, _ignoreArms;
        static float _bodyPartTime;

        internal static IntPtr ThrownKnife;                // ANBKnife of the last assisted arrow, for the hit log

        internal static void LearnFromKnife(ANBKnife k)
        {
            if (_haveKnife || !U.Alive(k) || k.isArrow || !k.allowAssistedThrow) return;
            var t = k.ThrowScript;
            if (!U.Alive(t)) return;
            _haveKnife = true;
            _speed = t.speed; _stopDistance = t.stopDistance;
            _searchOverride = t.targetSearchDistanceOverride; _flyOverride = t.maxFlyDistanceOverride;
            _ignoreBody = k.ignoreBodyPartsOnThrow; _ignoreLegs = k.ignoreBodyPartsOnThrow_Legs;
            _ignoreArms = k.ignoreBodyPartsOnThrow_Arms; _bodyPartTime = k.throwableBodyPartTime;
            if (U.Dbg) Log.Msg($"throw assist tuning from knife '{k.name}': speed {_speed:0.#} m/s, stop {_stopDistance:0.##} m, " +
                               $"search {_searchOverride:0.#}, max fly {_flyOverride:0.#}, skip body parts {_ignoreBody} (legs {_ignoreLegs}, arms {_ignoreArms}, {_bodyPartTime:0.##} s)");
        }

        // Fast enough to count as a throw (the same threshold the game uses for knives).
        internal static bool IsThrow(HVRArrow arrow)
        {
            var rb = arrow.Rigidbody;
            if (!U.Alive(rb)) return false;
            var game = ANBStaticGameManager.ANBmain;
            float min = game != null ? game.assistedThrowAtVelocity : 2f;
            var v = rb.linearVelocity;
            return v.x * v.x + v.y * v.y + v.z * v.z >= min * min;
        }

        internal static void Throw(HVRArrow arrow)
        {
            if (Settings.ThrowAssist == null || !Settings.ThrowAssist.Value || !U.Alive(arrow)) return;
            try
            {
                var game = ANBStaticGameManager.ANBmain;
                var rb = arrow.Rigidbody;
                var knife = arrow.GetComponent<ANBKnife>();
                if (game == null || !U.Alive(rb) || !U.Alive(knife))
                {
                    if (U.Dbg) Log.Msg($"arrow throw assist: nothing to work with (game {game != null}, rigidbody {U.Alive(rb)}, knife {U.Alive(knife)})");
                    return;
                }
                var v = rb.linearVelocity;
                float speed = MathF.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
                if (!game.assistedThrow)
                {
                    if (U.Dbg) Log.Msg($"arrow thrown at {speed:0.0} m/s - no assist: the game's assisted throw setting is off");
                    return;
                }
                if (speed < game.assistedThrowAtVelocity)
                {
                    if (U.Dbg) Log.Msg($"arrow dropped at {speed:0.0} m/s - below the {game.assistedThrowAtVelocity:0.0} m/s throw threshold");
                    return;
                }

                bool had = U.Alive(knife.ThrowScript);
                var t = ThrowScriptOf(arrow, knife, rb);
                if (t == null) return;
                // No "already homing" check: the constructor sets isHoming = true, so a fresh script
                // reads as homing (1.1.0 returned there on every throw, silently).
                if (U.Dbg) Log.Msg($"arrow throw script: {(had ? "the arrow's own" : "added by the mod")}, allowAssistedThrow {knife.allowAssistedThrow}");
                t.dontUse = false;
                if (Settings.ThrowAssistSpeed.Value > 0f) t.speed = Settings.ThrowAssistSpeed.Value;
                // Keep the arrow pointing at the target the whole way, no knife spin.
                t.changeRotation = true;
                t.torque = 0f;
                t.aimCorrectionAngle = AimCorrection(arrow);

                if (_haveKnife)
                {
                    knife.ignoreBodyPartsOnThrow = _ignoreBody;
                    knife.ignoreBodyPartsOnThrow_Legs = _ignoreLegs;
                    knife.ignoreBodyPartsOnThrow_Arms = _ignoreArms;
                    knife.throwableBodyPartTime = _bodyPartTime;
                }
                // The same three steps as ANBKnife.releaseKnife.
                if (knife.ignoreBodyPartsOnThrow) knife.StartCoroutine(knife.ToggleBodyPartCollisionsExec(0f));
                knife.makeTempStabAll();
                t.StartAssistedThrow();

                ThrownKnife = knife.Pointer;
                if (U.Dbg)
                {
                    var target = t.homingTarget;
                    Log.Msg(U.Alive(target)
                        ? $"arrow thrown at {speed:0.0} m/s -> homing on '{target.name}' {U.Dist(target.position, rb.position):0.0} m away at {t.speed:0.#} m/s (tip correction {t.aimCorrectionAngle:0} deg)"
                        : $"arrow thrown at {speed:0.0} m/s - no target in view (radius {game.assistedThrowViewRadius:0.#} m, angle {game.assistedThrowViewAngle:0.#} deg)");
                }
            }
            catch (Exception e) { Log.Warning($"arrow throw assist skipped: {e.GetType().Name}: {e.Message}"); }
        }

        // The arrow's own ThrowScript if the prefab has one, else a new one on the rigidbody's object
        // (its Awake takes the Rigidbody from its own GameObject and destroys itself without one).
        static ANBAssistedThrowingObject ThrowScriptOf(HVRArrow arrow, ANBKnife knife, Rigidbody rb)
        {
            var t = knife.ThrowScript;
            if (!U.Alive(t)) t = rb.GetComponent<ANBAssistedThrowingObject>();
            if (U.Alive(t)) return t;
            t = rb.gameObject.AddComponent(Il2CppType.Of<ANBAssistedThrowingObject>())?.TryCast<ANBAssistedThrowingObject>();
            if (!U.Alive(t)) { Log.Warning("arrow throw assist: could not add a throw script to the arrow"); return null; }
            if (!U.Alive(t.rb)) t.rb = rb;
            t.speed = _speed;
            t.stopDistance = _stopDistance;
            t.targetSearchDistanceOverride = _searchOverride;
            t.maxFlyDistanceOverride = _flyOverride;
            return t;
        }

        // The homing turns the object with LookRotation(to target) * Euler(0, aimCorrectionAngle, 0), so
        // the local axis that ends up facing the target is Euler(0, -angle, 0) * forward. Pick the angle
        // that makes that the tip. (The knife default, 90, is for a blade held sideways.)
        static float AimCorrection(HVRArrow arrow)
        {
            var at = arrow.transform;
            var tip = at.InverseTransformDirection(Quiver.TipDirection(arrow, at.position));
            return -MathF.Atan2(tip.x, tip.z) * 180f / MathF.PI;
        }
    }

    // Learn the throw tuning from the game's own throwing knives as they appear.
    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.Start))]
    internal static class KnifeStartPatch
    {
        static void Postfix(ANBKnife __instance)
        {
            try { ThrowAssist.LearnFromKnife(__instance); } catch { }
        }
    }
}
