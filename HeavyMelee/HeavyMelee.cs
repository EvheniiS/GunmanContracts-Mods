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

[assembly: MelonInfo(typeof(HeavyMelee.HeavyMeleeMod), "Heavy Melee", "1.0.0", "Evgeeso")]
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
    //   mirrors that). It gets up once unpinnedTimer passes getUpDelay and the body has stopped moving.
    //
    // So the mod takes over collisionEnter for (a) a held gun or bow, or a hand holding one, and
    // (b) a bare fist on an enemy that is already stunned or down, and calls the game's own
    // TakeMeleeDamage with a damage override. Standing enemies hit by a bare fist still go through
    // the game's path, with the damage scaled. No hit does more than MaxDamagePerHit after the
    // game's head/torso multiplier, so nobody drops from one blow. A hit on a downed enemy holds it
    // on the ground for KeepDownSeconds, a hit while it is getting up knocks it back down, and a
    // limb hit on a downed enemy with no health left knocks it out.
    //
    // Numbers from the 0.1.0 test log (Sep 25 2026): meleeDamage 10, enemy health 100,
    // meleeMagnitude 0 (so the game's only speed gate is its hard-coded rb speed > 1 m/s), stumble
    // above 5 kg, hand rigidbody 20 kg, pistol and bow 1.5 kg, BehaviourPuppet.getUpDelay 0.
    public class HeavyMeleeMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, WeaponHits, DownedHits, KeepDown, KnockDownGettingUp, KnockoutOnGround,
            OpenPalmNoHit, IgnoreKickedObjects, DebugLog;
        internal static MelonPreferences_Entry<float> FistDamageMultiplier, WeaponDamageMultiplier, MaxDamagePerHit, MinPunchSpeed, MinWeaponSpeed,
            FistGrip, OpenPalmStrikeSpeed, HitCooldown, KeepDownSeconds;

        internal static bool Dbg => DebugLog.Value;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("HeavyMelee", "Heavy Melee");
            Enabled = c.CreateEntry("Enabled", true, description: "Master switch for the whole mod.");
            WeaponHits = c.CreateEntry("WeaponHits", true, description: "Hitting an enemy with a held gun or bow (or with the hand holding it) counts as a melee hit and makes them stumble.");
            DownedHits = c.CreateEntry("DownedHits", true, description: "Punches and weapon hits do damage to an enemy that is already stunned or on the ground (the game ignores them).");
            KeepDown = c.CreateEntry("KeepDown", true, description: "A hit on an enemy lying on the ground keeps them down for KeepDownSeconds.");
            KeepDownSeconds = c.CreateEntry("KeepDownSeconds", 2.0f, description: "Seconds a downed enemy stays on the ground after your last hit (the game lets them get up as soon as they stop moving).");
            KnockDownGettingUp = c.CreateEntry("KnockDownGettingUp", true, description: "A hit while an enemy is getting back up knocks them down again.");
            KnockoutOnGround = c.CreateEntry("KnockoutOnGround", true, description: "Enemies on the ground can be knocked out by hits to any body part, not only the head or torso.");
            FistDamageMultiplier = c.CreateEntry("FistDamageMultiplier", 2.0f, description: "Bare-fist damage, as a multiple of the game's melee damage (10). 1 = the game's value.");
            WeaponDamageMultiplier = c.CreateEntry("WeaponDamageMultiplier", 3.0f, description: "Gun/bow hit damage, as a multiple of the game's melee damage (10).");
            MaxDamagePerHit = c.CreateEntry("MaxDamagePerHit", 50f, description: "Most damage one hit can do, after the game's head x5 / torso x2. Enemies have 100 health, so 50 = never one hit, head or torso twice. 0 = no limit.");
            MinPunchSpeed = c.CreateEntry("MinPunchSpeed", 3.0f, description: "How fast (m/s) a fist must move to count as a punch. Slower touches do nothing (the game counts anything over 1 m/s). Real punches log 3-8 m/s.");
            MinWeaponSpeed = c.CreateEntry("MinWeaponSpeed", 3.0f, description: "How fast (m/s) a held gun or bow must move to count as a hit.");
            OpenPalmNoHit = c.CreateEntry("OpenPalmNoHit", true, description: "Touching an enemy with an open hand (grip not squeezed) does nothing: no damage, no stumble. Squeeze grip to make a fist and punch.");
            FistGrip = c.CreateEntry("FistGrip", 0.5f, description: "How far the grip must be squeezed (0-1) for the hand to count as a fist.");
            OpenPalmStrikeSpeed = c.CreateEntry("OpenPalmStrikeSpeed", 8.0f, description: "An open hand moving at least this fast (m/s) hits like a punch: a slap or a chop to the back. Touches and pushes are far slower. 0 = an open hand never hits.");
            IgnoreKickedObjects = c.CreateEntry("IgnoreKickedObjects", true, description: "An enemy on the ground or getting up is not hurt by loose objects they push around themselves (the game counts a chair they kick as a thrown object hitting them). Thrown objects still hurt.");
            HitCooldown = c.CreateEntry("HitCooldown", 0.3f, description: "Seconds before the same enemy can take another hit (one swing touches several body parts).");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log every hit the mod handles, and the game's melee values.");
            LoggerInstance.Msg($"loaded - fists x{FistDamageMultiplier.Value:0.##}, weapons x{WeaponDamageMultiplier.Value:0.##}, max {MaxDamagePerHit.Value:0} per hit" +
                               $"{(WeaponHits.Value ? ", weapon hits on" : "")}{(DownedHits.Value ? ", downed hits on" : "")}.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            LastHit.Clear();
            PalmLogAt.Clear();
            TuningLogged.Clear();
            ReleaseAll();
        }

        enum Source { None, Fist, Weapon, Holding }

        // Set while the game's own collisionEnter handles a bare-fist punch, so the TakeMeleeDamage
        // prefix knows to scale it (and nothing else: thrown props, enemies, nunchucks).
        internal static bool FistContext;
        static float PunchSpeed;
        static string PunchWhat;
        static readonly Dictionary<IntPtr, float> PalmLogAt = new();

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
                var src = Classify(col, out string what, out Rigidbody rb, out HVRHandGrabber hand);
                if (src == Source.None) return KickedObjectCheck(npc, bmc, col, rb);
                // A hand gripping their wrist or mouth keeps touching them; the game would count every
                // new contact as a 20 kg punch and stumble them.
                if (src == Source.Holding) return false;

                // One swing touches several body parts. The first contact that counts (the game's
                // punch or the mod's hit) starts the cooldown, and the rest of the swing is ignored.
                float now = Time.time;
                IntPtr key = npc.Pointer;
                if (LastHit.TryGetValue(key, out float last) && now - last < HitCooldown.Value) return false;

                float speed = rb.linearVelocity.magnitude;
                float min = Mathf.Max(src == Source.Fist ? MinPunchSpeed.Value : MinWeaponSpeed.Value, mgr.meleeMagnitude);
                if (src == Source.Fist)
                {
                    // Grabbing a hand or the mouth, a push, a touch: not a punch. The game would count
                    // anything over 1 m/s and stumble them (the hand weighs 20 kg, over the 5 kg rule).
                    // A fast open hand is a slap or a chop, and hits like a punch.
                    if (OpenPalmNoHit.Value && hand != null && hand.Controller != null && hand.Controller.Grip < FistGrip.Value)
                    {
                        float strike = OpenPalmStrikeSpeed.Value;
                        if (strike <= 0f || speed < strike)
                        {
                            // One swing makes many contacts; log it once.
                            if (Dbg && speed > min && (!PalmLogAt.TryGetValue(key, out float at) || now - at > 0.5f))
                            {
                                PalmLogAt[key] = now;
                                Log.Msg($"{npc.name}: {what} open-palm touch at {speed:0.0} m/s (grip {hand.Controller.Grip:0.00}, strike at {strike:0.#}) - ignored");
                            }
                            return false;
                        }
                        what = what.Replace("fist", "open-palm strike");
                    }
                    if (speed <= min) return false;
                }

                bool stunned = npc.isGettingHit || npc.isOffBalance;
                if (src == Source.Fist && (!stunned || !DownedHits.Value))
                {
                    FistContext = true;                    // the game's punch, with scaled damage
                    PunchSpeed = speed;
                    PunchWhat = what;
                    return true;
                }
                if (src == Source.Weapon && !WeaponHits.Value) return true;
                if (src == Source.Weapon && stunned && !DownedHits.Value) return true;

                // Too slow: a gun resting on them is not a hit. Skip the game too, which would count a
                // 1.5 kg gun at 1 m/s as a hit.
                if (speed <= min) return false;
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

        // ---- Loose objects pushed by the enemy's own body.
        // The game's collisionEnter treats any rigidbody moving over 1 m/s as a thrown object hitting
        // the enemy (TakeMeleeDamage, and a stumble over 5 kg). An enemy thrashing on the ground or
        // getting up kicks chairs and boxes, they move, and that counts as hits. A thrown object moves
        // faster than the body part it hits; a kicked one moves no faster than the body part pushing it.
        static bool KickedObjectCheck(ANBBasicNPC npc, ANBBodyMeleeCollision bmc, Collider col, Rigidbody rb)
        {
            if (!IgnoreKickedObjects.Value || rb == null || !npc.isOffBalance) return true;
            var go = col.gameObject;
            // Other enemies' bodies, the player's hands and the game's own melee weapons keep the game's rules.
            if (go.GetComponent<ANBEnemyDetect>() != null || go.GetComponentInParent<HVRHandGrabber>() != null ||
                go.GetComponentInParent<ANBBluntWeapon>() != null || go.GetComponentInParent<ANBKnife>() != null) return true;
            var own = bmc.myCol != null ? bmc.myCol.attachedRigidbody : null;
            float objSpeed = rb.linearVelocity.magnitude;
            float bodySpeed = own != null ? own.linearVelocity.magnitude : 0f;
            if (objSpeed > bodySpeed + 1f) return true;   // it came at them: a thrown object
            if (Dbg && objSpeed > 1f)
                Log.Msg($"{npc.name}: ignored '{col.name}' ({rb.mass:0.#} kg, {objSpeed:0.0} m/s) pushed by its own {bmc.myCol?.name} ({bodySpeed:0.0} m/s)");
            return false;
        }

        static Source Classify(Collider col, out string what, out Rigidbody rb, out HVRHandGrabber hand)
        {
            what = col.name;
            hand = null;
            rb = col.attachedRigidbody;
            if (rb == null) return Source.None;
            var go = col.gameObject;
            // These have their own melee logic in the game.
            if (go.GetComponentInParent<ANBBluntWeapon>() != null || go.GetComponentInParent<ANBKnife>() != null ||
                go.GetComponentInParent<ANBHVRAmmo>() != null) return Source.None;

            hand = go.GetComponentInParent<HVRHandGrabber>();
            if (hand != null)
            {
                string side = hand.IsLeftHand ? "left" : "right";
                var held = hand.GrabbedTarget;
                if (held == null) { what = $"{side} fist"; return Source.Fist; }
                if (WeaponName(held, out string w)) { what = $"{side} hand holding {w}"; return Source.Weapon; }
                what = $"{side} hand holding {held.name}";
                return Source.Holding;                     // an enemy's hand or head, the phone, a prop: not a punch
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
            float mult = PartMultiplier(npc, bmc.myCol);
            float dmg = Capped(game.meleeDamage * (src == Source.Weapon ? WeaponDamageMultiplier.Value : FistDamageMultiplier.Value), mult);
            // A weapon always staggers a standing enemy; a fist uses the game's rule. Never on the ground.
            bool stumble = !down && (src == Source.Weapon || npc.isGrabbed || (pm != null && rb.mass > pm.StumbleOnRBMass));
            float before = npc.health;

            npc.TakeMeleeDamage(bmc.myCol, collision, stumble, false, false, dmg);

            string extra = HoldDown(npc, puppet, state);

            // Limb hits never kill in the game (health stops at 1). On the ground, they can knock out.
            if (down && KnockoutOnGround.Value && !npc.isDead && before - dmg * mult <= 0f)
            {
                if (npc.isEnemy && npc.pointsPos != null) game.AddMeleekill(npc.pointsPos.position);
                npc.KillNPC(null);
                extra += " - KNOCKED OUT";
            }

            if (Dbg)
                Log.Msg($"{npc.name}: {what} hit {bmc.myCol?.name} at {speed:0.0} m/s (min {min:0.0}, mass {rb.mass:0.##} kg), " +
                        $"{(down ? "on the ground" : npc.isGettingHit ? "stunned" : "standing")}, damage {dmg * mult:0.#}" +
                        $"{(stumble ? ", stumble" : "")}: health {before:0.#} -> {npc.health:0.#}{(npc.isDead ? " (dead)" : "")}{extra}");
        }

        // TakeMeleeDamage multiplies the damage by 5 for the head and 2 for the torso (checked in that order).
        static float PartMultiplier(ANBBasicNPC npc, Collider part)
        {
            if (part == null) return 1f;
            var go = part.gameObject;
            if (npc.CheckBodypart(go, "head")) return 5f;
            if (npc.CheckBodypart(go, "torso")) return 2f;
            return 1f;
        }

        // Damage to pass to TakeMeleeDamage so that after its multiplier it is at most MaxDamagePerHit.
        static float Capped(float dmg, float mult) =>
            MaxDamagePerHit.Value > 0f ? Mathf.Min(dmg, MaxDamagePerHit.Value / mult) : dmg;

        // ---- Keeping a downed enemy on the ground.
        // BehaviourPuppet.OnFixedUpdate gets up once unpinnedTimer passes getUpDelay and the body has
        // stopped moving. The game's getUpDelay is 0, so resetting the timer alone does nothing (0.1.0's
        // "stays down" was a no-op). The mod raises getUpDelay by KeepDownSeconds while it holds an
        // enemy, resets the timer on every hit, and puts the game's value back once the enemy is up,
        // dead, or gone.
        class Hold { public ANBBasicNPC Npc; public BehaviourPuppet Puppet; public float OrigDelay; }
        static readonly Dictionary<IntPtr, Hold> Holds = new();

        static string HoldDown(ANBBasicNPC npc, BehaviourPuppet puppet, BehaviourPuppet.State before)
        {
            if (puppet == null || npc.isDead) return "";
            string what;
            if (before == BehaviourPuppet.State.Unpinned && KeepDown.Value) what = " - held down";
            else if (before == BehaviourPuppet.State.GetUp && KnockDownGettingUp.Value)
            {
                puppet.SetState(BehaviourPuppet.State.Unpinned);
                what = " - knocked back down";
            }
            else return "";
            if (KeepDown.Value && KeepDownSeconds.Value > 0f)
            {
                IntPtr key = puppet.Pointer;
                if (!Holds.TryGetValue(key, out var h))
                    Holds[key] = h = new Hold { Npc = npc, Puppet = puppet, OrigDelay = puppet.getUpDelay };
                puppet.getUpDelay = h.OrigDelay + KeepDownSeconds.Value;
                what += $" for {KeepDownSeconds.Value:0.#} s";
            }
            puppet.unpinnedTimer = 0f;
            return what;
        }

        public override void OnUpdate()
        {
            if (Holds.Count == 0) return;
            List<IntPtr> done = null;
            foreach (var kv in Holds)
            {
                var h = kv.Value;
                try
                {
                    if (h.Puppet != null && h.Npc != null && !h.Npc.isDead && h.Puppet.state == BehaviourPuppet.State.Unpinned) continue;
                    if (h.Puppet != null) h.Puppet.getUpDelay = h.OrigDelay;
                    if (Dbg && h.Npc != null && !h.Npc.isDead) Log.Msg($"{h.Npc.name}: getting up");
                }
                catch (Exception ex) { Log.Warning($"hold: {ex.Message}"); }
                (done ??= new()).Add(kv.Key);
            }
            if (done != null) foreach (var k in done) Holds.Remove(k);
        }

        static void ReleaseAll()
        {
            foreach (var h in Holds.Values)
                try { if (h.Puppet != null) h.Puppet.getUpDelay = h.OrigDelay; } catch { }
            Holds.Clear();
        }

        // ---- TakeMeleeDamage prefix: scale the game's own bare-fist punch.
        internal static void OnTakeMeleeDamage(ANBBasicNPC npc, Collider bodyPart, bool fromEnemy, bool noDamage, ref float damageOverride)
        {
            if (!FistContext) return;
            FistContext = false;
            try
            {
                if (fromEnemy || noDamage || damageOverride > 0f) return;
                var game = ANBStaticGameManager.ANBmain;
                if (game == null) return;
                LastHit[npc.Pointer] = Time.time;          // the rest of this swing must not count again
                float mult = PartMultiplier(npc, bodyPart);
                damageOverride = Capped(game.meleeDamage * FistDamageMultiplier.Value, mult);
                if (Dbg) Log.Msg($"{npc.name}: {PunchWhat} (game path) on {bodyPart?.name} at {PunchSpeed:0.0} m/s, damage {game.meleeDamage * mult:0.#} -> {damageOverride * mult:0.#}, health {npc.health:0.#}");
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
                if (puppet == null) return;
                string what = HoldDown(npc, puppet, puppet.state);
                if (Dbg && what != "") Log.Msg($"{npc.name}: blunt weapon hit{what}, health {npc.health:0.#}");
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
        static void Prefix(ANBBasicNPC __instance, Collider bodyPart, bool fromEnemy, bool noDamage, ref float damageOverride) =>
            HeavyMeleeMod.OnTakeMeleeDamage(__instance, bodyPart, fromEnemy, noDamage, ref damageOverride);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.TakeBluntWeaponDamage))]
    internal static class BluntPatch
    {
        static void Postfix(ANBBasicNPC __instance) => HeavyMeleeMod.AfterBluntHit(__instance);
    }
}
