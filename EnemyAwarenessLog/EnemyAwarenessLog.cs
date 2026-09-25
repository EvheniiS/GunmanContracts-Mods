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

[assembly: MelonInfo(typeof(EnemyAwarenessLog.EnemyAwarenessLogMod), "Enemy Awareness Log", "0.3.0", "Evgeeso")]
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
    // Output: the MelonLoader console and UserData\EnemyAwarenessLog\session-<time>.log.
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
            W($"---- scene '{sceneName}' ----");
        }

        public override void OnUpdate()
        {
            if (!Enabled.Value || Time.time < nextSample) return;
            nextSample = Time.time + Mathf.Max(0.05f, SampleInterval.Value);
            Tracker.Sample();
        }

        internal static void W(string s)
        {
            string line = $"[t={Time.time,7:0.0}] {s}";
            Log.Msg(line);
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
        static void W(string s) => EnemyAwarenessLogMod.W(s);

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
            public int Uid;
            // "#<n>" = the enemy's Unity instance id; Enemy Awareness Fix prints the same, so lines
            // from both mods can be matched.
            public string Tag => $"E{Id} #{Uid} ({Name})";
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
        static Vector3 lastPlayerPos;

        internal static void Reset()
        {
            Tracks.Clear();
            spawners.Clear();
            spawnersLogged.Clear();
            waveSeen.Clear();
            encounterLogged = false;
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

        static string P(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

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
            return $"{d.magnitude:0.0} m {comp}{(rel.Length > 0 ? ", " + rel : "")}";
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
                W($"spawn {t.Tag} at '{used.name}', {Where(used.transform.position, pp)} " +
                  $"[spawner '{s.name}', order {s.SpawnPointOrder}, wave {s.waveSurvived + 1}, starts as {t.State}, " +
                  $"3D {Vector3.Distance(used.transform.position, pp):0.0} m, min allowed {(s.SpawnpointCheckPlayerdistance ? Mathf.Sqrt(s.minSpawnDistanceToPlayer).ToString("0.#") + " m" : "off")}]");
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
                    W($"==== wave {w + 1} ('{s.name}'): size {s.waveSize:0}, max at once {s.maxEnemiesAtOnce:0}, total {s.totalSpawns:0}");
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
                    W($"{t.Tag} FIRST SAW YOU from {P(ep)}, {where}, {Time.time - t.SpawnT:0.0} s after spawn" +
                      $"{(t.SpawnPoint != null ? $" (spawned at '{t.SpawnPoint}')" : "")}");
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
                        nav += $", stopped {ag.isStopped}, hasPath {ag.hasPath}, path {ag.pathStatus}, remaining {ag.remainingDistance:0.0} m, " +
                               $"stopping distance {ag.stoppingDistance:0.0} m, destination {Flat(d, body):0.0} m from it / {Flat(d, pp):0.0} m from you";
                    }
                    else if (ag.isActiveAndEnabled) nav += ", NOT ON NAVMESH";
                }
            }
            catch (Exception ex) { nav += $" (read failed: {ex.Message})"; }
            string cover = n.isCovering ? ", in cover" : "";
            W($"STUCK? {t.Tag} hasn't moved for {now - t.MoveT:0} s ({StateOf(n)}{cover}), {Where(body, pp)} | {nav} | " +
              $"its last-known position is {Flat(n.lastKnownPosition, pp):0.0} m from you{Sound.DoorNote(n.Pointer)}");
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
            if (t.EpMovedSamples < 3) { verdict = "you didn't move 3 m from the reference point, can't tell"; epUnclear++; }
            else
            {
                float f = (float)t.EpTracked / t.EpMovedSamples;
                if (f >= 0.6f) { verdict = $"LIVE-TRACKED you ({t.EpTracked}/{t.EpMovedSamples} checks within 1.5 m of you)"; epTracked++; }
                else if (f <= 0.2f) { verdict = $"searched where it lost you ({t.EpTracked}/{t.EpMovedSamples} checks within 1.5 m of you)"; epSearched++; }
                else { verdict = $"partly tracked you ({t.EpTracked}/{t.EpMovedSamples} checks within 1.5 m of you)"; epUnclear++; }
            }
            epCount++;
            string dest = t.EpDestN > 0
                ? $" | walking to: avg {t.EpDestPlayer / t.EpDestN:0.0} m from you, {t.EpDestRef / t.EpDestN:0.0} m from {(t.EpRefIsSighting ? "where it last saw you" : "where you were")}"
                : "";
            W($"{t.Tag} unseen chase {dur:0.0} s [{t.EpStates}], reference = {(t.EpRefIsSighting ? "where it last saw you" : "where you were (it never saw you)")}, " +
              $"you moved up to {t.EpMaxMove:0.0} m from it -> {verdict} | it walked {t.EpTravel:0} m, stood still {t.EpStill:0} s" +
              $"{(t.EpStill > 0.8f * dur ? " (STUCK?)" : "")} | last-known-position writes while unseen: {t.EpUnseenWrites}{dest}" +
              $"{(t.EpFlank ? " | flanked" : "")} | end: {why}");
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
                    ? $"after {Time.time - t.TimerT:0.0} s (+/- {EnemyAwarenessLogMod.SampleInterval.Value:0.##}) of game time from timer value {t.TimerVal:0.#}"
                    : "(timer start not seen)";
                if (t.TimerT >= 0) { timerRuns++; timerRealSum += Time.time - t.TimerT; timerValSum += t.TimerVal; }
                t.TimerT = -1;
                W($"{t.Tag} LOST-TARGET timer ran out {timer} -> {(n.canLoseTarget ? "gives up the chase (hunt or idle)" : "canLoseTarget off: restarts the attack")}");
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
            ResetWave(waveLabel);
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
        static int gunShots, silencedShots, bowShots, arrowHits, doorKicks, shatters, enemyShots;
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
            gunShots = silencedShots = bowShots = arrowHits = doorKicks = shatters = enemyShots = 0;
            alerts.Clear();
            reactions.Clear();
        }

        static void Action(string what) { lastAction = what; lastActionT = Time.time; }
        static string Recent => Time.time - lastActionT <= 1.5f ? lastAction : null;
        static void SetCause(string c) { if (depth == 0) cause = c; }
        internal static void ClearCause() { if (depth == 0) cause = null; }

        internal static void Gunshot(ANBHVRGunBase gun, float fromEnemy, bool silenced)
        {
            if (fromEnemy > 0) { enemyShots++; SetCause("enemy gunfire"); return; }
            gunShots++;
            if (silenced) silencedShots++;
            string what = silenced ? "your silenced shot" : "your gunshot";
            Action(what);
            SetCause(what);
            W($"YOU FIRED '{Tracker.Name(gun)}'{(silenced ? " (silenced)" : "")}");
        }

        internal static void BowShot()
        {
            bowShots++;
            Action("your bow shot");
            W("YOU SHOT AN ARROW");
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
                who = npc != null ? $"enemy {Tracker.NpcTag(npc)}" : fromEnemy ? $"an enemy ('{Tracker.Name(origin)}')" : $"you? ('{Tracker.Name(origin)}')";
            }
            catch (Exception ex) { EnemyAwarenessLogMod.Warn("door opener", ex); }
            lastDoor = $"'{Tracker.Name(door)}' opened by {who}{(kick ? ", KICKED" : "")}";
            lastDoorT = Time.time;
            try
            {
                var npc = origin != null ? origin.GetComponentInParent<ANBBasicNPC>() : null;
                if (npc != null)
                {
                    float now = Time.time;
                    if (!doorsByNpc.TryGetValue(npc.Pointer, out var d) || d.Name != Tracker.Name(door) || now - d.LastT > 10f)
                        d = (Tracker.Name(door), 0, now, now);
                    doorsByNpc[npc.Pointer] = (d.Name, d.Count + 1, d.FirstT, now);
                }
            }
            catch { }
            if (kick || fromEnemy)
            {
                string line = lastDoor;
                Tracker.Alert("door|" + line, () => $"door {line}", 10f);
            }
        }

        // Per enemy: the door it keeps opening (name, opens, first, last), for the STUCK? report.
        static readonly Dictionary<IntPtr, (string Name, int Count, float FirstT, float LastT)> doorsByNpc = new();

        internal static string DoorNote(IntPtr npc)
        {
            if (!doorsByNpc.TryGetValue(npc, out var d) || Time.time - d.LastT > 10f) return "";
            return $" | has opened door '{d.Name}' {d.Count}x in the last {Time.time - d.FirstT:0} s";
        }

        internal static void DoorKick()
        {
            doorKicks++;
            string by = Time.time - lastDoorT <= 3f ? lastDoor : "door (opener unknown)";
            SetCause($"door kick: {by}");
            Tracker.Alert("kick|" + by, () => $"DOOR KICK ALERT: {by} - the game alerts every enemy to you", 10f);
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
            alerts[curKey] = alerts.TryGetValue(curKey, out int a) ? a + 1 : 1;
            curDesc = $"{c} [target '{Tracker.Name(target)}', origin '{Tracker.Name(origin)}', attack {attack}, " +
                      $"radius {(radius > 0 ? radius.ToString("0.#") + " m" : "default")}, hear only {hearOnly}]";
        }

        internal static void Reacted(ANBBasicNPC n, string what)
        {
            if (depth == 0 || n == null || !reactedPtrs.Add(n.Pointer)) return;
            try
            {
                var p = n.Playertarget;
                string d = p != null ? $" {Vector3.Distance(Tracker.Body(n), p.position):0} m from you" : "";
                bool already = n.isAttacking || n.isHunting;
                reacted.Add($"{Tracker.NpcTag(n)} {what}{d}{(already ? " (was already chasing)" : "")}");
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
                W($"HEARD {curDesc} -> {string.Join("; ", reacted)}");
            }
            else
            {
                string d = curDesc;
                Tracker.Alert("quiet|" + curKey, () => $"sound, nobody reacted: {d}");
            }
            reacted.Clear();
        }

        internal static void SummaryLines()
        {
            if (!Any) return;
            W($"  your weapons: gunshots {gunShots} (silenced {silencedShots}), bow shots {bowShots}, arrows hitting the world {arrowHits}, " +
              $"doors kicked/breached {doorKicks}, things shattered {shatters}; enemy gunshots {enemyShots}");
            if (alerts.Count > 0)
                W("  enemies newly alerted, by cause: " + string.Join(", ", alerts
                    .Select(k => (k.Key, k.Value, R: reactions.TryGetValue(k.Key, out int r) ? r : 0))
                    .OrderByDescending(x => x.R)
                    .Select(x => $"{x.Key}: {x.R} (from {x.Value} alerts)")));
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
            Tracker.Alert("alertWorld", () => $"{Tracker.NpcTag(__instance)} alerts the others (attack {attack})");
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
        static void Prefix(ANBHVRGunBase source, float FromEnemy, bool isSilenced)
        {
            if (EnemyAwarenessLogMod.Enabled.Value) Sound.Gunshot(source, FromEnemy, isSilenced);
        }

        static void Postfix() => Sound.ClearCause();
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
