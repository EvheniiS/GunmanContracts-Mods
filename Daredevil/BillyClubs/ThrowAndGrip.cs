using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    // Thrown clubs: flight orientation (tip first, flat spin, end-over-end spin), ricochet to the next enemy
    // in sight after hitting an enemy or a wall, and in-game grip tuning keys.
    //
    // The game's throw assist (ANBAssistedThrowingObject.StartAssistedThrow, on release) picks a target when the
    // game setting assistedThrow is on and the throw is faster than assistedThrowAtVelocity, then runs a coroutine
    // that zeroes the velocity AND the spin and drags the object with Rigidbody.MovePosition every physics step
    // (built for knives). On a club that looked janky, killed the spin and pushed through collisions. The mod lets
    // the game choose the target, stops its coroutine and steers the club itself with real velocity, keeping the
    // spin and the collisions. An impact is a sudden turn or speed drop of the velocity.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<string> ThrowStyle;
        internal static MelonPreferences_Entry<float> HandThrowBoost, TipTurnTime, SpinSpeed, GripSlide, GripTiltA, GripTiltB, RicochetRange, FloorShotAngle, ThrowAssistMaxTurnAngle;
        internal static MelonPreferences_Entry<int> Ricochets;
        internal static MelonPreferences_Entry<bool> RicochetAimHead, RicochetFromEnemy, DirectThrowAssist;

        static readonly string[] Styles = { "TipFirst", "Natural", "SpinEnd" };
        const float MinThrowSpeed = 2.5f;
        const float TipMaxTurn = 60f; // rad/s: TipFirst turns 120 deg in about 0.05 s

        internal class Flight
        {
            public float Start, Speed, TargetUntil;
            public Vector3 Dir;
            public int Bounces;
            public ANBBasicNPC Target;
            public readonly List<IntPtr> Hit = new();
            public bool Oriented, Ours; // Ours = the mod is steering (after a ricochet)
            public CollisionDetectionMode OldMode;
            public bool FloorShot;
            public bool HeadAim;
            public float PrevVy;
            public float TipAtRelease, TipLast; // degrees between the club's tip and the flight line (diagnostic)
            public Transform AimT;               // the game's assist target (an enemy bone or a non-enemy target)
            public float OldMaxSpin;
            public string Assist = "none";
            public Follow Follow;
            public ANBBasicNPC EnemyHit; // set by the damage hook: the collision itself says which enemy was hit
            public float SteerAt; // speed the mod steers at: the throw's own speed, within MinSteerSpeed..ThrowSpeed
        }

        // The second after a flight ends against an enemy: a 20 m/s club ends up inside the enemy's colliders
        // and depenetration + animation spun it wildly (0.5.1 test). Spin and speed are capped, and the peak logged.
        internal class Settle
        {
            public float Until, PeakSpin, PeakSpeed, OldDepen;
            public int Clamps;
            public string What;
        }

        const float SettleTime = 1f, SettleMaxSpin = 12f, SettleMaxSpeed = 6f, SettleDepen = 2f;

        static void InitThrowPrefs(MelonPreferences_Category c)
        {
            ThrowStyle = c.CreateEntry("ThrowStyle", "TipFirst", description: "How a thrown club flies: TipFirst (turns from upright to metal tip forward, then holds it), Natural (keeps whatever spin your hand gave it) or SpinEnd (spinning end over end). Ctrl+T cycles them in game.");
            if (Array.IndexOf(Styles, ThrowStyle.Value) < 0) ThrowStyle.Value = ThrowStyle.Value == "Game" ? "Natural" : "TipFirst";
            RicochetFromEnemy = c.CreateEntry("RicochetFromEnemy", false, description: "A club that hits an enemy bounces on to the next enemy. Off = only walls and the floor send it on (the enemy it then hits stops it).");
            DirectThrowAssist = c.CreateEntry("DirectThrowAssist", true, description: "Guide a direct club throw to an enemy selected by the game's assisted throw. Turn off for free throws at a wall or past an enemy; ricochets have their own setting.");
            ThrowAssistMaxTurnAngle = c.CreateEntry("ThrowAssistMaxTurnAngle", 25f, description: "Maximum angle in degrees the club can turn from its release direction to a direct assist target. A throw farther off line stays free. 0 requires nearly exact aim; 180 allows every game-selected target.");
            SpinSpeed = c.CreateEntry("SpinSpeed", 5f, description: "Turns per second for SpinEnd.");
            HandThrowBoost = c.CreateEntry("HandThrowBoost", 1.3f, description: "Multiplies the club's release velocity before direct assist chooses its 13-18 m/s steering speed. Also affects free throws and the follow-through hand-speed comparison. 1 = unscaled release speed.");
            TipTurnTime = c.CreateEntry("TipTurnTime", 0.04f, description: "TipFirst: how quickly the club turns its metal tip to the front, in seconds (time constant: about 90% there after twice this). Smaller = snappier.");
            Ricochets = c.CreateEntry("Ricochets", 2, description: "How many times a thrown club bounces on to the next enemy in sight after hitting an enemy or a wall. 0 = off.");
            RicochetRange = c.CreateEntry("RicochetRange", 12f, description: "How far (m) a ricochet looks for the next enemy.");
            FloorShotAngle = c.CreateEntry("FloorShotAngle", 15f, description: "Throw at least this many degrees below horizontal and it is a floor shot: the game's homing is skipped, the club hits the floor and ricochets off it to the nearest enemy in sight. 0 = off (a club that happens to hit the floor still ricochets).");
            RicochetAimHead = c.CreateEntry("RicochetAimHead", false, description: "Ricochets aim at the head (a fast club hit to the head is a kill). Off = the chest. A normal assisted throw follows Throw Assist's AimHead.");
            GripSlide = c.CreateEntry("GripSlide", 0f, description: "Moves the club in the hand along its length, in metres. + = the hand sits closer to the grip end. In game: Ctrl+J / Ctrl+L.");
            GripTiltA = c.CreateEntry("GripTiltA", 0f, description: "Tilts the club in the hand, in degrees (first axis). In game: Ctrl+K / Ctrl+I.");
            GripTiltB = c.CreateEntry("GripTiltB", 0f, description: "Tilts the club in the hand, in degrees (second axis). In game: Ctrl+[ / Ctrl+]. Ctrl+R resets the grip.");
        }

        // ---------- grip ----------

        static Vector3 GripPoint, GripPerpA, GripPerpB;
        static Vector3 ClubAxis = Vector3.right, ClubCenter;

        // Called once the template is built: GripPoint = the crowbar's main grab point, in the club's space.
        static void InitGrip(Vector3 gripPoint, Vector3 gripForward)
        {
            GripPoint = gripPoint;
            var a = Vector3.ProjectOnPlane(gripForward, TemplateAxis);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.ProjectOnPlane(Vector3.up, TemplateAxis);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.ProjectOnPlane(Vector3.forward, TemplateAxis);
            GripPerpA = a.normalized;
            GripPerpB = Vector3.Cross(TemplateAxis, GripPerpA).normalized;
            RecomputeGrip();
        }

        static Quaternion GripRot => Quaternion.AngleAxis(GripTiltA.Value, GripPerpA) * Quaternion.AngleAxis(GripTiltB.Value, GripPerpB);

        static void RecomputeGrip()
        {
            var r = GripRot;
            ClubAxis = r * TemplateAxis;
            ClubCenter = GripPoint + r * (TemplateCenter - GripPoint) + ClubAxis * GripSlide.Value;
        }

        // Moves everything except the grab points (so the hand stays where it is and the club moves in it).
        static void ApplyGrip(GameObject club)
        {
            if (!Alive(club) || !Alive(Template)) return;
            var r = GripRot;
            var root = club.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                var ch = root.GetChild(i);
                if (ch.name == "Base - Grab") continue;
                var tch = Template.transform.Find(ch.name);
                if (tch == null) continue;
                ch.localPosition = GripPoint + r * (tch.localPosition - GripPoint) + ClubAxis * GripSlide.Value;
                ch.localRotation = r * tch.localRotation;
            }
        }

        static string GripInfo()
        {
            float fromEnd = Vector3.Dot(GripPoint - (ClubCenter - ClubAxis * Length.Value * 0.5f), ClubAxis);
            return $"grip: hand {fromEnd * 100f:0} cm from the grip end, tilt A {GripTiltA.Value:0}, tilt B {GripTiltB.Value:0} deg (slide {GripSlide.Value * 100f:0} cm)";
        }

        static void TuningKeys()
        {
            Keyboard kb;
            try { kb = Keyboard.current; } catch { return; }
            if (kb == null) return;
            // All tuning keys need Ctrl, and no arrows (the game uses them to move the player): J/L slide the grip, K/I and [ / ] tilt it, R resets it,
            // T cycles the throw style.
            bool ctrl;
            try { ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed; } catch { return; }
            if (!ctrl) return;
            bool changed = false;
            if (Pressed(kb.jKey)) { GripSlide.Value -= 0.01f; changed = true; }
            if (Pressed(kb.lKey)) { GripSlide.Value += 0.01f; changed = true; }
            if (Pressed(kb.kKey)) { GripTiltA.Value -= 5f; changed = true; }
            if (Pressed(kb.iKey)) { GripTiltA.Value += 5f; changed = true; }
            if (Pressed(kb.leftBracketKey)) { GripTiltB.Value -= 5f; changed = true; }
            if (Pressed(kb.rightBracketKey)) { GripTiltB.Value += 5f; changed = true; }
            if (Pressed(kb.rKey)) { GripSlide.Value = 0f; GripTiltA.Value = 0f; GripTiltB.Value = 0f; changed = true; }
            if (changed)
            {
                RecomputeGrip();
                foreach (var k in Clubs) ApplyGrip(k.Go);
                Cat.SaveToFile(false);
                Log.Msg(GripInfo());
            }
            if (Pressed(kb.tKey))
            {
                int i = Array.IndexOf(Styles, ThrowStyle.Value);
                ThrowStyle.Value = Styles[(i + 1) % Styles.Length];
                Cat.SaveToFile(false);
                Log.Msg($"throw style: {ThrowStyle.Value}");
            }
        }

        static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl k)
        {
            try { return k != null && k.wasPressedThisFrame; } catch { return false; }
        }

        // ---------- flight ----------

        static void StartFlight(Club k)
        {
            if (!Alive(k.Rb)) return;
            var v = k.Rb.linearVelocity;
            float sp = v.magnitude;
            if (sp < MinThrowSpeed) return;
            if (k.Fly != null && Time.time - k.Fly.Start < 0.3f) return; // already started by the throw assist hook
            var f = k.Fly = new Flight { Start = Time.time, Speed = sp, Dir = v / sp, PrevVy = v.y, OldMode = k.Rb.collisionDetectionMode, OldMaxSpin = k.Rb.maxAngularVelocity };
            // The rigidbody's spin limit was 30 rad/s (every spin throw topped out at 4.8 turns/s).
            k.Rb.maxAngularVelocity = Mathf.Max(f.OldMaxSpin, SpinSpeed.Value * 2f * Mathf.PI + 10f, TipMaxTurn + 5f);
            if (HandThrowBoost.Value > 0f && !Mathf.Approximately(HandThrowBoost.Value, 1f))
            {
                v *= HandThrowBoost.Value; sp *= HandThrowBoost.Value;
                k.Rb.linearVelocity = v; f.Speed = sp;
            }
            // At 20 m/s the club moves ~20 cm per physics step: sweep it so it can't tunnel into a wall or an enemy.
            k.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // Floor shot: aimed down at the floor. The game's homing would pull it up to the target before it lands.
            float down = Mathf.Asin(Mathf.Clamp(-f.Dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (FloorShotAngle.Value > 0f && Ricochets.Value > 0 && down >= FloorShotAngle.Value)
            {
                f.FloorShot = true;
                k.Rb.useGravity = true;
            }
            f.TipAtRelease = f.TipLast = TipAngle(k, f.Dir);
            StartFollow(k, f);
            if (Dbg) Log.Msg($"club thrown at {sp:0.0} m/s ({ThrowStyle.Value}, tip {f.TipAtRelease:0} deg off the flight line){(f.FloorShot ? $", {down:0} deg down - floor shot" : "")}");
        }

        static float SteerSpeed(float thrown) => Mathf.Clamp(thrown, MinSteerSpeed.Value, Mathf.Max(ThrowSpeed.Value, MinSteerSpeed.Value));

        // Called from the StartAssistedThrow hook, after the game chose (or didn't choose) a target.
        static void ApplyAssist(Club k, Transform target, string why)
        {
            var f = k.Fly;
            if (f == null) return;
            if (f.FloorShot) { f.Assist = "skipped (floor shot)"; if (Dbg && target != null) Log.Msg("  throw assist: skipped - floor shot"); return; }
            if (!DirectThrowAssist.Value) { f.Assist = "off (DirectThrowAssist)"; if (Dbg) Log.Msg("  throw assist: off - free throw"); return; }
            if (target == null) { f.Assist = "none"; if (Dbg) Log.Msg($"  throw assist: none - {why}"); return; }
            var rb = k.Rb;
            var npc = target.GetComponentInParent<ANBBasicNPC>();
            float speed = SteerSpeed(f.Speed);
            f.HeadAim = npc != null && ChooseThrowHead(npc, rb.worldCenterOfMass, f.Dir);
            var aim = npc != null ? AimPoint(npc, f.HeadAim) : target.position;
            var to = aim - rb.worldCenterOfMass;
            float turn = Vector3.Angle(to, f.Dir);
            if (turn > Mathf.Clamp(ThrowAssistMaxTurnAngle.Value, 0f, 180f))
            {
                f.Assist = $"skipped ({turn:0} deg turn exceeds {ThrowAssistMaxTurnAngle.Value:0} deg)";
                if (Dbg) Log.Msg($"  throw assist: {f.Assist} - free throw");
                return;
            }
            f.Ours = true; f.Target = npc; f.AimT = target;
            f.TargetUntil = Time.time + ThrowMaxFly.Value / speed + 0.3f;
            f.Start = Time.time;
            f.Dir = to.normalized; f.Speed = speed; f.SteerAt = speed;
            rb.linearVelocity = f.Dir * speed; f.PrevVy = rb.linearVelocity.y;
            f.Assist = $"'{(npc != null ? npc.name : target.name)}'";
            if (Dbg) Log.Msg($"  throw assist: {(npc != null ? "enemy" : "target")} {f.Assist} (at the {(npc == null ? target.name : f.HeadAim ? "head" : "chest")}) {to.magnitude:0.0} m away, {turn:0} deg off the throw - steered by the mod at {speed:0} m/s, spin kept");
        }

        // Every way a flight ends. hitNpc != null = it ended against an enemy: bounce off gently and settle.
        static void EndFlight(Club k, ANBBasicNPC hitNpc, string what)
        {
            var f = k.Fly;
            k.Fly = null;
            if (f != null) { k.FlightEndedAt = Time.time; EndFollow(f); }
            if (!Alive(k.Rb)) return;
            if (f != null && Dbg)
                Log.Msg($"  flight {Time.time - f.Start:0.00} s since the last launch ({ThrowStyle.Value}, assist {f.Assist}): spin {k.Rb.angularVelocity.magnitude / (2f * Mathf.PI):0.0} turns/s, tip {f.TipLast:0} deg off the flight line");
            if (f != null) k.Rb.maxAngularVelocity = f.OldMaxSpin;
            // The game's homing coroutine can still be pushing the club at its target.
            var ato = k.Go.GetComponent<ANBAssistedThrowingObject>();
            if (ato != null) { ato.StopAllCoroutines(); ato.homingTarget = null; }
            if (f != null) k.Rb.collisionDetectionMode = f.OldMode;
            if (k.In == null && !k.Floating) k.Rb.useGravity = true;
            if (hitNpc == null || k.In != null) return;
            var back = f != null ? -f.Dir : Vector3.zero;
            k.Rb.linearVelocity = back * 2f + Vector3.up * 1.5f;
            k.Rb.angularVelocity = Vector3.ClampMagnitude(k.Rb.angularVelocity, 4f);
            k.Calm = new Settle { Until = Time.time + SettleTime, What = what, OldDepen = k.Rb.maxDepenetrationVelocity };
            k.Rb.maxDepenetrationVelocity = SettleDepen;
        }

        static void SettleStep(Club k)
        {
            var c = k.Calm;
            var rb = k.Rb;
            if (!Alive(rb) || k.In != null || k.Fly != null || IsHandHeld(k) || Time.time > c.Until)
            {
                k.Calm = null;
                if (Alive(rb)) rb.maxDepenetrationVelocity = c.OldDepen;
                if (Dbg) Log.Msg($"  after hitting {c.What}: peak spin {c.PeakSpin:0} rad/s, peak speed {c.PeakSpeed:0.0} m/s, capped {c.Clamps} steps");
                return;
            }
            float w = rb.angularVelocity.magnitude, s = rb.linearVelocity.magnitude;
            c.PeakSpin = Mathf.Max(c.PeakSpin, w); c.PeakSpeed = Mathf.Max(c.PeakSpeed, s);
            if (w > SettleMaxSpin || s > SettleMaxSpeed)
            {
                rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, SettleMaxSpin);
                rb.linearVelocity = Vector3.ClampMagnitude(rb.linearVelocity, SettleMaxSpeed);
                c.Clamps++;
            }
        }

        public override void OnFixedUpdate()
        {
            foreach (var k in Clubs)
            {
                if (k.Calm != null) SettleStep(k);
                SampleHand(k);
                var f = k.Fly;
                if (f == null) continue;
                // A new grab ends the flight in BeforeGrab. (IsHandGrabbed can still read true for a moment at release.)
                if (!Alive(k.Go) || !Alive(k.Rb) || k.In != null) { EndFlight(k, null, null); continue; }
                var rb = k.Rb;
                var v = rb.linearVelocity;
                float sp = v.magnitude;
                float age = Time.time - f.Start;
                // Impact: the velocity turns hard or loses a lot of speed in one step. Ignore the first 0.05 s (the
                // release). The old 0.15 s for unassisted throws was for the game's homing, which the mod stops at once;
                // it let a close wall hit go unseen, so the flight ended silently with no ricochet.
                // A floor hit at a shallow angle turns the velocity less than 50 deg: catch it as a lost fall instead
                // (falling at > 1 m/s, then most of that gone in one step, with the floor right below).
                bool floor = f.PrevVy < -1f && v.y > f.PrevVy * 0.3f && FloorBelow(rb.worldCenterOfMass, rb);
                const float grace = 0.05f;
                if (f.EnemyHit != null || (age > grace && sp > 0.01f && (floor || Vector3.Angle(v, f.Dir) > 50f || sp < f.Speed * 0.6f)))
                {
                    Impact(k, f, sp);
                    if (k.Fly == null) continue;
                    v = rb.linearVelocity; sp = v.magnitude;
                }
                else if (sp < 2f || age > 4f)
                {
                    if (Dbg) Log.Msg($"  flight ended: {(sp < 2f ? $"slowed to {sp:0.0} m/s with no impact seen" : "4 s up")}");
                    EndFlight(k, null, null); continue;
                }

                FollowStep(k, f);
                if (k.Fly == null) continue;
                if (f.Ours && (f.Target != null || f.AimT != null))
                {
                    if (Time.time > f.TargetUntil || (f.Target != null && (!Alive(f.Target) || f.Target.isDead)) || (f.Target == null && !Alive(f.AimT)))
                    { f.Target = null; f.AimT = null; }
                    else
                    {
                        var to = (f.Target != null ? AimPoint(f.Target, f.Bounces > 0 ? RicochetAimHead.Value : f.HeadAim) : f.AimT.position) - rb.worldCenterOfMass;
                        v = to.normalized * f.SteerAt;
                        rb.linearVelocity = v; sp = v.magnitude;
                    }
                }
                if (sp > 0.01f) { f.Dir = v / sp; f.Speed = sp; }
                f.PrevVy = v.y;
                f.TipLast = TipAngle(k, f.Dir); // before the mod turns it: what the last physics step left
                Orient(k, f);
            }
        }

        static bool IsHandHeld(Club k)
        {
            try { return Alive(k.Grab) && k.Grab.IsHandGrabbed; } catch { return false; }
        }

        static void Orient(Club k, Flight f)
        {
            var rb = k.Rb;
            if (ThrowStyle.Value == "TipFirst")
            {
                // Turn the tip onto the flight line with an angular velocity that shrinks as it gets there: a smooth
                // swing from upright to tip-forward that settles and holds. Shortest turn, so no roll.
                var q = Quaternion.FromToRotation(rb.rotation * ClubAxis, f.Dir);
                q.ToAngleAxis(out float deg, out var ax);
                if (deg > 180f) deg -= 360f;
                if (Mathf.Abs(deg) < 0.5f || ax.sqrMagnitude < 1e-6f) { rb.angularVelocity = Vector3.zero; return; }
                float rate = Mathf.Min(Mathf.Abs(deg) * Mathf.Deg2Rad / Mathf.Max(TipTurnTime.Value, 0.01f), TipMaxTurn);
                rb.angularVelocity = ax.normalized * (Mathf.Sign(deg) * rate);
                return;
            }
            if (ThrowStyle.Value != "SpinEnd") return; // Natural: keep the hand's spin
            var flat = Vector3.ProjectOnPlane(f.Dir, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) return;
            var axis = Vector3.Cross(Vector3.up, flat.normalized).normalized; // top of the club moving forward
            if (!f.Oriented)
            {
                // Smallest turn that puts the club in the spin plane (usually tiny: clubs are thrown upright).
                var tip = rb.rotation * ClubAxis;
                var inPlane = Vector3.ProjectOnPlane(tip, axis);
                if (inPlane.sqrMagnitude < 1e-3f) inPlane = Vector3.up;
                rb.rotation = Quaternion.FromToRotation(tip, inPlane.normalized) * rb.rotation;
                f.Oriented = true;
            }
            rb.angularVelocity = axis * (SpinSpeed.Value * Mathf.PI * 2f);
        }

        static float TipAngle(Club k, Vector3 dir) => Alive(k.Rb) ? Vector3.Angle(k.Rb.rotation * ClubAxis, dir) : -1f;

        // ---------- ricochet ----------

        static void Impact(Club k, Flight f, float speedNow)
        {
            var pos = k.Rb.worldCenterOfMass;
            // 0.7.3 log: a knocked-out enemy's ragdoll had already left the 0.8 m sphere, so the hit read as a wall
            // and the club ricocheted on. The damage hook's record comes first.
            var hitNpc = Alive(f.EnemyHit) ? f.EnemyHit : NearestNpc(pos, 0.8f);
            f.EnemyHit = null;
            if (hitNpc != null) f.Hit.Add(hitNpc.Pointer);
            bool onFloor = hitNpc == null && FloorBelow(pos, k.Rb);
            string what = hitNpc != null ? $"enemy '{hitNpc.name}'" : onFloor ? "the floor" : "a wall/object";
            if (onFloor) pos += Vector3.up * 0.08f; // look and fly from just above the floor
            if (hitNpc != null && !RicochetFromEnemy.Value)
            {
                if (Dbg) Log.Msg($"club hit {what} at {f.Speed:0.0} m/s - stops (no ricochet from enemies)");
                EndFlight(k, hitNpc, what);
                return;
            }
            if (f.Bounces >= Ricochets.Value)
            {
                if (Dbg) Log.Msg($"club hit {what} at {f.Speed:0.0} m/s - no ricochets left");
                EndFlight(k, hitNpc, what);
                return;
            }
            ANBBasicNPC best = null; float bestD = RicochetRange.Value;
            foreach (var npc in NpcsAround(pos, RicochetRange.Value))
            {
                if (npc.isDead || f.Hit.Contains(npc.Pointer)) continue;
                var aim = AimPoint(npc, RicochetAimHead.Value);
                float d = Vector3.Distance(pos, aim);
                if (d >= bestD || !InSight(pos, aim, npc, k.Rb)) continue;
                best = npc; bestD = d;
            }
            if (best == null)
            {
                if (Dbg) Log.Msg($"club hit {what} at {f.Speed:0.0} m/s - no enemy in sight within {RicochetRange.Value:0} m to ricochet to");
                EndFlight(k, hitNpc, what);
                return;
            }
            // The game's homing must not steer against the ricochet.
            var ato = k.Go.GetComponent<ANBAssistedThrowingObject>();
            if (ato != null) { ato.StopAllCoroutines(); ato.homingTarget = null; }
            f.Bounces++;
            f.Target = best;
            f.TargetUntil = Time.time + 1.5f;
            f.Ours = true;
            f.AimT = null;
            f.Oriented = false; // new direction: line the spin up with it again
            f.Start = Time.time; // restarts the impact grace period
            var dir = (AimPoint(best, RicochetAimHead.Value) - pos).normalized;
            f.SteerAt = SteerSpeed(Mathf.Max(f.Speed, speedNow));
            k.Rb.linearVelocity = dir * f.SteerAt;
            f.Dir = dir; f.Speed = k.Rb.linearVelocity.magnitude; f.PrevVy = k.Rb.linearVelocity.y;
            if (Dbg) Log.Msg($"club hit {what} at {speedNow:0.0} m/s - ricochet {f.Bounces}/{Ricochets.Value} to '{best.name}' {bestD:0.0} m away at {f.SteerAt:0.0} m/s");
        }

        // A normal assisted club throw uses Throw Assist's release-direction head rule.
        static MelonPreferences_Entry<bool> throwAimHead;
        static MelonPreferences_Entry<float> throwHeadAngle;
        static bool ThrowAimHead()
        {
            throwAimHead ??= MelonPreferences.GetEntry<bool>("ThrowAssist", "AimHead");
            return throwAimHead != null && throwAimHead.Value;
        }

        static bool ChooseThrowHead(ANBBasicNPC npc, Vector3 from, Vector3 releaseDir)
        {
            if (!ThrowAimHead() || npc.aimAtHead == null) return false;
            throwHeadAngle ??= MelonPreferences.GetEntry<float>("ThrowAssist", "HeadAimMaxAngle");
            var toHead = npc.aimAtHead.position - from;
            float headAngle = Vector3.Angle(releaseDir, toHead);
            float chestAngle = Vector3.Angle(releaseDir, toHead - Vector3.up * 0.35f);
            float limit = throwHeadAngle != null ? throwHeadAngle.Value : 25f;
            bool chosen = headAngle < chestAngle && headAngle <= Mathf.Max(0f, limit);
            if (Dbg) Log.Msg($"  club aim check: head {headAngle:0.0} deg, chest {chestAngle:0.0} deg, limit {limit:0.0} deg -> {(chosen ? "head" : "chest")}");
            return chosen;
        }

        static Vector3 AimPoint(ANBBasicNPC npc, bool atHead)
        {
            var head = npc.aimAtHead;
            if (head != null) return atHead ? head.position : head.position - Vector3.up * 0.35f;
            return npc.transform.position + Vector3.up * 1.2f;
        }

        // First solid thing between the club and the aim point, skipping the club's own colliders.
        static bool InSight(Vector3 from, Vector3 to, ANBBasicNPC npc, Rigidbody self)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            var hits = Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit? first = null;
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                var arb = h.collider.attachedRigidbody;
                if (arb != null && self != null && arb.Pointer == self.Pointer) continue;
                if (first == null || h.distance < first.Value.distance) first = h;
            }
            if (first == null) return true;
            var n = first.Value.collider.GetComponentInParent<ANBBasicNPC>();
            return n != null && n.Pointer == npc.Pointer;
        }

        // Walkable ground (not an enemy, not the club) within a few cm below the club's centre.
        static bool FloorBelow(Vector3 pos, Rigidbody self)
        {
            foreach (var h in Physics.RaycastAll(pos + Vector3.up * 0.05f, Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.normal.y < 0.7f) continue;
                var arb = h.collider.attachedRigidbody;
                if (arb != null && self != null && arb.Pointer == self.Pointer) continue;
                if (h.collider.GetComponentInParent<ANBBasicNPC>() != null) continue;
                return true;
            }
            return false;
        }

        static ANBBasicNPC NearestNpc(Vector3 pos, float r)
        {
            ANBBasicNPC best = null; float bestD = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(pos, r))
            {
                var n = c.GetComponentInParent<ANBBasicNPC>();
                if (n == null) continue;
                float d = (c.ClosestPoint(pos) - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        static List<ANBBasicNPC> NpcsAround(Vector3 pos, float r)
        {
            var list = new List<ANBBasicNPC>();
            var seen = new HashSet<IntPtr>();
            foreach (var c in Physics.OverlapSphere(pos, r))
            {
                var n = c.GetComponentInParent<ANBBasicNPC>();
                if (n != null && seen.Add(n.Pointer)) list.Add(n);
            }
            return list;
        }
    }
}
