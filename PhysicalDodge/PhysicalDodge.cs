using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.ControllerInput;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(PhysicalDodge.PhysicalDodgeMod), "Physical Dodge", "0.4.1", "Evgeeso")]
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
    // (ApplyRandomAngle(dir, enemyBulletSpreadAddition + tmpEnemyBulletspread), or ShotRadius per shotgun
    // pellet) and calls ANBGameLogic.FireBullet. So a prefix on FireBulletNew sees each bullet separately.
    // The bullet is harmless (flies through you) when UsedByNPC.InCooldown || nonLethalFire ||
    // !weaponFiredOnce: the game's free misses.
    //
    // How a bullet hurts you: ANBGameLogic.BulletImpact calls HurtPlayer only when the collider it hit
    // sits on PlayerHitTarget (a 30 cm sphere at your head), PlayerHitTargetHips or PlayerHitTargetLegs.
    //
    // The mod draws the game's spread itself (the game's own ApplyRandomAngle), applies it to both the
    // game's aim and the lagged aim, and tells the game to add no spread for that bullet. So it knows
    // exactly whether the shot would have hit and whether it now misses. A shot that would have hit and
    // now misses is a DODGE: its line is pushed out until it clears your body by DodgeMargin, so a dodge
    // never turns into a hit. (0.3.0 judged a razor-thin miss a dodge, then the bullet clipped you.)
    //
    // 0.1.0 opened a 0.3 s dodge window on a sudden change of movement, with a cooldown after it: only 7 of
    // 28 shots at you fell in a window. 0.2.0 switched to the aim lag.
    public class PhysicalDodgeMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, StickMovementDodges, RushDodges, Haptics, SlowMotion, DebugLog;
        internal static MelonPreferences_Entry<float> AimLagSeconds, DodgeMargin, SlowMotionSeconds, SlowMotionCooldown;
        internal static MelonPreferences_Entry<int> SlowMotionStrength;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("PhysicalDodge", "Physical Dodge");
            Enabled = c.CreateEntry("Enabled", true, description: "Enemies aim where you were a moment ago: stand still and you get hit, move and they miss.");
            AimLagSeconds = c.CreateEntry("AimLagSeconds", 0.2f, description: "How far behind you enemies aim, in seconds. Higher = easier to dodge. 0 = the game's normal aim.");
            StickMovementDodges = c.CreateEntry("StickMovementDodges", false, description: "Off (default): only your real body movement counts - steps, leans, ducks. On = moving with the stick counts too.");
            RushDodges = c.CreateEntry("RushDodges", true, description: "Moving straight at (or away from) the shooter also makes them miss. Off = only sideways and up/down movement does, as in real life.");
            DodgeMargin = c.CreateEntry("DodgeMargin", 0.1f, description: "A dodged bullet passes at least this far (m) outside your body, so it can't clip you.");
            Haptics = c.CreateEntry("Haptics", true, description: "A short buzz on both controllers when you dodge a shot.");
            SlowMotion = c.CreateEntry("SlowMotion", true, description: "A brief slow motion when you dodge a shot. Uses the game's own slow motion (the one when the last enemy dies).");
            SlowMotionSeconds = c.CreateEntry("SlowMotionSeconds", 1.0f, description: "How long the dodge slow motion lasts, in real seconds.");
            SlowMotionStrength = c.CreateEntry("SlowMotionStrength", 2, description: "1 = light, 2 = medium (the game's last-enemy slow motion), 3 = strongest.");
            SlowMotionCooldown = c.CreateEntry("SlowMotionCooldown", 1.5f, description: "Real seconds from the start of one dodge slow motion to the next.");
            DebugLog = c.CreateEntry("DebugLog", true, description: "Log each dodge and hit (one short line each), a line when a wave starts, and a summary per wave.");
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

    // One bullet, between the FireBulletNew prefix and postfix: the game's values to put back.
    internal class Shot
    {
        public Vector3 Direction;
        public float Spread, ShotRadius;
        public bool Shotgun;
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

        static double lastShotAt = -99;

        // Waves: the spawner that spawned last (Harmony postfix on spawnNPC), and its waveSurvived counter.
        internal static ANBNpcSpawner Spawner;
        static string wave = "";
        static int sAtYou, sDodged, sHit, sHarmless, sMissAnyway, sHurt, sSlow;
        static float sMovedDodged;

        internal static void Reset(string scene)
        {
            WaveSummary("scene change");
            Samples.Clear();
            lastParent = null;
            cols = new Collider[3];
            Spawner = null;
            wave = "";
            if (Debug) W($"scene '{scene}'");
        }

        internal static void Update()
        {
            CheckWave();
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

        // ---- waves ----------------------------------------------------------------------------------

        static void CheckWave()
        {
            string w = "";
            try { if (PhysicalDodgeMod.Alive(Spawner)) w = $"wave {Spawner.waveSurvived + 1}"; } catch { }
            if (w.Length == 0 || w == wave) return;
            WaveSummary("wave over");
            wave = w;
            if (Debug) W($"==== {wave} ====");
        }

        static void WaveSummary(string why)
        {
            if (Debug && sAtYou > 0)
                W($"{(wave.Length > 0 ? wave : "no wave")} summary ({why}): {sAtYou} shots at you: dodged {sDodged}" +
                  (sDodged > 0 ? $" (avg move {sMovedDodged / sDodged:0.00} m, slow-mo {sSlow}x)" : "") +
                  $", hit {sHit} (hurt {sHurt}x), free miss {sHarmless}, spread miss {sMissAnyway}");
            sAtYou = sDodged = sHit = sHarmless = sMissAnyway = sHurt = sSlow = 0; sMovedDodged = 0;
        }

        // ---- enemy bullets ------------------------------------------------------------------------

        internal static Shot BeforeEnemyBullet(ANBHVRGunBase gun)
        {
            if (!PhysicalDodgeMod.Enabled.Value || !gun.EnemyGun) return null;
            var src = gun.tmpEnemyBulletSource;
            var game = ANBStaticGameManager.ANBmain;
            var cam = ANBStaticGameManager.MainCam;
            if (!PhysicalDodgeMod.Alive(src) || !PhysicalDodgeMod.Alive(game) || !PhysicalDodgeMod.Alive(cam)) return null;
            if (!Colliders(game)) return null;

            Vector3 origin = src.position;
            Vector3 f = gun.tmpEnemyBulletdirection;
            if (f.sqrMagnitude < 1e-6f) return null;
            f.Normalize();
            if (!AtYou(origin, f)) return null;

            Vector3 you = cam.transform.position;
            float t = Vector3.Dot(you - origin, f);           // distance along the line to your head
            if (t <= 0.2f) return null;
            float range = t + 3f;
            float lag = PhysicalDodgeMod.AimLagSeconds.Value;
            Moved(lag, out Vector3 moved);
            string shooter = Debug ? Shooter(gun, origin, you) : "";
            sAtYou++;
            lastShotAt = Now;

            // The game's free miss: the bullet uses a layer mask that leaves you out. Nothing to do.
            var npc = gun.UsedByNPC;
            bool harmless = PhysicalDodgeMod.Alive(npc) && (npc.InCooldown || npc.nonLethalFire || !npc.weaponFiredOnce);
            if (harmless)
            {
                sHarmless++;                                    // counted in the wave summary, no line
                return null;
            }

            // Where you were: the aim point moves back by your movement. Only the part across the line
            // changes where the bullet goes; with RushDodges, movement along the line (straight at or away
            // from them) throws the aim off by the same distance, sideways.
            Vector3 back = -moved;
            Vector3 across = back - Vector3.Dot(back, f) * f;
            float size = lag > 0 ? (PhysicalDodgeMod.RushDodges.Value ? back.magnitude : across.magnitude) : 0;
            Vector3 dir = across.sqrMagnitude > 0.03f * 0.03f ? across.normalized : Sideways(f);
            Vector3 aimed = origin + f * t;

            // The game's spread for this bullet, drawn once the way the game does it, applied to both lines.
            bool shotgun = gun.isShotgun;
            float spread = shotgun ? gun.ShotRadius : gun.enemyBulletSpreadAddition + gun.tmpEnemyBulletspread;
            Vector3 lagged = (aimed + dir * size - origin).normalized;
            Quaternion scatter = spread > 0 ? Quaternion.FromToRotation(lagged, game.ApplyRandomAngle(lagged, spread)) : Quaternion.identity;
            string hitGame = HitPart(origin, scatter * f, range);
            Vector3 final = scatter * lagged;
            string hitNow = HitPart(origin, final, range);

            string what = null;
            if (hitGame != null && hitNow == null)
            {
                // A dodge: make sure it misses cleanly, by DodgeMargin all round.
                for (float p = 0; p <= 0.6f; p += 0.05f)
                {
                    Vector3 d = scatter * (aimed + dir * (size + p) - origin).normalized;
                    if (Clears(origin, d, range)) { final = d; break; }
                }
                sDodged++; sMovedDodged += moved.magnitude;
                what = $"DODGED ({hitGame})";
                if (PhysicalDodgeMod.Haptics.Value) Buzz();
                if (PhysicalDodgeMod.SlowMotion.Value) what += SlowDown();
            }
            else if (hitNow != null) { sHit++; what = $"HIT ({hitNow})"; }
            else sMissAnyway++;                                 // counted in the wave summary, no line
            if (what != null) Line(t, what, moved, shooter);

            var shot = new Shot { Direction = gun.tmpEnemyBulletdirection, Spread = gun.tmpEnemyBulletspread, ShotRadius = gun.ShotRadius, Shotgun = shotgun };
            gun.tmpEnemyBulletdirection = final;
            // The spread is already in 'final', so the game must add none.
            if (shotgun) gun.ShotRadius = 0;
            else gun.tmpEnemyBulletspread = -gun.enemyBulletSpreadAddition;
            return shot;
        }

        // e.g. "DODGED (head) -> slow-mo, 4.5 m, moved 0.31 | #99136 attack, sees, in view, body 5.2, gun 0.6"
        static void Line(float dist, string what, Vector3 moved, string shooter)
        {
            if (Debug) W($"{what}, {dist:0.0} m, moved {moved.magnitude:0.00}{shooter}");
        }

        internal static void AfterEnemyBullet(ANBHVRGunBase gun, Shot shot)
        {
            gun.tmpEnemyBulletdirection = shot.Direction;
            gun.tmpEnemyBulletspread = shot.Spread;
            if (shot.Shotgun) gun.ShotRadius = shot.ShotRadius;
        }

        internal static void PlayerHurt(string type, float dmg)
        {
            sHurt++;
            if (!Debug) return;
            bool recent = Now - lastShotAt < 0.5;
            W($"hurt: {type} {dmg:0.#}{(recent ? "" : " (not from a logged shot)")}");
        }

        // The game's own timed slow motion (scriptedSlowmotionMin / VeryMed / Max -> scriptedSlowmotionExecute):
        // it saves the current slow-motion step, sets the new one (time scale, sound pitch, the slow-motion
        // post effect), waits the time in REAL seconds and puts the saved step back. The wrappers do nothing
        // while another scripted slow motion runs. Skipped while your own slow motion (right B) is on,
        // because the game would restore "off" afterwards and end yours.
        static double slowAt = -99;
        static string SlowDown()
        {
            var game = ANBStaticGameManager.ANBmain;
            if (!PhysicalDodgeMod.Alive(game) || game.Paused) return "";
            if (Now - slowAt < PhysicalDodgeMod.SlowMotionCooldown.Value) return " (slow-mo cooldown)";
            if (game.scriptedSlowmotionActive) return " (slow-mo running)";
            if (game.Slowmotion > 0) return " (your slow-mo on)";
            float secs = Mathf.Clamp(PhysicalDodgeMod.SlowMotionSeconds.Value, 0.2f, 5f);
            switch (PhysicalDodgeMod.SlowMotionStrength.Value)
            {
                case <= 1: game.scriptedSlowmotionMin(secs); break;
                case 2: game.scriptedSlowmotionVeryMed(secs); break;
                default: game.scriptedSlowmotionMax(secs); break;
            }
            slowAt = Now;
            sSlow++;
            return " -> slow-mo";
        }

        // Who fired, for the log: the enemy's id (same #number as Enemy Awareness Log), how far its gun is
        // from its body (the NavMesh agent the game moves), its state, and whether it is in your view (the
        // game's own isInView: renderer visible and not blocked).
        static string Shooter(ANBHVRGunBase gun, Vector3 muzzle, Vector3 you)
        {
            try
            {
                var n = gun.UsedByNPC;
                if (!PhysicalDodgeMod.Alive(n)) return " | no NPC";
                var body = PhysicalDodgeMod.Alive(n.agentTransform) ? n.agentTransform : n.transform;
                Vector3 b = body.position;
                string state = n.isAttacking ? "attack" : n.isHunting ? "hunt" : "idle";
                return $" | #{Math.Abs(n.GetInstanceID()) % 100000} {state}, {(n.targetInSight ? "sees" : "BLIND")}, {(n.isInView ? "in view" : "OUT of view")}, " +
                       $"body {Vector3.Distance(b, you):0.0}, gun {Vector3.Distance(muzzle, b + Vector3.up * (muzzle.y - b.y)):0.0}";
            }
            catch (Exception e) { return $" | {e.GetType().Name}"; }
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

        // Nine parallel rays: the line itself and eight around it at DodgeMargin. None may touch any of
        // your hit colliders.
        static bool Clears(Vector3 origin, Vector3 d, float range)
        {
            Vector3 u = Vector3.Cross(d, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(d, Vector3.forward);
            u.Normalize();
            Vector3 w = Vector3.Cross(d, u);
            float m = PhysicalDodgeMod.DodgeMargin.Value;
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
        static bool Colliders(ANBGameLogic game)
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

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.spawnNPC))]
    internal static class SpawnPatch
    {
        static void Postfix(ANBNpcSpawner __instance)
        {
            try { Dodge.Spawner = __instance; } catch { }
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
