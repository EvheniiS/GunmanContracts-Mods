using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RadarSense
{
    // Daredevil's radar sense: while slow motion is on (your own right-B slow motion, and optionally the brief
    // one Physical Dodge starts on a dodge), or always (ActiveWhen = Always), enemies show through walls as a red silhouette.
    //
    // How: each enemy body SkinnedMeshRenderer gets a child SkinnedMeshRenderer with the same mesh, bones and root
    // bone, so it skins with the enemy for free. Its material ignores the depth buffer the way a see-through
    // overlay does: Hidden (default) = depth test Greater on enemies you can't see directly (ANBBasicNPC.isInView), Behind =
    // Greater on every enemy (only the parts hidden behind something show), Blocked = depth test
    // Always, and only on enemies the game says you can't see (ANBBasicNPC.isInView). The copies are built the
    // first time the sense turns on, and only switched on/off after that.
    //
    // The game hides enemies you can't see (ANBBasicNPC.checkVisibilityRelatedActions): out of view longer than
    // switchObjectsAfter, further than outOfViewObjectsSaveDist and not in a custom animation -> NpcMeshes renderers
    // off, outOfViewObjects deactivated, animator culled (outOfViewObjectsOff). Back in view (or outOfViewTime below
    // switchObjectsAfter) it turns them all back on and sets the animator to AlwaysAnimate. 0.1.0 followed the hidden
    // renderers, so enemies only lit up once you had seen them. Since 0.1.1 the sense keeps outOfViewTime at 0 for
    // every enemy in range while it is on, so the game itself keeps them shown and animated.
    //
    // Reveal (0.1.4): Awake = leave the game's hiding alone (enemies it wakes up, e.g. shouting, still show);
    // Moving = also keep enemies shown while their body moves faster than StepSpeed, as if you hear their steps;
    // All = every enemy in Range (what 0.1.1-0.1.2 did, ~+1% missed frames with 2-3 kept).
    //
    // Clubs (0.3.0): Billy Clubs (read from BillyClubsMod.CopyClubs every 0.5 s; same DLL) get the same treatment with a MeshRenderer copy and their own colour, always depth test Greater, and only
    // when more than ClubMinDistance from your head (a held or holstered club would glow under your glove). The
    // materials live for the whole session, since clubs carry across scenes with their copies.
    //
    // FocusRevealsAll (0.2.1): while your own slow motion is on, Reveal acts as All.
    //
    // Shader: the game has no shader of its own for this, so the mod uses a built-in one found in the build's
    // shader list whose depth test is a material property: Hidden/Internal-Colored (_ZTest, _Cull, blend) or
    // UI/Default (unity_GUIZTestMode). Both are pipeline-agnostic passes, which URP draws as SRPDefaultUnlit.
    // Part of the Daredevil package: Daredevil.cs calls Init / Scene / Tick from the one MelonMod.
    public class RadarSenseMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> ShowStill, IncludeLods, Enabled, WithDodgeSlowMotion, FocusRevealsAll, ClubHighlight, PerfLog, DebugLog;
        internal static MelonPreferences_Entry<string> Detail, ClubColor, LoudSteps, Reveal, ActiveWhen, Color, Style, ShaderName, SkipParts;
        internal static MelonPreferences_Entry<float> StillBrightness, Brightness, SeenFraction, ClubMinDistance, Opacity, Range, StepSpeed, HearDistance, LoudRadius, StepVolume;

        internal static void Init(MelonLogger.Instance log)
        {
            Log = log;
            var c = MelonPreferences.CreateCategory("RadarSense", "Daredevil: Radar Sense");
            Enabled = c.CreateEntry("Enabled", true, description: "Master switch for seeing enemies through walls.");
            ActiveWhen = c.CreateEntry("ActiveWhen", "Always", description: "SlowMotion (the sense is on only while slow motion is) or Always (always on).");
            FocusRevealsAll = c.CreateEntry("FocusRevealsAll", true, description: "While your own slow motion (focus, right B) is on, every enemy in Range shows, even ones you haven't detected, whatever Reveal is set to.");
            WithDodgeSlowMotion = c.CreateEntry("WithDodgeSlowMotion", true, description: "The brief slow motion on a dodge (Physical Dodge) turns the sense on too, not only your own slow motion.");
            Color = c.CreateEntry("Color", "#FF1010", description: "Colour of the silhouettes.");
            Brightness = c.CreateEntry("Brightness", 0.6f, description: "Multiplies the silhouette colour, live. Overlapping body meshes stack their opacity, so a bright colour reaches full brightness and the game's bloom turns it into a red aura around the enemies. Lower = darker red and less glow (0.3 to 1).");
            ShowStill = c.CreateEntry("ShowStill", true, description: "Enemies you haven't seen that stand still also show, dimmed (StillBrightness), so you still see where they wait behind a wall. Moving ones, and everything while you focus, keep full Brightness. Needs Reveal Moving or All. Costs a little: the game's out-of-view hiding is switched off for every enemy in Range.");
            StillBrightness = c.CreateEntry("StillBrightness", 0.2f, description: "Multiplies Brightness for standing-still enemies (ShowStill), live. 0.1 to 1.");
            Opacity = c.CreateEntry("Opacity", 0.6f, description: "How solid the silhouettes are, 0.05 to 1.");
            Style = c.CreateEntry("Style", "Hidden", description: "Hidden (enemies you can see directly get no highlight; the rest show the parts hidden behind something), Behind (every enemy, including ones you can see, shows its hidden parts) or Blocked (the whole body, only on enemies you can't see).");
            Reveal = c.CreateEntry("Reveal", "Moving", description: "Which enemies you haven't seen show. The game hides enemies out of view for a few seconds (no mesh, no animation) to save performance. Awake (only enemies the game still shows: seen recently, or woken up, e.g. shouting), Moving (also enemies walking or running, as if you hear their steps; the default) or All (every enemy in Range; the biggest performance cost).");
            StepSpeed = c.CreateEntry("StepSpeed", 0.5f, description: "For Reveal = Moving: an enemy moving faster than this (m/s) is heard.");
            Detail = c.CreateEntry("Detail", "Full", description: "How much of an enemy's body is outlined, live. Core (the skin mesh and the vest: the plainest silhouette, cheapest), Body (+ shirt/jacket, trousers, sleeves, hands), Clothes (+ shoes) or Full (everything, hair, beard, tie and holster too).");
            IncludeLods = c.CreateEntry("IncludeLods", false, description: "Also outline the lower detail levels (LOD1-4) of an enemy's body. Off by default: the game switches LODs without turning the other levels' renderers off, so this draws every level on top of each other (about 4x the silhouette meshes) and did not fix any missing outline. Applies to enemies seen after the change.");
            SeenFraction = c.CreateEntry("SeenFraction", 0.6f, description: "How much of an enemy you must see for it to count as seen directly (no highlight with Style Hidden/Blocked). The game counts an enemy as in view when ONE of its 14 sight points has a clear line to your head, so a head over a doorframe or counter switched the whole silhouette off. This is the share of sight points that must be clear: 0.7 = 10 of 14. 0 = the game's own rule (one point).");
            Range = c.CreateEntry("Range", 60f, description: "Enemies further away than this (m) don't show.");
            SkipParts = c.CreateEntry("SkipParts", "eye,teeth,tooth,tongue,lash,brow", description: "Body parts left out, by renderer or mesh name (comma separated). Parts inside the head would show through the face.");
            ShaderName = c.CreateEntry("Shader", "Auto", description: "Auto picks the first that loads: Hidden/Internal-Colored, then UI/Default. Or a shader name. Applies after a game restart.");
            ClubHighlight = c.CreateEntry("ClubHighlight", true, description: "Billy Clubs that are away from you (dropped, thrown, stuck behind something) show through walls too, so you can find them.");
            ClubColor = c.CreateEntry("ClubColor", "#FFC000", description: "Colour of the club highlight.");
            ClubMinDistance = c.CreateEntry("ClubMinDistance", 2f, description: "A club closer to your head than this (m) isn't highlighted, so clubs in your hand or holster don't glow where your glove covers them.");
            LoudSteps = c.CreateEntry("LoudSteps", "Always", description: "Louder enemy footsteps, played on each enemy's own sound source so gunfire can't cut them off. Always (all the time), Sense (only while the radar sense is on) or Off (the game's own steps).");
            HearDistance = c.CreateEntry("HearDistance", 30f, description: "Enemy steps are heard up to this far (m). The game plays none beyond its own limit (logged).");
            LoudRadius = c.CreateEntry("LoudRadius", 3f, description: "Steps are at full volume within this distance (m) and fade with distance beyond it. Bigger = louder from far away.");
            StepVolume = c.CreateEntry("StepVolume", 1f, description: "Step volume multiplier times the game's sound effects volume. Values above 1 make steps louder than the game's normal effect volume.");
            PerfLog = c.CreateEntry("PerfLog", false, description: "Once a minute, log frame times split by sense off / on / on and keeping unseen enemies shown, plus the mod's own cost. For comparing the Reveal settings.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log which shader is used, each enemy's body parts once, and one line each time the sense turns on/off.");
            Log.Msg("loaded - slow motion shows enemies through walls.");
        }

        internal static void Scene() { Radar.Reset(); Steps.Reset(); }

        internal static void Tick()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { Radar.Update(); Steps.Tick(); }
            catch (Exception e) { Log.Warning($"update: {e.GetType().Name}: {e.Message}"); }
            Perf.Frame(System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }
    }

    internal static class Radar
    {
        static void W(string s) => RadarSenseMod.Log.Msg(s);
        static bool Debug => RadarSenseMod.DebugLog.Value;

        class Part { public SkinnedMeshRenderer Src, Copy; public int Rank; public bool Dim; }
        class Enemy { public ANBBasicNPC Npc; public bool Shown; public Vector3 LastPos; public float LastT = -1; public readonly List<Part> Parts = new(); public int InView = -1, DiagLines; public float NextDiag, NextSig, MovingUntil, NextRescan; public bool Dim; }

        static readonly Dictionary<IntPtr, Enemy> Enemies = new();
        class MPart { public MeshRenderer Src, Copy; }
        class ClubMark { public GameObject Root; public bool Shown; public readonly List<MPart> Parts = new(); }
        static readonly Dictionary<IntPtr, ClubMark> Clubs = new();
        static Material clubMat;
        static float nextClubScan;
        static readonly HashSet<string> LoggedBodies = new();
        static Material mat, matDim;
        static string matMode;          // "Internal-Colored" or "UI" = how the depth test is set
        static bool shaderFailed, on;
        static float onSince, nextScan, nextShow;
        static int shownPeak;
        static readonly HashSet<IntPtr> Unhidden = new(), Heard = new();
        internal static bool On => on;
        internal static int KeptNow;    // enemies out of your view that the sense is keeping shown this frame

        internal static void Reset()
        {
            if (on && Debug) W("radar off (scene change)");
            on = false;
            Enemies.Clear();            // the copies are children of the enemies and went with the scene
            Clubs.Clear();              // clubs may carry over; ScanClubs reuses their copies
            shaderFailed = false;       // the materials stay: carried-over club copies still use them
        }

        // Your own slow motion (right B): Slowmotion step set and no scripted one (the dodge or last-enemy slow motion).
        static bool Focus()
        {
            var game = ANBStaticGameManager.ANBmain;
            return RadarSenseMod.Alive(game) && game.Slowmotion > 0 && !game.scriptedSlowmotionActive;
        }

        static bool Wanted()
        {
            if (!RadarSenseMod.Enabled.Value) return false;
            var game = ANBStaticGameManager.ANBmain;
            if (!RadarSenseMod.Alive(game) || game.Paused) return false;
            if (string.Equals(RadarSenseMod.ActiveWhen.Value?.Trim(), "Always", StringComparison.OrdinalIgnoreCase)) return true;
            if (game.scriptedSlowmotionActive) return RadarSenseMod.WithDodgeSlowMotion.Value;
            return game.Slowmotion > 0;
        }

        internal static void Update()
        {
            bool want = Wanted();
            if (!want)
            {
                if (on) TurnOff();
                return;
            }
            if (!on)
            {
                if (!EnsureMaterial()) return;
                on = true;
                onSince = Time.unscaledTime;
                shownPeak = 0;
                Unhidden.Clear();
                Heard.Clear();
                nextScan = nextShow = nextClubScan = 0;
            }
            // 10 times a second is enough for on/off switching; every frame cost 0.26 ms with 21 tracked enemies.
            float now = Time.unscaledTime;
            if (now >= nextScan) { nextScan = now + 0.25f; Scan(); }
            if (now >= nextClubScan) { nextClubScan = now + 0.5f; ScanClubs(); }
            if (now >= nextShow) { nextShow = now + 0.1f; ApplyMaterial(); Show(); ShowClubs(); }
        }

        static void TurnOff()
        {
            on = false;
            KeptNow = 0;
            foreach (var e in Enemies.Values)
            {
                e.Shown = false;
                foreach (var p in e.Parts)
                    if (RadarSenseMod.Alive(p.Copy)) p.Copy.enabled = false;
            }
            foreach (var m in Clubs.Values)
            {
                m.Shown = false;
                foreach (var p in m.Parts)
                    if (RadarSenseMod.Alive(p.Copy)) p.Copy.enabled = false;
            }
            if (Debug) W($"radar off after {Time.unscaledTime - onSince:0.0} s, up to {shownPeak} enemies shown, {Unhidden.Count} were hidden by the game, {Heard.Count} heard moving");
        }

        // ------------------------------------------------------------------ enemies

        static void Scan()
        {
            var list = ANBStaticGameManager.ANBmain?.encounterSystem?.allEnemies;
            if (list == null) return;
            int added = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (n == null || !n.isEnemy || Enemies.ContainsKey(n.Pointer)) continue;
                try { Enemies[n.Pointer] = Build(n); added++; }
                catch (Exception ex) { RadarSenseMod.Log.Warning($"build: {ex.GetType().Name}: {ex.Message}"); Enemies[n.Pointer] = new Enemy { Npc = n }; }
            }
            if (added > 0 && Debug) W($"radar on: {added} new enemies ({Enemies.Count} tracked)");
        }

        static Regex skipRx;
        static string skipFor;
        static Regex SkipRx()
        {
            string s = RadarSenseMod.SkipParts.Value ?? "";
            if (skipRx != null && skipFor == s) return skipRx;
            var words = new List<string>();
            foreach (var w in s.Split(',')) if (w.Trim().Length > 0) words.Add(Regex.Escape(w.Trim()));
            skipFor = s;
            skipRx = new Regex(words.Count > 0 ? "(?i)" + string.Join("|", words) : "(?!)");
            return skipRx;
        }
        static readonly Regex LowerLod = new(@"(?i)LOD[1-9]");

        // Full = the baked whole-body mesh contract enemies use (the separate skin/clothes meshes are off there), so it is Core.
        // Detail levels: Core 0, Body 1, Clothes 2, Full 3. A part is outlined when its rank is at most the level.
        // Names from the logged enemy bodies: CC_Combined = the skin mesh, Vest_1, Male_Shirt/Suit_Jacket, Suit_Pants,
        // Sleeves, Hands, DressShoes; hair (Receded_, Hair_, Side_Part, Short_Middle_Part), Beard_, ANB_tie, Holster and
        // anything unknown count as Full.
        static readonly Regex CoreRx = new(@"(?i)^(CC_Combined|Vest|Full$)");
        static readonly Regex BodyRx = new(@"(?i)^(Male_Shirt|Suit_Jacket|Suit_Pants|Sleeves|Hands)");
        static readonly Regex ClothesRx = new(@"(?i)^DressShoes");
        static int RankOf(string name) => CoreRx.IsMatch(name) ? 0 : BodyRx.IsMatch(name) ? 1 : ClothesRx.IsMatch(name) ? 2 : 3;
        static int DetailLevel()
        {
            string d = RadarSenseMod.Detail.Value?.Trim() ?? "";
            return d.Equals("Core", StringComparison.OrdinalIgnoreCase) ? 0
                 : d.Equals("Body", StringComparison.OrdinalIgnoreCase) ? 1
                 : d.Equals("Clothes", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
        }

        static Enemy Build(ANBBasicNPC n)
        {
            var e = new Enemy { Npc = n };
            var used = new List<string>();
            var skipped = new List<string>();
            foreach (var src in n.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (src == null || src.sharedMesh == null) continue;
                string name = src.name, mesh = src.sharedMesh.name;
                if (name == "RadarSense") continue;
                if (SkipRx().IsMatch(name) || SkipRx().IsMatch(mesh) || (!RadarSenseMod.IncludeLods.Value && LowerLod.IsMatch(name))) { skipped.Add(name); continue; }
                e.Parts.Add(new Part { Src = src, Copy = MakeCopy(src), Rank = RankOf(name) });
                used.Add(name);
            }
            if (Debug)
            {
                string sig = string.Join(",", used) + " | " + string.Join(",", skipped);
                if (LoggedBodies.Add(sig))
                    W($"enemy body '{n.name}': {used.Count} parts [{string.Join(", ", used)}], skipped [{string.Join(", ", skipped)}]");
            }
            return e;
        }

        // Renderers added to an enemy after Build. Ones whose name says hair/beard/tie/holster keep their rank; anything
        // else new is treated as body (Core), since a runtime-made renderer on an enemy is most likely the merged body.
        static readonly Regex Extras = new(@"(?i)(hair|beard|tie|holster|shadow|afro|receded|part|holo)");
        static int rescanLines;
        static void Rescan(Enemy e)
        {
            var n = e.Npc;
            e.Parts.RemoveAll(p => !RadarSenseMod.Alive(p.Src) || !RadarSenseMod.Alive(p.Copy));   // the old body, destroyed on re-dress
            var have = new HashSet<IntPtr>();
            foreach (var p in e.Parts) if (RadarSenseMod.Alive(p.Src)) have.Add(p.Src.Pointer);
            var added = new List<string>();
            int all = 0, live = 0;
            foreach (var src in n.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (src == null || src.name == "RadarSense") continue;
                all++;
                bool on = src.enabled && src.gameObject.activeInHierarchy;
                if (on) live++;
                if (have.Contains(src.Pointer) || src.sharedMesh == null) continue;
                string name = src.name, mesh = src.sharedMesh.name;
                if (SkipRx().IsMatch(name) || SkipRx().IsMatch(mesh) || (!RadarSenseMod.IncludeLods.Value && LowerLod.IsMatch(name))) continue;
                int rank = RankOf(name);
                if (rank == 3 && !Extras.IsMatch(name)) rank = 0;
                e.Parts.Add(new Part { Src = src, Copy = MakeCopy(src), Rank = rank });
                added.Add($"{name} [{mesh}] {(on ? "on" : "off")} r{rank}");
            }
            if (!Debug || rescanLines >= 40) return;
            if (added.Count > 0) { rescanLines++; W($"rescan #{Math.Abs(n.GetInstanceID()) % 100000}: {added.Count} new renderers: {string.Join(", ", added)}"); }
            else if (live == 0 || e.DiagLines < 3)
            {
                rescanLines++;
                // nothing new: list what IS switched on anywhere under the enemy (any Renderer type), to find what draws it
                var drawn = new List<string>();
                foreach (var r in n.GetComponentsInChildren<Renderer>(false))
                    if (r != null && r.enabled && r.name != "RadarSense" && drawn.Count < 12) drawn.Add($"{r.name}:{r.GetIl2CppType().Name}");
                W($"rescan #{Math.Abs(n.GetInstanceID()) % 100000}: nothing new, skinned {live}/{all} on; renderers on: {string.Join(", ", drawn)}");
            }
        }

        static void SetMat(Part p, bool dim)
        {
            var m = dim ? matDim : mat;
            int subs = Math.Max(1, p.Copy.sharedMesh != null ? p.Copy.sharedMesh.subMeshCount : 1);
            var mats = new Il2CppReferenceArray<Material>(subs);
            for (int i = 0; i < subs; i++) mats[i] = m;
            p.Copy.sharedMaterials = mats;
            p.Dim = dim;
        }

        static SkinnedMeshRenderer MakeCopy(SkinnedMeshRenderer src)
        {
            var go = new GameObject("RadarSense");
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src.transform, false);
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.enabled = false;
            r.sharedMesh = src.sharedMesh;
            r.rootBone = src.rootBone;
            r.bones = src.bones;
            r.localBounds = src.localBounds;
            r.updateWhenOffscreen = true;               // copied bounds may be stale; only runs while the sense is on
            r.allowOcclusionWhenDynamic = false;        // occlusion culling would hide it behind the very walls it's for
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            int subs = Math.Max(1, src.sharedMesh.subMeshCount);
            var mats = new Il2CppReferenceArray<Material>(subs);
            for (int i = 0; i < subs; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            return r;
        }

        static void Show()
        {
            var cam = ANBStaticGameManager.MainCam;
            Vector3 eye = RadarSenseMod.Alive(cam) ? cam.transform.position : Vector3.zero;
            float range = RadarSenseMod.Range.Value;
            string style = RadarSenseMod.Style.Value?.Trim() ?? "";
            bool onlyUnseen = !style.Equals("Behind", StringComparison.OrdinalIgnoreCase);   // Hidden and Blocked
            string rv = RadarSenseMod.Reveal.Value?.Trim() ?? "";
            bool revealAll = rv.Equals("All", StringComparison.OrdinalIgnoreCase) || (RadarSenseMod.FocusRevealsAll.Value && Focus());
            bool revealMoving = revealAll || !rv.Equals("Awake", StringComparison.OrdinalIgnoreCase);
            float stepSpeed = RadarSenseMod.StepSpeed.Value;
            bool showStill = RadarSenseMod.ShowStill.Value && revealMoving;
            int shown = 0, kept = 0, level = DetailLevel();
            foreach (var e in Enemies.Values)
            {
                bool show;
                try
                {
                    var n = e.Npc;
                    // In range and alive. isInView is the game's own test: its VisCheck renderer on screen and
                    // BlockedSight's rays not blocked, i.e. you can see the enemy directly.
                    bool near = RadarSenseMod.Alive(n) && !n.isDead && n.gameObject.activeInHierarchy
                                && (!RadarSenseMod.Alive(cam) || Vector3.Distance(eye, Body(n)) <= range);
                    // Moving: game-time speed of the body (the NavMesh agent keeps moving it while the game hides the mesh).
                    bool moving = false;
                    if (near && revealMoving && !revealAll)
                    {
                        Vector3 pos = Body(n);
                        float t = Time.time, dt = t - e.LastT;
                        if (e.LastT >= 0 && dt > 0.001f) moving = (pos - e.LastPos).magnitude / dt > stepSpeed;
                        e.LastPos = pos; e.LastT = t;
                        if (moving) e.MovingUntil = t + 1f;     // no flicker between bright and dim when it pauses
                    }
                    bool bright = revealAll || moving || Time.time < e.MovingUntil;
                    bool still = near && showStill && !bright;
                    e.Dim = still;
                    if (near && (bright || still))
                    {
                        if (moving && !n.isInView) Heard.Add(n.Pointer);
                        if (n.outOfViewObjectsOff) Unhidden.Add(n.Pointer);
                        if (!n.isInView) kept++;
                        n.outOfViewTime = 0f;   // the game shows it again (or never hides it)
                    }
                    show = near && (!onlyUnseen || !SeenDirectly(n, eye));
                    if (Debug && near) { ViewDiag(e, n, eye); PartsDiag(e, Body(n), eye); }
                }
                catch { show = false; }
                if (show) shown++;
                if (!show && !e.Shown) continue;    // already off: dead, pooled, far or in view
                e.Shown = show;
                int srcOn = 0;
                foreach (var p in e.Parts)
                {
                    if (!RadarSenseMod.Alive(p.Copy)) continue;
                    bool live = RadarSenseMod.Alive(p.Src) && p.Src.enabled && p.Src.gameObject.activeInHierarchy;
                    if (live) srcOn++;
                    bool s = show && p.Rank <= level && live;
                    if (s && p.Copy.sharedMesh != p.Src.sharedMesh) p.Copy.sharedMesh = p.Src.sharedMesh;
                    if (s && p.Dim != e.Dim && RadarSenseMod.Alive(matDim)) SetMat(p, e.Dim);
                    if (p.Copy.enabled != s) p.Copy.enabled = s;
                }
                // Only the vest (or nothing) switched on while the enemy is wanted: the game may have given it renderers
                // that didn't exist when Build ran (pooled enemies are re-dressed / merged on respawn). Look again.
                if (show && srcOn <= 2 && Time.unscaledTime >= e.NextRescan)
                {
                    e.NextRescan = Time.unscaledTime + 2f;
                    try { Rescan(e); } catch (Exception ex) { if (Debug) W($"rescan: {ex.GetType().Name}: {ex.Message}"); }
                }
            }
            if (shown > shownPeak) shownPeak = shown;
            KeptNow = kept;
        }

        // Why does an enemy behind a wall count as "in view" when it is close (the highlight vanishes)? Logs the inputs of
        // the game's isInView (checkVisibility): VisCheck.isVisible, useRaycastVisCheck, and with it on, which sight dots
        // (children of HiddenPosCheckDots) have a clear ray from your head (BlockedSight reverse: head -> dot, length
        // distance - 0.1, viewBlockMask). Own ray head -> body centre says what is really in between.
        // Lines: every isInView flip within 8 m, and every 1 s while "in view" with a wall in the way within 8 m. 12 per enemy.
        // The game's isInView with a stricter test: renderer visible and at least SeenFraction of the sight dots clear
        // from your head (same rays as the game's checkVisibility: head -> dot, length distance - 0.1, viewBlockMask).
        static bool SeenDirectly(ANBBasicNPC n, Vector3 eye)
        {
            if (!n.isInView) return false;
            float frac = RadarSenseMod.SeenFraction.Value;
            var root = n.HiddenPosCheckDots;
            if (frac <= 0f || !n.useRaycastVisCheck || !RadarSenseMod.Alive(root)) return true;
            int total = root.childCount;
            if (total == 0) return true;
            int need = (int)Math.Ceiling(Math.Min(frac, 1f) * total - 1e-4f), clear = 0;
            var pt = n.Playertarget;
            Vector3 head = RadarSenseMod.Alive(pt) ? pt.position : eye;
            int mask = n.viewBlockMask.value;
            for (int i = 0; i < total; i++)
            {
                Vector3 d = root.GetChild(i).position - head;
                float len = d.magnitude - 0.1f;
                if (len <= 0f || !Physics.Raycast(head, d.normalized, len, mask)) clear++;
                if (clear >= need) return true;
                if (clear + (total - 1 - i) < need) return false;
            }
            return false;
        }

        // Which body meshes the game has switched on, once per distinct set (so a Core/Body outline that shows only the
        // vest can be explained): [name:rank, ...] of the enabled source meshes, for enemies within 8 m.
        static readonly HashSet<string> LoggedSets = new();
        static void PartsDiag(Enemy e, Vector3 body, Vector3 eye)
        {
            float now = Time.unscaledTime;
            if (now < e.NextSig || LoggedSets.Count >= 40 || Vector3.Distance(eye, body) > DiagRange) return;
            e.NextSig = now + 2f;
            var on = new List<string>();
            foreach (var p in e.Parts)
                if (RadarSenseMod.Alive(p.Src) && p.Src.enabled && p.Src.gameObject.activeInHierarchy) on.Add($"{p.Src.name}:{p.Rank}");
            string sig = string.Join(",", on);
            if (LoggedSets.Add(sig)) W($"enabled meshes ({on.Count}): {sig}");
        }

        const float DiagRange = 8f;
        static bool diagHeader;
        static string DiagRay(Vector3 from, Vector3 to, int mask)
        {
            Vector3 d = to - from;
            float len = d.magnitude - 0.1f;
            if (len <= 0f) return "clear(0)";
            return Physics.Raycast(from, d.normalized, out RaycastHit h, len, mask)
                ? $"HIT '{h.collider.name}' L{h.collider.gameObject.layer} {h.distance:0.00}/{len + 0.1f:0.00} m"
                : "clear";
        }

        static void ViewDiag(Enemy e, ANBBasicNPC n, Vector3 eye)
        {
            if (e.DiagLines >= 60) return;
            Vector3 body = Body(n);
            float dist = Vector3.Distance(eye, body);
            bool seen = SeenDirectly(n, eye);       // seen = no highlight
            int sv = seen ? 1 : 0;
            if (dist > DiagRange) { e.InView = sv; return; }
            bool flip = e.InView >= 0 && e.InView != sv;
            e.InView = sv;
            float now = Time.unscaledTime;
            int srcOn = 0, copyOn = 0, lvl = DetailLevel();
            foreach (var p in e.Parts)
            {
                if (p.Rank <= lvl && RadarSenseMod.Alive(p.Src) && p.Src.enabled && p.Src.gameObject.activeInHierarchy) srcOn++;
                if (RadarSenseMod.Alive(p.Copy) && p.Copy.enabled) copyOn++;
            }
            bool notDrawn = !seen && srcOn == 0;    // highlight wanted, but the game has the body meshes off
            var pt = n.Playertarget;
            Vector3 head = RadarSenseMod.Alive(pt) ? pt.position : eye;
            int mask = n.viewBlockMask.value;
            string own = DiagRay(head, body + Vector3.up * 0.2f, mask);
            bool stuck = seen && own.StartsWith("HIT");     // no highlight although a wall is in the way
            if (now < e.NextDiag || !(flip || notDrawn || stuck)) return;
            e.NextDiag = now + (flip ? 0.3f : 1f);
            e.DiagLines++;
            if (!diagHeader)
            {
                diagHeader = true;
                W($"view diag: viewBlockMask {mask:X}, useRaycastVisCheck {n.useRaycastVisCheck}, vischeck pulse {n.vischeckPulseRate:0.00} s, VisCheck '{(RadarSenseMod.Alive(n.VisCheck) ? n.VisCheck.name : "null")}'");
            }
            string vis = RadarSenseMod.Alive(n.VisCheck) ? (n.VisCheck.isVisible ? "visible" : "NOT visible") : "no renderer";
            string dots = "";
            var root = n.HiddenPosCheckDots;
            if (n.useRaycastVisCheck && RadarSenseMod.Alive(root))
            {
                int clear = 0, total = root.childCount;
                for (int i = 0; i < total; i++)
                    if (DiagRay(head, root.GetChild(i).position, mask).StartsWith("clear")) clear++;
                dots = $", dots clear {clear}/{total}";
            }
            string kind = notDrawn ? "NOT DRAWN" : flip ? (seen ? "SEEN" : "HIGHLIGHT") : "STAY";
            W($"view {kind} #{Math.Abs(n.GetInstanceID()) % 100000} d={dist:0.0} m: isInView {n.isInView}, renderer {vis}{dots}, chest {own}, body meshes on {srcOn}/{e.Parts.Count}, copies on {copyOn}, game hid {n.outOfViewObjectsOff}");
        }

        // ------------------------------------------------------------------ clubs

        // Billy Clubs' own list (same DLL). 0.3.0 searched every loaded HVRGrabbable, assets included, every 2 s:
        // the mod's cost went from 0.125 to 0.23 ms/frame with 3 enemies.
        static readonly List<GameObject> clubList = new();
        static void ScanClubs()
        {
            if (!RadarSenseMod.ClubHighlight.Value) return;
            BillyClubs.BillyClubsMod.CopyClubs(clubList);
            foreach (var go in clubList)
            {
                if (Clubs.ContainsKey(go.Pointer)) continue;
                var m = new ClubMark { Root = go };
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r == null || !r.enabled || r.name == "RadarSense") continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    m.Parts.Add(new MPart { Src = r, Copy = MakeMeshCopy(r, mf.sharedMesh) });
                }
                Clubs[go.Pointer] = m;
                if (Debug) W($"club '{go.name}': {m.Parts.Count} visible parts");
            }
        }

        static MeshRenderer MakeMeshCopy(MeshRenderer src, Mesh mesh)
        {
            var old = src.transform.Find("RadarSense");            // carried over from an earlier scene
            var go = old != null ? old.gameObject : new GameObject("RadarSense");
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src.transform, false);
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            if (r == null) r = go.AddComponent<MeshRenderer>();
            r.enabled = false;
            r.allowOcclusionWhenDynamic = false;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            int subs = Math.Max(1, mesh.subMeshCount);
            var mats = new Il2CppReferenceArray<Material>(subs);
            for (int i = 0; i < subs; i++) mats[i] = clubMat;
            r.sharedMaterials = mats;
            return r;
        }

        static readonly List<IntPtr> goneClubs = new();
        static void ShowClubs()
        {
            if (Clubs.Count == 0) return;
            bool enabled = RadarSenseMod.ClubHighlight.Value;
            var cam = ANBStaticGameManager.MainCam;
            if (!RadarSenseMod.Alive(cam)) return;
            Vector3 eye = cam.transform.position;
            float min = RadarSenseMod.ClubMinDistance.Value, range = RadarSenseMod.Range.Value;
            goneClubs.Clear();
            foreach (var kv in Clubs)
            {
                var m = kv.Value;
                if (!RadarSenseMod.Alive(m.Root)) { goneClubs.Add(kv.Key); continue; }
                float d = Vector3.Distance(eye, m.Root.transform.position);
                bool show = enabled && m.Root.activeInHierarchy && d > min && d <= range;
                if (!show && !m.Shown) continue;
                m.Shown = show;
                foreach (var p in m.Parts)
                {
                    if (!RadarSenseMod.Alive(p.Copy)) continue;
                    bool on = show && RadarSenseMod.Alive(p.Src) && p.Src.enabled;
                    if (p.Copy.enabled != on) p.Copy.enabled = on;
                }
            }
            foreach (var k in goneClubs) Clubs.Remove(k);
        }

        // Where the enemy really is: the root stays where it spawned, the body moves with the NavMeshAgent's object.
        static Vector3 Body(ANBBasicNPC n)
        {
            var t = n.agentTransform;
            if (t != null) return t.position;
            t = n.visionBase;
            return t != null ? t.position : n.transform.position;
        }

        static bool Blocked() => string.Equals(RadarSenseMod.Style.Value?.Trim(), "Blocked", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ material

        static bool EnsureMaterial()
        {
            if (RadarSenseMod.Alive(mat)) return true;
            if (shaderFailed) return false;
            string wanted = RadarSenseMod.ShaderName.Value?.Trim();
            var names = string.IsNullOrEmpty(wanted) || wanted.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Hidden/Internal-Colored", "UI/Default" }
                : new[] { wanted };
            foreach (var name in names)
            {
                var sh = FindShader(name);
                if (sh == null) { if (Debug) W($"shader '{name}' not in the build"); continue; }
                mat = new Material(sh) { name = "RadarSense", hideFlags = HideFlags.DontUnloadUnusedAsset };
                matDim = new Material(sh) { name = "RadarSenseDim", hideFlags = HideFlags.DontUnloadUnusedAsset };
                clubMat = new Material(sh) { name = "RadarSenseClub", hideFlags = HideFlags.DontUnloadUnusedAsset };
                matMode = mat.HasProperty("_ZTest") ? "Internal-Colored" : "UI";
                mat.renderQueue = 3100;         // after every opaque wall has written its depth
                matDim.renderQueue = 3100;
                clubMat.renderQueue = 3100;
                lastKey = null;
                ApplyMaterial();
                if (Debug) W($"shader '{sh.name}' ({matMode} depth control), properties: {Props(sh)}");
                return true;
            }
            shaderFailed = true;
            RadarSenseMod.Log.Warning("no usable shader found - radar sense is off until the next level load");
            return false;
        }

        static string lastKey;
        static void ApplyMaterial()
        {
            string key = $"{RadarSenseMod.StillBrightness.Value}|{RadarSenseMod.Brightness.Value}|{RadarSenseMod.Color.Value}|{RadarSenseMod.ClubColor.Value}|{RadarSenseMod.Opacity.Value}|{RadarSenseMod.Style.Value}";
            if (key == lastKey) return;
            lastKey = key;
            if (!ColorUtility.TryParseHtmlString(RadarSenseMod.Color.Value?.Trim(), out var col)) col = new Color(1f, 0.06f, 0.06f);
            col.a = Mathf.Clamp(RadarSenseMod.Opacity.Value, 0.05f, 1f);
            float br = Mathf.Clamp(RadarSenseMod.Brightness.Value, 0.1f, 1f);
            col = new Color(col.r * br, col.g * br, col.b * br, col.a);
            int test = (int)(Blocked() ? CompareFunction.Always : CompareFunction.Greater);   // Hidden, Behind: Greater
            SetUp(mat, col, test);
            float sb = Mathf.Clamp(RadarSenseMod.StillBrightness.Value, 0.1f, 1f);
            if (RadarSenseMod.Alive(matDim)) SetUp(matDim, new Color(col.r * sb, col.g * sb, col.b * sb, col.a), test);
            if (!ColorUtility.TryParseHtmlString(RadarSenseMod.ClubColor.Value?.Trim(), out var cc)) cc = new Color(1f, 0.75f, 0f);
            cc.a = col.a;
            if (RadarSenseMod.Alive(clubMat)) SetUp(clubMat, cc, (int)CompareFunction.Greater);   // a club in plain sight: no glow
        }

        static void SetUp(Material m, Color col, int test)
        {
            m.SetColor("_Color", col);
            if (matMode == "Internal-Colored")
            {
                m.SetInt("_ZTest", test);
                m.SetInt("_ZWrite", 0);
                m.SetInt("_Cull", (int)CullMode.Back);    // fewer of the enemy's own far-side faces
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }
            else m.SetInt("unity_GUIZTestMode", test);
        }

        static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s != null) return s;
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Shader>()))
            {
                var t = o.TryCast<Shader>();
                if (t != null && t.name == name) return t;
            }
            return null;
        }

        static string Props(Shader sh)
        {
            var l = new List<string>();
            try { for (int i = 0; i < sh.GetPropertyCount(); i++) l.Add(sh.GetPropertyName(i)); } catch { }
            return string.Join(" ", l);
        }
    }

    // Frame times per minute, split by state, so the cost of keeping unseen enemies shown can be read from the log:
    // off = sense off; on = sense on, nothing kept; keeping = sense on and keeping out-of-view enemies shown.
    // In VR the frame time sits on the headset's refresh (11.1 ms at 90 Hz), so the cost shows as MISSED frames:
    // "slow" = frames over 1.4x the minute's median. Headroom below the refresh needs fpsVR / CapFrameX.
    internal static class Perf
    {
        static readonly List<float>[] Dt = { new(), new(), new() };
        static readonly string[] Names = { "off", "on", "keeping unseen" };
        static long keptSum, modTicks;
        static int frames;
        static float since = -1;

        internal static void Frame(long modCost)
        {
            if (!RadarSenseMod.PerfLog.Value) { since = -1; return; }
            float now = Time.realtimeSinceStartup;
            if (since < 0) { since = now; Clear(); return; }
            int b = !Radar.On ? 0 : Radar.KeptNow > 0 ? 2 : 1;
            Dt[b].Add(Time.unscaledDeltaTime * 1000f);
            if (b == 2) keptSum += Radar.KeptNow;
            modTicks += modCost;
            frames++;
            if (now - since >= 60f) { Report(now - since); since = now; Clear(); }
        }

        static void Clear() { foreach (var l in Dt) l.Clear(); keptSum = 0; modTicks = 0; frames = 0; }

        static void Report(float secs)
        {
            var all = new List<float>();
            foreach (var l in Dt) all.AddRange(l);
            if (all.Count == 0) return;
            all.Sort();
            float median = all[all.Count / 2];
            var parts = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var l = Dt[i];
                if (l.Count == 0) continue;
                l.Sort();
                float sum = 0; int slow = 0;
                foreach (var v in l) { sum += v; if (v > median * 1.4f) slow++; }
                string kept = i == 2 ? $", {keptSum / (float)l.Count:0.0} kept" : "";
                parts.Add($"{Names[i]} {l.Count} fr avg {sum / l.Count:0.0} p95 {l[(int)(l.Count * 0.95f)]:0.0} max {l[^1]:0} ms, slow {100f * slow / l.Count:0.0}%{kept}");
            }
            double modMs = modTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, frames);
            string steps = Steps.Played > 0 ? $" | {Steps.Played} steps from {Steps.WalkersCount} enemies" : "";
            Steps.ClearCounts();
            RadarSenseMod.Log.Msg($"perf {secs:0} s (median {median:0.0} ms): {string.Join(" | ", parts)} | mod {modMs:0.000} ms/frame{steps}");
        }
    }
}
