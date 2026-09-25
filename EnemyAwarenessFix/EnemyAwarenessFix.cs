using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;

[assembly: MelonInfo(typeof(EnemyAwarenessFix.EnemyAwarenessFixMod), "Enemy Awareness Fix", "0.1.3", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace EnemyAwarenessFix
{
    // Enemies stop knowing exactly where you are when they can't see you.
    //
    // What the game does (GameAssembly.dll, confirmed in play with Enemy Awareness Log):
    // - Attack mode walks to ANBBasicNPC.lastKnownPosition. While the player is out of sight,
    //   AL_targetCheck runs
    //     if (lostTargetTime > lostTargetTime - loseTargetPositionUpdatesAfter || !canLoseTarget)
    //         markLastKnownTargetPosition(attackTarget.position);
    //   The compare is always true for any positive loseTargetPositionUpdatesAfter, and wave
    //   spawners also set canLoseTarget = false, so "last known" is the player's live position.
    // - Flanking, searching and evading pick points with FindRandomNavMeshPosition(target, ...)
    //   around the target Transform = the live player. Turning uses rotateToTarget(target).
    // - Wave spawners with SpawnPointOrder = Closest always take the nearest valid spawn point.
    //
    // What this mod does:
    // - Each enemy keeps its own belief of where you are. Seeing you (or you being within
    //   SenseDistance) updates it live, as before. After losing sight it keeps following you for
    //   TrackAfterLosingSight seconds (it saw which way you went), then the belief freezes.
    // - An enemy that never saw you (wave spawns start already attacking) gets a rough guess: a
    //   random point GuessMin..GuessRadius from you. Another enemy seeing you shares that sighting
    //   with enemies within ShareRadius, scattered a little and at most every ShareInterval.
    // - On reaching its belief without finding you, the enemy searches random points around it
    //   for SearchSeconds. Then wave enemies (canLoseTarget off) get a new rough guess, and other
    //   enemies are allowed to give up the game's usual way (hunt, then idle).
    // - Flank / search / evade points and turning use the belief instead of your live position.
    // - Closest-order spawners pick randomly among the few nearest valid points, and their minimum
    //   spawn distance can be raised.
    public class EnemyAwarenessFixMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, DebugLog, FixAwareness, FixSpawns;
        internal static MelonPreferences_Entry<float> TrackAfterLosingSight, SenseDistance, GuessMin, GuessRadius,
            ShareRadius, ShareInterval, ShareScatter, SearchSeconds, SearchRadius, MinSpawnDistance;
        internal static MelonPreferences_Entry<int> RandomSpawnAmongNearest;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("EnemyAwarenessFix", "Enemy Awareness Fix");
            Enabled = c.CreateEntry("Enabled", true, description: "Master switch.");
            FixAwareness = c.CreateEntry("FixAwareness", true, description: "Enemies that can't see you go to where they last saw you (or a rough guess) and search there, instead of walking to your exact position.");
            TrackAfterLosingSight = c.CreateEntry("TrackAfterLosingSight", 1.5f, description: "Seconds an enemy keeps following you after losing sight (it saw which way you went).");
            SenseDistance = c.CreateEntry("SenseDistance", 3.0f, description: "Within this many metres an enemy always knows where you are (it hears you).");
            GuessMin = c.CreateEntry("GuessMin", 3.0f, description: "Enemies that never saw you head for a random point at least this far from you...");
            GuessRadius = c.CreateEntry("GuessRadius", 8.0f, description: "...and at most this far.");
            ShareRadius = c.CreateEntry("ShareRadius", 25.0f, description: "When an enemy sees you, enemies within this many metres of it learn where you were. 0 = all enemies, -1 = no sharing.");
            ShareInterval = c.CreateEntry("ShareInterval", 3.0f, description: "An enemy accepts a shared sighting at most this often (seconds).");
            ShareScatter = c.CreateEntry("ShareScatter", 2.5f, description: "Shared sightings are scattered by up to this many metres per enemy, so they don't all run to one spot.");
            SearchSeconds = c.CreateEntry("SearchSeconds", 20.0f, description: "How long an enemy searches around the point where it lost you before giving up (story) or getting a new rough guess (waves).");
            SearchRadius = c.CreateEntry("SearchRadius", 6.0f, description: "Radius of the search around that point (grows to double as the search goes on).");
            FixSpawns = c.CreateEntry("FixSpawns", true, description: "Wave spawners don't always use the nearest spawn point.");
            RandomSpawnAmongNearest = c.CreateEntry("RandomSpawnAmongNearest", 3, description: "Closest-order spawners pick randomly among this many nearest valid spawn points. 1 = game behaviour.");
            MinSpawnDistance = c.CreateEntry("MinSpawnDistance", 10.0f, description: "Minimum spawn distance from you in metres, for spawners that check it (the wave spawner's own is 6). Only raises, never lowers. 0 = game value.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log each enemy's knowledge changes (lost sight, guesses, shared sightings, searches) and spawn picks.");
            Log.Msg("loaded");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => Awareness.Reset();

        public override void OnUpdate()
        {
            if (Enabled.Value && FixAwareness.Value) Awareness.Tick();
        }

        internal static void D(string s)
        {
            if (DebugLog.Value) Log.Msg($"[t={Time.time:0.0}] {s}");
        }

        static readonly HashSet<string> warned = new();
        internal static void Warn(string where, Exception ex)
        {
            if (warned.Add(where + ex.GetType().Name)) Log.Warning($"{where}: {ex.Message}");
        }
    }

    internal static class Awareness
    {
        static void D(string s) => EnemyAwarenessFixMod.D(s);

        internal enum Kind { None, Seen, Shared, Guess }

        internal class Npc
        {
            public ANBBasicNPC N;
            public IntPtr Ptr;
            public int Id;
            public bool Dead;
            public float LastSeenT = float.NegativeInfinity;
            public bool InBelief;          // out of sight past the grace time: the mod owns the target position
            public Kind K;
            public float InfoT = float.NegativeInfinity;   // when the current anchor's information is from
            public Vector3 Anchor;         // centre of what it believes / searches
            public Vector3 Pos;            // current destination (anchor, then search points)
            public float PosSetT;
            public float AnchorT;          // when the current anchor was set (caps the time before the search starts)
            public Vector3 MovePos;
            public float MoveT;            // last time it moved 0.5 m
            public float ArrivedT = -1, Dwell;
            public float SearchStartT = -1;
            public int SearchPoints;
            public bool SearchDone;
            public float GaveUpT = -1;
            public Transform Ghost;
            public int Uid;
            // "#<n>" = Unity instance id, the same number Enemy Awareness Log prints.
            public string Tag => $"E{Id} #{Uid}";
        }

        static readonly Dictionary<IntPtr, Npc> Npcs = new();
        static int nextId = 1;
        static float nextTick;

        // Last sighting of the player by any enemy.
        static bool haveSighting;
        static Vector3 sightingPos, sightingFrom;
        static float sightingT = float.NegativeInfinity;

        internal static void Reset()
        {
            Npcs.Clear();
            haveSighting = false;
            sightingT = float.NegativeInfinity;
        }

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

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        internal static Npc Get(ANBBasicNPC n)
        {
            if (n == null) return null;
            return Npcs.TryGetValue(n.Pointer, out var s) ? s : null;
        }

        static Npc Track(ANBBasicNPC n)
        {
            IntPtr p = n.Pointer;
            if (Npcs.TryGetValue(p, out var s)) return s;
            s = new Npc { N = n, Ptr = p, Id = nextId++, Uid = Math.Abs(n.GetInstanceID()) % 100000 };
            Npcs[p] = s;
            return s;
        }

        static void Forget(Npc s)
        {
            s.InBelief = false;
            s.K = Kind.None;
            s.InfoT = float.NegativeInfinity;
            s.LastSeenT = float.NegativeInfinity;
            s.SearchStartT = s.ArrivedT = s.GaveUpT = -1;
            s.SearchPoints = 0;
            s.SearchDone = false;
        }

        // Is this enemy chasing the player (attack or hunt), and is the player out of its sight?
        static bool Chasing(ANBBasicNPC n, out bool seen)
        {
            var pt = n.Playertarget;
            bool att = n.isAttacking && Same(n.attackTarget, pt);
            bool hunt = !att && n.isHunting && Same(n.huntTarget, pt);
            seen = att && n.targetInSight;
            return att || hunt;
        }

        // ------------------------------------------------------------------ per frame

        internal static void Tick()
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + 0.1f;
            Transform player;
            try { player = ANBStaticGameManager.ANBmain?.PlayerHitTarget; } catch { return; }
            if (player == null) return;
            Vector3 pp = player.position;

            try
            {
                var list = ANBStaticGameManager.ANBmain?.encounterSystem?.allEnemies;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i];
                    if (n == null || !n.isEnemy) continue;
                    var s = Track(n);
                    try { Update(s, n, pp); }
                    catch (Exception ex) { EnemyAwarenessFixMod.Warn("update", ex); }
                }
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("enemy list", ex); }
        }

        static void Update(Npc s, ANBBasicNPC n, Vector3 pp)
        {
            if (n.isDead) { if (!s.Dead) { s.Dead = true; Forget(s); } return; }
            if (s.Dead) { s.Dead = false; Forget(s); }                    // back from the pool
            if (!n.gameObject.activeInHierarchy) return;

            float now = Time.time;
            if (!Chasing(n, out bool seen))
            {
                if (s.InBelief || s.K != Kind.None) Forget(s);
                return;
            }

            Vector3 np = Body(n);
            bool sensed = Vector3.Distance(np, pp) <= EnemyAwarenessFixMod.SenseDistance.Value;
            if (seen || sensed)
            {
                if (s.InBelief) D($"{s.Tag} {(seen ? "sees" : "senses")} you again");
                s.LastSeenT = now;
                s.InBelief = false;
                s.K = Kind.Seen;
                s.InfoT = now;
                s.Anchor = s.Pos = pp;
                s.SearchStartT = s.ArrivedT = s.GaveUpT = -1;
                s.SearchPoints = 0;
                s.SearchDone = false;
                if (seen) { haveSighting = true; sightingPos = pp; sightingFrom = np; sightingT = now; }
                return;
            }
            if (now - s.LastSeenT <= EnemyAwarenessFixMod.TrackAfterLosingSight.Value)
            {
                s.Anchor = s.Pos = pp;                                     // still following your trail
                s.InfoT = now;
                return;
            }

            if (!s.InBelief) EnterBelief(s, n, pp, now);
            TryShared(s, np, now);

            // Searching: arrive, look around, move to the next point. "Arrived" also covers an enemy
            // that stopped short (agent stopping distance, cover) or can't get closer: it stood still
            // for 3 s. And the search starts after 30 s at the latest, so nothing waits forever.
            if (Vector3.Distance(np, s.MovePos) > 0.5f) { s.MovePos = np; s.MoveT = now; }
            float stop = 2f;
            var agent = n.agent;
            try { if (agent != null && agent.isActiveAndEnabled) stop = Mathf.Max(2f, agent.stoppingDistance + 1f); } catch { }
            string arrived = Flat(np, s.Pos) <= stop ? "reached"
                           : now - s.MoveT >= 3f && now - s.PosSetT >= 3f ? "stopped short of"
                           : s.SearchStartT < 0 && now - s.AnchorT > 30f ? "couldn't reach" : null;
            if (arrived != null)
            {
                if (s.SearchStartT < 0)
                {
                    s.SearchStartT = now;
                    D($"{s.Tag} {arrived} {(s.K == Kind.Guess ? "its guess" : s.K == Kind.Shared ? "the shared sighting" : "where it lost you")} " +
                      $"({Flat(np, s.Pos):0.0} m off), searching; you are {Flat(np, pp):0.0} m away");
                }
                if (s.ArrivedT < 0) { s.ArrivedT = now; s.Dwell = UnityEngine.Random.Range(1.0f, 2.5f); }
                if (now - s.ArrivedT >= s.Dwell) NextSearchPoint(s);
            }
            else if (now - s.PosSetT > 15f) NextSearchPoint(s);            // still walking after 15 s

            if (s.SearchStartT >= 0 && !s.SearchDone && now - s.SearchStartT > EnemyAwarenessFixMod.SearchSeconds.Value)
            {
                if (!n.canLoseTarget) NewGuess(s, pp, now, "search over, wave enemy gets a new rough guess");
                else { s.SearchDone = true; s.GaveUpT = now; D($"{s.Tag} search over, may give up now"); }
            }
            // A "give up" that bounces straight back to attack (StartHunt does that without a
            // collective position) would loop; send it to a new guess instead.
            if (s.SearchDone && n.isAttacking && s.GaveUpT >= 0 && now - s.GaveUpT > 1.5f)
                NewGuess(s, pp, now, "gave up but the game put it back into attack, new rough guess");

            if (n.isAttacking) n.lastKnownPosition = s.Pos;
            if (s.Ghost != null) s.Ghost.position = n.isHunting ? s.Anchor : s.Pos;
        }

        static void EnterBelief(Npc s, ANBBasicNPC n, Vector3 pp, float now)
        {
            s.InBelief = true;
            s.SearchStartT = s.ArrivedT = s.GaveUpT = -1;
            s.SearchPoints = 0;
            s.SearchDone = false;
            EnsureGhost(s);
            if (s.K == Kind.Seen)
            {
                s.Pos = s.Anchor;
                s.PosSetT = s.AnchorT = s.MoveT = now;
                D($"{s.Tag} lost sight of you, holding {Flat(s.Anchor, pp):0.0} m from where you are now");
            }
            else if (!TryShared(s, Body(n), now))
                NewGuess(s, pp, now, "never saw you, rough guess");
        }

        static void NewGuess(Npc s, Vector3 pp, float now, string why)
        {
            float lo = Mathf.Max(0f, EnemyAwarenessFixMod.GuessMin.Value);
            float hi = Mathf.Max(lo + 0.1f, EnemyAwarenessFixMod.GuessRadius.Value);
            Vector3 g = pp;
            bool ok = false;
            for (int i = 0; i < 10 && !ok; i++)
            {
                Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                float r = UnityEngine.Random.Range(lo, hi) * (i < 6 ? 1f : 0.5f);   // later tries closer to you
                ok = Reachable(s.N, pp + new Vector3(dir.x, 0, dir.y) * r, out g);
            }
            if (!ok) { g = pp; why += " (no reachable point near you, heading for your area)"; }
            s.K = Kind.Guess;
            s.InfoT = now;
            s.Anchor = s.Pos = g;
            s.PosSetT = s.AnchorT = s.MoveT = now;
            s.SearchStartT = s.ArrivedT = s.GaveUpT = -1;
            s.SearchPoints = 0;
            s.SearchDone = false;
            D($"{s.Tag} {why}: {Flat(g, pp):0.0} m from you");
        }

        static bool TryShared(Npc s, Vector3 npcPos, float now)
        {
            float radius = EnemyAwarenessFixMod.ShareRadius.Value;
            if (!haveSighting || radius < 0 || sightingT <= s.InfoT) return false;
            if (now - s.InfoT < EnemyAwarenessFixMod.ShareInterval.Value && s.K != Kind.None) return false;
            if (radius > 0 && Vector3.Distance(npcPos, sightingFrom) > radius) return false;
            Vector2 d = UnityEngine.Random.insideUnitCircle * EnemyAwarenessFixMod.ShareScatter.Value;
            if (!Reachable(s.N, sightingPos + new Vector3(d.x, 0, d.y), out Vector3 p)) p = sightingPos;
            s.K = Kind.Shared;
            s.InfoT = sightingT;
            s.Anchor = s.Pos = p;
            s.PosSetT = s.AnchorT = s.MoveT = now;
            s.SearchStartT = s.ArrivedT = s.GaveUpT = -1;
            s.SearchPoints = 0;
            s.SearchDone = false;
            D($"{s.Tag} told where you were {now - sightingT:0.0} s ago by another enemy");
            return true;
        }

        static void NextSearchPoint(Npc s)
        {
            float r = EnemyAwarenessFixMod.SearchRadius.Value * Mathf.Min(2f, 1f + 0.25f * s.SearchPoints);
            Vector3 p = s.Anchor;
            bool ok = false;
            for (int i = 0; i < 8 && !ok; i++)
            {
                Vector2 d = UnityEngine.Random.insideUnitCircle * r;
                ok = Reachable(s.N, s.Anchor + new Vector3(d.x, 0, d.y), out p) && Flat(p, s.Pos) > 1.5f;
            }
            if (!ok) p = Body(s.N);                           // nowhere to go: look around here
            s.Pos = p;
            s.PosSetT = Time.time;
            s.ArrivedT = -1;
            s.SearchPoints++;
        }

        static bool Sample(Vector3 p, out Vector3 result)
        {
            result = p;
            try
            {
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) { result = hit.position; return true; }
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("navmesh sample", ex); }
            return false;
        }

        static readonly NavMeshPath path = new NavMeshPath();

        // A navmesh point near p that this enemy can walk to (a complete path from where it stands).
        // SamplePosition alone can land on a shelf, the upper floor or a closed-off patch.
        static bool Reachable(ANBBasicNPC n, Vector3 p, out Vector3 result)
        {
            result = p;
            if (!Sample(p, out result)) return false;
            try
            {
                if (!Sample(Body(n), out Vector3 from)) return true;   // can't judge: accept
                int mask = NavMesh.AllAreas;
                var agent = n.agent;
                if (agent != null) mask = agent.areaMask;
                return NavMesh.CalculatePath(from, result, mask, path) && path.status == NavMeshPathStatus.PathComplete;
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("navmesh path", ex); return true; }
        }

        static void EnsureGhost(Npc s)
        {
            if (s.Ghost != null) return;
            var go = new GameObject($"EnemyAwarenessFix_belief_{s.Id}");
            s.Ghost = go.transform;
        }

        // ------------------------------------------------------------------ hooks

        // Where the belief applies: chasing the player and out of sight past the grace time.
        internal static Npc Believing(ANBBasicNPC n)
        {
            if (!EnemyAwarenessFixMod.Enabled.Value || !EnemyAwarenessFixMod.FixAwareness.Value) return null;
            var s = Get(n);
            return s != null && s.InBelief && s.Ghost != null ? s : null;
        }

        // markLastKnownTargetPosition: skip the game's write while the mod owns the belief. The
        // first write for a fresh enemy can arrive before Tick has seen it, so decide here too.
        internal static bool AllowMark(ANBBasicNPC n)
        {
            if (!EnemyAwarenessFixMod.Enabled.Value || !EnemyAwarenessFixMod.FixAwareness.Value) return true;
            if (n.targetInSight || !n.isEnemy) return true;
            var player = n.Playertarget;
            if (player == null || !Same(n.attackTarget, player)) return true;
            var s = Get(n) ?? Track(n);
            float now = Time.time;
            Vector3 pp = player.position;
            if (Vector3.Distance(Body(n), pp) <= EnemyAwarenessFixMod.SenseDistance.Value) return true;
            if (now - s.LastSeenT <= EnemyAwarenessFixMod.TrackAfterLosingSight.Value) return true;
            if (!s.InBelief) EnterBelief(s, n, pp, now);
            n.lastKnownPosition = s.Pos;
            return false;
        }

        // A "give up" while the mod is still searching would start the game's hunt too early.
        internal static bool AllowLoseTarget(ANBBasicNPC n)
        {
            var s = Believing(n);
            return s == null || s.SearchDone;
        }

        internal static Transform RedirectPlayer(ANBBasicNPC n, Transform t)
        {
            if (t == null) return null;
            var s = Believing(n);
            if (s == null || !Same(t, n.Playertarget)) return null;
            return s.Ghost;
        }
    }

    internal static class Spawns
    {
        [ThreadStatic] static bool respawning;

        // spawnCheck (dynamic spawners) picks the point itself - the same "sort by distance, first
        // valid" logic as getSpawnPoint - and passes it as overrideSP. Re-issue the call with a
        // random point among the nearest valid ones instead.
        internal static bool SpawnNPC(ANBNpcSpawner sp, ANBNpcSpawnPoint overrideSP, float spawnSize, ref bool result)
        {
            if (respawning || overrideSP == null) return true;
            var pick = Pick(sp, overrideSP);
            if (pick == null || pick.Pointer == overrideSP.Pointer) return true;
            respawning = true;
            try { result = sp.spawnNPC(pick, spawnSize); }
            finally { respawning = false; }
            return false;
        }

        internal static void BeforePick(ANBNpcSpawner sp)
        {
            if (!EnemyAwarenessFixMod.Enabled.Value || !EnemyAwarenessFixMod.FixSpawns.Value) return;
            try
            {
                float d = EnemyAwarenessFixMod.MinSpawnDistance.Value;
                // initSpawner squares the distance values in place; spawnPointValidation compares sqrMagnitude.
                if (d > 0 && sp.SpawnpointCheckPlayerdistance && sp.minSpawnDistanceToPlayer < d * d)
                {
                    EnemyAwarenessFixMod.D($"spawner '{sp.name}': min spawn distance {Mathf.Sqrt(sp.minSpawnDistanceToPlayer):0.#} -> {d:0.#} m");
                    sp.minSpawnDistanceToPlayer = d * d;
                }
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("min spawn distance", ex); }
        }

        // getSpawnPoint in Closest mode sorts spawnPoints by distance to you and returns the first
        // valid one. Pick randomly among the first few valid ones instead.
        internal static ANBNpcSpawnPoint Pick(ANBNpcSpawner sp, ANBNpcSpawnPoint chosen)
        {
            if (chosen == null || !EnemyAwarenessFixMod.Enabled.Value || !EnemyAwarenessFixMod.FixSpawns.Value) return null;
            int want = EnemyAwarenessFixMod.RandomSpawnAmongNearest.Value;
            if (want <= 1 || (int)sp.SpawnPointOrder != 0 /* Closest */) return null;
            try
            {
                var pts = sp.spawnPoints;
                if (pts == null) return null;
                int start = -1;
                for (int i = 0; i < pts.Count; i++)
                    if (pts[i] != null && pts[i].Pointer == chosen.Pointer) { start = i; break; }
                if (start < 0) return null;
                var valid = new List<ANBNpcSpawnPoint> { chosen };
                for (int i = start + 1; i < pts.Count && valid.Count < want; i++)
                {
                    var p = pts[i];
                    if (p != null && sp.spawnPointValidation(p)) valid.Add(p);
                }
                var pick = valid[UnityEngine.Random.Range(0, valid.Count)];
                EnemyAwarenessFixMod.D($"spawner '{sp.name}': '{pick.name}' ({valid.IndexOf(pick) + 1} of the {valid.Count} nearest valid points)");
                return pick;
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("spawn pick", ex); return null; }
        }
    }

    // ------------------------------------------------------------------ Harmony

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.markLastKnownTargetPosition))]
    internal static class MarkLastKnownPatch
    {
        static bool Prefix(ANBBasicNPC __instance)
        {
            try { return Awareness.AllowMark(__instance); }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("markLastKnown", ex); return true; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.LooseTarget))]
    internal static class LooseTargetPatch
    {
        static bool Prefix(ANBBasicNPC __instance)
        {
            try { return Awareness.AllowLoseTarget(__instance); }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("LooseTarget", ex); return true; }
        }
    }

    // Flank, hunt-search, evade and melee points are picked around the target Transform.
    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.FindRandomNavMeshPosition))]
    internal static class FindRandomNavMeshPositionPatch
    {
        static bool Prefix(ANBBasicNPC __instance, Transform target, float searchRadius, float clearDistance, bool dontMoveCloser, ref Vector3 __result)
        {
            try
            {
                var ghost = Awareness.RedirectPlayer(__instance, target);
                if (ghost == null) return true;
                __result = __instance.FindRandomNavMeshPosition(ghost, searchRadius, clearDistance, dontMoveCloser);
                return false;
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("FindRandomNavMeshPosition", ex); return true; }
        }
    }

    // Turning toward the target: face the belief, not your live position through walls.
    [HarmonyLib.HarmonyPatch(typeof(ANBBasicNPC), nameof(ANBBasicNPC.rotateToTarget))]
    internal static class RotateToTargetPatch
    {
        static bool Prefix(ANBBasicNPC __instance, Transform rotationTarget, Vector3 pos, float rotSpeed, float offset)
        {
            try
            {
                var ghost = Awareness.RedirectPlayer(__instance, rotationTarget);
                if (ghost == null) return true;
                __instance.rotateToTarget(ghost, pos, rotSpeed, offset);
                return false;
            }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("rotateToTarget", ex); return true; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.spawnNPC))]
    internal static class SpawnNPCPatch
    {
        static bool Prefix(ANBNpcSpawner __instance, ANBNpcSpawnPoint overrideSP, float spawnSize, ref bool __result)
        {
            try { return Spawns.SpawnNPC(__instance, overrideSP, spawnSize, ref __result); }
            catch (Exception ex) { EnemyAwarenessFixMod.Warn("spawnNPC", ex); return true; }
        }
    }

    // Both spawn paths validate points here, so the minimum distance is applied here.
    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.spawnPointValidation))]
    internal static class SpawnPointValidationPatch
    {
        static void Prefix(ANBNpcSpawner __instance) => Spawns.BeforePick(__instance);
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBNpcSpawner), nameof(ANBNpcSpawner.getSpawnPoint))]
    internal static class GetSpawnPointPatch
    {
        static void Postfix(ANBNpcSpawner __instance, ref ANBNpcSpawnPoint __result)
        {
            var p = Spawns.Pick(__instance, __result);
            if (p != null) __result = p;
        }
    }
}
