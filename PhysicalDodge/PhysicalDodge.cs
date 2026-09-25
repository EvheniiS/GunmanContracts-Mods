using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(PhysicalDodge.PhysicalDodgeMod), "Physical Dodge", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace PhysicalDodge
{
    // A sharp step, lean, stick burst or duck opens a short dodge window. Enemy shots fired at you in
    // that window go where you were just before you moved, so they fly past you.
    //
    // How enemies shoot (GameAssembly.dll): ANBBasicNPC.fireWeaponExec aims at attackTarget and calls
    // ANBHVRGunBase.EnemyTriggerPulled(source, direction, spread), which only stores the three in
    // tmpEnemyBulletSource / tmpEnemyBulletdirection / tmpEnemyBulletspread. Every bullet then goes
    // through FireBulletNew, whose enemy branch (EnemyGun) re-reads them: direction =
    // ApplyRandomAngle(tmpEnemyBulletdirection, enemyBulletSpreadAddition + tmpEnemyBulletspread), or
    // ApplyRandomAngle(..., ShotRadius) per pellet on a shotgun, from tmpEnemyBulletSource, into
    // ANBGameLogic.FireBullet. So a prefix on FireBulletNew sees every bullet of a burst, one by one.
    //
    // How a bullet hurts you: ANBGameLogic.BulletImpact calls HurtPlayer only when the collider the
    // bullet hit sits on PlayerHitTarget, PlayerHitTargetHips or PlayerHitTargetLegs. The mod aims
    // clear of those three colliders, with the game's spread applied first and then set to zero for
    // that bullet, so a re-aimed shot really misses. Nothing is made harmless by a flag.
    public class PhysicalDodgeMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, StickMovementDodges, DebugLog;
        internal static MelonPreferences_Entry<float> StepSpeed, DuckSpeed, DodgeSeconds, CooldownSeconds,
            AimLagSeconds, MissMargin, MaxDeflect;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("PhysicalDodge", "Physical Dodge");
            Enabled = c.CreateEntry("Enabled", true, description: "Step, lean or duck as an enemy fires and the shot goes where you were.");
            StepSpeed = c.CreateEntry("StepSpeed", 1.0f, description: "A step, lean or stick move counts as a dodge when your head's speed changes by at least this much (m/s) in about 0.15 s. Steady running doesn't count, only the burst.");
            DuckSpeed = c.CreateEntry("DuckSpeed", 0.8f, description: "Ducking counts as a dodge when your head drops at least this fast (m/s).");
            StickMovementDodges = c.CreateEntry("StickMovementDodges", true, description: "Moving with the stick counts too. Off = only your real body movement counts.");
            DodgeSeconds = c.CreateEntry("DodgeSeconds", 0.3f, description: "How long one dodge lasts. Shots fired at you in this time miss.");
            CooldownSeconds = c.CreateEntry("CooldownSeconds", 0.5f, description: "After a dodge ends, how long before the next one can start.");
            AimLagSeconds = c.CreateEntry("AimLagSeconds", 0.15f, description: "During a dodge, enemies aim where you were this long before it started.");
            MissMargin = c.CreateEntry("MissMargin", 0.15f, description: "How far (m) a dodged bullet passes outside your body.");
            MaxDeflect = c.CreateEntry("MaxDeflect", 1.5f, description: "How far (m) a shot may be moved off its line to miss. If that isn't enough (point blank), it hits as normal.");
            DebugLog = c.CreateEntry("DebugLog", true, description: "Log each dodge, every hit you take, and a summary every 30 s, for tuning.");
            LoggerInstance.Msg("loaded - step, lean or duck as they fire.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => Dodge.Reset(sceneName);

        public override void OnUpdate()
        {
            if (!Enabled.Value) return;
            try { Dodge.Update(); }
            catch (Exception e) { Log.Warning($"update: {e.GetType().Name}: {e.Message}"); }
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }
    }

    // The state of one bullet between the FireBulletNew prefix and postfix.
    internal class Shot
    {
        public Vector3 Direction;
        public float Spread, ShotRadius;
        public bool Shotgun, Harmless;
        public float Push;
    }

    internal static class Dodge
    {
        static void W(string s) => PhysicalDodgeMod.Log.Msg(s);
        static bool Debug => PhysicalDodgeMod.DebugLog.Value;

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double Now => Clock.Elapsed.TotalSeconds;

        // Head samples for the last second, in real time: slow motion slows the enemies, not you.
        struct Sample { public double T; public Vector3 World, Body; }
        static readonly List<Sample> Samples = new();
        static Transform lastParent;
        static Vector3 lastLocal, body;        // body = head movement you made yourself, summed in world space

        const double VelocitySpan = 0.1, CompareBack = 0.15;
        const float ThreatRadius = 1.0f;       // a shot whose line passes this close to your body is "at you"

        // The current or last dodge.
        static double start = -1, end = -1;
        static Vector3 anchor;                 // your head just before you moved
        static string kind;
        static float speed;
        static int reAimed, harmless, missed, hitsDuring;
        static float minPush, maxPush;

        // 30 s summary.
        static double summaryAt;
        static int sDodges, sSteps, sDucks, sReAimed, sMissed, sHitsDuring, sHitsOutside, sShotsOutside;

        static Collider[] cols = new Collider[3];
        static readonly string[] ColNames = { "chest", "hips", "legs" };
        static bool colsLogged;

        internal static Shot Current;          // the bullet being fired right now, for the FireBullet hook

        internal static bool Active => start >= 0 && Now < end;

        internal static void Reset(string scene)
        {
            Samples.Clear();
            lastParent = null;
            start = end = -1;
            cols = new Collider[3];
            colsLogged = false;
            Current = null;
            summaryAt = Now + 30;
            if (Debug) W($"scene '{scene}'");
        }

        // ---- detecting a dodge ------------------------------------------------------------------

        internal static void Update()
        {
            var cam = ANBStaticGameManager.MainCam;
            if (!PhysicalDodgeMod.Alive(cam)) return;
            var game = ANBStaticGameManager.ANBmain;
            if (!PhysicalDodgeMod.Alive(game)) return;

            double now = Now;
            var head = cam.transform;
            Vector3 world = head.position;

            // A jump of over 1.5 m in one frame is a teleport or a respawn, not a dodge.
            if (Samples.Count > 0 && (world - Samples[^1].World).sqrMagnitude > 1.5f * 1.5f) { Samples.Clear(); lastParent = null; }

            // Your own movement: the head's local position under the tracking space, turned into world
            // space. Stick movement and turning move the tracking space, not this.
            var parent = head.parent;
            Vector3 local = head.localPosition;
            if (parent != null && lastParent != null && parent.Pointer == lastParent.Pointer) body += parent.TransformVector(local - lastLocal);
            lastParent = parent; lastLocal = local;

            Samples.Add(new Sample { T = now, World = world, Body = body });
            while (Samples.Count > 2 && now - Samples[0].T > 1.0) Samples.RemoveAt(0);

            if (end >= 0 && now >= end && start >= 0) FinishDodge();
            if (now >= summaryAt) Summary();

            if (game.Paused || Active || now < end + PhysicalDodgeMod.CooldownSeconds.Value) return;

            bool stick = PhysicalDodgeMod.StickMovementDodges.Value;
            if (!Velocity(now, stick, out Vector3 v) || !Velocity(now - CompareBack, stick, out Vector3 before)) return;
            Vector3 dv = v - before; dv.y = 0;
            // Ducking is judged on your real head movement only when the stick doesn't count, so a
            // stick crouch (which lowers the tracking space) is a duck only in the default mode.
            if (-v.y >= PhysicalDodgeMod.DuckSpeed.Value) StartDodge(now, "duck", -v.y);
            else if (dv.magnitude >= PhysicalDodgeMod.StepSpeed.Value) StartDodge(now, "step", dv.magnitude);
        }

        // Average head velocity over the VelocitySpan ending at t.
        static bool Velocity(double t, bool world, out Vector3 v)
        {
            v = default;
            if (!At(t, out var b) || !At(t - VelocitySpan, out var a) || b.T - a.T < 0.03) return false;
            v = ((world ? b.World - a.World : b.Body - a.Body)) / (float)(b.T - a.T);
            return true;
        }

        // The latest sample at or before t.
        static bool At(double t, out Sample s)
        {
            s = default;
            for (int i = Samples.Count - 1; i >= 0; i--)
                if (Samples[i].T <= t) { s = Samples[i]; return true; }
            return false;
        }

        static void StartDodge(double now, string what, float spd)
        {
            start = now;
            end = now + PhysicalDodgeMod.DodgeSeconds.Value;
            kind = what; speed = spd;
            reAimed = harmless = missed = hitsDuring = 0;
            minPush = float.MaxValue; maxPush = 0;
            anchor = At(now - PhysicalDodgeMod.AimLagSeconds.Value, out var s) ? s.World : Samples[0].World;
            sDodges++;
            if (what == "duck") sDucks++; else sSteps++;
        }

        static void FinishDodge()
        {
            if (Debug && (reAimed > 0 || missed > 0 || hitsDuring > 0))
            {
                string line = $"dodge ({kind}, {speed:0.0} m/s): ";
                line += reAimed > 0 ? $"{reAimed} shot(s) at you went where you were, {minPush:0.00}-{maxPush:0.00} m off their line" : "no shot re-aimed";
                if (harmless > 0) line += $" ({harmless} would have been harmless anyway)";
                if (missed > 0) line += $"; {missed} couldn't be moved clear (over {PhysicalDodgeMod.MaxDeflect.Value:0.#} m needed)";
                if (hitsDuring > 0) line += $"; HIT {hitsDuring}x during it";
                W(line);
            }
            start = -1;
        }

        static void Summary()
        {
            summaryAt = Now + 30;
            if (!Debug || sDodges + sShotsOutside + sHitsOutside == 0) return;
            W($"last 30 s: {sDodges} dodge(s) ({sSteps} step, {sDucks} duck); shots at you during a dodge: {sReAimed} sent past you, " +
              $"{sMissed} not movable, hits {sHitsDuring}; outside a dodge: {sShotsOutside} shot(s) at you, hits {sHitsOutside}");
            sDodges = sSteps = sDucks = sReAimed = sMissed = sHitsDuring = sHitsOutside = sShotsOutside = 0;
        }

        // ---- enemy bullets ------------------------------------------------------------------------

        internal static Shot BeforeEnemyBullet(ANBHVRGunBase gun)
        {
            if (!PhysicalDodgeMod.Enabled.Value || !gun.EnemyGun) return null;
            var src = gun.tmpEnemyBulletSource;
            var game = ANBStaticGameManager.ANBmain;
            var cam = ANBStaticGameManager.MainCam;
            if (!PhysicalDodgeMod.Alive(src) || !PhysicalDodgeMod.Alive(game) || !PhysicalDodgeMod.Alive(cam)) return null;
            if (!Colliders(game, cam)) return null;

            Vector3 origin = src.position;
            Vector3 f = gun.tmpEnemyBulletdirection;
            if (f.sqrMagnitude < 1e-6f) return null;
            f.Normalize();
            if (!AtYou(origin, f)) return null;

            if (!Active)
            {
                sShotsOutside++;
                return null;
            }

            // The point on the game's line nearest your chest: the game's own aim, including its aim
            // error. It moves with the shift from where you are now back to where you were.
            var chest = game.PlayerHitTarget;
            Vector3 target = PhysicalDodgeMod.Alive(chest) ? chest.position : cam.transform.position;
            float t = Vector3.Dot(target - origin, f);
            if (t <= 0.2f) return null;
            Vector3 aimed = origin + f * t;
            Vector3 shift = anchor - cam.transform.position;
            Vector3 side = shift - Vector3.Dot(shift, f) * f;       // only the part across the line moves the shot
            Vector3 push = side.sqrMagnitude > 0.05f * 0.05f ? side.normalized : Away(f, target - aimed);
            Vector3 basePoint = aimed + side;

            bool shotgun = gun.isShotgun;
            float spread = shotgun ? gun.ShotRadius : gun.enemyBulletSpreadAddition + gun.tmpEnemyBulletspread;

            // The game's spread for this bullet, drawn once, the same way the game does it.
            Vector3 d0 = (basePoint - origin).normalized;
            Quaternion scatter = Quaternion.identity;
            if (spread > 0) scatter = Quaternion.FromToRotation(d0, game.ApplyRandomAngle(d0, spread));

            float max = PhysicalDodgeMod.MaxDeflect.Value;
            for (float p = 0; p <= max + 1e-3f; p += 0.05f)
            {
                Vector3 d = scatter * (basePoint + push * p - origin).normalized;
                if (!Clears(origin, d, t + 3f)) continue;
                var shot = new Shot
                {
                    Direction = gun.tmpEnemyBulletdirection,
                    Spread = gun.tmpEnemyBulletspread,
                    ShotRadius = gun.ShotRadius,
                    Shotgun = shotgun,
                    Push = (side + push * p).magnitude,
                };
                gun.tmpEnemyBulletdirection = d;
                // The spread is already in d, so the game must add none.
                if (shotgun) gun.ShotRadius = 0;
                else gun.tmpEnemyBulletspread = -gun.enemyBulletSpreadAddition;
                Current = shot;
                return shot;
            }
            missed++; sMissed++;
            return null;
        }

        internal static void AfterEnemyBullet(ANBHVRGunBase gun, Shot shot)
        {
            Current = null;
            gun.tmpEnemyBulletdirection = shot.Direction;
            gun.tmpEnemyBulletspread = shot.Spread;
            if (shot.Shotgun) gun.ShotRadius = shot.ShotRadius;
            reAimed++; sReAimed++;
            if (shot.Harmless) harmless++;
            minPush = Mathf.Min(minPush, shot.Push);
            maxPush = Mathf.Max(maxPush, shot.Push);
        }

        internal static void PlayerHurt(string type, float dmg, ANBBasicNPC attacker)
        {
            bool bullet = type != null && type.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0;
            if (Active) { hitsDuring++; sHitsDuring++; }
            else if (bullet) sHitsOutside++;
            if (!Debug) return;
            string state = Active ? $"DURING a dodge ({kind}, {Now - start:0.00} s in)"
                : start < 0 && end >= 0 && Now < end + PhysicalDodgeMod.CooldownSeconds.Value ? $"in the cooldown ({end + PhysicalDodgeMod.CooldownSeconds.Value - Now:0.00} s left)"
                : "no dodge";
            W($"you were hurt: {type} {dmg:0.#} damage{(attacker != null ? " by an enemy" : "")} - {state}");
        }

        // Push direction when your movement was along the line of fire (straight at or away from the
        // shooter): sideways, away from where the line passes you, or a random side.
        static Vector3 Away(Vector3 f, Vector3 lineToYou)
        {
            Vector3 across = Vector3.Cross(Vector3.up, f);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(Vector3.forward, f);
            across.Normalize();
            float s = Vector3.Dot(lineToYou, across);
            if (Mathf.Abs(s) < 0.02f) s = UnityEngine.Random.value < 0.5f ? -1 : 1;
            return across * (s > 0 ? -1 : 1);
        }

        // Does this line pass within ThreatRadius of your body?
        static bool AtYou(Vector3 origin, Vector3 f)
        {
            foreach (var c in cols)
            {
                if (c == null) continue;
                var b = c.bounds;
                Vector3 to = b.center - origin;
                float along = Vector3.Dot(to, f);
                if (along <= 0) continue;
                if ((to - f * along).magnitude - b.extents.magnitude <= ThreatRadius) return true;
            }
            return false;
        }

        // Nine parallel rays: the line itself and eight around it at MissMargin. None may touch any of
        // your three hit colliders. Collider.Raycast tests only that collider, ignoring walls.
        static bool Clears(Vector3 origin, Vector3 d, float range)
        {
            Vector3 u = Vector3.Cross(d, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(d, Vector3.forward);
            u.Normalize();
            Vector3 w = Vector3.Cross(d, u);
            float m = PhysicalDodgeMod.MissMargin.Value;
            for (int i = 0; i < 9; i++)
            {
                Vector3 off = i == 0 ? Vector3.zero : (Mathf.Cos(i * Mathf.PI / 4) * u + Mathf.Sin(i * Mathf.PI / 4) * w) * m;
                var ray = new Ray(origin + off, d);
                foreach (var c in cols)
                    if (c != null && c.Raycast(ray, out RaycastHit _, range)) return false;
            }
            return true;
        }

        // The colliders BulletImpact treats as you (on the three hit-target transforms themselves).
        static bool Colliders(ANBGameLogic game, Camera cam)
        {
            bool any = false;
            var ts = new[] { game.PlayerHitTarget, game.PlayerHitTargetHips, game.PlayerHitTargetLegs };
            for (int i = 0; i < 3; i++)
            {
                if (cols[i] != null && PhysicalDodgeMod.Alive(cols[i])) { any = true; continue; }
                cols[i] = null;
                if (!PhysicalDodgeMod.Alive(ts[i])) continue;
                var c = ts[i].GetComponent<Collider>();
                if (PhysicalDodgeMod.Alive(c)) { cols[i] = c; any = true; }
            }
            if (any && !colsLogged && Debug)
            {
                colsLogged = true;
                float headY = cam.transform.position.y;
                var parts = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    if (cols[i] == null) { parts.Add($"{ColNames[i]}: none"); continue; }
                    var b = cols[i].bounds;
                    parts.Add($"{ColNames[i]} '{cols[i].name}' {cols[i].GetIl2CppType().Name} {b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00} m, " +
                              $"centre {headY - b.center.y:0.00} m below your head");
                }
                W("your hit colliders: " + string.Join("; ", parts));
            }
            return any;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBHVRGunBase), nameof(ANBHVRGunBase.FireBulletNew))]
    internal static class FireBulletNewPatch
    {
        static void Prefix(ANBHVRGunBase __instance, out Shot __state)
        {
            __state = null;
            try { __state = Dodge.BeforeEnemyBullet(__instance); }
            catch (Exception e) { PhysicalDodgeMod.Log.Warning($"enemy bullet: {e.GetType().Name}: {e.Message}"); }
        }

        static void Postfix(ANBHVRGunBase __instance, Shot __state)
        {
            if (__state == null) return;
            try { Dodge.AfterEnemyBullet(__instance, __state); }
            catch (Exception e) { PhysicalDodgeMod.Log.Warning($"enemy bullet restore: {e.GetType().Name}: {e.Message}"); }
        }
    }

    // FireBullet(source, bulletSource, direction, range, force, damage, FromEnemy, NotUsed, isSilenced,
    // enemyBulletCooldowned, flame, reduced): read only, to count shots that were harmless anyway
    // (enemy cooldown, warning shot, first second of fire).
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.FireBullet))]
    internal static class FireBulletPatch
    {
        static void Prefix(bool __9)
        {
            var s = Dodge.Current;
            if (s != null) s.Harmless = __9;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.HurtPlayer))]
    internal static class HurtPlayerPatch
    {
        static void Prefix(string __0, float __1, ANBBasicNPC __2)
        {
            try { Dodge.PlayerHurt(__0, __1, __2); } catch { }
        }
    }
}
