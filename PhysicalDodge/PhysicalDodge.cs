using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.ControllerInput;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(PhysicalDodge.PhysicalDodgeMod), "Physical Dodge", "0.2.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace PhysicalDodge
{
    // Enemies react late: every shot at you is aimed where you were AimLagSeconds ago. Stand still and
    // you get hit; step, lean, duck or keep moving and the shots go where you were.
    //
    // How enemies shoot (GameAssembly.dll): ANBBasicNPC.fireWeaponExec aims at your live position and
    // calls ANBHVRGunBase.EnemyTriggerPulled(source, direction, spread), which only stores the three in
    // tmpEnemyBulletSource / tmpEnemyBulletdirection / tmpEnemyBulletspread. Every bullet then goes
    // through FireBulletNew, whose enemy branch (EnemyGun) re-reads them, adds the game's spread
    // (ApplyRandomAngle) and calls ANBGameLogic.FireBullet with the final direction. So a prefix on
    // FireBulletNew re-aims each bullet of a burst separately, and the game's own spread still applies.
    //
    // How a bullet hurts you: ANBGameLogic.BulletImpact calls HurtPlayer only when the collider it hit
    // sits on PlayerHitTarget (a 30 cm sphere at your head), PlayerHitTargetHips or PlayerHitTargetLegs.
    // The mod tests the final bullet line against those three colliders to tell you whether moving
    // saved you.
    //
    // 0.1.0 opened a 0.3 s dodge window on a sudden change of movement, with a cooldown after it. The
    // test log showed every shot in a window missed, but only 7 of 28 shots at you fell in one: steady
    // movement didn't count, and ordinary movement used up the windows.
    public class PhysicalDodgeMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, StickMovementDodges, RushDodges, Haptics, DebugLog;
        internal static MelonPreferences_Entry<float> AimLagSeconds;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("PhysicalDodge", "Physical Dodge");
            Enabled = c.CreateEntry("Enabled", true, description: "Enemies aim where you were a moment ago: stand still and you get hit, move and they miss.");
            AimLagSeconds = c.CreateEntry("AimLagSeconds", 0.2f, description: "How far behind you enemies aim, in seconds. Higher = easier to dodge. 0 = the game's normal aim.");
            StickMovementDodges = c.CreateEntry("StickMovementDodges", false, description: "Off (default): only your real body movement counts - steps, leans, ducks. On = moving with the stick counts too.");
            RushDodges = c.CreateEntry("RushDodges", true, description: "Moving straight at (or away from) the shooter also makes them miss. Off = only sideways and up/down movement does, as in real life.");
            Haptics = c.CreateEntry("Haptics", true, description: "A short buzz on both controllers when a shot that would have hit you goes past because you moved.");
            DebugLog = c.CreateEntry("DebugLog", true, description: "Log every shot at you (dodged, hit, or harmless anyway) and a summary every 30 s, for tuning.");
            LoggerInstance.Msg($"loaded - enemies aim {AimLagSeconds.Value:0.##} s behind you.");
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

    // One bullet, from the FireBulletNew prefix through FireBullet to the postfix.
    internal class Shot
    {
        public Vector3 GameDirection, OurDirection, Origin;
        public float Distance, Moved, Across;
        public bool Resolved;
    }

    internal static class Dodge
    {
        static void W(string s) => PhysicalDodgeMod.Log.Msg(s);
        static bool Debug => PhysicalDodgeMod.DebugLog.Value;

        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static double Now => Clock.Elapsed.TotalSeconds;

        // Head samples for the last second, in real time.
        struct Sample { public double T; public Vector3 World, Body; }
        static readonly List<Sample> Samples = new();
        static Transform lastParent;
        static Vector3 lastLocal, body;        // head movement you made yourself, summed in world space

        const float ThreatRadius = 1.0f;       // a shot whose line passes this close to your body is "at you"

        static Collider[] cols = new Collider[3];
        static readonly string[] ColNames = { "head", "hips", "legs" };

        internal static Shot Current;          // the bullet being fired right now
        static Shot last;                      // the last shot at you, for the hurt line
        static double lastAt;

        // 30 s summary.
        static double summaryAt;
        static int sAtYou, sDodged, sHit, sHarmless, sMissAnyway;
        static float sMovedDodged;

        internal static void Reset(string scene)
        {
            Samples.Clear();
            lastParent = null;
            cols = new Collider[3];
            Current = last = null;
            summaryAt = Now + 30;
            if (Debug) W($"scene '{scene}'");
        }

        internal static void Update()
        {
            var cam = ANBStaticGameManager.MainCam;
            if (!PhysicalDodgeMod.Alive(cam)) return;

            double now = Now;
            var head = cam.transform;
            Vector3 world = head.position;

            // A jump of over 1.5 m in one frame is a teleport or a respawn, not movement.
            if (Samples.Count > 0 && (world - Samples[^1].World).sqrMagnitude > 1.5f * 1.5f) { Samples.Clear(); lastParent = null; }

            // Your own movement: the head's local position under the tracking space, turned into world
            // space. Stick movement and turning move the tracking space, not this.
            var parent = head.parent;
            Vector3 local = head.localPosition;
            if (parent != null && lastParent != null && parent.Pointer == lastParent.Pointer) body += parent.TransformVector(local - lastLocal);
            lastParent = parent; lastLocal = local;

            Samples.Add(new Sample { T = now, World = world, Body = body });
            while (Samples.Count > 2 && now - Samples[0].T > 1.0) Samples.RemoveAt(0);

            if (now >= summaryAt) Summary();
        }

        // How far your head moved in the last 'lag' seconds (now minus then).
        static bool Moved(float lag, out Vector3 moved)
        {
            moved = default;
            if (Samples.Count < 2) return false;
            var now = Samples[^1];
            Sample then = Samples[0];
            for (int i = Samples.Count - 1; i >= 0; i--)
                if (Samples[i].T <= now.T - lag) { then = Samples[i]; break; }
            if (now.T - then.T < lag * 0.5) return false;       // not enough history yet
            moved = PhysicalDodgeMod.StickMovementDodges.Value ? now.World - then.World : now.Body - then.Body;
            return true;
        }

        // ---- enemy bullets ------------------------------------------------------------------------

        internal static Shot BeforeEnemyBullet(ANBHVRGunBase gun)
        {
            if (!PhysicalDodgeMod.Enabled.Value || !gun.EnemyGun) return null;
            float lag = PhysicalDodgeMod.AimLagSeconds.Value;
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

            // The point on the game's line nearest your head: the game's own aim, including its aim error.
            Vector3 you = cam.transform.position;
            float t = Vector3.Dot(you - origin, f);
            if (t <= 0.2f) return null;
            var shot = new Shot { GameDirection = f, OurDirection = f, Origin = origin, Distance = t };
            if (lag > 0 && Moved(lag, out Vector3 moved))
            {
                // Aim where you were: the aim point moves back by your movement. Only the part across the
                // line changes where the bullet goes; with RushDodges, movement along the line (straight at
                // or away from them) throws the aim off by the same distance, sideways.
                Vector3 back = -moved;
                Vector3 across = back - Vector3.Dot(back, f) * f;
                float size = PhysicalDodgeMod.RushDodges.Value ? back.magnitude : across.magnitude;
                Vector3 dir = across.sqrMagnitude > 0.03f * 0.03f ? across.normalized : Sideways(f);
                Vector3 aim = origin + f * t + dir * size;
                shot.OurDirection = (aim - origin).normalized;
                shot.Moved = moved.magnitude;
                shot.Across = size;
                gun.tmpEnemyBulletdirection = shot.OurDirection;
            }
            Current = shot;
            return shot;
        }

        // FireBullet prefix: the final direction, with the game's spread in it.
        internal static void FinalBullet(Vector3 direction, bool harmless)
        {
            var s = Current;
            if (s == null || s.Resolved) return;
            s.Resolved = true;
            Vector3 d = direction.normalized;
            // The same spread applied to the game's own aim tells whether it would have hit.
            Vector3 gameD = Quaternion.FromToRotation(s.OurDirection, d) * s.GameDirection;
            string hitNow = HitPart(s.Origin, d, s.Distance + 3f);
            string hitGame = HitPart(s.Origin, gameD, s.Distance + 3f);
            sAtYou++;
            last = s; lastAt = Now;

            string what;
            if (harmless) { sHarmless++; what = "harmless anyway (the game's free miss)"; }
            else if (hitNow == null && hitGame != null)
            {
                sDodged++; sMovedDodged += s.Moved;
                what = $"DODGED - would have hit your {hitGame}";
                if (PhysicalDodgeMod.Haptics.Value) Buzz();
            }
            else if (hitNow != null) { sHit++; what = $"hits your {hitNow}"; }
            else { sMissAnyway++; what = "misses anyway (the game's spread)"; }

            if (Debug)
                W($"shot from {s.Distance:0.0} m: {what}; you moved {s.Moved:0.00} m in {PhysicalDodgeMod.AimLagSeconds.Value:0.##} s, " +
                  $"aim thrown {s.Across:0.00} m off you");
        }

        internal static void AfterEnemyBullet(ANBHVRGunBase gun, Shot shot)
        {
            Current = null;
            gun.tmpEnemyBulletdirection = shot.GameDirection;
        }

        internal static void PlayerHurt(string type, float dmg)
        {
            if (!Debug) return;
            bool recent = last != null && Now - lastAt < 0.5;
            W($"you were hurt: {type} {dmg:0.#}{(recent ? $" (last shot: you had moved {last.Moved:0.00} m)" : "")}");
        }

        static void Summary()
        {
            summaryAt = Now + 30;
            if (!Debug || sAtYou == 0) return;
            W($"last 30 s: {sAtYou} shot(s) at you - dodged {sDodged}" +
              (sDodged > 0 ? $" (you moved {sMovedDodged / sDodged:0.00} m on average)" : "") +
              $", hit {sHit}, harmless anyway {sHarmless}, missed anyway {sMissAnyway}");
            sAtYou = sDodged = sHit = sHarmless = sMissAnyway = 0; sMovedDodged = 0;
        }

        static void Buzz()
        {
            try
            {
                var im = HVRInputManager.Instance;
                if (!PhysicalDodgeMod.Alive(im)) return;
                im.LeftController?.Vibrate(0.25f, 0.05f, 180f);
                im.RightController?.Vibrate(0.25f, 0.05f, 180f);
            }
            catch { }
        }

        // Sideways, for movement straight along the line of fire: a random side.
        static Vector3 Sideways(Vector3 f)
        {
            Vector3 across = Vector3.Cross(Vector3.up, f);
            if (across.sqrMagnitude < 1e-4f) across = Vector3.Cross(Vector3.forward, f);
            return across.normalized * (UnityEngine.Random.value < 0.5f ? -1 : 1);
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

        // Which of your hit colliders this line hits first, or null. Collider.Raycast tests only that
        // collider, so walls are ignored: this is "on target", not "reached you".
        static string HitPart(Vector3 origin, Vector3 d, float range)
        {
            var ray = new Ray(origin, d);
            string best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 3; i++)
                if (cols[i] != null && cols[i].Raycast(ray, out RaycastHit h, range) && h.distance < bestDist) { bestDist = h.distance; best = ColNames[i]; }
            return best;
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
    // enemyBulletCooldowned, flame, reduced): read only. direction already has the game's spread in it;
    // enemyBulletCooldowned = the game's free miss (enemy cooldown, warning shot, first second of fire).
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.FireBullet))]
    internal static class FireBulletPatch
    {
        static void Prefix(Vector3 __2, bool __9)
        {
            if (Dodge.Current == null) return;
            try { Dodge.FinalBullet(__2, __9); }
            catch (Exception e) { PhysicalDodgeMod.Log.Warning($"bullet check: {e.GetType().Name}: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.HurtPlayer))]
    internal static class HurtPlayerPatch
    {
        static void Prefix(string __0, float __1)
        {
            try { Dodge.PlayerHurt(__0, __1); } catch { }
        }
    }
}
