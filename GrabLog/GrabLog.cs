using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppInterop.Runtime;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(GrabLog.GrabLogMod), "Grab Log", "0.1.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace GrabLog;

// Observation only. Never calls CanGrab/CanHover, selects a grab point, retries a grab,
// changes collisions, or writes any game fields. Predicate results come from postfixes
// on calls the game (or another mod) actually made, with their original frame/time.
public sealed class GrabLogMod : MelonMod
{
    internal static MelonPreferences_Entry<bool> Enabled;
    internal static MelonPreferences_Entry<float> SampleInterval, ObservationRadius;
    internal static MelonPreferences_Entry<int> MaxFileMB;
    internal static MelonLogger.Instance Log;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static double Now => Clock.Elapsed.TotalSeconds;
    internal static bool Active => Enabled != null && Enabled.Value && file != null && !capped;
    static StreamWriter file;
    static bool capped, wasEnabled;
    static long bytes, sequence;
    static double flushAt, scanAt = -1, pruneAt;
    static readonly HashSet<string> warnings = new();
    static readonly Dictionary<string, int> counts = new();
    internal static readonly Dictionary<int, HVRGrabbable> Items = new();
    internal static readonly Dictionary<int, HandState> Hands = new();
    internal static readonly Dictionary<int, ReleaseState> Releases = new();

    internal sealed class HandState
    {
        internal HVRHandGrabber Hand;
        internal bool Grip, Trigger, Initialized;
        internal int Held, Hover, TriggerHover;
        internal Details.PromptKey? Prompt;
        internal double SampleAt, PressAt = -1;
        internal bool GotObject, EmptyAtPress;
        internal long PressId;
        internal int LastInputFrame = -1;
        internal readonly Dictionary<string, Gate> Gates = new();
    }
    internal sealed class Gate
    {
        public bool result { get; set; }
        public int frame { get; set; }
        public double time { get; set; }
    }
    internal sealed class ReleaseState
    {
        internal double Time;
        internal int Frame, Count;
        internal string Hand;
    }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var c = MelonPreferences.CreateCategory("GrabLog", "Grab Log");
        Enabled = c.CreateEntry("Enabled", true, description: "Observe grab input, prompts, detection bags, attempts and released items. Changes no grab behaviour.");
        SampleInterval = c.CreateEntry("SampleInterval", 0.10f, description: "Seconds between nearby-item snapshots (0.05 to 2). Input and grab events are recorded separately.");
        ObservationRadius = c.CreateEntry("ObservationRadius", 0.75f, description: "Metres to include nearby items in diagnostic samples (0.1 to 3). This does NOT change the game's grab radius.");
        MaxFileMB = c.CreateEntry("MaxFileMB", 128, description: "Stop recording after this many MiB per session (8 to 1024); restart the game for a new session.");
        try
        {
            var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "GrabLog");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jsonl");
            file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            Write("session", new { version = "0.1.0", utc = DateTime.UtcNow, units = "metres, degrees, seconds", mods = MelonMod.RegisteredMelons.Select(m => m.Info.Name + " " + m.Info.Version).ToArray() });
            file.Flush();
            Log.Msg($"Observation only. Logging to {path}");
        }
        catch (Exception e) { Warn("open file", e); }
        wasEnabled = Enabled.Value;
        scanAt = Now + 3;
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        Safe("scene", () =>
        {
            // Retain live persistent hands/items but discard all scene-relative observation history.
            foreach (var s in Hands.Values) EndPress(s, "scene change");
            Write("summary", new { reason = "scene change", counts = new Dictionary<string, int>(counts) });
            Hands.Clear(); Releases.Clear(); Items.Clear(); counts.Clear(); Details.Reset();
            Write("scene", new { buildIndex, sceneName });
            scanAt = Now + 3;
            file?.Flush();
        });
    }

    public override void OnUpdate()
    {
        Safe("update", () =>
        {
            if (wasEnabled != Enabled.Value)
            {
                Write("enabled", new { enabled = Enabled.Value });
                foreach (var s in Hands.Values) EndPress(s, "logging toggled");
                Hands.Clear(); Releases.Clear();
                wasEnabled = Enabled.Value;
                if (wasEnabled) scanAt = Now;
            }
            if (Now >= flushAt) { flushAt = Now + 1; file?.Flush(); }
            if (!Active) return;
            if (scanAt >= 0 && Now >= scanAt)
            {
                scanAt = -1;
                foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRGrabbable>(), FindObjectsSortMode.None)) Register(o.TryCast<HVRGrabbable>());
                foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None)) State(o.TryCast<HVRHandGrabber>());
                Write("discovery", new { hands = Hands.Count, items = Items.Count });
            }
            if (Now >= pruneAt)
            {
                pruneAt = Now + 2;
                foreach (var id in Items.Where(p => !Alive(p.Value)).Select(p => p.Key).ToArray()) { Items.Remove(id); Releases.Remove(id); }
                foreach (var id in Hands.Where(p => !Alive(p.Value.Hand)).Select(p => p.Key).ToArray()) { EndPress(Hands[id], "hand destroyed"); Hands.Remove(id); }
                foreach (var s in Hands.Values)
                    foreach (var key in s.Gates.Where(p => Now - p.Value.time > 5).Select(p => p.Key).ToArray()) s.Gates.Remove(key);
            }
        });
    }

    public override void OnApplicationQuit()
    {
        Safe("close", () =>
        {
            foreach (var s in Hands.Values) EndPress(s, "game closed");
            Write("summary", new { reason = "game closed", counts });
            file?.Dispose(); file = null;
        });
    }

    internal static void Register(HVRGrabbable g) { if (Alive(g)) Items[g.GetInstanceID()] = g; }
    internal static HandState State(HVRHandGrabber h)
    {
        if (!Alive(h)) return null;
        int id = h.GetInstanceID();
        if (!Hands.TryGetValue(id, out var s)) Hands[id] = s = new HandState { Hand = h };
        return s;
    }
    internal static bool Alive(Object o) { try { return o != null && !o.WasCollected && o; } catch { return false; } }
    internal static int Id(Object o) => Alive(o) ? o.GetInstanceID() : 0;
    internal static string Side(HVRHandGrabber h) => h.IsLeftHand ? "left" : "right";
    internal static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    internal static float[] Q(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
    internal static float Clamp(float value, float fallback, float min, float max) => float.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
    internal static void Safe(string where, Action action) { try { action(); } catch (Exception e) { Warn(where, e); } }
    internal static void Warn(string where, Exception e) { if (warnings.Add(where + e.GetType().Name)) Log?.Warning($"{where}: {e.Message}"); }

    internal static void Write(string kind, object data)
    {
        if (file == null || capped) return;
        try
        {
            string line = JsonSerializer.Serialize(new { seq = ++sequence, time = Now, frame = Time.frameCount, gameTime = Time.time, kind, data });
            long size = System.Text.Encoding.UTF8.GetByteCount(line) + 2;
            if (bytes + size > Math.Clamp(MaxFileMB.Value, 8, 1024) * 1024L * 1024L)
            {
                file.WriteLine(JsonSerializer.Serialize(new { kind = "log_limit", time = Now, counts }));
                file.Flush(); capped = true;
                Log.Warning("Grab Log reached MaxFileMB and stopped recording. Restart the game for a new session.");
                return;
            }
            file.WriteLine(line); bytes += size;
            counts.TryGetValue(kind, out int n); counts[kind] = n + 1;
        }
        catch (Exception e) { capped = true; Warn("write (logging stopped)", e); }
    }

    // Called after HVR has refreshed its input, before CheckGrab can consume the press.
    internal static void Input(HVRHandGrabber h)
    {
        if (!Active) return;
        var s = State(h);
        if (s.LastInputFrame == Time.frameCount) return;
        s.LastInputFrame = Time.frameCount;
        bool grip = h.IsGripGrabActive, trigger = h.IsTriggerGrabActive;
        if (grip && !s.Grip)
        {
            s.PressAt = Now; s.PressId++; s.GotObject = false; s.EmptyAtPress = !h.IsGrabbing && !Alive(h.GrabbedTarget);
            Event("grip_press", h);
        }
        if (!grip && s.Grip) { Event("grip_release", h); EndPress(s, "grip released"); }
        if (trigger != s.Trigger) Event(trigger ? "trigger_press" : "trigger_release", h);
        s.Grip = grip; s.Trigger = trigger;
    }

    static void EndPress(HandState s, string reason)
    {
        if (s.PressAt < 0) return;
        Write("grip_cycle", new { hand = Id(s.Hand), press = s.PressId, seconds = Now - s.PressAt, s.EmptyAtPress, s.GotObject, reason });
        s.PressAt = -1;
    }

    internal static void Observe(HVRHandGrabber h)
    {
        if (!Active || !Alive(h)) return;
        var s = State(h);
        int held = Id(h.GrabbedTarget), hover = Id(h.HoverTarget), trigger = Id(h.TriggerHoverTarget);
        if (!s.Initialized || held != s.Held || hover != s.Hover || trigger != s.TriggerHover)
        {
            if (held != 0 && held != s.Held) s.GotObject = true;
            Event("hand_state", h, h.GrabbedTarget);
            s.Held = held; s.Hover = hover; s.TriggerHover = trigger; s.Initialized = true;
        }
        Prompt(h);
        if (Now < s.SampleAt) return;
        s.SampleAt = Now + Clamp(SampleInterval.Value, .1f, .05f, 2);
        var near = Details.Candidates(h);
        if (near.Count > 0 || h.IsGripGrabActive || h.IsTriggerGrabActive || h.IsGrabbing)
            Write("sample", new { hand = Details.Hand(h), candidates = near.Select(g => Details.Item(h, g)).ToArray() });
    }

    internal static void Prompt(HVRHandGrabber h)
    {
        if (!Active || !Alive(h)) return;
        var s = State(h);
        var key = Details.PromptSignature(h);
        if (s.Prompt == key) return;
        s.Prompt = key;
        Event("prompt_state", h, h.HoverTarget);
    }

    internal static void Event(string kind, HVRHandGrabber h, HVRGrabbable target = null, object extra = null)
    {
        if (!Active || !Alive(h)) return;
        Register(target);
        var candidates = Details.Candidates(h);
        if (Alive(target) && !candidates.Any(g => Id(g) == Id(target))) candidates.Add(target);
        Write(kind, new { hand = Details.Hand(h), target = Id(target), extra, candidates = candidates.Select(g => Details.Item(h, g)).ToArray() });
    }

    internal static void Predicate(HVRHandGrabber h, HVRGrabbable g, string method, bool result)
    {
        if (!Active || !Alive(h) || !Alive(g)) return;
        Register(g);
        var s = State(h);
        string key = method + ":" + Id(g);
        // Bounded even if a scene produces an unending stream of disposable items.
        if (s.Gates.Count >= 256 && !s.Gates.ContainsKey(key)) s.Gates.Clear();
        bool changed = !s.Gates.TryGetValue(key, out var previous) || previous.result != result;
        bool pressCheck = (h.IsGripGrabActivated || h.IsTriggerGrabActivated) && (previous == null || previous.frame != Time.frameCount);
        s.Gates[key] = new Gate { result = result, frame = Time.frameCount, time = Now };
        if (changed || pressCheck)
            Write("gate_result", new { hand = Id(h), side = Side(h), target = Id(g), method, result,
                gripHeld = h.IsGripGrabActive, gripActivated = h.IsGripGrabActivated, hover = Id(h.HoverTarget), held = Id(h.GrabbedTarget) });
    }

    internal static object GateFor(HVRHandGrabber h, HVRGrabbable g, string method)
        => State(h).Gates.TryGetValue(method + ":" + Id(g), out var gate) ? gate : null;

    internal static void Released(HVRHandGrabber h, HVRGrabbable g)
    {
        if (!Active || !Alive(g)) return;
        Register(g);
        Releases.TryGetValue(Id(g), out var old);
        Releases[Id(g)] = new ReleaseState { Time = Now, Frame = Time.frameCount, Hand = Side(h), Count = (old?.Count ?? 0) + 1 };
        Event("released", h, g);
    }
}
