using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ThrowAssist.ThrowAssistMod), "Throw Assist", "0.2.1", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace ThrowAssist
{
    // Throw assist with real physics, and thrown pistols that hurt.
    //
    // The game's assist (ANBAssistedThrowingObject.StartAssistedThrow, wired to each item's release event) picks a
    // target when the game option assistedThrow is on and the throw is faster than assistedThrowAtVelocity, then runs
    // a coroutine that zeroes the velocity AND the spin and drags the item with Rigidbody.MovePosition every physics
    // step (built for knives). This mod lets the game pick the target, stops the coroutine and steers the item itself
    // with real velocity, so it keeps its spin and its collisions. (First built for Billy Clubs, which keeps its own
    // club flight: objects named "BillyClub-*" are left to it.)
    //
    // Pistols: every pistol prefab has an ANBAssistedThrowingObject, but the game ships all of them with
    // dontUse = true. PistolAssist switches it on for throws of at least PistolAssistMinSpeed.
    //
    // Pistol damage: a thrown pistol reaches ANBBodyMeleeCollisionManager.collisionEnter on the gun path:
    // meleeDamage (10, x2 torso, x5 head, only head/torso can kill), never a stagger (needs > 5 kg; a pistol is
    // 1.5), and nothing during the enemy's hit stun. With PistolDamage, a pistol in flight (or just landed) that hits
    // an enemy calls the game's TakeMeleeDamage itself: meleeDamage x PistolThrowDamage and a stagger, once per enemy
    // per throw. Heavy Melee only takes over HELD guns, so the two don't overlap.
    //
    // Other items (knives, katanas, bottles, pans, the crowbar): SteerOtherItems (off) steers them the same way.
    // Knives (anything with an ANBKnife) fly like the game's own knife assist: at least the item's own assist speed
    // (17 m/s for combat knives) and blade first. Each step the knife is turned so its stab line (HVRStabber,
    // base to tip) faces the flight direction, spin removed, so it lands point first and the stabber stabs.
    public class ThrowAssistMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> PistolAssist, PistolDamage, PistolStagger, SteerOtherItems, AimHead, DebugLog;
        internal static MelonPreferences_Entry<float> MinAssistSpeed, MaxSpeed, SearchDistance, MaxFlyDistance, PistolThrowDamage, KnifeSpeedMultiplier;
        internal static bool Dbg => DebugLog.Value;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("ThrowAssist", "Throw Assist");
            PistolAssist = c.CreateEntry("PistolAssist", true, description: "Thrown pistols home in on enemies. The game ships its pistol assist switched off. Needs the game's own assisted throw option on.");
            SteerOtherItems = c.CreateEntry("SteerOtherItems", true, description: "Use velocity steering for any item with the game's assisted-throw component, including knives and props. Billy Clubs keep their own assist. Existing saved preferences retain their value.");
            MinAssistSpeed = c.CreateEntry("MinAssistSpeed", 6f, description: "A pistol or other item thrown slower than this (m/s) gets no assist and just flies. Knives use the game's own threshold (3.5).");
            MaxSpeed = c.CreateEntry("MaxSpeed", 18f, description: "An assisted item flies at your own throw speed, up to this (m/s).");
            KnifeSpeedMultiplier = c.CreateEntry("KnifeSpeedMultiplier", 1f, description: "Knife flight speed relative to that knife's own game assist speed (normally 17 m/s). 1 matches the game; increase only if logged face impacts are too slow to stab.");
            SearchDistance = c.CreateEntry("SearchDistance", 15f, description: "How far the assist looks for a target (m). Pistols as shipped: 6.");
            MaxFlyDistance = c.CreateEntry("MaxFlyDistance", 20f, description: "How far an assisted throw flies before it gives up (m). Pistols as shipped: 4.");
            AimHead = c.CreateEntry("AimHead", false, description: "Assisted throws aim at the head. Off = the chest.");
            PistolDamage = c.CreateEntry("PistolDamage", true, description: "A thrown pistol that hits an enemy does real damage (PistolThrowDamage). Off = the game's weak hit.");
            PistolThrowDamage = c.CreateEntry("PistolThrowDamage", 3f, description: "Thrown pistol damage as a multiple of the game's melee damage (10). The game then doubles it on the torso and multiplies by 5 on the head: 3 = 60 to the body, a head hit kills.");
            PistolStagger = c.CreateEntry("PistolStagger", true, description: "A thrown pistol that hits a standing enemy makes them stumble.");
            DebugLog = c.CreateEntry("DebugLog", true, description: "Record release settings, target choice, knife impact and stab details, flight end, and pistol damage in MelonLoader/Latest.log. Disable after testing.");
            LoggerInstance.Msg($"loaded: debug={Dbg}, other items={SteerOtherItems.Value}, knife speed x{KnifeSpeedMultiplier.Value:0.##}");
        }

        public override void OnFixedUpdate()
        {
            try { Throws.Step(); }
            catch (Exception e) { Log.Warning($"step: {e.GetType().Name}: {e.Message}"); }
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }
    }

    internal static class Throws
    {
        static void W(string s) => ThrowAssistMod.Log.Msg(s);
        static bool Alive(Object o) => ThrowAssistMod.Alive(o);
        const float MinThrowSpeed = 2.5f;

        class Flight
        {
            public Rigidbody Rb;
            public ANBAssistedThrowingObject Ato;
            public HVRGrabbable Grab;
            public string Name;
            public bool Pistol;
            public ANBKnife Knife;
            public string Stab, EndInfo, Impact;
            public float LastTip = -1f;
            public float Start, Speed, SteerAt, Until, EndedAt = -1f;
            public Vector3 Dir;
            public ANBBasicNPC Target;
            public Transform AimT;
            public CollisionDetectionMode OldMode;
            public readonly List<IntPtr> Hit = new();
        }

        static readonly List<Flight> Flights = new();
        static Vector3 preV, preW;
        static bool preOurs;

        static bool Ours(ANBAssistedThrowingObject ato, out ANBHVRGunBase gun, out Rigidbody rb)
        {
            gun = null; rb = null;
            if (!Alive(ato)) return false;
            var go = ato.gameObject;
            if (go.name.StartsWith("BillyClub")) return false;       // Billy Clubs flies its own clubs
            gun = go.GetComponent<ANBHVRGunBase>();
            if (gun == null) gun = go.GetComponentInParent<ANBHVRGunBase>();
            if (gun == null && !ThrowAssistMod.SteerOtherItems.Value) return false;
            rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.GetComponentInParent<Rigidbody>();
            return rb != null;
        }

        internal static void Before(ANBAssistedThrowingObject ato)
        {
            preOurs = Ours(ato, out var gun, out var rb);
            if (!preOurs) return;
            preV = rb.linearVelocity; preW = rb.angularVelocity;
            // Knives keep the game's own threshold (assistedThrowAtVelocity, 3.5 m/s): the game assists them from there.
            bool knife = gun == null && IsKnife(ato);
            bool assist = (knife || preV.magnitude >= ThrowAssistMod.MinAssistSpeed.Value) && (gun == null || ThrowAssistMod.PistolAssist.Value);
            // A soft toss isn't a throw at anyone. Pistols are switched on here (the game ships them off).
            if (gun != null) ato.dontUse = !assist;
            else if (!assist) ato.dontUse = true;   // restored below; the game would still drag a slow throw
            if (assist)
            {
                ato.targetSearchDistanceOverride = ThrowAssistMod.SearchDistance.Value;
                ato.maxFlyDistanceOverride = ThrowAssistMod.MaxFlyDistance.Value;
            }
        }

        internal static void After(ANBAssistedThrowingObject ato)
        {
            if (!preOurs) return;
            preOurs = false;
            if (!Ours(ato, out var gun, out var rb)) return;
            if (gun == null && !IsKnife(ato) && preV.magnitude < ThrowAssistMod.MinAssistSpeed.Value) ato.dontUse = false;   // undo Before
            Transform target = null;
            if (ato.isHoming)
            {
                target = ato.homingTarget;
                ato.StopAllCoroutines();
                ato.isHoming = false;
                ato.homingTarget = null;
                rb.linearVelocity = preV;    // the coroutine's first step zeroed both
                rb.angularVelocity = preW;
            }
            float sp = preV.magnitude;
            bool pistol = gun != null;
            var knife = pistol ? null : ato.GetComponent<ANBKnife>() ?? ato.GetComponentInParent<ANBKnife>();
            if (sp < MinThrowSpeed || (!pistol && target == null))    // nothing to steer, no damage to track
            {
                if (ThrowAssistMod.Dbg) W($"{(pistol ? "pistol" : knife != null ? "knife" : "item")} '{ato.name}' released at {sp:0.0} m/s: {(pistol && !ThrowAssistMod.PistolAssist.Value ? "PistolAssist off" : NoAssist(sp, knife != null))}; game threshold {GameThreshold():0.0}, item speed {ato.speed:0.0}, dontUse {ato.dontUse}");
                return;
            }

            Flights.RemoveAll(p => !Alive(p.Rb) || p.Rb.Pointer == rb.Pointer);
            var f = new Flight
            {
                Rb = rb, Ato = ato, Grab = ato.GetComponent<HVRGrabbable>(), Name = pistol ? gun.name : ato.name, Pistol = pistol,
                Knife = knife,
                Start = Time.time, Speed = sp, Dir = preV / sp, OldMode = rb.collisionDetectionMode,
            };
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;   // fast and small: don't tunnel
            Flights.Add(f);

            string assist;
            if (target != null)
            {
                var npc = target.GetComponentInParent<ANBBasicNPC>();
                f.Target = npc; f.AimT = target;
                float min = ThrowAssistMod.MinAssistSpeed.Value;
                f.SteerAt = Mathf.Clamp(sp, min, Mathf.Max(ThrowAssistMod.MaxSpeed.Value, min));
                if (knife != null) f.SteerAt = Mathf.Max(f.SteerAt, ato.speed * Mathf.Max(0.1f, ThrowAssistMod.KnifeSpeedMultiplier.Value));
                f.Until = Time.time + ThrowAssistMod.MaxFlyDistance.Value / f.SteerAt + 0.3f;
                var to = (npc != null ? AimPoint(npc) : target.position) - rb.worldCenterOfMass;
                f.Dir = to.normalized; f.Speed = f.SteerAt;
                rb.linearVelocity = f.Dir * f.SteerAt;
                if (knife != null) BladeFirst(f);
                assist = $"{(npc != null ? "enemy" : "target")} '{(npc != null ? npc.name : target.name)}' {to.magnitude:0.0} m, steered at {f.SteerAt:0} m/s";
            }
            else assist = !ThrowAssistMod.PistolAssist.Value ? "none (PistolAssist off)" : NoAssist(sp, false);
            if (ThrowAssistMod.Dbg) W($"{(pistol ? "pistol" : knife != null ? "knife" : "item")} '{f.Name}' released at {sp:0.0} m/s, assist: {assist}; game threshold {GameThreshold():0.0}, item speed {ato.speed:0.0}, spin {preW.magnitude:0.0} rad/s, aim {(ThrowAssistMod.AimHead.Value ? "head" : "chest")}");
        }

        static float GameThreshold() => ANBStaticGameManager.ANBmain != null ? ANBStaticGameManager.ANBmain.assistedThrowAtVelocity : -1f;

        static bool IsKnife(ANBAssistedThrowingObject ato) =>
            ato.GetComponent<ANBKnife>() != null || ato.GetComponentInParent<ANBKnife>() != null;

        static string NoAssist(float sp, bool knife)
        {
            var game = ANBStaticGameManager.ANBmain;
            return !knife && sp < ThrowAssistMod.MinAssistSpeed.Value ? $"none ({sp:0.0} m/s is below MinAssistSpeed {ThrowAssistMod.MinAssistSpeed.Value:0.#})"
                : game != null && !game.assistedThrow ? "none (off in the game settings)"
                : game != null && sp < game.assistedThrowAtVelocity ? $"none ({sp:0.0} m/s is below the game's {game.assistedThrowAtVelocity:0.0})"
                : "none (no target in view)";
        }

        // ANBKnife.releaseKnife runs the game's assist only for a throw faster than assistedThrowAtVelocity, and
        // only if the knife allows it. A release that never reaches StartAssistedThrow is logged here, so every
        // knife throw leaves a line.
        static int releaseDepth;
        static bool reachedAssist;
        static float releaseSpeed;

        internal static void KnifeReleaseBefore(ANBKnife k)
        {
            if (releaseDepth++ > 0) return;
            reachedAssist = false;
            var rb = k.GetComponent<Rigidbody>() ?? k.GetComponentInParent<Rigidbody>();
            releaseSpeed = rb != null ? rb.linearVelocity.magnitude : 0f;
        }

        internal static void KnifeReleaseAfter(ANBKnife k)
        {
            if (--releaseDepth > 0) return;
            releaseDepth = 0;
            if (reachedAssist || !ThrowAssistMod.Dbg || releaseSpeed < MinThrowSpeed) return;   // a drop, not a throw
            var game = ANBStaticGameManager.ANBmain;
            string why = !k.allowAssistedThrow ? "not allowed on this knife"
                : game != null && !game.assistedThrow ? "off in the game settings"
                : game != null && releaseSpeed < game.assistedThrowAtVelocity ? $"{releaseSpeed:0.0} m/s is below the game's {game.assistedThrowAtVelocity:0.0}"
                : "the game didn't start it";
            W($"knife '{k.name}' released at {releaseSpeed:0.0} m/s, assist: none ({why}); game threshold {GameThreshold():0.0}, allowAssistedThrow {k.allowAssistedThrow}, other items {ThrowAssistMod.SteerOtherItems.Value}");
        }

        internal static void AssistReached() => reachedAssist = true;

        static Vector3 AimPoint(ANBBasicNPC npc)
        {
            var head = npc.aimAtHead;
            if (head != null) return ThrowAssistMod.AimHead.Value ? head.position : head.position - Vector3.up * 0.35f;
            return npc.transform.position + Vector3.up * 1.2f;
        }

        // Turn the knife so its stab line (base to tip) points along the flight, and stop its spin.
        static void BladeFirst(Flight f)
        {
            var st = f.Knife.Stabber;
            if (!Alive(st)) return;
            var line = st.StabLineWorld;
            if (line.sqrMagnitude < 1e-6f) return;
            f.Rb.rotation = Quaternion.FromToRotation(line, f.Dir) * f.Rb.rotation;
            f.Rb.angularVelocity = Vector3.zero;
        }

        static float TipOff(Flight f)
        {
            try
            {
                var st = f.Knife.Stabber;
                var v = f.Rb.linearVelocity;
                return Alive(st) && v.sqrMagnitude > 0.01f ? Vector3.Angle(st.StabLineWorld, v) : -1f;
            }
            catch { return -1f; }
        }

        // StabEnemyFinal prefix: remember what a steered knife stabbed.
        internal static void KnifeStab(ANBKnife k, Collider col)
        {
            if (k == null || Flights.Count == 0) return;
            foreach (var f in Flights)
                if (f.Knife != null && Alive(f.Knife) && f.Knife.Pointer == k.Pointer && f.Stab == null)
                {
                    bool zone = col != null && col.GetComponent<ANBEnemyDetect>() != null;
                    f.Stab = $"stabbed {(col != null ? col.name : "?")}{(zone ? "" : " (not a hit zone, no damage)")}";
                    return;
                }
        }

        static void End(Flight f, string reason)
        {
            if (f.EndedAt >= 0f) return;
            f.EndedAt = Time.time;
            f.Target = null; f.AimT = null;
            f.EndInfo = $"{reason}, {f.EndedAt - f.Start:0.00} s, last speed {f.Speed:0.0} m/s";
            if (f.Knife != null) f.EndInfo += $", tip {f.LastTip:0} deg off flight";
            if (!Alive(f.Rb)) return;
            f.Rb.collisionDetectionMode = f.OldMode;
            if (Alive(f.Ato)) { f.Ato.StopAllCoroutines(); f.Ato.homingTarget = null; }
        }

        internal static void Step()
        {
            for (int i = 0; i < Flights.Count; i++)
            {
                var f = Flights[i];
                if (!Alive(f.Rb)) { Flights.RemoveAt(i--); continue; }
                if (f.EndedAt >= 0f)
                {
                    if (Time.time - f.EndedAt > 0.3f)   // kept briefly for the damage and stab hooks
                    {
                        if (ThrowAssistMod.Dbg) W($"  {(f.Pistol ? "pistol" : f.Knife != null ? "knife" : "item")} '{f.Name}': {f.Stab ?? "no stab"}; {f.Impact ?? "no body collision observed"}; {f.EndInfo}");
                        Flights.RemoveAt(i--);
                    }
                    continue;
                }
                var rb = f.Rb;
                float age = Time.time - f.Start;
                bool held = false;
                try { held = Alive(f.Grab) && f.Grab.IsHandGrabbed; } catch { }
                if (held && age > 0.2f) { End(f, "grabbed"); continue; }
                var v = rb.linearVelocity;
                float sp = v.magnitude;
                // Impact: the velocity turns hard or loses a lot of speed in one step.
                if (age > 0.05f && sp > 0.01f && (Vector3.Angle(v, f.Dir) > 50f || sp < f.Speed * 0.6f)) { End(f, $"impact-like velocity change ({sp:0.0} m/s, turn {Vector3.Angle(v, f.Dir):0} deg)"); continue; }
                if (sp < 2f || age > 4f) { End(f, sp < 2f ? "slowed below 2 m/s" : "4 s timeout"); continue; }
                if (f.Target != null || f.AimT != null)
                {
                    if (Time.time > f.Until || (f.Target != null && (!Alive(f.Target) || f.Target.isDead)) || (f.Target == null && !Alive(f.AimT)))
                    { f.Target = null; f.AimT = null; }
                    else
                    {
                        var to = (f.Target != null ? AimPoint(f.Target) : f.AimT.position) - rb.worldCenterOfMass;
                        v = to.normalized * f.SteerAt;
                        rb.linearVelocity = v; sp = f.SteerAt;
                    }
                }
                if (sp > 0.01f) { f.Dir = v / sp; f.Speed = sp; }
                if (f.Knife != null && Alive(f.Knife)) { f.LastTip = TipOff(f); BladeFirst(f); }
            }
        }

        // collisionEnter prefix. false = the mod dealt the hit, skip the game's.
        internal static bool BodyHit(ANBBodyMeleeCollisionManager mgr, ANBBodyMeleeCollision bmc, Collision collision)
        {
            if (Flights.Count == 0 || collision == null || bmc == null) return true;
            var rb = collision.collider != null ? collision.collider.attachedRigidbody : null;
            if (rb == null) return true;
            if (ThrowAssistMod.Dbg)
                foreach (var p in Flights)
                    if (p.Knife != null && Alive(p.Rb) && p.Rb.Pointer == rb.Pointer && p.Impact == null)
                        p.Impact = $"body collision with '{(mgr.ANBNpc != null ? mgr.ANBNpc.name : "?")}' part '{(bmc.myCol != null ? bmc.myCol.name : "?")}' at {collision.relativeVelocity.magnitude:0.0} m/s, tip {TipOff(p):0} deg";
            if (!ThrowAssistMod.PistolDamage.Value) return true;
            Flight f = null;
            foreach (var p in Flights) if (p.Pistol && Alive(p.Rb) && p.Rb.Pointer == rb.Pointer) { f = p; break; }
            if (f == null) return true;
            var npc = mgr.ANBNpc;
            if (npc == null || npc.isDead || npc.NPCPaused || !npc.NPCStarted || npc.NoBodyCollisions || npc.isUnhittable) return true;
            var game = ANBStaticGameManager.ANBmain;
            if (game == null || !game.gameStarted) return true;
            if (f.Hit.Contains(npc.Pointer)) return false;    // one hit per enemy per throw (it touches several parts)
            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinThrowSpeed) return true;
            f.Hit.Add(npc.Pointer);

            var part = bmc.myCol;
            float mult = part == null ? 1f : npc.CheckBodypart(part.gameObject, "head") ? 5f : npc.CheckBodypart(part.gameObject, "torso") ? 2f : 1f;
            float dmg = game.meleeDamage * ThrowAssistMod.PistolThrowDamage.Value;
            bool stagger = ThrowAssistMod.PistolStagger.Value && !npc.isOffBalance;
            float before = npc.health;
            npc.TakeMeleeDamage(part, collision, stagger, false, false, dmg);
            End(f, "pistol hit");
            if (ThrowAssistMod.Dbg)
                W($"pistol '{f.Name}' hit '{npc.name}' {(Alive(part) ? part.name : "?")} at {speed:0.0} m/s: damage {dmg * mult:0}{(stagger ? ", stagger" : "")}, " +
                  $"health {before:0} -> {npc.health:0}{(npc.isDead ? " (down for good)" : "")}");
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBAssistedThrowingObject), nameof(ANBAssistedThrowingObject.StartAssistedThrow))]
    static class AssistPatch
    {
        static void Prefix(ANBAssistedThrowingObject __instance)
        {
            Throws.AssistReached();
            try { Throws.Before(__instance); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"before assist: {e.Message}"); }
        }

        static void Postfix(ANBAssistedThrowingObject __instance)
        {
            try { Throws.After(__instance); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"after assist: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch]
    static class KnifeReleasePatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in HarmonyLib.AccessTools.GetDeclaredMethods(typeof(ANBKnife)))
                if (m.Name == nameof(ANBKnife.releaseKnife)) yield return m;
        }

        static void Prefix(ANBKnife __instance)
        {
            try { Throws.KnifeReleaseBefore(__instance); } catch (Exception e) { ThrowAssistMod.Log.Warning($"knife release: {e.Message}"); }
        }

        static void Postfix(ANBKnife __instance)
        {
            try { Throws.KnifeReleaseAfter(__instance); } catch (Exception e) { ThrowAssistMod.Log.Warning($"knife release: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.StabEnemyFinal))]
    static class KnifeStabPatch
    {
        static void Prefix(Collider collider, ANBKnife knifeScript)
        {
            try { Throws.KnifeStab(knifeScript, collider); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBodyMeleeCollisionManager), nameof(ANBBodyMeleeCollisionManager.collisionEnter))]
    static class BodyHitPatch
    {
        static bool Prefix(ANBBodyMeleeCollisionManager __instance, ANBBodyMeleeCollision bmc, Collision collision)
        {
            try { return Throws.BodyHit(__instance, bmc, collision); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"body hit: {e.Message}"); return true; }
        }
    }
}
