using System;
using Il2Cpp;
using Il2CppRootMotion.Dynamics;
using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // Club damage. The game's blunt path (ANBBluntWeapon.hit -> ANBBasicNPC.TakeBluntWeaponDamage):
    // - damage by speed tier: >= fastSpeed 34, >= mediumSpeed 10, else 5 (crowbar values); legs x0.65;
    //   the head x10 but only when canKill.
    // - only a head hit can take the last health: any other hit that would leaves 1 health. So body hits never
    //   knock anyone out, however hard (0.5.3 test: "you need to hit him several times").
    // - hit() clears canHit and waits 0.6 s (hitPause) before the weapon can hit again, so the second enemy
    //   of a ricochet chain took no damage.
    // The mod scales the tier damage (swing / thrown), knocks out when a body hit takes the last health, and
    // lets a club hit a different enemy inside the game's pause. Every club hit is logged.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<float> ChestStunSeconds, SwingDamage, ThrowDamage, SameEnemyCooldown;
        internal static MelonPreferences_Entry<bool> BodyKnockout, KneelOnLegHits;
        internal static MelonPreferences_Entry<string> ThrowReaction;

        static void InitDamagePrefs(MelonPreferences_Category c)
        {
            SwingDamage = c.CreateEntry("SwingDamageMultiplier", 1.5f, description: "Club damage in your hand, as a multiple of the game's crowbar damage (fast hit 34, medium 10, slow 5; enemies have 100 health). 1 = the game's.");
            ThrowDamage = c.CreateEntry("ThrowDamageMultiplier", 2f, description: "Damage of a thrown or ricocheting club, as a multiple of the crowbar damage. 2 = a fast throw to the body does 68: the enemy reacts (ThrowReaction) and the next hit knocks them out. 3 (102) knocks a full-health enemy out in one throw.");
            KneelOnLegHits = c.CreateEntry("KneelOnLegHits", true, description: "A club hit to the legs (thrown or swung) drops the enemy to one knee, like a knee shot, instead of the game's stumble. Knee Shot Stun makes the kneel longer.");
            ThrowReaction = c.CreateEntry("ThrowReaction", "Knockdown", description: "What a thrown club does to the body of an enemy it doesn't knock out: Stun (flinches and is stunned for ChestStunSeconds), Kneel (drops to one knee, the game's leg-hit stun; Knee Shot Stun makes it longer), Knockdown (falls to the ground), None (the game's short flinch).");
            ChestStunSeconds = c.CreateEntry("ChestStunSeconds", 1f, description: "ThrowReaction = Stun: seconds an enemy stays stunned (can't act) after a thrown club to the body. The game's own blunt-hit stun is much shorter.");
            BodyKnockout = c.CreateEntry("BodyKnockout", true, description: "A club hit to the body that takes the last health knocks the enemy out. The game only allows that for the head (other hits leave 1 health). Leg hits knock out only an enemy that is already down.");
            SameEnemyCooldown = c.CreateEntry("SameEnemyCooldown", 0.3f, description: "Seconds before a club can hurt the same enemy again. A different enemy can always be hit (the game makes the weapon wait 0.6 s after any hit, which made ricochets harmless).");
        }

        static Club ClubOf(Component c)
        {
            if (!Alive(c)) return null;
            var go = c.gameObject;
            foreach (var k in Clubs) if (Alive(k.Go) && k.Go.Pointer == go.Pointer) return k;
            return null;
        }

        static bool Thrown(Club k) => k.Fly != null || Time.time - k.FlightEndedAt < 0.3f;

        // ANBBluntWeapon.hit prefix: the game's 0.6 s pause only blocks the same enemy.
        internal static void BeforeBluntHit(ANBBluntWeapon w, ANBBasicNPC npc)
        {
            PauseSkipped = false;
            var k = ClubOf(w);
            if (k == null || !Alive(npc)) return;
            bool same = Alive(k.LastHitNpc) && k.LastHitNpc.Pointer == npc.Pointer;
            if (same && Time.time - k.LastHitAt < SameEnemyCooldown.Value) { w.canHit = false; return; }
            if (!w.canHit)
            {
                w.canHit = true;
                PauseSkipped = true;
            }
            k.LastHitNpc = npc; k.LastHitAt = Time.time;
        }

        static bool PauseSkipped;

        class HitCtx
        {
            public Club K;
            public ANBBluntWeapon W;
            public float Before, Mult, Slow, Med, Fast;
            public bool LegsBefore, Thrown, Kneel, LegKneel;
            public Collider Part;
            public string Leg;
        }

        static HitCtx Ctx;
        static IntPtr KneelNpc; // GetHit, called inside TakeBluntWeaponDamage, must not stumble this one

        internal static void BeforeGetHit(ANBBasicNPC npc, ref bool isRunning)
        {
            if (KneelNpc != IntPtr.Zero && npc.Pointer == KneelNpc) isRunning = false;
        }

        internal static void BeforeBluntDamage(ANBBasicNPC npc, ANBBluntWeapon w, ref Collider bodyPart)
        {
            Ctx = null;
            var k = ClubOf(w);
            if (k == null || !Alive(npc)) return;
            bool thrown = Thrown(k);
            if (thrown && k.Fly != null) k.Fly.EnemyHit = npc;
            var c = Ctx = new HitCtx
            {
                K = k, W = w, Before = npc.health, Thrown = thrown, Mult = thrown ? ThrowDamage.Value : SwingDamage.Value,
                Slow = w.damageOnSlow, Med = w.damageOnMedium, Fast = w.damageOnFast, LegsBefore = npc.gettingHitLegs, Part = bodyPart,
            };
            // Real leg hit: kneel instead of the stumble.
            if (KneelOnLegHits.Value && bodyPart != null && bodyPart.name.Contains("Leg") && !npc.isOffBalance && !npc.gettingHitLegs)
            {
                c.LegKneel = true; c.Leg = bodyPart.name;
                KneelNpc = npc.Pointer;
            }
            // Kneel: hand the game a leg as the hit part. Its own leg path then plays the kneel (hit_legs_1) and stuns;
            // the leg damage cut (x0.65) is undone so the damage stays a body hit's.
            if (thrown && ThrowReaction.Value == "Kneel" && bodyPart != null && !npc.isOffBalance && !npc.gettingHitLegs
                && !npc.CheckBodypart(bodyPart.gameObject, "head") && !bodyPart.name.Contains("Leg"))
            {
                var leg = FindLeg(npc);
                if (leg != null)
                {
                    bodyPart = leg;
                    c.Kneel = true; c.Leg = leg.name;
                    KneelNpc = npc.Pointer;
                    if (w.limbMultiplier > 0.01f) c.Mult /= w.limbMultiplier;
                }
            }
            w.damageOnSlow *= c.Mult; w.damageOnMedium *= c.Mult; w.damageOnFast *= c.Mult;
            npc.gettingHitLegs = false; // the game sets it on a leg hit: that is how the mod tells legs apart
        }

        // The blunt path never plays the knee-shot animation (only the gunshot path crossfades it), and its GetHit
        // stumbles the enemy. The stumble is blocked above; here the Hit layer is crossfaded to the kneel like a leg
        // shot. Knee Shot Stun, which registered the enemy in its GetHit prefix, then holds it.
        static string StartKneel(ANBBasicNPC npc, string leg)
        {
            var anim = npc.animator;
            if (anim == null) return " - kneel: no animator";
            int layer = anim.GetLayerIndex("Hit");
            if (layer < 0) return " - kneel: no Hit layer";
            npc.gettingHitLegs = true;
            anim.CrossFadeInFixedTime(leg != null && leg.StartsWith("Right") ? "hit_legs_1_m" : "hit_legs_1", 0.1f, layer, 0f);
            return " - KNEELS";
        }

        static Collider FindLeg(ANBBasicNPC npc)
        {
            foreach (var col in npc.GetComponentsInChildren<Collider>())
                if (col.enabled && !col.isTrigger && (col.name == "RightLeg" || col.name == "LeftLeg")) return col;
            return null;
        }

        internal static void AfterBluntDamage(ANBBasicNPC npc, float speed)
        {
            var c = Ctx; Ctx = null;
            KneelNpc = IntPtr.Zero;
            bool skipped = PauseSkipped; PauseSkipped = false;
            if (c == null) return;
            var w = c.W;
            w.damageOnSlow = c.Slow; w.damageOnMedium = c.Med; w.damageOnFast = c.Fast;
            try
            {
                var bodyPart = c.Part;
                bool legs = npc.gettingHitLegs && !c.Kneel;
                if (!npc.gettingHitLegs) npc.gettingHitLegs = c.LegsBefore;
                bool head = bodyPart != null && npc.CheckBodypart(bodyPart.gameObject, "head");
                string tier = speed >= w.fastSpeed ? "fast" : speed >= w.mediumSpeed ? "medium" : "slow";
                float dmg = (speed >= w.fastSpeed ? c.Fast : speed >= w.mediumSpeed ? c.Med : c.Slow) * c.Mult;
                if (head && w.canKill) dmg *= w.headMultiplier;
                else if (legs || c.Kneel) dmg *= w.limbMultiplier;
                string part = head ? "head" : legs ? "legs" : "body";
                string extra = "";
                if (npc.isUnhittable) extra = " (unhittable: no damage)";
                // The game left 1 health where the hit should have taken the last of it.
                else if (!npc.isDead && c.Before - dmg <= 0f)
                {
                    if (BodyKnockout.Value && (!legs || npc.isOffBalance))
                    {
                        var game = ANBStaticGameManager.ANBmain;
                        if (game != null && npc.isEnemy && npc.pointsPos != null) game.AddMeleekill(npc.pointsPos.position);
                        npc.KillNPC(null);
                        extra = " - KNOCKED OUT";
                    }
                    else extra = " - left at 1 health (game rule)";
                }
                if (!npc.isDead && extra == "" && c.LegKneel && legs) extra = StartKneel(npc, c.Leg);
                if (!npc.isDead && extra == "" && c.Thrown && !head && !legs)
                {
                    if (c.Kneel) extra = StartKneel(npc, c.Leg);
                    else if (ThrowReaction.Value == "Stun")
                    {
                        // GetHit (inside the call) just set the game's short stun; stretch the same timer.
                        float before = npc.overallHitTime;
                        if (npc.overallHitTime < ChestStunSeconds.Value) npc.overallHitTime = ChestStunSeconds.Value;
                        npc.isGettingHit = true;
                        extra = $" - STUNNED {npc.overallHitTime:0.#} s (game {before:0.#} s)";
                    }
                    else if (ThrowReaction.Value == "Knockdown" && !npc.isOffBalance)
                    {
                        var puppet = npc.ANBPM != null ? npc.ANBPM.puppet : null;
                        if (puppet != null) { puppet.SetState(BehaviourPuppet.State.Unpinned); extra = " - KNOCKED DOWN"; }
                    }
                }
                if (Dbg)
                    Log.Msg($"club hit '{npc.name}' {part} ({Name(bodyPart)}) at {speed:0.0} m/s ({tier}), {(c.Thrown ? "thrown" : "swing")} x{c.Mult:0.#}: " +
                            $"damage {dmg:0}, health {c.Before:0} -> {npc.health:0}{(npc.isDead ? " (down for good)" : "")}{extra}{(skipped ? " [game's hit pause skipped: new enemy]" : "")}");
            }
            catch (Exception e) { Log.Warning($"club hit: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBluntWeapon), nameof(ANBBluntWeapon.hit))]
    static class BluntHitPatch
    {
        static void Prefix(ANBBluntWeapon __instance, ANBBasicNPC ANBNpc)
        {
            try { BillyClubsMod.BeforeBluntHit(__instance, ANBNpc); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] BeforeBluntHit: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.GetHit))]
    static class GetHitPatch
    {
        static void Prefix(ANBBasicNPC __instance, ref bool isRunning)
        {
            try { BillyClubsMod.BeforeGetHit(__instance, ref isRunning); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] BeforeGetHit: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.TakeBluntWeaponDamage))]
    static class BluntDamagePatch
    {
        static void Prefix(ANBBasicNPC __instance, ANBBluntWeapon BluntWeapon, ref Collider bodyPart)
        {
            try { BillyClubsMod.BeforeBluntDamage(__instance, BluntWeapon, ref bodyPart); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] BeforeBluntDamage: {e.Message}"); }
        }

        static void Postfix(ANBBasicNPC __instance, float speed)
        {
            try { BillyClubsMod.AfterBluntDamage(__instance, speed); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] AfterBluntDamage: {e.Message}"); }
        }
    }
}
