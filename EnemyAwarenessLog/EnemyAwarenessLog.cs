using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.AI;

[assembly: MelonInfo(typeof(EnemyAwarenessLog.EnemyAwarenessLogMod), "Enemy Awareness Log", "0.5.2", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace EnemyAwarenessLog
{
    // Diagnostic only: changes nothing in the game. Logs how enemies find the player, to check two
    // suspicions against real play:
    //
    // 1. "They always know where I am." ANBBasicNPC.AL_targetCheck, while the player is out of sight
    //    and lostTargetTime > 0, runs
    //      if (lostTargetTime > lostTargetTime - loseTargetPositionUpdatesAfter || !canLoseTarget)
    //          markLastKnownTargetPosition(attackTarget.position);
    //    Same field on both sides, so any loseTargetPositionUpdatesAfter > 0 (ctor default 17) makes it
    //    always true: "last known position" follows the player's live position, and attack navigation
    //    walks to it. Each unseen chase is logged with whether its last known position followed the
    //    player after they moved away from where the enemy last saw them.
    //
    // 2. "They all come through the same door." ANBNpcSpawner.getSpawnPoint, with SpawnPointOrder =
    //    Closest, re-sorts the spawn points by distance to the player and takes the first valid one.
    //    Each spawn is logged with its spawn point, and each enemy's first sight of the player with
    //    where it stood. Per-wave summaries group both.
    //
    // Also: which floor each enemy is on relative to you, who is still alive when a wave or scene
    // ends, your stealth visibility (the game's ANBPlayerDetector: height, light, movement,
    // gunfire), and how many enemy shots the game made harmless (see ROADMAP.md).
    //
    // Output: UserData\EnemyAwarenessLog\session-<time>.log. The MelonLoader console gets only the
    // scene and wave headers (to line up other mods' logs) and warnings. 0.5.0 keeps the file lean on
    // purpose: non-events ("nobody reacted", arrows, stance flips, door opens) are counted in the wave
    // summary instead of logged one by one.
    public class EnemyAwarenessLogMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, LogStateChanges, LogAlerts;
        internal static MelonPreferences_Entry<float> SampleInterval;

        static StreamWriter File;
        static float nextSample;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("EnemyAwarenessLog", "Enemy Awareness Log");
            Enabled = c.CreateEntry("Enabled", true, description: "Log enemy awareness: spawns, first sight of you, unseen chases, lost-target timers, wave summaries.");
            SampleInterval = c.CreateEntry("SampleInterval", 0.25f, description: "Seconds between checks of each enemy.");
            LogStateChanges = c.CreateEntry("LogStateChanges", true, description: "Log every enemy state change (idle / investigate / attack / hunt / flee).");
            LogAlerts = c.CreateEntry("LogAlerts", true, description: "Log alerts and noises that wake enemies (rate-limited).");
            try
            {
                string dir = Path.Combine(MelonEnvironment.UserDataDirectory, "EnemyAwarenessLog");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File = new StreamWriter(path) { AutoFlush = true };
                Log.Msg($"loaded - logging to {path}");
            }
            catch (Exception ex) { Log.Warning($"no log file: {ex.Message}"); }
        }

        public override void OnApplicationQuit()
        {
            Tracker.FlushChases("game closed");
            Tracker.Summary("game closed");
            File?.Dispose();
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Tracker.FlushChases("scene ended");
            Tracker.Summary("scene change");
            Tracker.Reset();
            W($"---- scene '{sceneName}' ----", true);
        }

        public override void OnUpdate()
        {
            if (!Enabled.Value || Time.time < nextSample) return;
            nextSample = Time.time + Mathf.Max(0.05f, SampleInterval.Value);
            Tracker.Sample();
        }

        internal static void W(string s, bool console = false)
        {
            string line = $"[t={Time.time,7:0.0}] {s}";
            if (console) Log.Msg(line);
            try { File?.WriteLine(line); } catch { }
        }

        static readonly HashSet<string> warned = new();
        internal static void Warn(string where, Exception ex)
        {
            string key = where + ex.GetType().Name;
            if (warned.Add(key)) Log.Warning($"{where}: {ex.Message}");
        }
    }

    internal static class Tracker
    {
        static void W(string s, bool console = false) => EnemyAwarenessLogMod.W(s, console);

        internal class Track
        {
            public ANBBasicNPC Npc;
            public IntPtr Ptr;
            public int Id;
            public string Name = "";
            public string SpawnPoint;
            public float SpawnT;
            public bool Dead;
            public string State = "";
            public bool HasSeen, ContactLogged;
            public Vector3 LastSeen;
            // lost-target timer
            public float TimerT = -1, TimerVal;
            // unseen chase ("episode")
            public bool Ep;
            public float EpT;
            public Vector3 EpRef;
            public bool EpRefIsSighting;
            public string EpStates = "";
            public int EpSamples, EpMovedSamples, EpTracked, EpUnseenWrites, EpDestN;
            public float EpMaxMove, EpDestPlayer, EpDestRef, EpTravel, EpStill;
            public Vector3 EpLastNpcPos;
            public bool EpFlank;
            // stuck detection
            public Vector3 MoveRef;
            public float MoveT, StuckReportT = -99;
            // last sample, for the "still alive" list (the objects may be gone by then)
            public bool Sampled, SawYou;
            public Vector3 LastPos;
            public int Uid;
            // "#<n>" = the enemy's Unity instance id; Enemy Awareness Fix prints the same, so lines
            // from both mods can be matched.
            public string Tag => $"E{Id} #{Uid}";
        }

        static readonly Dictionary<IntPtr, Track> Tracks = new();
        static int nextId = 1;
        static readonly HashSet<string> npcConfigsSeen = new();
        static readonly HashSet<IntPtr> spawnersLogged = new();
        static readonly List<ANBNpcSpawner> spawners = new();
        static bool encounterLogged;

        // ---- per-wave stats
        class Cluster { public Vector3 Sum; public int N; public Vector3 Center => Sum / N; public Dictionary<string, int> Dirs = new(); }
        static float waveStart = -1;
        static string waveLabel = "";
        static readonly Dictionary<string, int> spawnsByPoint = new();
        static readonly List<Cluster> contacts = new();
        static int epCount, epTracked, epSearched, epUnclear, timerRuns;
        static float timerRealSum, timerValSum;
        static bool haveWaveStartPos;
        static Vector3 waveStartPos;
        static float maxMoveFromStart, pathLen;
        static Vector3 lastPlayerPos, lastPlayerFloor;

        internal static void Reset()
        {
            Tracks.Clear();
            spawners.Clear();
            spawnersLogged.Clear();
            waveSeen.Clear();
            encounterLogged = false;
            Stealth.Reset();
            ResetWave("");
        }

        static void ResetWave(string label)
        {
            waveStart = Time.time;
            waveLabel = label;
            spawnsByPoint.Clear();
            contacts.Clear();
            epCount = epTracked = epSearched = epUnclear = timerRuns = 0;
            Sound.ResetCounters();
            Stealth.ResetWave();
            timerRealSum = timerValSum = 0;
            haveWaveStartPos = false;
            maxMoveFromStart = pathLen = 0;
        }

        // ---------------------------------------------------------------- helpers

        static bool Same(Component a, Component b) => a != null && b != null && a.Pointer == b.Pointer;

        // Where the enemy really is. ANBBasicNPC sits on a root object that stays where the enemy
        // spawned; the body moves with the NavMeshAgent's object (agentTransform). The game itself
        // measures distance to the player from visionBase (the eyes).
        internal static Vector3 Body(ANBBasicNPC n)
        {
            try
            {
                var t = n.agentTransform;
                if (t != null) return t.position;
                t = n.visionBase;
                if (t != null) return t.position;
            }
            catch { }
            return n.transform.position;
        }

        static Transform PlayerT()
        {
            try { return ANBStaticGameManager.ANBmain?.PlayerHitTarget; } catch { return null; }
        }

        internal static string P(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        static int floorFrame = -1;
        static Vector3 floorPos;

        // Your position at floor level (PlayerHitTarget is at chest height). Same method as Enemy
        // Awareness Fix: sample the navmesh a metre lower, so it lands on your own floor.
        internal static Vector3 Floor(Vector3 pp)
        {
            if (Time.frameCount == floorFrame) return floorPos;
            floorFrame = Time.frameCount;
            Vector3 low = pp + Vector3.down;
            floorPos = low + Vector3.down * 0.3f;
            try
            {
                if (NavMesh.SamplePosition(low, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) floorPos = hit.position;
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("floor sample", ex); }
            return floorPos;
        }

        // Floors are about 3 m apart: more than 1.5 m of height between two feet positions is
        // another floor. pos is at floor level (an enemy's feet, a spawn point, a door).
        static string FloorNote(Vector3 pos, Vector3 pp)
        {
            float dy = pos.y - Floor(pp).y;
            return Mathf.Abs(dy) <= 1.5f ? "" : dy > 0 ? ", FLOOR ABOVE you" : ", FLOOR BELOW you";
        }

        static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        // Where 'pos' is as seen from the player: compass sector plus front/left/right/behind relative to the view.
        static string Where(Vector3 pos, Vector3 player)
        {
            Vector3 d = pos - player; d.y = 0;
            if (d.sqrMagnitude < 0.01f) return "at you";
            float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            string comp = Compass[(int)Mathf.Repeat(Mathf.Round(ang / 45f), 8)];
            string rel = "";
            try
            {
                var cam = ANBStaticGameManager.MainCam;
                if (cam != null)
                {
                    Vector3 f = cam.transform.forward; f.y = 0;
                    float a = Vector3.SignedAngle(f, d, Vector3.up);
                    rel = Mathf.Abs(a) < 45 ? "in front" : Mathf.Abs(a) > 135 ? "BEHIND you" : a > 0 ? "to your right" : "to your left";
                }
            }
            catch { }
            return $"{d.magnitude:0.0} m {comp}{(rel.Length > 0 ? ", " + rel : "")}{FloorNote(pos, player)}";
        }

        static string StateOf(ANBBasicNPC n)
        {
            if (n.isDead) return "dead";
            if (n.isFleeing) return "flee";
            if (n.isAttacking) return n.isFlanking ? "attack+flank" : "attack";
            if (n.isHunting) return "hunt";
            if (n.isInvestigating) return "investigate";
            if (n.isFollowingPlayer) return "follow";
            if (n.isIdle) return "idle";
            return "other";
        }

        internal static Track Get(ANBBasicNPC npc, bool create = true)
        {
            if (npc == null) return null;
            IntPtr p = npc.Pointer;
            if (Tracks.TryGetValue(p, out var t)) return t;
            if (!create) return null;
            t = new Track { Npc = npc, Ptr = p, Id = nextId++, Name = npc.name, SpawnT = Time.time, Uid = Math.Abs(npc.GetInstanceID()) % 100000, MoveT = Time.time };
            Tracks[p] = t;
            LogNpcConfig(npc);
            return t;
        }

        // A pooled NPC coming back: same object, new enemy.
        static Track Respawn(ANBBasicNPC npc)
        {
            Tracks.Remove(npc.Pointer);
            return Get(npc);
        }

        static void LogNpcConfig(ANBBasicNPC n)
        {
            try
            {
                string cfg = $"canLoseTarget {n.canLoseTarget}, lostTargetTime min/max/spawn {n.lostTargetTimeMin:0.#}/{n.lostTargetTimeMax:0.#}/{n.lostTargetTimeSpawn:0.#}, " +
                             $"loseTargetPositionUpdatesAfter {n.loseTargetPositionUpdatesAfter:0.##}, ActionPulseRate {n.ActionPulseRate:0.##}, " +
                             $"view {n.viewRadius:0.#}/{n.viewRadiusEngaged:0.#} m {n.viewAngle:0.#}/{n.viewAngleEngaged:0.#} deg (normal/engaged), huntRadius {n.huntRadius:0.#}, " +
                             $"startAttackingPlayer {n.startAttackingPlayer}, alwaysAttackPlayerOnAlert {n.alwaysAttackPlayerOnAlert}, attackOnSight {n.attackOnSight}, " +
                             $"autoFollowPlayer {n.autoFollowPlayer}, canHear/SeeWorld {n.canHearWorld}/{n.canSeeWorld}";
                if (!npcConfigsSeen.Add(n.name + "|" + cfg)) return;
                bool leak = n.loseTargetPositionUpdatesAfter > 0 || !n.canLoseTarget;
                W($"enemy type '{n.name}': {cfg}");
                W($"   -> code predicts its last known position {(leak ? "FOLLOWS you while it can't see you" : "freezes when it loses sight of you")}" +
                  $"{(!n.canLoseTarget ? " (canLoseTarget is off: it never gives up)" : "")}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("npc config", ex); }
        }

        static void LogEncounterConfig()
        {
            if (encounterLogged) return;
            try
            {
                var enc = ANBStaticGameManager.ANBmain?.encounterSystem;
                if (enc == null) return;
                encounterLogged = true;
                W($"encounter system: collective player position {enc.useCollectivePlayerposition} (keep {enc.lastKnownPlayerPositionKeepTime:0.#} s), " +
                  $"flanking {enc.useFlanking} (max {enc.maxFlankingEnemies:0}), open enemy slots {enc.openEnemySlots:0}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("encounter config", ex); }
        }

        internal static void LogSpawner(ANBNpcSpawner s, string why)
        {
            if (s == null) return;
            if (!spawners.Any(x => x.Pointer == s.Pointer)) spawners.Add(s);
            if (!spawnersLogged.Add(s.Pointer)) return;
            try
            {
                var pts = s.spawnPoints;
                int np = pts?.Count ?? 0;
                W($"spawner '{s.name}' ({why}): behaviour {s.SpawnBehaviour}, point order {s.SpawnPointOrder}, {np} points, " +
                  $"point cooldown {s.spawnPointCooldown:0.#} s / seen {s.spawnPointVisCooldown:0.#} s, " +
                  // initSpawner squares these in place; spawnPointValidation compares them to sqrMagnitude.
                  $"checks: hidden {s.SpawnpointCheckHidden}, min dist to you {(s.SpawnpointCheckPlayerdistance ? Mathf.Sqrt(s.minSpawnDistanceToPlayer).ToString("0.#") + " m" : "off")}, " +
                  $"max radius {(s.SpawnpointCheckRadius ? Mathf.Sqrt(s.maxSpawnRadius).ToString("0.#") + " m" : "off")}, dynamic {s.dynamicSpawning}, " +
                  $"waves: size {s.waveSize:0}, max at once {s.maxEnemiesAtOnce:0}, total {s.totalSpawns:0}, max waves {s.maxWaves}");
                W($"   spawner overrides (useBasicOverrides {s.useBasicOverrides}): startAttackingPlayer {s.startAttackingPlayer}, canLoseTarget {s.canLoseTarget}, " +
                  $"lostTargetTimeSpawn {s.lostTargetTimeSpawn:0.#}, alwaysAttackPlayerOnAlert {s.alwaysAttackPlayerOnAlert}, attackOnSight {s.attackOnSight}");
                if (np > 0)
                {
                    var names = new List<string>();
                    for (int i = 0; i < np; i++)
                    {
                        var sp = pts[i];
                        if (sp != null) names.Add($"'{sp.name}' {P(sp.transform.position)}");
                    }
                    W($"   points: {string.Join(", ", names)}");
                }
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("spawner config", ex); }
        }

        // ---------------------------------------------------------------- spawns

        static readonly Dictionary<IntPtr, IntPtr> spawnSnapshot = new();
        static IntPtr lastSpawnPtr;
        static int lastSpawnFrame = -1;

        internal static void BeforeSpawn(ANBNpcSpawner s)
        {
            spawnSnapshot.Clear();
            try
            {
                LogSpawner(s, "first spawn");
                var pts = s.spawnPoints;
                if (pts == null) return;
                for (int i = 0; i < pts.Count; i++)
                {
                    var sp = pts[i];
                    if (sp == null) continue;
                    var m = sp.mySpawnie;
                    spawnSnapshot[sp.Pointer] = m == null ? IntPtr.Zero : m.Pointer;
                }
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("before spawn", ex); }
        }

        internal static void AfterSpawn(ANBNpcSpawner s, bool ok)
        {
            if (!ok) return;
            try
            {
                var pts = s.spawnPoints;
                ANBNpcSpawnPoint used = null;
                ANBBasicNPC npc = null;
                if (pts != null)
                    for (int i = 0; i < pts.Count && used == null; i++)
                    {
                        var sp = pts[i];
                        if (sp == null) continue;
                        var m = sp.mySpawnie;
                        if (m == null) continue;
                        if (!spawnSnapshot.TryGetValue(sp.Pointer, out var before) || before != m.Pointer) { used = sp; npc = m; }
                    }
                var player = PlayerT();
                Vector3 pp = player != null ? player.position : Vector3.zero;
                if (npc == null)
                {
                    W($"spawn by '{s.name}' (wave {s.waveSurvived + 1}): spawn point not identified");
                    return;
                }
                // Enemy Awareness Fix re-issues spawnNPC from inside its prefix, so the outer
                // postfix can see the same spawn again in the same frame.
                if (npc.Pointer == lastSpawnPtr && Time.frameCount == lastSpawnFrame) return;
                lastSpawnPtr = npc.Pointer;
                lastSpawnFrame = Time.frameCount;
                var t = Respawn(npc);
                t.SpawnPoint = used.name;
                t.State = StateOf(npc);
                spawnsByPoint[used.name] = spawnsByPoint.TryGetValue(used.name, out int c) ? c + 1 : 1;
                W($"spawn {t.Tag} at '{used.name}', {Where(used.transform.position, pp)}, {t.State}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("after spawn", ex); }
        }

        // waveSurvived per spawner, polled: the wave counter is advanced inside coroutines.
        static readonly Dictionary<IntPtr, int> waveSeen = new();

        static void CheckWaves()
        {
            foreach (var s in spawners)
            {
                try
                {
                    if (s == null || s.WasCollected) continue;
                    int w = s.waveSurvived;
                    if (waveSeen.TryGetValue(s.Pointer, out int old) && old == w) continue;
                    bool first = !waveSeen.ContainsKey(s.Pointer);
                    waveSeen[s.Pointer] = w;
                    if (first && w == 0) { if (waveLabel.Length == 0) waveLabel = $"wave 1 ('{s.name}')"; continue; }
                    Summary($"wave {w} survived");
                    ResetWave($"wave {w + 1} ('{s.name}')");
                    W($"==== wave {w + 1} ('{s.name}'): size {s.waveSize:0}, max at once {s.maxEnemiesAtOnce:0}, total {s.totalSpawns:0}", true);
                }
                catch (Exception ex) { EnemyAwarenessLogMod.Warn("wave check", ex); }
            }
        }

        // ---------------------------------------------------------------- per-enemy sampling

        internal static void Sample()
        {
            var player = PlayerT();
            if (player == null) return;
            Vector3 pp = player.position;
            LogEncounterConfig();
            CheckWaves();

            if (!haveWaveStartPos) { waveStartPos = pp; lastPlayerPos = pp; haveWaveStartPos = true; }
            pathLen += Flat(pp, lastPlayerPos);
            lastPlayerPos = pp;
            lastPlayerFloor = Floor(pp);
            Stealth.Sample(pp, lastPlayerFloor);
            maxMoveFromStart = Mathf.Max(maxMoveFromStart, Flat(pp, waveStartPos));

            try
            {
                var list = ANBStaticGameManager.ANBmain?.encounterSystem?.allEnemies;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var n = list[i];
                        if (n != null && n.gameObject.activeInHierarchy) Get(n);
                    }
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("enemy list", ex); }

            var gone = new List<IntPtr>();
            foreach (var t in Tracks.Values.ToList())   // SampleOne can re-add a respawned enemy
            {
                try
                {
                    var n = t.Npc;
                    if (n == null || n.WasCollected) { gone.Add(t.Ptr); continue; }
                    SampleOne(t, n, pp);
                }
                catch (Exception ex) { EnemyAwarenessLogMod.Warn("sample", ex); }
            }
            foreach (var p in gone) Tracks.Remove(p);
        }

        static void SampleOne(Track t, ANBBasicNPC n, Vector3 pp)
        {
            if (n.isDead)
            {
                if (!t.Dead)
                {
                    t.Dead = true;
                    EndEpisode(t, pp, "died");
                    if (EnemyAwarenessLogMod.LogStateChanges.Value) W($"{t.Tag} died");
                    t.State = "dead";
                }
                return;
            }
            if (t.Dead) t = Respawn(n);   // came back from the pool without a spawner hook
            if (!n.gameObject.activeInHierarchy) return;
            t.Sampled = true;
            t.LastPos = Body(n);
            t.SawYou = n.targetInSight;
            CheckStuck(t, n, pp);

            string st = StateOf(n);
            if (st != t.State)
            {
                if (EnemyAwarenessLogMod.LogStateChanges.Value && t.State.Length > 0)
                    W($"{t.Tag}: {t.State} -> {st}, {Where(Body(n), pp)}");
                t.State = st;
                if (t.Ep && !t.EpStates.EndsWith(st)) t.EpStates += ">" + st;
            }

            var pt = n.Playertarget;
            bool att = n.isAttacking, hunt = n.isHunting;
            bool onPlayer = att ? Same(n.attackTarget, pt) : hunt && Same(n.huntTarget, pt);
            bool seen = att && onPlayer && n.targetInSight;

            if (seen)
            {
                if (t.Ep) EndEpisode(t, pp, "saw you again");
                t.HasSeen = true;
                t.LastSeen = pp;
                if (!t.ContactLogged)
                {
                    t.ContactLogged = true;
                    Vector3 ep = Body(n);
                    string where = Where(ep, pp);
                    AddContact(ep, where);
                    W($"{t.Tag} FIRST SAW YOU: {where}, {Time.time - t.SpawnT:0} s after spawn");
                }
                t.TimerT = -1;
            }
            else if (onPlayer)
            {
                if (!t.Ep) StartEpisode(t, pp);
                UpdateEpisode(t, n, pp);
            }
            else if (t.Ep) EndEpisode(t, pp, $"stopped chasing ({st})");
        }

        // An enemy that is after you (attack / hunt / investigate) but hasn't moved 0.5 m for 8 s:
        // say why, as far as the navigation state tells. Repeats every 20 s while it stays put.
        static void CheckStuck(Track t, ANBBasicNPC n, Vector3 pp)
        {
            float now = Time.time;
            Vector3 body = Body(n);
            if (Vector3.Distance(body, t.MoveRef) > 0.5f) { t.MoveRef = body; t.MoveT = now; return; }
            bool busy = n.isAttacking || n.isHunting || n.isInvestigating;
            if (!busy || now - t.MoveT < 8f || now - t.StuckReportT < 20f) return;
            if (Flat(body, pp) > 50f) return;                    // you're far away (dead, elevator): not a finding
            t.StuckReportT = now;
            string nav = "no agent";
            try
            {
                var ag = n.agent;
                if (ag != null)
                {
                    nav = $"agent {(ag.isActiveAndEnabled ? "on" : "OFF")}";
                    if (ag.isActiveAndEnabled && ag.isOnNavMesh)
                    {
                        Vector3 d = ag.destination;
                        nav += $"{(ag.isStopped ? ", STOPPED" : "")}, path {ag.pathStatus}, remaining {ag.remainingDistance:0.0}/stop {ag.stoppingDistance:0.0} m, " +
                               $"dest {Flat(d, body):0.0} m from it, {Flat(d, pp):0.0} m from you, y {d.y:0.0}";
                    }
                    else if (ag.isActiveAndEnabled) nav += ", NOT ON NAVMESH";
                }
            }
            catch (Exception ex) { nav += $" (read failed: {ex.Message})"; }
            string cover = n.isCovering ? ", in cover" : "";
            W($"STUCK? {t.Tag} still {now - t.MoveT:0} s ({StateOf(n)}{cover}), {Where(body, pp)} | {nav} | " +
              $"last-known {Flat(n.lastKnownPosition, pp):0.0} m from you{Sound.DoorNote(n.Pointer)}");
        }

        // Chases still open when the scene ends get their line too.
        internal static void FlushChases(string why)
        {
            foreach (var t in Tracks.Values.ToList())
                try { if (t.Ep) EndEpisode(t, lastPlayerPos, why); } catch { }
        }

        static void StartEpisode(Track t, Vector3 pp)
        {
            t.Ep = true;
            t.EpT = Time.time;
            t.EpRefIsSighting = t.HasSeen;
            t.EpRef = t.HasSeen ? t.LastSeen : pp;
            t.EpStates = t.State;
            t.EpSamples = t.EpMovedSamples = t.EpTracked = t.EpUnseenWrites = t.EpDestN = 0;
            t.EpMaxMove = t.EpDestPlayer = t.EpDestRef = t.EpTravel = t.EpStill = 0;
            t.EpLastNpcPos = Body(t.Npc);
            t.EpFlank = false;
        }

        static void UpdateEpisode(Track t, ANBBasicNPC n, Vector3 pp)
        {
            t.EpSamples++;
            float move = Flat(pp, t.EpRef);
            t.EpMaxMove = Mathf.Max(t.EpMaxMove, move);
            if (move >= 3f)
            {
                t.EpMovedSamples++;
                if (Vector3.Distance(n.lastKnownPosition, pp) <= 1.5f) t.EpTracked++;
            }
            var agent = n.agent;
            if (agent != null && agent.isActiveAndEnabled && agent.hasPath)
            {
                Vector3 d = agent.destination;
                t.EpDestPlayer += Flat(d, pp);
                t.EpDestRef += Flat(d, t.EpRef);
                t.EpDestN++;
            }
            if (n.isFlanking) t.EpFlank = true;
            Vector3 np = Body(n);
            float step = Flat(np, t.EpLastNpcPos);
            t.EpTravel += step;
            if (step < 0.05f) t.EpStill += EnemyAwarenessLogMod.SampleInterval.Value;
            t.EpLastNpcPos = np;

            float lt = n.lostTargetTime;
            if (lt > 0 && t.TimerT < 0) { t.TimerT = Time.time; t.TimerVal = lt; }
            else if (lt <= 0) t.TimerT = -1;
        }

        static void EndEpisode(Track t, Vector3 pp, string why)
        {
            if (!t.Ep) return;
            t.Ep = false;
            float dur = Time.time - t.EpT;
            if (dur < 1f) return;   // a blink out of sight

            string verdict;
            bool unclear = t.EpMovedSamples < 3;
            if (unclear) { verdict = "unclear (you moved < 3 m)"; epUnclear++; }
            else
            {
                float f = (float)t.EpTracked / t.EpMovedSamples;
                string k = $"{t.EpTracked}/{t.EpMovedSamples} near you";
                if (f >= 0.6f) { verdict = $"LIVE-TRACKED ({k})"; epTracked++; }
                else if (f <= 0.2f) { verdict = $"searched ({k})"; epSearched++; }
                else { verdict = $"partly tracked ({k})"; epUnclear++; }
            }
            epCount++;
            bool stuck = t.EpStill > 0.8f * dur;
            if (unclear && !stuck) return;                     // counted in the summary; a line says nothing
            string dest = t.EpDestN > 0 ? $", dest {t.EpDestPlayer / t.EpDestN:0.0} m from you / {t.EpDestRef / t.EpDestN:0.0} m from ref" : "";
            W($"{t.Tag} unseen {dur:0} s [{t.EpStates}] {(t.EpRefIsSighting ? "after losing you" : "never saw you")}: {verdict}, you moved {t.EpMaxMove:0.0} m; " +
              $"it walked {t.EpTravel:0} m, still {t.EpStill:0} s{(stuck ? " (STUCK?)" : "")}{dest}{(t.EpFlank ? ", flanked" : "")}; end {why}");
        }

        // Called from the markLastKnownTargetPosition prefix.
        internal static void MarkLastKnown(ANBBasicNPC n)
        {
            var t = Get(n, false);
            if (t == null || !t.Ep) return;
            if (!n.targetInSight) t.EpUnseenWrites++;
        }

        internal static void LooseTarget(ANBBasicNPC n)
        {
            try
            {
                var t = Get(n);
                string timer = t.TimerT >= 0
                    ? $"after {Time.time - t.TimerT:0.0} s from {t.TimerVal:0.#}"
                    : "(timer start not seen)";
                if (t.TimerT >= 0) { timerRuns++; timerRealSum += Time.time - t.TimerT; timerValSum += t.TimerVal; }
                t.TimerT = -1;
                W($"{t.Tag} lost-target timer ran out {timer} -> {(n.canLoseTarget ? "gives up (hunt/idle)" : "restarts attack")}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("lose target", ex); }
        }

        internal static void HuntTarget(ANBBasicNPC n, Vector3 pos)
        {
            try
            {
                var t = Get(n);
                var player = PlayerT();
                if (player == null) return;
                W($"{t.Tag} hunt point {Flat(pos, player.position):0.0} m from you" +
                  $"{(t.HasSeen ? $", {Flat(pos, t.LastSeen):0.0} m from where it last saw you" : "")}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("hunt target", ex); }
        }

        // ---------------------------------------------------------------- alerts

        static readonly Dictionary<string, float> alertNext = new();
        static readonly Dictionary<string, int> alertSuppressed = new();

        internal static void Alert(string key, Func<string> text, float window = 2f)
        {
            if (!EnemyAwarenessLogMod.LogAlerts.Value) return;
            float now = Time.time;
            if (alertNext.TryGetValue(key, out float next) && now < next)
            {
                alertSuppressed[key] = alertSuppressed.TryGetValue(key, out int c) ? c + 1 : 1;
                return;
            }
            alertNext[key] = now + window;
            int sup = alertSuppressed.TryGetValue(key, out int s) ? s : 0;
            alertSuppressed[key] = 0;
            try { W(text() + (sup > 0 ? $" (+{sup} similar in the last {window:0} s)" : "")); }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("alert", ex); }
        }

        internal static string Name(Component c) => c == null ? "none" : c.name;

        internal static string NpcTag(ANBBasicNPC n) => Get(n)?.Tag ?? "?";

        // ---------------------------------------------------------------- summaries

        static void AddContact(Vector3 pos, string where)
        {
            string dir = where.Split(',')[0].Split(' ').Last();
            Cluster best = null;
            foreach (var c in contacts)
                if (Flat(c.Center, pos) <= 3f) { best = c; break; }
            if (best == null) contacts.Add(best = new Cluster());
            best.Sum += pos;
            best.N++;
            best.Dirs[dir] = best.Dirs.TryGetValue(dir, out int k) ? k + 1 : 1;
        }

        internal static void Summary(string why)
        {
            if (waveStart < 0) return;
            if (spawnsByPoint.Count == 0 && contacts.Count == 0 && epCount == 0 && timerRuns == 0 && !Sound.Any) return;
            W($"==== SUMMARY {waveLabel} ({why}), {Time.time - waveStart:0} s ====");
            W($"  you: {(maxMoveFromStart < 3f ? "stayed put" : "moved around")}, at most {maxMoveFromStart:0.0} m from where the wave started, walked {pathLen:0} m");
            if (spawnsByPoint.Count > 0)
                W($"  spawn points used: {string.Join(", ", spawnsByPoint.OrderByDescending(k => k.Value).Select(k => $"'{k.Key}' x{k.Value}"))}");
            if (contacts.Count > 0)
            {
                int total = contacts.Sum(c => c.N);
                var parts = contacts.OrderByDescending(c => c.N).Select(c =>
                    $"{P(c.Center)} x{c.N} ({string.Join("/", c.Dirs.OrderByDescending(d => d.Value).Select(d => d.Key))})");
                W($"  first sight of you ({total} enemies, grouped within 3 m): {string.Join(", ", parts)}");
            }
            if (epCount > 0)
                W($"  unseen chases: {epCount} - live-tracked {epTracked}, searched where they lost you {epSearched}, unclear {epUnclear}");
            if (timerRuns > 0)
                W($"  lost-target timer ran out {timerRuns}x: avg {timerRealSum / timerRuns:0.0} s game time from avg value {timerValSum / timerRuns:0.#}");
            Sound.SummaryLines();
            Stealth.SummaryLine();
            SurvivorLines();
            ResetWave(waveLabel);
        }

        static bool StillAlive(Track t)
        {
            if (!t.Sampled || t.Dead) return false;
            try { if (t.Npc != null && !t.Npc.WasCollected) return !t.Npc.isDead && t.Npc.gameObject.activeInHierarchy; }
            catch { }
            return true;   // the scene is already gone: go by the last sample
        }

        // Who is still alive, where they last were, and whether they stood still - from the last
        // sample, since at a scene change the enemies are already destroyed.
        static void SurvivorLines()
        {
            var alive = Tracks.Values.Where(StillAlive).ToList();
            if (alive.Count == 0) return;
            float now = Time.time;
            W($"  still alive ({alive.Count}), where they last were:");
            foreach (var t in alive)
            {
                float dy = t.LastPos.y - lastPlayerFloor.y;
                string floor = Mathf.Abs(dy) <= 1.5f ? "your floor" : dy > 0 ? "FLOOR ABOVE you" : "FLOOR BELOW you";
                W($"    {t.Tag} {t.State}, {Flat(t.LastPos, lastPlayerPos):0.0} m from you, {floor}, " +
                  $"{(t.SawYou ? "sees you" : "doesn't see you")}, still {now - t.MoveT:0} s{Sound.DoorNote(t.Ptr)}");
            }
        }
    }

    // Player weapon use and what each sound alerted. The bow release calls no alert in the game
    // (only FireBullet -> GunFireAlert does), but an arrow hitting the world (StabWorld ->
    // SpawnDecal) can make an "impact" noise, physics sounds make "noise", and a door kick calls
    // QuickAlertToPlayer. AlertAllNPCs calls StartAttack / StartHunt on each enemy that hears, so
    // hooking those while inside it names exactly who reacted.
    internal static class Sound
    {
        static void W(string s) => EnemyAwarenessLogMod.W(s);

        static string lastAction;
        static float lastActionT = -99;
        static int gunShots, silencedShots, bowShots, arrowHits, doorKicks, shatters, enemyShots, enemyShotsHarmless;
        static int doorLoops, longestDoorStreak;
        internal static int hitGraces;
        static readonly Dictionary<string, int> alerts = new(), reactions = new();

        // Cause of the alert being processed: set by the prefix of whatever raised it (a shot, a
        // noise, a door kick, a breakable), cleared by its postfix. AlertAllNPCs can nest.
        static string cause, curKey, curDesc;
        static int depth;
        static readonly List<string> reacted = new();
        static readonly HashSet<IntPtr> reactedPtrs = new();

        internal static bool Any => gunShots + bowShots + arrowHits + doorKicks + shatters > 0 || alerts.Count > 0;

        internal static void ResetCounters()
        {
            gunShots = silencedShots = bowShots = arrowHits = doorKicks = shatters = enemyShots = enemyShotsHarmless = hitGraces = 0;
            doorLoops = longestDoorStreak = 0;
            alerts.Clear();
            reactions.Clear();
        }

        static void Action(string what) { lastAction = what; lastActionT = Time.time; }
        static string Recent => Time.time - lastActionT <= 1.5f ? lastAction : null;
        static void SetCause(string c) { if (depth == 0) cause = c; }
        internal static void ClearCause() { if (depth == 0) cause = null; }

        // An enemy bullet the game marks "cooldowned" (warning shot, first second of fire, or the
        // grace after you hit someone) uses HitLayerMaskEnemyCooldowned - most likely it can't hit you.
        internal static void Gunshot(ANBHVRGunBase gun, float fromEnemy, bool silenced, bool cooldowned)
        {
            if (fromEnemy > 0)
            {
                enemyShots++;
                if (cooldowned) enemyShotsHarmless++;
                SetCause("enemy gunfire");
                return;
            }
            gunShots++;
            if (silenced) silencedShots++;
            string what = silenced ? "your silenced shot" : "your gunshot";
            Action(what);
            SetCause(what);
            W($"you fired '{Tracker.Name(gun)}'{(silenced ? " (silenced)" : "")}");
        }

        internal static void BowShot()
        {
            bowShots++;
            Action("your bow shot");                          // counted only
        }

        internal static void ArrowHitWorld()
        {
            arrowHits++;
            Action("your arrow hitting the world");
            SetCause("your arrow hitting the world");
        }

        // ANBPhysicsDoor.kickDoor (run by openDoor / closeDoor / toggleDoor) plays the kick sound and
        // calls QuickAlertToPlayer a moment later, whoever opened the door. Remember who did.
        static string lastDoor;
        static float lastDoorT = -99;

        internal static void DoorOpened(Component door, Transform origin, bool fromEnemy, bool kick)
        {
            string who = "?";
            try
            {
                var npc = origin != null ? origin.GetComponentInParent<ANBBasicNPC>() : null;
                who = npc != null ? Tracker.NpcTag(npc) : fromEnemy ? $"an enemy ('{Tracker.Name(origin)}')" : "you";
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("door opener", ex); }
            // Several doors share a name ('DoorModular'): the position says which one.
            string dn = $"'{Tracker.Name(door)}' at {Tracker.P(door.transform.position)}";
            lastDoor = $"{dn} by {who}";
            lastDoorT = Time.time;
            try
            {
                var npc = origin != null ? origin.GetComponentInParent<ANBBasicNPC>() : null;
                if (npc != null)
                {
                    float now = Time.time;
                    if (!doorsByNpc.TryGetValue(npc.Pointer, out var d) || d.Name != dn || now - d.LastT > 10f)
                        d = (dn, 0, now, now);
                    doorsByNpc[npc.Pointer] = (d.Name, d.Count + 1, d.FirstT, now);
                    // A door loop: the same enemy opening the same door again and again. Kicks alone can't
                    // show it (the game alerts only now and then), so count the opens.
                    if (d.Count + 1 > longestDoorStreak) longestDoorStreak = d.Count + 1;
                    if (d.Count + 1 == 10)
                    {
                        doorLoops++;
                        W($"DOOR LOOP: {who} opened {dn} 10x in {now - d.FirstT:0.0} s");
                    }
                }
            }
            catch { }
            // No line here: DoorKick logs the kick once; a loop shows as its "+N similar" count.
        }

        // Per enemy: the door it keeps opening (name, opens, first, last), for the STUCK? report.
        static readonly Dictionary<IntPtr, (string Name, int Count, float FirstT, float LastT)> doorsByNpc = new();

        internal static string DoorNote(IntPtr npc)
        {
            if (!doorsByNpc.TryGetValue(npc, out var d) || Time.time - d.LastT > 10f) return "";
            return $" | has opened door {d.Name} {d.Count}x in the last {Time.time - d.FirstT:0} s";
        }

        internal static void DoorKick()
        {
            doorKicks++;
            string by = Time.time - lastDoorT <= 3f ? lastDoor : "door (opener unknown)";
            SetCause($"door kick: {by}");
            Tracker.Alert("kick|" + by, () => $"door kick: {by} (alerts every enemy)", 10f);
        }

        internal static void Shatter(ANBBreakable b)
        {
            shatters++;
            string r = Recent;
            SetCause($"something shattering{(r != null ? " after " + r : "")} ('{Tracker.Name(b)}')");
        }

        internal static void Noise(string type, Transform source)
        {
            string r = Recent;
            SetCause($"'{type}' noise{(r != null ? " after " + r : "")} ('{Tracker.Name(source)}')");
        }

        internal static void AlertBegin(Transform target, Transform origin, bool attack, float radius, bool hearOnly)
        {
            if (depth++ > 0) return;
            reacted.Clear();
            reactedPtrs.Clear();
            string c = cause ?? (Tracker.Name(origin) == "NPC" ? "enemy callout/scream" : "unattributed alert");
            // Summary key: the cause without the object name in brackets.
            curKey = System.Text.RegularExpressions.Regex.Replace(c, @" \('[^']*'\)$", "");
            if (curKey.StartsWith("door kick")) curKey = curKey.EndsWith(" by you") ? "door kick by you" : "door kick by an enemy";
            alerts[curKey] = alerts.TryGetValue(curKey, out int a) ? a + 1 : 1;
            curDesc = $"{c}{(radius > 0 ? $" ({radius:0.#} m)" : "")}";
        }

        internal static void Reacted(ANBBasicNPC n, string what)
        {
            if (depth == 0 || n == null || !reactedPtrs.Add(n.Pointer)) return;
            try
            {
                var p = n.Playertarget;
                string d = p != null ? $" {Vector3.Distance(Tracker.Body(n), p.position):0} m from you" : "";
                bool already = n.isAttacking || n.isHunting;
                reacted.Add($"{Tracker.NpcTag(n)} {what}{d}{(already ? " (already chasing)" : "")}");
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("reacted", ex); }
        }

        internal static void AlertEnd()
        {
            if (--depth > 0) return;
            depth = 0;
            if (reacted.Count > 0)
            {
                int fresh = reacted.Count(r => !r.Contains("already"));
                reactions[curKey] = (reactions.TryGetValue(curKey, out int c) ? c : 0) + fresh;
                W($"HEARD {curDesc}: {string.Join("; ", reacted)}");
            }
            reacted.Clear();
        }

        internal static void SummaryLines()
        {
            if (!Any) return;
            W($"  weapons: your gunshots {gunShots} (silenced {silencedShots}), bow {bowShots}, arrows into the world {arrowHits}, " +
              $"door kicks {doorKicks}, shattered {shatters}; enemy gunshots {enemyShots} (harmless {enemyShotsHarmless}); your hits started the harmless window {hitGraces}x; door loops {doorLoops} (longest {longestDoorStreak} opens)");
            if (alerts.Count > 0)
            {
                var woke = alerts.Select(k => (k.Key, k.Value, R: reactions.TryGetValue(k.Key, out int r) ? r : 0)).Where(x => x.R > 0).OrderByDescending(x => x.R).ToList();
                W($"  alerts: {alerts.Values.Sum()}, newly woke enemies: " +
                  (woke.Count == 0 ? "none" : string.Join(", ", woke.Select(x => $"{x.Key} {x.R} (of {x.Value} alerts)"))));
            }
        }
    }

    // The game's stealth model: ANBPlayerDetector recomputes ANBGameLogic.playerVisibility every
    // frame from light on you, your height (head to feet), movement and recent gunfire (Expose).
    // Enemies multiply their view distance and the speed they notice you by it. Logged when your
    // stance changes, with the height of the point enemies aim at (PlayerHitTarget) next to your
    // head's, to see whether that point follows you down when you crouch.
    internal static class Stealth
    {
        static void W(string s) => EnemyAwarenessLogMod.W(s);

        static bool configLogged;
        static int n, nCrouch, nLow;
        static float sum, min, max;

        internal static void Reset()
        {
            configLogged = false;
            ResetWave();
        }

        internal static void ResetWave() { n = nCrouch = nLow = 0; sum = 0; min = 1; max = 0; }

        static string StanceOf(float h) => h >= 1.35f ? "standing" : h >= 0.95f ? "crouched" : "low";

        internal static void Sample(Vector3 pp, Vector3 floor)
        {
            try
            {
                var gl = ANBStaticGameManager.ANBmain;
                if (gl == null) return;
                var d = gl.playerDetector;
                if (!configLogged)
                {
                    configLogged = true;
                    W(d == null
                        ? "player detector: NONE in this scene - enemies see you the same crouched or standing, lit or dark"
                        : $"player detector: height {d.minHeight:0.##}-{d.maxHeight:0.##} m (weight {d.heightInfluence:0.##}), " +
                          $"movement up to {d.mobilityMaxSpeed:0.#} m/s (weight {d.mobilityInfluence:0.##}), light detection {d.useLightDetection} " +
                          $"(curve {d.lightCurvePower:0.##}), visibility {d.visibilityMin:0.##}-{d.visibilityMax:0.##}, " +
                          $"gunfire exposure {d.exposeTime:0.#} s x{d.exposureMultiplier:0.##}");
                }
                if (d == null) return;
                // No line per stance change any more (0.4.0 settled it: enemies aim at your head height in
                // every stance). The summary gives visibility and the share of time crouched / low.
                float v = gl.playerVisibility;
                n++; sum += v; min = Mathf.Min(min, v); max = Mathf.Max(max, v);
                string st = StanceOf(d.height);
                if (st == "low") nLow++; else if (st == "crouched") nCrouch++;
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("stealth", ex); }
        }

        internal static void SummaryLine()
        {
            if (n > 0) W($"  your visibility: avg {sum / n:0.00} (min {min:0.00}, max {max:0.00}); crouched {nCrouch * 100 / n}% of the time, low {nLow * 100 / n}%");
        }
    }

    // ------------------------------------------------------------------ Harmony

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.markLastKnownTargetPosition))]
    internal static class MarkLastKnownPatch
    {
        static void Prefix(ANBBasicNPC __instance)
        {
            if (!EnemyAwarenessLogMod.Enabled.Value) return;
            try { Tracker.MarkLastKnown(__instance); } catch (Exception ex) { EnemyAwarenessLogMod.Warn("markLastKnown", ex); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.LooseTarget))]
    internal static class LooseTargetPatch
    {
        static void Prefix(ANBBasicNPC __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.LooseTarget(__instance);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.UpdateHuntTarget))]
    internal static class UpdateHuntTargetPatch
    {
        static void Prefix(ANBBasicNPC __instance, Vector3 givenPos)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.HuntTarget(__instance, givenPos);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.alertWorld))]
    internal static class AlertWorldPatch
    {
        static void Prefix(ANBBasicNPC __instance, bool attack)
        {
            if (!EnemyAwarenessLogMod.Enabled.Value) return;
            // Counted through AlertAllNPCs ("enemy callout/scream"); no line of its own.
        }
    }

    // ---- sound: every alert names its cause and the enemies that reacted to it

    [HarmonyLib.HarmonyPatch(typeof(ANBAlertManagement), nameof(ANBAlertManagement.AlertAllNPCs))]
    internal static class AlertAllPatch
    {
        static void Prefix(Transform target, Transform origin, bool attack, float radius, bool hearOnly)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.AlertBegin(target, origin, attack, radius, hearOnly);
        }

        static void Postfix()
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.AlertEnd();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.StartAttack))]
    internal static class StartAttackPatch
    {
        static void Prefix(ANBBasicNPC __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Reacted(__instance, "attacks");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.StartHunt))]
    internal static class StartHuntPatch
    {
        static void Prefix(ANBBasicNPC __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Reacted(__instance, "hunts");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBAlertManagement), nameof(ANBAlertManagement.MakeNoiseExec))]
    internal static class NoisePatch
    {
        static void Prefix(string type, Transform source)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Noise(type, source);
        }

        static void Postfix() => Sound.ClearCause();
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.FireBullet))]
    internal static class FireBulletPatch
    {
        static void Prefix(ANBHVRGunBase source, float FromEnemy, bool isSilenced, bool enemyBulletCooldowned)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Gunshot(source, FromEnemy, isSilenced, enemyBulletCooldowned);
        }

        static void Postfix() => Sound.ClearCause();
    }

    // Your bullet (or stab / grab) hurting an enemy: every enemy's shots are harmless for a while.
    [HarmonyLib.HarmonyPatch(typeof(ANBEncounterSystem), nameof(ANBEncounterSystem.startEnemyCooldown))]
    internal static class EnemyCooldownPatch
    {
        static void Prefix(ANBEncounterSystem __instance)
        {
            if (!EnemyAwarenessLogMod.Enabled.Value) return;
            float secs = __instance.EnemyCooldownTime;
            Sound.hitGraces++;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRPhysicsBow), nameof(HVRPhysicsBow.ShootArrow))]
    internal static class ShootArrowPatch
    {
        static void Prefix()
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.BowShot();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.StabWorld))]
    internal static class StabWorldPatch
    {
        static void Prefix(bool isArrow)
        {
            if (EnemyAwarenessLogMod.Enabled.Value && isArrow) Sound.ArrowHitWorld();
        }

        static void Postfix() => Sound.ClearCause();
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBAlertManagement), nameof(ANBAlertManagement.QuickAlertToPlayer))]
    internal static class QuickAlertPatch
    {
        static void Prefix()
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.DoorKick();
        }

        static void Postfix() => Sound.ClearCause();
    }

    [HarmonyLib.HarmonyPatch(typeof(Il2CppHurricaneVR.Framework.Components.ANBPhysicsDoor), nameof(Il2CppHurricaneVR.Framework.Components.ANBPhysicsDoor.openDoor))]
    internal static class OpenDoorPatch
    {
        static void Prefix(Il2CppHurricaneVR.Framework.Components.ANBPhysicsDoor __instance, Transform origin, bool fromenemy, bool kick)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.DoorOpened(__instance, origin, fromenemy, kick);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBreakable), nameof(ANBBreakable.shatterMe))]
    internal static class ShatterPatch
    {
        static void Prefix(ANBBreakable __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Shatter(__instance);
        }

        static void Postfix() => Sound.ClearCause();
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.spawnNPC))]
    internal static class SpawnPatch
    {
        static void Prefix(ANBNpcSpawner __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.BeforeSpawn(__instance);
        }

        static void Postfix(ANBNpcSpawner __instance, bool __result)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.AfterSpawn(__instance, __result);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.triggerSpawner))]
    internal static class TriggerPatch
    {
        static void Prefix(ANBNpcSpawner __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.LogSpawner(__instance, "triggered");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.triggerWaveSpawner))]
    internal static class TriggerWavePatch
    {
        static void Prefix(ANBNpcSpawner __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.LogSpawner(__instance, "wave triggered");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.onAllDeadTriggers))]
    internal static class AllDeadPatch
    {
        static void Prefix(ANBNpcSpawner __instance)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Tracker.Summary($"all dead ('{__instance.name}')");
        }
    }
}
