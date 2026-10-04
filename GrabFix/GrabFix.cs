using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(GrabFix.GrabFixMod), "Grab Fix", "1.1.2", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace GrabFix;

public sealed class GrabFixMod : MelonMod
{
    internal static MelonPreferences_Entry<bool> Enabled, BufferPress, RegrabAssist, DebugLog;
    internal static MelonPreferences_Entry<string> Direction;
    internal static MelonPreferences_Entry<float> NearRadius, PalmOffset, DistanceWidth, BufferSeconds, RegrabRadius, RegrabSeconds, CatchBufferSeconds, EnemyRadius;
    internal static MelonLogger.Instance Log;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static double Now => Clock.Elapsed.TotalSeconds;
    internal static bool Active => Enabled != null && Enabled.Value;
    internal static bool BothMode => !string.Equals(Direction.Value, "Finger", StringComparison.OrdinalIgnoreCase);
    internal static readonly Dictionary<int, HandState> Hands = new();
    static readonly Dictionary<int, ReleasedItem> Released = new();
    static readonly HashSet<string> warned = new();
    static double scanAt, pruneAt;
    static bool wasEnabled;

    internal sealed class HandState
    {
        internal HVRHandGrabber Hand;
        internal readonly GripWindow Intent = new();
        internal readonly Geometry Geometry = new();
        internal bool ForceInput;
        internal int InputFrame = -1;
        internal double RetryAt;
    }
    sealed class ReleasedItem { internal HVRGrabbable Item; internal double At; }

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        var c = MelonPreferences.CreateCategory("GrabFix", "Grab Fix");
        Enabled = c.CreateEntry("Enabled", true, description: "Palm targeting and buffered pickups. Off restores detector geometry and stops assists.");
        Direction = c.CreateEntry("GrabDirection", "Both", description: "Both (the game's finger-aimed pickup plus a palm-aimed copy; finger targets win), Finger (the game's aim only, still widened). Changes detection, not how a held item sits in your hand.");
        NearRadius = c.CreateEntry("NearGrabRadius", .12f, description: "Minimum local pickup sphere radius in metres (the tested game uses 0.08). Both mode grows it further to cover fingers and palm. 0 = original radius; maximum 0.25.");
        PalmOffset = c.CreateEntry("PalmOffset", .03f, description: "Both mode: the local pickup sphere reaches this far in front of the palm, in metres (0 to 0.08).");
        DistanceWidth = c.CreateEntry("DistanceGrabWidth", 1.2f, description: "Width multiplier for distance pickup detectors (1 = original width, maximum 2). Does not extend distance-grab reach.");
        BufferPress = c.CreateEntry("BufferGripPress", true, description: "Let a grip pressed just before an item becomes available complete the pickup while grip stays held.");
        BufferSeconds = c.CreateEntry("GripBufferSeconds", .30f, description: "Real seconds to remember a missed grip press for local/distance pickup (0 to 0.6).");
        RegrabAssist = c.CreateEntry("RecentReleaseAssist", true, description: "Extra local catch tolerance for a loose item recently released from either of your hands. Requires a fresh grip press.");
        RegrabRadius = c.CreateEntry("RecentReleaseRadius", .18f, description: "Metres from the palm to a recently released item's collider surface for a catch (0.05 to 0.3).");
        RegrabSeconds = c.CreateEntry("RecentReleaseSeconds", 15f, description: "How long an item stays eligible after release, in real seconds (0 to 60).");
        CatchBufferSeconds = c.CreateEntry("CatchBufferSeconds", .60f, description: "Real seconds to keep a fresh grip press ready for a recently released nearby item (0 to 1).");
        EnemyRadius = c.CreateEntry("EnemyGrabRadius", .10f, description: "Enemies and downed (ragdoll) enemies can only be grabbed when their body is within this many metres of your palm, so a fist swung near a face or a lying body does not grab it. No distance grabs and no buffered grabs on enemies. 0 = no extra limit; maximum 0.25.");
        DebugLog = c.CreateEntry("DebugLog", false, description: "Log assisted pickup outcomes and detector setup.");
        wasEnabled = Active; scanAt = Now + 3;
        Log.Msg($"1.1.2 (enemy grab radius {EnemyRadius.Value:0.00} m) - release candidate - direction={Direction.Value}, wider local sphere, grip buffer, recent-release catches; native grab checks retained.");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        foreach (var s in Hands.Values) Safe("restore", s.Geometry.Restore);
        Hands.Clear(); Released.Clear(); scanAt = Now + 3;
    }

    public override void OnUpdate()
    {
        if (wasEnabled != Active)
        {
            foreach (var s in Hands.Values) { s.Intent.Reset(); s.ForceInput = false; Safe("restore", s.Geometry.Restore); }
            Released.Clear(); wasEnabled = Active;
            if (Active) scanAt = Now;
        }
        if (!Active) return;
        if (scanAt >= 0 && Now >= scanAt)
        {
            scanAt = -1;
            Safe("scene scan", () =>
            {
                foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None)) Register(o.TryCast<HVRHandGrabber>());
            });
        }
        if (Now < pruneAt) return;
        pruneAt = Now + 1;
        foreach (var id in Released.Where(x => !Alive(x.Value.Item) || Now - x.Value.At > Limit(RegrabSeconds.Value, 15, 0, 60)).Select(x => x.Key).ToArray()) Released.Remove(id);
        foreach (var id in Hands.Where(x => !Alive(x.Value.Hand)).Select(x => x.Key).ToArray())
        { Safe("restore dead hand", Hands[id].Geometry.Restore); Hands.Remove(id); }
    }

    public override void OnFixedUpdate()
    {
        foreach (var s in Hands.Values) Safe("geometry", () => s.Geometry.Apply(s.Hand));
    }
    public override void OnApplicationQuit() { foreach (var s in Hands.Values) Safe("restore", s.Geometry.Restore); }

    internal static HandState Register(HVRHandGrabber h)
    {
        if (!Alive(h)) return null;
        int id = h.GetInstanceID();
        if (!Hands.TryGetValue(id, out var s)) Hands[id] = s = new HandState { Hand = h };
        return s;
    }
    internal static bool Alive(Object o) { try { return o != null && !o.WasCollected && o; } catch { return false; } }
    internal static float Limit(float v, float fallback, float min, float max) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : fallback;
    internal static void Safe(string where, Action a) { try { a(); } catch (Exception e) { if (warned.Add(where + e.GetType().Name)) Log?.Warning($"{where}: {e.Message}"); } }
    internal static Vector3 Palm(HVRHandGrabber h) => Alive(h.Palm) ? h.Palm.position : h.transform.position;
    static bool Blocked(HVRHandGrabber h)
    {
        var game = ANBStaticGameManager.ANBmain;
        return !Active || !Alive(h) || !h.isActiveAndEnabled || !h.AllowGrabbing || h.NoDetectionDueToMenu ||
            (Alive(game) && game.Paused) || Time.timeScale <= 0 || h.IsGrabbing || Alive(h.GrabbedTarget) || h.IsForceGrabbing;
    }

    // Verified call immediately after IL2CPP's inlined UpdateGrabInputs, before the
    // native toggle, hover and grab checks. Never extend an old press by holding it.
    internal static void Input(HVRHandGrabber h)
    {
        var s = Register(h);
        if (!Active) { s.Intent.Reset(); s.Geometry.Restore(); return; }
        s.Geometry.Apply(h);
        if (s.InputFrame == Time.frameCount) return;
        s.InputFrame = Time.frameCount;
        if (s.Intent.Update(h.IsGripGrabActive, h.IsGripGrabActivated, Blocked(h), Now, Time.frameCount))
            s.ForceInput = Alive(h.Inputs) && h.Inputs.GetForceGrabActivated(h.HandSide);
        if (Blocked(h)) s.Intent.Consume();
    }

    internal static bool Buffered(HVRHandGrabber h)
        => Active && BufferPress.Value && !Blocked(h) && h.IsGripGrabActive &&
            Register(h).Intent.Open(Now, Time.frameCount, Limit(BufferSeconds.Value, .3f, 0, .6f));

    // CanHover's only extra gates in the inspected game are force-grabbing and
    // holding grip on a new target. Retain CanGrab and all other native eligibility.
    internal static bool AllowBufferedHover(HVRHandGrabber h, HVRGrabbable g)
        => Buffered(h) && h.AllowHovering && Alive(g) &&
           (g.GrabControl.ToString() == "GripOnly" || g.GrabControl.ToString() == "GripOrTrigger") && h.CanGrab(g);

    internal static void AfterHand(HVRHandGrabber h)
    {
        if (!Active || !Alive(h)) return;
        var s = Register(h);
        if (Blocked(h)) { s.Intent.Consume(); return; }
        if (!h.IsGripGrabActive || Now < s.RetryAt) return;
        if (Buffered(h) && Eligible(h.HoverTarget))
        {
            if (Try(h, h.HoverTarget, "buffered local", s)) return;
        }
        if (!RegrabAssist.Value || !s.Intent.Open(Now, Time.frameCount, Limit(CatchBufferSeconds.Value, .6f, 0, 1))) return;
        // An existing local hover has priority. Never pull a second item over it.
        if (Alive(h.HoverTarget) || Alive(h.TriggerHoverTarget) || h.IsHoveringSocket) return;
        HVRGrabbable best = null;
        float bestDistance = Limit(RegrabRadius.Value, .18f, .05f, .3f);
        foreach (var release in Released.Values)
        {
            var g = release.Item;
            if (Now - release.At > Limit(RegrabSeconds.Value, 15, 0, 60) || !Eligible(g)) continue;
            float distance = SurfaceDistance(g, Palm(h));
            if (distance <= bestDistance && h.CanGrab(g)) { best = g; bestDistance = distance; }
        }
        if (Alive(best)) Try(h, best, $"recent release ({bestDistance * 100:0.0} cm)", s);
    }

    internal static void AfterForce(HVRForceGrabber f)
    {
        if (!Active || !Alive(f) || !f.isActiveAndEnabled || !f.AllowGrabbing || !f.IsHovering) return;
        var h = f.HandGrabber;
        if (!Alive(h) || !Buffered(h)) return;
        var s = Register(h);
        if (!s.ForceInput || Now < s.RetryAt || f.IsGrabbing || f.IsForceGrabbing || h.IsHovering || h.IsHoveringSocket) return;
        // Same flick/style guard as native CheckGripButtonGrab. Only retry a real
        // force activation captured with this grip, never invent a new binding.
        if (f.RequiresFlick && (int)f.GrabStyle != 1) return;
        var g = f.HoverTarget;
        if (!Eligible(g)) return;
        s.RetryAt = Now + .06;
        bool ok = f.TryGrab(g, false);
        if (ok) s.Intent.Consume();
        if (DebugLog.Value) Log.Msg($"buffered distance {(h.IsLeftHand ? "L" : "R")} '{g.name}' result={ok} age={(Now - s.Intent.PressAt) * 1000:0}ms");
    }

    // Kinematic = the game (or a mod) has it docked somewhere - a holster, a socket, a wall
    // mount - as opposed to a loose item that gravity/physics is actually driving (lying on
    // the floor or in flight). Never widen reach for a docked item; only its own holster slot
    // should place your hand close enough to trigger the game's native, unwidened detection.
    internal static bool Docked(HVRGrabbable g) => Alive(g?.Rigidbody) && g.Rigidbody.isKinematic;

    // A limb or body part of an enemy (standing, stunned or ragdolled). Looked up on demand: the
    // callers only ask once a hover is already true, and a gun taken from an enemy stops counting
    // the moment it is reparented.
    internal static bool EnemyPart(HVRGrabbable g)
    {
        try { return Alive(g) && Alive(g.GetComponentInParent<ANBBasicNPC>()); } catch { return false; }
    }

    // True if an enemy part may be hovered/grabbed by this hand: its collider surface must be within
    // EnemyGrabRadius of the palm. A fist swung at one enemy brushes the face or body of another
    // (or of a downed one), and the widened pickup sphere turned every such brush into a grab.
    internal static bool EnemyReachable(HVRHandGrabber h, HVRGrabbable g)
    {
        float r = Limit(EnemyRadius.Value, .10f, 0, .25f);
        if (r <= 0) return true;
        float d = SurfaceDistance(g, Palm(h));
        if (float.IsInfinity(d)) // no usable collider to measure: fall back to the body part's own position
            d = Vector3.Distance(Palm(h), Alive(g.Rigidbody) ? g.Rigidbody.worldCenterOfMass : g.transform.position);
        return d <= r;
    }

    static bool Eligible(HVRGrabbable g)
    {
        if (!Alive(g) || !g.isActiveAndEnabled || !g.CanBeGrabbed || g.BeingDestroyed || g.IsBeingHeld || g.IsSocketed ||
            g.IsBeingForcedGrabbed || g.Stationary || g.RequiresGrabbable || !Alive(g.Rigidbody) || Docked(g) || EnemyPart(g)) return false;
        // Do not turn trigger-only controls, bow strings, body grabs, or support grips
        // into generic loose-prop grabs. Enum values are verified against the interop.
        string control = g.GrabControl.ToString();
        return control == "GripOnly" || control == "GripOrTrigger";
    }

    static bool Try(HVRHandGrabber h, HVRGrabbable g, string reason, HandState s)
    {
        s.RetryAt = Now + .06;
        bool ok = h.TryGrab(g, false);
        if (ok) s.Intent.Consume();
        if (DebugLog.Value) Log.Msg($"{reason} {(h.IsLeftHand ? "L" : "R")} '{g.name}' result={ok} age={(Now - s.Intent.PressAt) * 1000:0}ms timeScale={Time.timeScale:0.00}");
        return ok;
    }

    internal static void OnRelease(HVRHandGrabber h, HVRGrabbable g)
    {
        if (!Active || !Alive(h) || !Alive(g)) return;
        Register(h).Intent.Consume(); // a thrown/lost item must not reuse the throw's old press
        if (Released.Count >= 128) Released.Remove(Released.OrderBy(x => x.Value.At).First().Key);
        Released[g.GetInstanceID()] = new ReleasedItem { Item = g, At = Now };
    }

    internal static float SurfaceDistance(HVRGrabbable g, Vector3 p)
    {
        float d = float.PositiveInfinity;
        if (g.Colliders == null) return d;
        // g.Colliders holds only the item's solid shapes (HVR keeps real triggers in g.Triggers). A socketed or
        // holstered item has them switched to triggers (HVRGrabbable.SetAllToTrigger), so isTrigger is not skipped:
        // the native grab bag doesn't skip them either, and docked items must stay drawable.
        foreach (var c in g.Colliders)
        {
            if (!Alive(c) || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            var mesh = c.TryCast<MeshCollider>();
            if (!(Alive(c.TryCast<SphereCollider>()) || Alive(c.TryCast<BoxCollider>()) || Alive(c.TryCast<CapsuleCollider>()) || (Alive(mesh) && mesh.convex))) continue;
            d = Math.Min(d, Vector3.Distance(p, c.ClosestPoint(p)));
        }
        return d;
    }
}
