using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ThrowAssist.ThrowAssistMod), "Throw Assist", "0.2.3", "Evgeeso")]
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
    // Other eligible items use the same steering when SteerOtherItems is on.
    // Knives (anything with an ANBKnife) fly like the game's own knife assist: at least the item's own assist speed
    // (17 m/s for combat knives) and blade first. Each step the knife is turned so its stab line (HVRStabber,
    // base to tip) faces the flight direction, spin removed, so it lands point first and the stabber stabs.
    // A knife is steered by its TIP, not its centre of mass: a katana's middle is ~0.5 m behind the point, so
    // steering the middle to the aim point drove the blade through the body first. Steering stops once the tip
    // is within SteerStopDistance of the aim point or has passed it; the item then flies straight. Before 0.2.3
    // it kept steering, reversed inside the body, BladeFirst spun the 1 m blade around in there, and the physics
    // depenetration threw it away at 30+ m/s with no stab and no body collision logged.
    public class ThrowAssistMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> PistolAssist, PistolDamage, PistolStagger, SteerOtherItems, AimHead, DebugLog, KnifeVerticalSpin, AllowKneeHit;
        internal static MelonPreferences_Entry<float> MinAssistSpeed, PistolAssistMinSpeed, PistolSteerMinSpeed, MaxSpeed, SearchDistance, MaxFlyDistance, PistolThrowDamage, KnifeSpeedMultiplier, HeadAimMaxAngle, KneeAimMaxAngle;
        internal static bool Dbg => DebugLog.Value;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("ThrowAssist", "Throw Assist");
            PistolAssist = c.CreateEntry("PistolAssist", true, description: "Thrown pistols home in on enemies. The game ships its pistol assist switched off. Needs the game's own assisted throw option on.");
            SteerOtherItems = c.CreateEntry("SteerOtherItems", true, description: "Use velocity steering for knives and other items with the game's assisted-throw component. Billy Clubs keep their own assist. Existing saved preferences retain their value.");
            MinAssistSpeed = c.CreateEntry("MinAssistSpeed", 3.5f, description: "A non-pistol prop thrown slower than this (m/s) gets no assist. Knives use the game's own threshold; pistols use PistolAssistMinSpeed.");
            PistolAssistMinSpeed = c.CreateEntry("PistolAssistMinSpeed", 3.5f, description: "Minimum pistol release speed (m/s) for assist, never below the game's own assisted-throw threshold. Soft throws above this can still be steered.");
            PistolSteerMinSpeed = c.CreateEntry("PistolSteerMinSpeed", 13f, description: "Minimum flight speed (m/s) for a pistol after assist finds a target. It does not change unassisted throws.");
            MaxSpeed = c.CreateEntry("MaxSpeed", 18f, description: "An assisted item flies at your own throw speed, up to this (m/s).");
            KnifeSpeedMultiplier = c.CreateEntry("KnifeSpeedMultiplier", 1f, description: "Knife flight speed relative to that knife's own game assist speed (normally 17 m/s). 1 matches the game; increase only if logged face impacts are too slow to stab.");
            KnifeVerticalSpin = c.CreateEntry("KnifeVerticalSpin", true, description: "On knife throws with no assist target, turn the release spin into an end-over-end tumble in the throw's vertical plane. Assisted knives stay blade first. Off keeps the hand's original spin.");
            SearchDistance = c.CreateEntry("SearchDistance", 15f, description: "How far the assist looks for a target (m). Pistols as shipped: 6.");
            MaxFlyDistance = c.CreateEntry("MaxFlyDistance", 20f, description: "How far an assisted throw flies before it gives up (m). Pistols as shipped: 4.");
            AimHead = c.CreateEntry("AimHead", true, description: "Allow head aim when the release direction points closest to the head. False disables head selection; optional knee selection still applies. Existing saved preferences retain their value.");
            AllowKneeHit = c.CreateEntry("allowKneeHit", false, description: "Allow knee assist and make actual thrown-item leg hits trigger the knee-shot animation. Knee Shot Stun extends the kneel when installed. Billy Clubs keep their own settings.");
            KneeAimMaxAngle = c.CreateEntry("KneeAimMaxAngle", 25f, description: "Maximum angle in degrees from the release direction to a knee. The knee must also be closer to the release direction than the chest and any eligible head target. Requires allowKneeHit.");
            HeadAimMaxAngle = c.CreateEntry("HeadAimMaxAngle", 25f, description: "Maximum angle in degrees between the release direction and the head for a head-seeking throw. The release must also point closer to the head than the chest. 25 balances challenge and reward; lower for stricter aim.");
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
        const float SteerStopDistance = 0.3f;   // stop steering this close to the aim point (m)

        internal enum AimRegion { Chest, Head, Knee }

        internal class Flight
        {
            public Rigidbody Rb;
            public ANBAssistedThrowingObject Ato;
            public HVRGrabbable Grab;
            public string Name;
            public bool Pistol;
            public ANBKnife Knife;
            public AimRegion Aim;
            public Collider Knee;
            public string Stab, EndInfo, Impact;
            public float LastTip = -1f;
            public float Start, Speed, SteerAt, Until, EndedAt = -1f;
            public float Reach;                         // knife: centre of mass to tip along the stab line (m)
            public float Closest = -1f, TopSpeed;       // closest steered approach to the aim point; top physics speed
            public string SteerEnd;
            public Vector3 Dir;
            public ANBBasicNPC Target;
            public Transform AimT;
            public CollisionDetectionMode OldMode;
            public readonly List<IntPtr> Hit = new();
            public readonly List<IntPtr> KneeHits = new();
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
            float threshold = gun != null ? Mathf.Max(GameThreshold(), ThrowAssistMod.PistolAssistMinSpeed.Value) : ThrowAssistMod.MinAssistSpeed.Value;
            bool assist = (knife || preV.magnitude >= threshold) && (gun == null || ThrowAssistMod.PistolAssist.Value);
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
            if (sp < MinThrowSpeed || (!pistol && knife == null && target == null && !ThrowAssistMod.AllowKneeHit.Value))
            {
                if (ThrowAssistMod.Dbg) W($"{(pistol ? "pistol" : knife != null ? "knife" : "item")} '{ato.name}' released at {sp:0.0} m/s: {(pistol && !ThrowAssistMod.PistolAssist.Value ? "PistolAssist off" : NoAssist(sp, knife != null, pistol))}; game threshold {GameThreshold():0.0}, item speed {ato.speed:0.0}, dontUse {ato.dontUse}");
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
            if (knife != null && target == null) VerticalSpin(f);

            string assist;
            if (target != null)
            {
                var npc = target.GetComponentInParent<ANBBasicNPC>();
                f.Target = npc; f.AimT = target;
                if (npc != null) ChooseAim(f, npc, rb.worldCenterOfMass, f.Dir);
                float min = pistol ? ThrowAssistMod.PistolSteerMinSpeed.Value : ThrowAssistMod.MinAssistSpeed.Value;
                f.SteerAt = Mathf.Clamp(sp, min, Mathf.Max(ThrowAssistMod.MaxSpeed.Value, min));
                if (knife != null) f.SteerAt = Mathf.Max(f.SteerAt, ato.speed * Mathf.Max(0.1f, ThrowAssistMod.KnifeSpeedMultiplier.Value));
                f.Until = Time.time + ThrowAssistMod.MaxFlyDistance.Value / f.SteerAt + 0.3f;
                if (knife != null) f.Reach = TipReach(knife, rb);
                var to = (npc != null ? AimPoint(npc, f) : target.position) - Lead(f);
                f.Dir = to.normalized; f.Speed = f.SteerAt;
                rb.linearVelocity = f.Dir * f.SteerAt;
                if (knife != null) BladeFirst(f);
                assist = $"{(npc != null ? "enemy" : "target")} '{(npc != null ? npc.name : target.name)}' {to.magnitude:0.0} m, steered at {f.SteerAt:0} m/s{(f.Reach > 0f ? $" by the tip ({f.Reach:0.00} m ahead)" : "")}";
            }
            else assist = pistol && !ThrowAssistMod.PistolAssist.Value ? "none (PistolAssist off)" : NoAssist(sp, knife != null, pistol);
            if (ThrowAssistMod.Dbg) W($"{(pistol ? "pistol" : knife != null ? "knife" : "item")} '{f.Name}' released at {sp:0.0} m/s, assist: {assist}; game threshold {GameThreshold():0.0}, item speed {ato.speed:0.0}, spin {preW.magnitude:0.0} rad/s, aim {f.Aim}");
        }

        static float GameThreshold() => ANBStaticGameManager.ANBmain != null ? ANBStaticGameManager.ANBmain.assistedThrowAtVelocity : -1f;

        // Keep the release's spin strength, but use the axis perpendicular to the horizontal throw direction.
        // This produces an end-over-end tumble rather than a flat helicopter spin. Apply once at release so
        // collisions and angular drag remain physical; assisted knives are aligned blade first instead.
        static void VerticalSpin(Flight f)
        {
            if (!ThrowAssistMod.KnifeVerticalSpin.Value || !Alive(f.Rb)) return;
            var horizontal = Vector3.ProjectOnPlane(f.Dir, Vector3.up);
            if (horizontal.sqrMagnitude < 0.01f) return;
            var axis = Vector3.Cross(Vector3.up, horizontal.normalized);
            var old = f.Rb.angularVelocity;
            float amount = Mathf.Min(old.magnitude, f.Rb.maxAngularVelocity);
            if (amount < 0.1f) return;
            f.Rb.angularVelocity = axis * (Vector3.Dot(old, axis) < 0f ? -amount : amount);
            if (ThrowAssistMod.Dbg) W($"  knife '{f.Name}' free spin: {old.magnitude:0.0} rad/s -> vertical tumble {amount:0.0} rad/s");
        }

        static void TrackFreeKnife(ANBKnife knife, Rigidbody rb)
        {
            if (!Alive(rb) || rb.linearVelocity.magnitude < MinThrowSpeed) return;
            Flights.RemoveAll(p => !Alive(p.Rb) || p.Rb.Pointer == rb.Pointer);
            float sp = rb.linearVelocity.magnitude;
            var f = new Flight { Rb = rb, Knife = knife, Grab = knife.GetComponent<HVRGrabbable>(), Name = knife.name,
                Start = Time.time, Speed = sp, Dir = rb.linearVelocity / sp, OldMode = rb.collisionDetectionMode };
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Flights.Add(f);
            VerticalSpin(f);
        }

        static bool IsKnife(ANBAssistedThrowingObject ato) =>
            ato.GetComponent<ANBKnife>() != null || ato.GetComponentInParent<ANBKnife>() != null;

        static string NoAssist(float sp, bool knife, bool pistol)
        {
            var game = ANBStaticGameManager.ANBmain;
            float minimum = pistol ? ThrowAssistMod.PistolAssistMinSpeed.Value : ThrowAssistMod.MinAssistSpeed.Value;
            string setting = pistol ? "PistolAssistMinSpeed" : "MinAssistSpeed";
            return !knife && sp < minimum ? $"none ({sp:0.0} m/s is below {setting} {minimum:0.#})"
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
            if (!reachedAssist)
                TrackFreeKnife(k, k.GetComponent<Rigidbody>() ?? k.GetComponentInParent<Rigidbody>());
            if (reachedAssist || !ThrowAssistMod.Dbg || releaseSpeed < MinThrowSpeed) return;   // a drop, not a throw
            var game = ANBStaticGameManager.ANBmain;
            string why = !k.allowAssistedThrow ? "not allowed on this knife"
                : game != null && !game.assistedThrow ? "off in the game settings"
                : game != null && releaseSpeed < game.assistedThrowAtVelocity ? $"{releaseSpeed:0.0} m/s is below the game's {game.assistedThrowAtVelocity:0.0}"
                : "the game didn't start it";
            W($"knife '{k.name}' released at {releaseSpeed:0.0} m/s, assist: none ({why}); game threshold {GameThreshold():0.0}, allowAssistedThrow {k.allowAssistedThrow}, other items {ThrowAssistMod.SteerOtherItems.Value}");
        }

        internal static void AssistReached() => reachedAssist = true;

        static void ChooseAim(Flight f, ANBBasicNPC npc, Vector3 from, Vector3 releaseDir)
        {
            var head = npc.aimAtHead;
            float chestAngle = Vector3.Angle(releaseDir, AimPoint(npc, f) - from);
            float headAngle = Alive(head) ? Vector3.Angle(releaseDir, head.position - from) : 180f;
            float best = chestAngle, kneeAngle = 180f;
            if (Alive(head) && ThrowAssistMod.AimHead.Value && headAngle < best && headAngle <= Mathf.Max(0f, ThrowAssistMod.HeadAimMaxAngle.Value))
            { f.Aim = AimRegion.Head; best = headAngle; }
            if (ThrowAssistMod.AllowKneeHit.Value && !npc.isOffBalance && !npc.gettingHitLegs)
                foreach (var col in npc.GetComponentsInChildren<Collider>())
                {
                    if (!col.enabled || col.isTrigger || !IsLeg(col)) continue;
                    float angle = Vector3.Angle(releaseDir, col.bounds.center - from);
                    kneeAngle = Mathf.Min(kneeAngle, angle);
                    if (angle < best && angle <= Mathf.Max(0f, ThrowAssistMod.KneeAimMaxAngle.Value))
                    { f.Aim = AimRegion.Knee; f.Knee = col; best = angle; }
                }
            if (ThrowAssistMod.Dbg) W($"  '{f.Name}' aim check: head {headAngle:0.0} deg, chest {chestAngle:0.0} deg, knee {kneeAngle:0.0} deg; head limit {ThrowAssistMod.HeadAimMaxAngle.Value:0.0}, knee limit {ThrowAssistMod.KneeAimMaxAngle.Value:0.0} -> {f.Aim}");
        }

        static bool IsLeg(Collider col) => Alive(col) && (col.name == "RightLeg" || col.name == "LeftLeg");

        static Vector3 AimPoint(ANBBasicNPC npc, Flight f)
        {
            if (f.Aim == AimRegion.Knee && Alive(f.Knee) && f.Knee.enabled) return f.Knee.bounds.center;
            var head = npc.aimAtHead;
            if (head != null) return f.Aim == AimRegion.Head ? head.position : head.position - Vector3.up * 0.35f;
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

        // Distance from the centre of mass to the knife's tip (ANBKnife.StabOrient), along the stab line.
        static float TipReach(ANBKnife knife, Rigidbody rb)
        {
            try
            {
                var tip = knife.StabOrient; var st = knife.Stabber;
                if (!Alive(tip) || !Alive(st)) return 0f;
                var line = st.StabLineWorld;
                if (line.sqrMagnitude < 1e-6f) return 0f;
                return Mathf.Max(0f, Vector3.Dot(tip.position - rb.worldCenterOfMass, line.normalized));
            }
            catch { return 0f; }
        }

        // The point steered onto the aim point: the tip of a blade-first knife, else the centre of mass.
        static Vector3 Lead(Flight f) => f.Rb.worldCenterOfMass + f.Dir * f.Reach;

        static void StopSteer(Flight f, string reason, float age)
        {
            f.Target = null; f.AimT = null;
            f.SteerEnd = $"{reason} at {age:0.00} s";
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

        // Scope the reaction to a real thrown-item leg contact. GetHit runs inside the game's
        // damage paths; setting the leg flag before it also lets Knee Shot Stun register the hit.
        internal class KneeImpact
        {
            internal KneeImpact Previous;
            internal Flight Flight;
            internal ANBBasicNPC Npc;
            internal string Leg;
            internal bool GotHit;
        }

        static KneeImpact CurrentKnee;

        static KneeImpact BeginKnee(Flight f, ANBBasicNPC npc, Collider part, float speed, bool legZone = false)
        {
            if (!ThrowAssistMod.AllowKneeHit.Value || f == null || (!legZone && !IsLeg(part)) || speed < MinThrowSpeed) return null;
            if (!Alive(npc) || npc.isDead || npc.NPCPaused || !npc.NPCStarted || npc.NoBodyCollisions || npc.isUnhittable
                || npc.isOffBalance || npc.gettingHitLegs || f.KneeHits.Contains(npc.Pointer)) return null;
            if (Alive(f.Grab) && f.Grab.IsHandGrabbed) return null;
            var game = ANBStaticGameManager.ANBmain;
            if (game == null || !game.gameStarted || npc.animator == null || npc.animator.GetLayerIndex("Hit") < 0) return null;
            f.KneeHits.Add(npc.Pointer); // body contact and stab may both report this same impact
            var hit = new KneeImpact { Previous = CurrentKnee, Flight = f, Npc = npc, Leg = part.name };
            CurrentKnee = hit;
            return hit;
        }

        internal static KneeImpact BeginBodyKnee(ANBBodyMeleeCollisionManager mgr, ANBBodyMeleeCollision bmc, Collision collision)
        {
            if (!ThrowAssistMod.AllowKneeHit.Value || collision == null || bmc == null) return null;
            var rb = collision.collider != null ? collision.collider.attachedRigidbody : null;
            if (!Alive(rb)) return null;
            foreach (var f in Flights)
                if (Alive(f.Rb) && f.Rb.Pointer == rb.Pointer)
                    return BeginKnee(f, mgr.ANBNpc, bmc.myCol, collision.relativeVelocity.magnitude);
            return null;
        }

        internal static KneeImpact BeginKnifeKnee(ANBKnife knife, Collider part)
        {
            if (!ThrowAssistMod.AllowKneeHit.Value || !Alive(knife) || !Alive(part)) return null;
            var zone = part.GetComponent<ANBEnemyDetect>();
            if (zone == null || (!zone.leftLeg && !zone.rightLeg)) return null;
            foreach (var f in Flights)
                if (Alive(f.Knife) && f.Knife.Pointer == knife.Pointer)
                {
                    var hit = BeginKnee(f, zone.NPCscript, part, f.Speed, true);
                    if (hit != null) hit.Leg = zone.rightLeg ? "RightLeg" : "LeftLeg";
                    return hit;
                }
            return null;
        }

        internal static void BeforeKneeGetHit(ANBBasicNPC npc, ref bool isRunning)
        {
            var hit = CurrentKnee;
            if (hit == null || npc.Pointer != hit.Npc.Pointer) return;
            npc.gettingHitLegs = true;
            isRunning = false; // same stumble suppression as Billy Clubs
            hit.GotHit = true;
        }

        internal static void FinishKnee(KneeImpact hit, bool succeeded)
        {
            if (hit == null) return;
            try
            {
                var npc = hit.Npc;
                if (!succeeded || !Alive(npc) || npc.isDead) return;
                npc.gettingHitLegs = true;
                if (!hit.GotHit) npc.GetHit(false, npc.stunAtHitTimeLegs, false, false);
                // Melee does not crossfade to the knee-shot clip itself. Preserve the game's
                // damage, then start the same animation Billy Clubs uses, once per throw/NPC.
                var anim = npc.animator;
                if (anim == null) return;
                int layer = anim.GetLayerIndex("Hit");
                if (layer < 0) return;
                anim.CrossFadeInFixedTime(hit.Leg.StartsWith("Right") ? "hit_legs_1_m" : "hit_legs_1", 0.1f, layer, 0f);
                End(hit.Flight, "leg hit, kneel");
                if (ThrowAssistMod.Dbg) W($"'{hit.Flight.Name}' hit '{npc.name}' {hit.Leg}: KNEELS");
            }
            finally { CurrentKnee = hit.Previous; }
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
            f.EndInfo += $", top {f.TopSpeed:0.0} m/s";
            if (f.SteerAt > 0f) f.EndInfo += $"; steering: {f.SteerEnd ?? "ran to the end"}, closest {f.Closest:0.00} m";
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
                if (sp > f.TopSpeed) f.TopSpeed = sp;
                // Impact: the velocity turns hard or loses a lot of speed in one step.
                if (age > 0.05f && sp > 0.01f && (Vector3.Angle(v, f.Dir) > 50f || sp < f.Speed * 0.6f)) { End(f, $"impact-like velocity change ({sp:0.0} m/s, turn {Vector3.Angle(v, f.Dir):0} deg)"); continue; }
                if (sp < 2f || age > 4f) { End(f, sp < 2f ? "slowed below 2 m/s" : "4 s timeout"); continue; }
                if (f.Target != null || f.AimT != null)
                {
                    string stop = Time.time > f.Until ? "max fly time"
                        : f.Target != null && (!Alive(f.Target) || f.Target.isDead) ? "target down"
                        : f.Target == null && !Alive(f.AimT) ? "target gone" : null;
                    var to = Vector3.zero;
                    if (stop == null)
                    {
                        to = (f.Target != null ? AimPoint(f.Target, f) : f.AimT.position) - Lead(f);
                        float dist = to.magnitude;
                        if (f.Closest < 0f || dist < f.Closest) f.Closest = dist;
                        // Never turn back toward a point already passed: that reversal happens inside the body.
                        stop = dist < SteerStopDistance ? "reached the aim point" : Vector3.Dot(to, f.Dir) <= 0f ? "passed the aim point" : null;
                    }
                    if (stop != null) StopSteer(f, stop, age);
                    else
                    {
                        v = to.normalized * f.SteerAt;
                        rb.linearVelocity = v; sp = f.SteerAt;
                    }
                }
                if (sp > 0.01f) { f.Dir = v / sp; f.Speed = sp; }
                if (f.Knife != null && Alive(f.Knife)) { f.LastTip = TipOff(f); if (f.SteerAt > 0f && (f.Target != null || f.AimT != null)) BladeFirst(f); }
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
            bool stagger = ThrowAssistMod.PistolStagger.Value && !npc.isOffBalance
                && !(ThrowAssistMod.AllowKneeHit.Value && IsLeg(part));
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
        static void Prefix(Collider collider, ANBKnife knifeScript, out Throws.KneeImpact __state)
        {
            __state = null;
            try { __state = Throws.BeginKnifeKnee(knifeScript, collider); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"knife knee: {e.Message}"); }
            try { Throws.KnifeStab(knifeScript, collider); } catch { }
        }

        static void Finalizer(Throws.KneeImpact __state, Exception __exception)
        {
            try { Throws.FinishKnee(__state, __exception == null); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"knife kneel: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.GetHit))]
    static class KneeGetHitPatch
    {
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
        static void Prefix(ANBBasicNPC __instance, ref bool isRunning) => Throws.BeforeKneeGetHit(__instance, ref isRunning);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBodyMeleeCollisionManager), nameof(ANBBodyMeleeCollisionManager.collisionEnter))]
    static class BodyHitPatch
    {
        static bool Prefix(ANBBodyMeleeCollisionManager __instance, ANBBodyMeleeCollision bmc, Collision collision, out Throws.KneeImpact __state)
        {
            __state = null;
            try { __state = Throws.BeginBodyKnee(__instance, bmc, collision); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"body knee: {e.Message}"); }
            try { return Throws.BodyHit(__instance, bmc, collision); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"body hit: {e.Message}"); return true; }
        }

        static void Finalizer(Throws.KneeImpact __state, Exception __exception)
        {
            try { Throws.FinishKnee(__state, __exception == null); }
            catch (Exception e) { ThrowAssistMod.Log.Warning($"body kneel: {e.Message}"); }
        }
    }
}
