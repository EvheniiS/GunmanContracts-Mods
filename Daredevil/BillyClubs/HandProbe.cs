using System;
using System.Collections.Generic;
using System.Text;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.ScriptableObjects;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    // Hand feel probe: how late is the glove behind the real controller, hands empty or full?
    //
    // Per hand, four poses are followed every frame:
    //   raw     the tracked controller (HVRHandGrabber.TrackedController): where your hand really is
    //   target  the pose the physics hand chases (PhysicsHandTarget), when it is a separate object
    //   body    the physics hand (the 20 kg rigidbody on the grabber's object)
    //   glove   the visible hand model (HVRHandGrabber.HandModel)
    // While the controller is still, each pose's offset from the raw pose is measured; then, for every hand movement
    // faster than 2.5 m/s (relative to the head, so walking doesn't count), the delay of each pose behind the raw one
    // is found by sliding the raw path in time until it best matches, and the biggest gap in cm is reported.
    // Position and rotation delays are separate. One line per movement, from SwingLog = true.
    //
    // HandStrengthScale is the first experiment on what the log shows: it scales the hand's drive toward the controller.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<float> HandStrengthScale, HandTorqueScale, ClubInertiaScale, ClubMaxSpin;

        static void InitHandProbePrefs(MelonPreferences_Category c)
        {
            HandStrengthScale = c.CreateEntry("HandStrengthScale", 1f, description: "Advanced: scales how hard the physics hand is pulled toward your controller's POSITION (both hands, whatever they hold). 2 = the hand follows with about half the lag; the damper is scaled to stay critically damped. 1 = the game's value. Too high and the hands jitter against walls. Live; range 0.5 to 4.");
            HandTorqueScale = c.CreateEntry("HandTorqueScale", 1.6f, description: "Scales how hard the physics hand is turned toward your controller's ROTATION. 1 = the game's value (500 spring, 50 damper, 75 N*m max), which is too weak for a swung club: a fast sideways swing keeps turning the hand after you stop. 1.6 (default) is the tested value. Live; range 0.5 to 6.");
            ClubInertiaScale = c.CreateEntry("ClubInertiaScale", 1f, description: "Advanced: scales the club's resistance to being turned (its rotational inertia) without changing its mass. 0.4 = it turns much more easily, still feels 3 kg when lifted or hit. 1 = the game's value. Live; range 0.1 to 1.5.");
            ClubMaxSpin = c.CreateEntry("ClubMaxSpin", 80f, description: "Fastest a held club can turn, in radians per second. The game's value is 30 (about 5 turns a second); a fast wrist flick is faster, which makes the club lag the hand. Live; range 10 to 150.");
            HandStrengthScale.OnEntryValueChanged.Subscribe((_, v) => ApplyHandStrength(true));
            HandTorqueScale.OnEntryValueChanged.Subscribe((_, v) => ApplyHandStrength(true));
            ClubInertiaScale.OnEntryValueChanged.Subscribe((_, v) => { foreach (var k in Clubs) ApplyInertia(k); });
        }

        // Rotational inertia: let Unity recompute it from the colliders and the mass, then scale it.
        static void ApplyInertia(Club k)
        {
            float f = Mathf.Clamp(ClubInertiaScale.Value, 0.1f, 1.5f);
            if (!Alive(k.Rb) || (f == 1f && !k.InertiaSet)) return;
            try
            {
                k.Rb.ResetInertiaTensor();
                if (f != 1f) k.Rb.inertiaTensor = k.Rb.inertiaTensor * f;
                k.InertiaSet = f != 1f;
                if (Dbg || SwingLog.Value) Log.Msg($"club '{k.Go.name}' inertia x{f:0.##}: {V(k.Rb.inertiaTensor)} kg m2");
            }
            catch (Exception e) { Log.Warning($"inertia scale failed: {e.Message}"); }
        }

        class HandRec
        {
            public HVRHandGrabber Hand;
            public bool Left;
            public Transform Raw, Target, Body, Glove;
            public readonly Vector3[] LPos = new Vector3[3];      // target, body, glove position in raw-controller space
            public readonly Quaternion[] LRot = new Quaternion[3]; // same for rotation
            public bool HaveBase, HavePrev, Moving;
            public float StillSince = -1f, LastBase = -99f, StartT, SlowSince = -1f;
            public Vector3 RawPrev, HeadPrev, SmoothV; public Quaternion RawRotPrev; public float SmoothTurn;
            public readonly List<HSample> H = new();
        }

        struct HSample
        {
            public float T, Speed, Turn;
            public Vector3 RawPos, P0, P1, P2;     // raw, target, body, glove
            public Quaternion RawRot, Q1, Q2;      // raw, body, glove
        }

        static readonly List<HandRec> ProbeHands = new();
        static float HandScanAt = -1f;
        static Camera HeadCam;

        static void HandProbeScene() { ProbeHands.Clear(); HandScanAt = Time.unscaledTime + 3f; }

        static void HandScan()
        {
            ProbeHands.Clear();
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None))
            {
                var h = o.TryCast<HVRHandGrabber>();
                if (!Alive(h)) continue;
                var raw = h.TrackedController;
                if (!Alive(raw)) continue;
                var tgt = h.PhysicsHandTarget;
                var r = new HandRec { Hand = h, Left = h.IsLeftHand, Raw = raw, Body = h.transform, Glove = Alive(h.HandModel) ? h.HandModel : null };
                r.Target = Alive(tgt) && tgt.Pointer != raw.Pointer ? tgt : null;
                ProbeHands.Add(r);
                var rb = h.Rigidbody;
                if (SwingLog.Value) Log.Msg($"hand {(r.Left ? "L" : "R")} chain: raw '{Path(raw)}'; target {(r.Target != null ? $"'{Path(r.Target)}'" : "= raw")}; body '{Path(r.Body)}' ({(Alive(rb) ? rb.interpolation.ToString() : "no rigidbody")}); glove {(r.Glove != null ? $"'{Path(r.Glove)}'" : "none")}");
            }
            if (ProbeHands.Count > 0) ApplyHandStrength(false);
        }

        static void HandProbeTick()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (HandScanAt >= 0f && t >= HandScanAt)
            {
                HandScanAt = -1f;
                try { HandScan(); } catch (Exception e) { Log.Error($"hand scan failed: {e.Message}"); }
            }
            if (!SwingLog.Value || ProbeHands.Count == 0 || dt <= 0f || dt > 0.1f) return;
            if (!Alive(HeadCam)) HeadCam = Camera.main;
            var head = Alive(HeadCam) ? HeadCam.transform.position : Vector3.zero;
            foreach (var r in ProbeHands)
            {
                if (!Alive(r.Hand) || !Alive(r.Raw)) { HandScanAt = t + 2f; return; }
                try { HandStep(r, t, dt, head); }
                catch (Exception e) { Log.Error($"hand probe stopped: {e.Message}"); ProbeHands.Clear(); return; }
            }
        }

        static void HandStep(HandRec r, float t, float dt, Vector3 head)
        {
            var rp = r.Raw.position; var rr = r.Raw.rotation;
            var body = r.Body; var bp = body.position; var bq = body.rotation;
            var tp = r.Target != null ? r.Target.position : rp;
            var gp = r.Glove != null ? r.Glove.position : bp; var gq = r.Glove != null ? r.Glove.rotation : bq;

            Vector3 vRaw = r.HavePrev ? (rp - r.RawPrev) / dt : Vector3.zero, vHead = r.HavePrev ? (head - r.HeadPrev) / dt : Vector3.zero;
            float turn = r.HavePrev ? Quaternion.Angle(rr, r.RawRotPrev) * Mathf.Deg2Rad / dt : 0f;
            var vRel = vRaw - vHead;
            if (vRel.magnitude > 30f) vRel = Vector3.zero;   // a tracking recenter
            r.SmoothV = Vector3.Lerp(r.SmoothV, vRel, 0.6f); r.SmoothTurn = Mathf.Lerp(r.SmoothTurn, Mathf.Min(turn, 60f), 0.6f);
            r.RawPrev = rp; r.RawRotPrev = rr; r.HeadPrev = head; r.HavePrev = true;

            bool still = vRaw.magnitude < 0.3f && turn < 1.5f;
            if (!still) r.StillSince = -1f; else if (r.StillSince < 0f) r.StillSince = t;
            if (!r.Moving && still && t - r.StillSince > 0.25f && t - r.LastBase > 0.5f)
            {
                var inv = Quaternion.Inverse(rr);
                r.LPos[0] = inv * (tp - rp); r.LPos[1] = inv * (bp - rp); r.LPos[2] = inv * (gp - rp);
                r.LRot[1] = inv * bq; r.LRot[2] = inv * gq; r.LRot[0] = Quaternion.identity;
                r.HaveBase = true; r.LastBase = t;
            }
            if (!r.HaveBase) return;

            r.H.Add(new HSample { T = t, Speed = r.SmoothV.magnitude, Turn = r.SmoothTurn, RawPos = rp, RawRot = rr, P0 = tp, P1 = bp, P2 = gp, Q1 = bq, Q2 = gq });
            if (r.H.Count > 400) r.H.RemoveRange(0, 100);

            float speed = r.SmoothV.magnitude;
            if (!r.Moving) { if (speed > 2f) { r.Moving = true; r.StartT = t; r.SlowSince = -1f; } return; }
            if (speed < 0.7f)
            {
                if (r.SlowSince < 0f) r.SlowSince = t;
                if (t - r.SlowSince <= 0.05f) return;
            }
            else { r.SlowSince = -1f; return; }
            r.Moving = false;
            HandFinish(r, r.SlowSince);
        }

        static void RawAt(List<HSample> h, float t, out Vector3 p, out Quaternion q)
        {
            if (t <= h[0].T) { p = h[0].RawPos; q = h[0].RawRot; return; }
            for (int i = h.Count - 1; i > 0; i--)
                if (h[i - 1].T <= t)
                {
                    float f = Mathf.InverseLerp(h[i - 1].T, h[i].T, t);
                    p = Vector3.Lerp(h[i - 1].RawPos, h[i].RawPos, f); q = Quaternion.Slerp(h[i - 1].RawRot, h[i].RawRot, f);
                    return;
                }
            p = h[0].RawPos; q = h[0].RawRot;
        }

        // Delay in ms of pose `stage` (0 target, 1 body, 2 glove) behind the raw controller; rot = compare rotations.
        static float StageDelay(HandRec r, float t0, float t1, int stage, bool rot)
        {
            var h = r.H;
            float best = float.MaxValue, bestMs = 0f;
            for (int ms = 0; ms <= 160; ms += 4)
            {
                float e = 0f; int n = 0;
                foreach (var x in h)
                {
                    if (x.T < t0 || x.T > t1) continue;
                    RawAt(h, x.T - ms * 0.001f, out var p, out var q);
                    if (rot) { var real = stage == 1 ? x.Q1 : x.Q2; float a = Quaternion.Angle(real, q * r.LRot[stage]); e += a * a; }
                    else { var real = stage == 0 ? x.P0 : stage == 1 ? x.P1 : x.P2; e += (real - (p + q * r.LPos[stage])).sqrMagnitude; }
                    n++;
                }
                if (n < 3) return -1f;
                if (e < best) { best = e; bestMs = ms; }
            }
            return bestMs;
        }

        static void HandFinish(HandRec r, float stopT)
        {
            var h = r.H;
            float t0 = r.StartT - 0.1f;
            float peak = 0f, peakTurn = 0f, lag0 = 0f, lag1 = 0f, lag2 = 0f;
            foreach (var x in h)
            {
                if (x.T < r.StartT || x.T > stopT) continue;
                peak = Mathf.Max(peak, x.Speed); peakTurn = Mathf.Max(peakTurn, x.Turn);
                lag0 = Mathf.Max(lag0, (x.P0 - (x.RawPos + x.RawRot * r.LPos[0])).magnitude);
                lag1 = Mathf.Max(lag1, (x.P1 - (x.RawPos + x.RawRot * r.LPos[1])).magnitude);
                lag2 = Mathf.Max(lag2, (x.P2 - (x.RawPos + x.RawRot * r.LPos[2])).magnitude);
            }
            if (peak < 2.5f) return;
            float d0 = StageDelay(r, t0, stopT, 0, false), d1 = StageDelay(r, t0, stopT, 1, false), d2 = StageDelay(r, t0, stopT, 2, false);
            float q1 = StageDelay(r, t0, stopT, 1, true), q2 = StageDelay(r, t0, stopT, 2, true);
            if (d1 < 0f) return;
            var g = r.Hand.GrabbedTarget;
            var sb = new StringBuilder();
            sb.Append($"hand {(r.Left ? "L" : "R")} [{(Alive(g) ? g.name : "empty")}]: controller peak {peak:0.0} m/s, turn {peakTurn:0} rad/s; ");
            sb.Append($"delay {(r.Target != null ? $"target {d0:0} ms, " : "")}body {d1:0} ms, glove {d2:0} ms; lag max {(r.Target != null ? $"target {lag0 * 100f:0}, " : "")}body {lag1 * 100f:0}, glove {lag2 * 100f:0} cm; turn delay body {q1:0} ms, glove {q2:0} ms");
            if (Time.timeScale < 0.95f) sb.Append($"; slow motion x{Time.timeScale:0.00}");
            Log.Msg(sb.ToString());
        }

        // ---------- HandStrengthScale ----------

        static readonly Dictionary<IntPtr, float[]> OrigStrength = new();

        static void ApplyHandStrength(bool logIt)
        {
            float f = Mathf.Clamp(HandStrengthScale.Value, 0.5f, 4f), g = Mathf.Clamp(HandTorqueScale.Value, 0.5f, 6f);
            int n = 0; string last = "";
            var done = new HashSet<IntPtr>();
            foreach (var r in ProbeHands)
            {
                if (!Alive(r.Hand)) continue;
                var sh = r.Hand.StrengthHandler;
                var ds = sh?.DefaultStrength;
                if (ds == null || !done.Add(ds.Pointer)) continue;
                if (!OrigStrength.TryGetValue(ds.Pointer, out var o))
                    OrigStrength[ds.Pointer] = o = new[] { ds.Spring, ds.Damper, ds.MaxForce, ds.TorqueSpring, ds.TorqueDamper, ds.MaxTorque };
                float rt = Mathf.Sqrt(f);
                ds.Spring = o[0] * f; ds.Damper = o[1] * rt; ds.MaxForce = o[2] * f;
                float rg = Mathf.Sqrt(g);
                ds.TorqueSpring = o[3] * g; ds.TorqueDamper = o[4] * rg; ds.MaxTorque = o[5] * g;
                n++; last = $"spring {ds.Spring:0}, damper {ds.Damper:0}, maxF {ds.MaxForce:0}, torque {ds.TorqueSpring:0}/{ds.TorqueDamper:0}/{ds.MaxTorque:0}";
            }
            // The two hands share one strength asset; push it into every hand's joint.
            foreach (var r in ProbeHands)
            {
                var sh = Alive(r.Hand) ? r.Hand.StrengthHandler : null;
                if (sh?.CurrentStrength != null && sh.DefaultStrength != null && sh.CurrentStrength.Pointer == sh.DefaultStrength.Pointer) sh.UpdateStrength(sh.CurrentStrength);
            }
            if (logIt || f != 1f || g != 1f) Log.Msg($"hand strength position x{f:0.##}, torque x{g:0.##}: {n} asset(s) {(n > 0 ? "- " + last : "(no hands found yet, applied when they appear)")}");
        }
    }
}
