using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Weapons;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using Il2CppRootMotion.Dynamics;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HeavyMelee.HeavyMeleeMod), "Heavy Melee", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace HeavyMelee
{
    // Guns hit like the heavy metal they are, fists hit harder, and an enemy on the ground still
    // feels every hit and stays down.
    //
    // How the game does melee (GameAssembly.dll):
    // - Every enemy body part has an ANBBodyMeleeCollision that forwards OnCollisionEnter to
    //   ANBBodyMeleeCollisionManager.collisionEnter. That looks at what touched it:
    //   * ANBBluntWeapon (nunchucks, bats) -> its own hit(), speed tiers, no stun gate.
    //   * a gun -> goes on only if its rigidbody moves faster than meleeMagnitude; then it counts
    //     only if the gun's rigidbody mass is > 1 kg, and it stumbles the enemy only if the mass is
    //     > ANBNpcPuppetmasterSettings.StumbleOnRBMass. No forced stumble for a swing.
    //   * a hand -> ignored completely unless ANBGameLogic.LeftHandWeapon / RightHandWeapon is
    //     "none". So the knuckles of a hand holding a pistol never count.
    //   * knives and magazines -> ignored (knives stab through their own path).
    // - Then: if the enemy isGettingHit (the hit stun the first punch starts, which also covers
    //   lying on the ground), TakeMeleeDamage runs with noDamage = true. That is why only one punch
    //   per knockdown counts and a downed enemy "doesn't feel" anything.
    // - TakeMeleeDamage: damage = ANBGameLogic.meleeDamage (x5 head, x2 torso). Only head and torso
    //   hits can kill; any other hit leaves at least 1 health.
    // - Lying on the ground = the PuppetMaster BehaviourPuppet is Unpinned (ANBBasicNPC.isOffBalance
    //   mirrors that). It gets up once unpinnedTimer passes getUpDelay.
    //
    // So the mod takes over collisionEnter for (a) a held gun or bow, or a hand holding one, and
    // (b) a bare fist on an enemy that is already stunned or down, and calls the game's own
    // TakeMeleeDamage with a damage override. Standing enemies hit by a bare fist still go through
    // the game's path, with the damage scaled. Every hit on a downed enemy resets its get-up timer,
    // a hit while it is getting up knocks it back down, and a limb hit on a downed enemy with no
    // health left knocks it out.
    public class HeavyMeleeMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, WeaponHits, DownedHits, KeepDown, KnockDownGettingUp, KnockoutOnGround, DebugLog;
        internal static MelonPreferences_Entry<float> FistDamageMultiplier, WeaponDamageMultiplier, WeaponMinSpeed, HitCooldown;

        internal static bool Dbg => DebugLog.Value;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("HeavyMelee", "Heavy Melee");
            Enabled = c.CreateEntry("Enabled", true, description: "Master switch for the whole mod.");
            WeaponHits = c.CreateEntry("WeaponHits", true, description: "Hitting an enemy with a held gun or bow (or with the hand holding it) counts as a melee hit and makes them stumble.");
            DownedHits = c.CreateEntry("DownedHits", true, description: "Punches and weapon hits do damage to an enemy that is already stunned or on the ground (the game ignores them).");
            KeepDown = c.CreateEntry("KeepDown", true, description: "Every hit on an enemy lying on the ground restarts their get-up timer.");
            KnockDownGettingUp = c.CreateEntry("KnockDownGettingUp", true, description: "A hit while an enemy is getting back up knocks them down again.");
            KnockoutOnGround = c.CreateEntry("KnockoutOnGround", true, description: "Enemies on the ground can be knocked out by hits to any body part, not only the head or torso.");
            FistDamageMultiplier = c.CreateEntry("FistDamageMultiplier", 2.0f, description: "Bare-fist damage, as a multiple of the game's melee damage. 1 = the game's value.");
            WeaponDamageMultiplier = c.CreateEntry("WeaponDamageMultiplier", 3.0f, description: "Gun/bow hit damage, as a multiple of the game's melee damage.");
            WeaponMinSpeed = c.CreateEntry("WeaponMinSpeed", 0f, description: "How fast (m/s) a weapon must move to count as a hit. 0 = the game's own punch threshold for that enemy.");
            HitCooldown = c.CreateEntry("HitCooldown", 0.3f, description: "Seconds before the same enemy can take another hit from the mod (one swing touches several body parts).");
            DebugLog = c.CreateEntry("DebugLog", true, description: "Log every hit the mod handles, and the game's melee values.");
            LoggerInstance.Msg($"loaded - fists x{FistDamageMultiplier.Value:0.##}, weapons x{WeaponDamageMultiplier.Value:0.##}" +
                               $"{(WeaponHits.Value ? ", weapon hits on" : "")}{(DownedHits.Value ? ", downed hits on" : "")}.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LastHit.Clear();
            TuningLogged.Clear();
        }

        enum Source { None, Fist, Weapon }

        // Set while the game's own collisionEnter handles a bare-fist punch, so the TakeMeleeDamage
        // prefix knows to scale it (and nothing else: thrown props, enemies, nunchucks).
        internal static bool FistContext;

        static readonly Dictionary<IntPtr, float> LastHit = new();
        static readonly HashSet<IntPtr> TuningLogged = new();

        // ---- collisionEnter prefix. Return false = the mod handled it, skip the game's handler.
        internal static bool OnBodyCollision(ANBBodyMeleeCollisionManager mgr, ANBBodyMeleeCollision bmc, Collision collision)
        {
            FistContext = false;
            if (!Enabled.Value) return true;
            try
            {
                var npc = mgr.ANBNpc;
                if (npc == null || bmc == null || collision == null) return true;
                // The game's own gates, before any hit is possible.
                if (npc.isDead || npc.NPCPaused || !npc.NPCStarted || npc.NoBodyCollisions || npc.isUnhittable ||
                    npc.gettingDisarmed || npc.isThrowing || mgr.collisionPauseCurrent > 0f) return true;
                var game = ANBStaticGameManager.ANBmain;
                if (game == null || !game.gameStarted) return true;

                var col = collision.collider;
                if (col == null) return true;
                var src = Classify(col, out string what, out Rigidbody rb);
                if (src == Source.None) return true;

                bool stunned = npc.isGettingHit || npc.isOffBalance;
                if (src == Source.Fist && (!stunned || !DownedHits.Value))
                {
                    FistContext = true;                    // the game's punch, with scaled damage
                    return true;
                }
                if (src == Source.Weapon && !WeaponHits.Value) return true;
                if (src == Source.Weapon && stunned && !DownedHits.Value) return true;

                float speed = rb.linearVelocity.magnitude;
                float min = WeaponMinSpeed.Value > 0f ? WeaponMinSpeed.Value : mgr.meleeMagnitude;
                if (speed <= min) return src == Source.Fist;   // too slow: a gun resting on them is not a hit

                float now = Time.time;
                IntPtr key = npc.Pointer;
                if (LastHit.TryGetValue(key, out float last) && now - last < HitCooldown.Value) return false;
                LastHit[key] = now;

                Strike(game, mgr, bmc, collision, npc, src, what, rb, speed, min);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning($"collision: {ex.Message}");
                return true;
            }
        }

        static Source Classify(Collider col, out string what, out Rigidbody rb)
        {
            what = col.name;
            rb = col.attachedRigidbody;
            if (rb == null) return Source.None;
            var go = col.gameObject;
            // These have their own melee logic in the game.
            if (go.GetComponentInParent<ANBBluntWeapon>() != null || go.GetComponentInParent<ANBKnife>() != null ||
                go.GetComponentInParent<ANBHVRAmmo>() != null) return Source.None;

            var hand = go.GetComponentInParent<HVRHandGrabber>();
            if (hand != null)
            {
                string side = hand.IsLeftHand ? "left" : "right";
                var held = hand.GrabbedTarget;
                if (held == null) { what = $"{side} fist"; return Source.Fist; }
                if (WeaponName(held, out string w)) { what = $"{side} hand holding {w}"; return Source.Weapon; }
                return Source.None;                        // phone, prop, knife: the game's rules
            }

            // The weapon itself. Held = its main grip or its foregrip is in a hand.
            var gun = go.GetComponentInParent<ANBHVRGunBase>();
            if (gun != null)
            {
                if (gun.EnemyGun || !(Held(gun.Grabbable) || Held(gun.StabilizerGrabbable))) return Source.None;
                what = gun.name;
                return Source.Weapon;
            }
            var bow = go.GetComponentInParent<HVRBowBase>();
            if (bow != null && Held(go.GetComponentInParent<HVRGrabbable>())) { what = bow.name; return Source.Weapon; }
            return Source.None;
        }

        static bool Held(HVRGrabbable g) => g != null && g.IsHandGrabbed;

        static bool WeaponName(HVRGrabbable g, out string name)
        {
            name = null;
            var gun = g.GetComponentInParent<ANBHVRGunBase>();
            if (gun != null) { if (gun.EnemyGun) return false; name = gun.name; return true; }
            var bow = g.GetComponentInParent<HVRBowBase>();
            if (bow != null) { name = bow.name; return true; }
            return false;
        }

        static void Strike(ANBGameLogic game, ANBBodyMeleeCollisionManager mgr, ANBBodyMeleeCollision bmc, Collision collision,
                           ANBBasicNPC npc, Source src, string what, Rigidbody rb, float speed, float min)
        {
            var pm = npc.ANBPM;
            var puppet = pm != null ? pm.puppet : null;
            if (Dbg && TuningLogged.Add(npc.Pointer))
                Log.Msg($"{npc.name}: game melee values - meleeDamage {game.meleeDamage:0.##}, punch speed {mgr.meleeMagnitude:0.##} m/s, " +
                        $"stumble above {(pm != null ? pm.StumbleOnRBMass : -1):0.##} kg, health {npc.health:0.#}, " +
                        $"get-up delay {(puppet != null ? puppet.getUpDelay : -1):0.##} s");

            var state = puppet != null ? puppet.state : BehaviourPuppet.State.Puppet;
            bool down = npc.isOffBalance;
            float dmg = game.meleeDamage * (src == Source.Weapon ? WeaponDamageMultiplier.Value : FistDamageMultiplier.Value);
            // A weapon always staggers a standing enemy; a fist uses the game's rule. Never on the ground.
            bool stumble = !down && (src == Source.Weapon || npc.isGrabbed || (pm != null && rb.mass > pm.StumbleOnRBMass));
            float before = npc.health;

            npc.TakeMeleeDamage(bmc.myCol, collision, stumble, false, false, dmg);

            string extra = "";
            if (puppet != null && !npc.isDead)
            {
                if (state == BehaviourPuppet.State.Unpinned && KeepDown.Value)
                {
                    puppet.unpinnedTimer = 0f;
                    extra = " - stays down";
                }
                else if (state == BehaviourPuppet.State.GetUp && KnockDownGettingUp.Value)
                {
                    puppet.SetState(BehaviourPuppet.State.Unpinned);
                    extra = " - knocked back down";
                }
            }

            // Limb hits never kill in the game (health stops at 1). On the ground, they can knock out.
            if (down && KnockoutOnGround.Value && !npc.isDead && before - dmg <= 0f)
            {
                if (npc.isEnemy && npc.pointsPos != null) game.AddMeleekill(npc.pointsPos.position);
                npc.KillNPC(null);
                extra += " - KNOCKED OUT";
            }

            if (Dbg)
                Log.Msg($"{npc.name}: {what} hit {bmc.myCol?.name} at {speed:0.0} m/s (min {min:0.0}, mass {rb.mass:0.##} kg), " +
                        $"{(down ? "on the ground" : npc.isGettingHit ? "stunned" : "standing")}, damage {dmg:0.#}" +
                        $"{(stumble ? ", stumble" : "")}: health {before:0.#} -> {npc.health:0.#}{(npc.isDead ? " (dead)" : "")}{extra}");
        }

        // ---- TakeMeleeDamage prefix: scale the game's own bare-fist punch.
        internal static void OnTakeMeleeDamage(ANBBasicNPC npc, bool fromEnemy, bool noDamage, ref float damageOverride)
        {
            if (!FistContext) return;
            FistContext = false;
            try
            {
                if (fromEnemy || noDamage || damageOverride > 0f) return;
                var game = ANBStaticGameManager.ANBmain;
                if (game == null) return;
                damageOverride = game.meleeDamage * FistDamageMultiplier.Value;
                if (Dbg) Log.Msg($"{npc.name}: punch (game path), damage {game.meleeDamage:0.#} -> {damageOverride:0.#}, health {npc.health:0.#}");
            }
            catch (Exception ex) { Log.Warning($"punch: {ex.Message}"); }
        }

        // ---- nunchucks and other blunt weapons keep their own damage; they only get the keep-down part.
        internal static void AfterBluntHit(ANBBasicNPC npc)
        {
            if (!Enabled.Value) return;
            try
            {
                var puppet = npc.ANBPM != null ? npc.ANBPM.puppet : null;
                if (puppet == null || npc.isDead) return;
                if (puppet.state == BehaviourPuppet.State.Unpinned && KeepDown.Value) puppet.unpinnedTimer = 0f;
                else if (puppet.state == BehaviourPuppet.State.GetUp && KnockDownGettingUp.Value) puppet.SetState(BehaviourPuppet.State.Unpinned);
                else return;
                if (Dbg) Log.Msg($"{npc.name}: blunt weapon hit on the ground - stays down, health {npc.health:0.#}");
            }
            catch (Exception ex) { Log.Warning($"blunt: {ex.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBodyMeleeCollisionManager), nameof(ANBBodyMeleeCollisionManager.collisionEnter))]
    internal static class CollisionEnterPatch
    {
        static bool Prefix(ANBBodyMeleeCollisionManager __instance, ANBBodyMeleeCollision bmc, Collision collision) =>
            HeavyMeleeMod.OnBodyCollision(__instance, bmc, collision);
        static void Postfix() => HeavyMeleeMod.FistContext = false;
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.TakeMeleeDamage))]
    internal static class TakeMeleeDamagePatch
    {
        static void Prefix(ANBBasicNPC __instance, bool fromEnemy, bool noDamage, ref float damageOverride) =>
            HeavyMeleeMod.OnTakeMeleeDamage(__instance, fromEnemy, noDamage, ref damageOverride);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.TakeBluntWeaponDamage))]
    internal static class BluntPatch
    {
        static void Postfix(ANBBasicNPC __instance) => HeavyMeleeMod.AfterBluntHit(__instance);
    }
}
