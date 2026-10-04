using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.Player;
using Il2CppHurricaneVR.Framework.Core.ScriptableObjects;
using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // Swing feel diagnostics ("the club lags my hand, then keeps going when I stop").
    //
    // The chain from your hand to the club has two soft links, and a log line has to tell them apart:
    //   A  controller -> physics hand   (HVRJointHand's ConfigurableJoint, driven by the hand's PDStrength)
    //   B  physics hand -> club         (the grab joint, driven by the grabbable's JointOverride / hand settings)
    // Each frame while a club is held the mod measures where the club tip, and the hand, would be if they were
    // bolted to the controller (the offsets are taken while the controller is still), and compares with where they
    // are. One line per swing says how long the tip lags (ms), how far (cm), which link it comes from, and what
    // happens after the hand stops (overshoot, swing back, twist). A one-off block per grip dumps the real physics
    // numbers (masses, inertia, hand strength, joint drives) so a setting can be judged against the measurement.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<bool> SwingLog;
        internal static MelonPreferences_Entry<float> SwingLogMinSpeed;

        static void InitSwingLogPrefs(MelonPreferences_Category c)
        {
            SwingLog = c.CreateEntry("SwingLog", false, description: "Log how the club follows your hand (one line per swing: tip lag in ms and cm, which link causes it, overshoot after you stop) plus the hand and joint physics numbers once per grip. For tuning the club's weight and feel. Also logs the delay of every hand movement behind the real controller (HandProbe.cs).");
            SwingLogMinSpeed = c.CreateEntry("SwingLogMinSpeed", 4f, description: "SwingLog: only swings where your controller moved the club tip faster than this (m/s) are logged. A fast baton swing is 6-10.");
        }

        internal struct SwSample
        {
            public float T;                       // unscaled seconds
            public Vector3 IdealTip, Tip, IdealHand, Hand;
            public float IdealSpeed, TipSpeed, TipLag, HandLag, Slack, AxisErr;
            public Vector3 IdealV;
            // flip trace: hand body vs controller (deg), club vs hand body (deg), spin rates (rad/s), hand body gap (m)
            public float BodyRot, Slip, ClubW, HandW, CtrlW, Gap;
            public bool JointOk, Returning;
        }

        internal class SwingTrack
        {
            public HVRHandGrabber Hand;
            public bool Left, Dumped, HaveBase, Dead;
            public float Since, StillSince = -1f, LastBase = -99f;
            public Vector3 CtrlPrev, IdealTipPrev, TipPrev; public Quaternion CtrlRotPrev; public bool HavePrev;
            public Vector3 LTip, LAxis, LHand, HTip;   // offsets measured while still: tip / axis / hand in controller space, tip in hand space
            public Vector3 SmoothIdealV, SmoothTipV;
            public readonly List<SwSample> H = new();
            public int Phase;                          // 0 idle, 1 swinging, 2 settling after the stop
            public float StartT, StopT, SlowSince = -1f;
            public int Swings; public readonly List<float> Delays = new(); public float WorstLagCm;
            public string LastDump = "";
            public Quaternion HQ, CQ;                  // baseline: hand body rotation in controller space; club rotation in hand space
            public HVRJointHand JointHand;
            public bool Flip, FlipLocked; public float FlipStart, FlipBackSince = -1f;
            public readonly List<SwSample> FlipFrames = new();
            public readonly List<(float t, string what)> Events = new();
        }

        const float SwingStartSpeed = 2.5f, SwingEndSpeed = 1f, SwingPostWindow = 0.35f, SwingPreRoll = 0.1f;

        // Called from BeforeGrab for every grab. Tracking itself starts in SwingEnsure, once exactly one hand holds the club.
        static void SwingGrabbed(Club k, HVRGrabberBase grabber)
        {
            if (Alive(grabber) && grabber.TryCast<HVRHandGrabber>() != null) ApplyInertia(k);
        }

        // One tracked hand per club: a two-handed hold (the second hand takes the club anywhere) follows neither controller.
        static void SwingEnsure(Club k)
        {
            var g = k.Grab;
            bool l = g.IsLeftHandGrabbed, r = g.IsRightHandGrabbed;
            if (l == r) { if (k.Sw != null) SwingEndHold(k); return; }
            var hand = l ? g.LeftHandGrabber : g.RightHandGrabber;
            if (!Alive(hand)) return;
            if (k.Sw != null && Alive(k.Sw.Hand) && k.Sw.Hand.Pointer == hand.Pointer) return;
            if (k.Sw != null) SwingEndHold(k);
            k.Sw = new SwingTrack { Hand = hand, Left = hand.IsLeftHand, Since = Time.unscaledTime, JointHand = hand.GetComponent<HVRJointHand>() };
        }

        static void SwingEndHold(Club k)
        {
            var s = k.Sw;
            k.Sw = null;
            if (s == null) return;
            if (s.Flip) FlipReport(s);
            if (s.Phase != 0) SwingFinish(k, s, false);
            if (s.Swings == 0) return;
            s.Delays.Sort();
            Log.Msg($"grip ended ({(s.Left ? "L" : "R")}): {s.Swings} swing(s), tip delay {s.Delays[0]:0}-{s.Delays[s.Delays.Count - 1]:0} ms (median {s.Delays[s.Delays.Count / 2]:0}), worst lag {s.WorstLagCm:0} cm");
        }

        // After every Update: sample while a club is in a hand.
        public override void OnLateUpdate()
        {
            HandProbeTick();
            foreach (var k in Clubs)
            {
                if (!Alive(k.Go)) continue;
                bool held = IsHeld(k.Grab);
                // The game caps a held club's spin at 30 rad/s; a fast flick of the wrist is faster than that.
                if (held && k.Fly == null && Alive(k.Rb) && Mathf.Abs(k.Rb.maxAngularVelocity - ClubMaxSpin.Value) > 0.5f) k.Rb.maxAngularVelocity = ClubMaxSpin.Value;
                if (!SwingLog.Value) continue;
                if (!held || k.Fly != null) { if (k.Sw != null) SwingEndHold(k); continue; }
                try { SwingEnsure(k); } catch { continue; }
                if (k.Sw == null || k.Sw.Dead) continue;
                try { SwingStep(k, k.Sw); }
                catch (Exception e) { k.Sw.Dead = true; Log.Error($"swing log stopped for this grip: {e.Message}"); }
            }
        }

        static void SwingStep(Club k, SwingTrack s)
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.1f) { s.HavePrev = false; return; }
            var hand = s.Hand;
            if (!Alive(hand)) { s.Dead = true; return; }
            var ctrl = hand.ControllerHandTarget;
            var ctrlT = Alive(ctrl) ? ctrl : hand.transform;
            var ct = k.Go.transform;
            var handT = hand.transform;
            var cp = ctrlT.position; var cr = ctrlT.rotation;
            var tipLocal = ClubCenter + ClubAxis * (Length.Value * 0.5f);
            var tip = ct.TransformPoint(tipLocal);
            var axisW = ct.TransformDirection(ClubAxis);
            var hp = handT.position; var hr = handT.rotation;

            // Controller stillness -> measure the offsets.
            float ctrlSpeed = s.HavePrev ? (cp - s.CtrlPrev).magnitude / dt : 0f;
            float ctrlTurn = s.HavePrev ? Quaternion.Angle(cr, s.CtrlRotPrev) * Mathf.Deg2Rad / dt : 0f;
            bool still = ctrlSpeed < 0.3f && ctrlTurn < 1.5f;
            if (!still) s.StillSince = -1f;
            else if (s.StillSince < 0f) s.StillSince = t;
            if (s.Phase == 0 && still && t - s.StillSince > 0.25f && t - s.LastBase > 0.5f)
            {
                var inv = Quaternion.Inverse(cr);
                s.LTip = inv * (tip - cp);
                s.LAxis = inv * axisW;
                s.LHand = inv * (hp - cp);
                s.HTip = Quaternion.Inverse(hr) * (tip - hp);
                s.HQ = inv * hr; s.CQ = Quaternion.Inverse(hr) * ct.rotation;
                s.HaveBase = true; s.LastBase = t;
            }
            if (!s.Dumped && t - s.Since > 0.3f) { s.Dumped = true; SwingDump(k, s); }

            var idealTip = cp + cr * s.LTip;
            var idealHand = cp + cr * s.LHand;
            var tipFromHand = hp + hr * s.HTip;
            var idealAxis = cr * s.LAxis;
            if (!s.HavePrev) { s.IdealTipPrev = idealTip; s.TipPrev = tip; s.SmoothIdealV = s.SmoothTipV = Vector3.zero; }
            var iv = (idealTip - s.IdealTipPrev) / dt;
            var tv = (tip - s.TipPrev) / dt;
            if (iv.magnitude > 40f) iv = Vector3.zero;   // a tracking recenter, not a swing
            if (tv.magnitude > 40f) tv = Vector3.zero;
            s.SmoothIdealV = Vector3.Lerp(s.SmoothIdealV, iv, 0.6f);
            s.SmoothTipV = Vector3.Lerp(s.SmoothTipV, tv, 0.6f);
            s.CtrlPrev = cp; s.CtrlRotPrev = cr; s.IdealTipPrev = idealTip; s.TipPrev = tip; s.HavePrev = true;
            if (!s.HaveBase) return;

            var sm = new SwSample
            {
                T = t, IdealTip = idealTip, Tip = tip, IdealHand = idealHand, Hand = hp,
                IdealSpeed = s.SmoothIdealV.magnitude, TipSpeed = s.SmoothTipV.magnitude, IdealV = s.SmoothIdealV,
                TipLag = (tip - idealTip).magnitude, HandLag = (hp - idealHand).magnitude, Slack = (tip - tipFromHand).magnitude,
                AxisErr = Vector3.Angle(axisW, idealAxis),
                BodyRot = Quaternion.Angle(hr, cr * s.HQ), Slip = Quaternion.Angle(hr * s.CQ, ct.rotation),
                ClubW = Alive(k.Rb) ? k.Rb.angularVelocity.magnitude : 0f,
                HandW = Alive(hand.Rigidbody) ? hand.Rigidbody.angularVelocity.magnitude : 0f,
                CtrlW = ctrlTurn, Gap = (hp - idealHand).magnitude, JointOk = Alive(hand.Joint),
                Returning = s.JointHand != null && s.JointHand.IsReturningToController,
            };
            s.H.Add(sm);
            FlipStep(s, sm);
            if (s.H.Count > 700) s.H.RemoveRange(0, 100);

            switch (s.Phase)
            {
                case 0:
                    if (sm.IdealSpeed > SwingStartSpeed) { s.Phase = 1; s.StartT = t; s.SlowSince = -1f; }
                    break;
                case 1:
                    if (sm.IdealSpeed < SwingEndSpeed)
                    {
                        if (s.SlowSince < 0f) s.SlowSince = t;
                        if (t - s.SlowSince > 0.05f) { s.Phase = 2; s.StopT = s.SlowSince; }
                    }
                    else s.SlowSince = -1f;
                    break;
                case 2:
                    if (sm.IdealSpeed > SwingStartSpeed) { SwingFinish(k, s, true); s.Phase = 1; s.StartT = t; s.SlowSince = -1f; }
                    else if (t - s.StopT > SwingPostWindow) { SwingFinish(k, s, false); s.Phase = 0; }
                    break;
            }
        }

        // Where the ideal series was `lag` seconds before sample time `t` (linear between samples).
        static Vector3 IdealAt(List<SwSample> h, float t, bool hand)
        {
            if (h.Count == 0) return Vector3.zero;
            if (t <= h[0].T) return hand ? h[0].IdealHand : h[0].IdealTip;
            for (int i = h.Count - 1; i > 0; i--)
            {
                if (h[i - 1].T <= t)
                {
                    float f = Mathf.InverseLerp(h[i - 1].T, h[i].T, t);
                    return hand ? Vector3.Lerp(h[i - 1].IdealHand, h[i].IdealHand, f) : Vector3.Lerp(h[i - 1].IdealTip, h[i].IdealTip, f);
                }
            }
            return hand ? h[0].IdealHand : h[0].IdealTip;
        }

        // Delay in ms that makes the real series best match the ideal one over the swing.
        static float BestDelay(List<SwSample> h, float t0, float t1, bool hand)
        {
            float best = float.MaxValue, bestMs = 0f;
            for (int ms = 0; ms <= 200; ms += 4)
            {
                float e = 0f; int n = 0;
                foreach (var x in h)
                {
                    if (x.T < t0 || x.T > t1) continue;
                    var real = hand ? x.Hand : x.Tip;
                    e += (real - IdealAt(h, x.T - ms * 0.001f, hand)).sqrMagnitude; n++;
                }
                if (n < 3) return -1f;
                if (e < best) { best = e; bestMs = ms; }
            }
            return bestMs;
        }

        static void SwingFinish(Club k, SwingTrack s, bool cut)
        {
            var h = s.H;
            float t0 = s.StartT - SwingPreRoll, t1 = s.Phase == 2 ? s.StopT : h.Count > 0 ? h[h.Count - 1].T : s.StartT;
            float peakIdeal = 0f, peakTip = 0f, peakAt = s.StartT, maxTip = 0f, maxHand = 0f, maxSlack = 0f, maxAxis = 0f, maxTipAt = 0f;
            Vector3 dirSum = Vector3.zero;
            foreach (var x in h)
            {
                if (x.T < s.StartT || x.T > t1) continue;
                if (x.IdealSpeed > peakIdeal) { peakIdeal = x.IdealSpeed; peakAt = x.T; }
                peakTip = Mathf.Max(peakTip, x.TipSpeed);
                if (x.TipLag > maxTip) { maxTip = x.TipLag; maxTipAt = x.T; }
                maxHand = Mathf.Max(maxHand, x.HandLag); maxSlack = Mathf.Max(maxSlack, x.Slack); maxAxis = Mathf.Max(maxAxis, x.AxisErr);
                dirSum += x.IdealV;
            }
            if (peakIdeal < SwingLogMinSpeed.Value || dirSum.sqrMagnitude < 1e-4f) return;
            float dTip = BestDelay(h, t0, t1, false), dHand = BestDelay(h, t0, t1, true);
            if (dTip < 0f) return;
            var dir = dirSum.normalized;

            // After the stop: does the club keep going (ahead > 0), and does it swing back?
            float aheadStop = 0f, aheadMax = 0f, aheadMaxAt = 0f, axisStop = 0f, axisPost = 0f, lastSign = 0f; int cross = 0;
            if (!cut && s.Phase == 2)
            {
                bool first = true;
                foreach (var x in h)
                {
                    if (x.T < s.StopT) continue;
                    float ahead = Vector3.Dot(x.Tip - x.IdealTip, dir);
                    if (first) { aheadStop = ahead; axisStop = x.AxisErr; first = false; }
                    if (ahead > aheadMax) { aheadMax = ahead; aheadMaxAt = x.T - s.StopT; }
                    axisPost = Mathf.Max(axisPost, x.AxisErr);
                    float sg = ahead > 0.015f ? 1f : ahead < -0.015f ? -1f : 0f;
                    if (sg != 0f) { if (lastSign != 0f && sg != lastSign) cross++; lastSign = sg; }
                }
            }

            s.Swings++; s.Delays.Add(dTip); s.WorstLagCm = Mathf.Max(s.WorstLagCm, maxTip * 100f);
            var sb = new StringBuilder();
            sb.Append($"swing {(s.Left ? "L" : "R")}: hand drives tip to {peakIdeal:0.0} m/s, club reaches {peakTip:0.0} ({peakTip / Mathf.Max(0.1f, peakIdeal) * 100f:0}%); ");
            sb.Append($"tip delay {dTip:0} ms (hand {Mathf.Max(0f, dHand):0} + grip {Mathf.Max(0f, dTip - dHand):0}), ");
            sb.Append($"lag max {maxTip * 100f:0} cm @{(maxTipAt - peakAt) * 1000f:+0;-0} ms from peak (hand {maxHand * 100f:0}, grip {maxSlack * 100f:0}), twist max {maxAxis:0} deg");
            if (!cut && s.Phase == 2)
                sb.Append($"; at stop {(aheadStop < 0 ? -aheadStop * 100f : aheadStop * 100f):0} cm {(aheadStop < 0 ? "behind" : "ahead")}, then {(aheadMax > 0.015f ? $"overshoots {aheadMax * 100f:0} cm after {aheadMaxAt * 1000f:0} ms" : "no overshoot")}, swings back x{cross}, twist {axisStop:0} -> {axisPost:0} deg");
            else sb.Append("; (next swing began before the stop)");
            if (Time.timeScale < 0.95f) sb.Append($"; slow motion x{Time.timeScale:0.00}");
            Log.Msg(sb.ToString());
        }

        // ---------- the physics numbers, once per grip (again only when they differ from the last dump) ----------

        static string Drive(JointDrive d) => $"{d.positionSpring:0}/{d.positionDamper:0}/{d.maximumForce:0.#}";
        static string Pd(PDStrength p) => p == null ? "none" : $"'{p.name}' mode {p.Mode} spring {p.Spring:0.#} damper {p.Damper:0.#} maxF {p.MaxForce:0.#}, torque {p.TorqueSpring:0.#}/{p.TorqueDamper:0.#}/{p.MaxTorque:0.#}";
        static string Hd(HVRJointDrive d) => d == null ? "-" : $"{d.Spring:0}/{d.Damper:0}/{d.MaxForce:0.#}";
        static string Ha(HVRAngularJointDrive d) => d == null ? "-" : $"{d.Spring:0}/{d.Damper:0}/{d.MaxForce:0.#}";
        static string Js(HVRJointSettings j) => j == null ? "none" : $"'{j.name}' mode {j.ApplyMode} x {Hd(j.XDrive)} y {Hd(j.YDrive)} z {Hd(j.ZDrive)} slerp {Ha(j.SlerpDrive)} angX {Ha(j.AngularXDrive)} angYZ {Ha(j.AngularYZDrive)} massScale {j.MassScale:0.##}/{j.ConnectedMassScale:0.##} critDamp {j.CriticalDampPosition} dampConnected {j.DampConnectedBody}";

        static string JointInfo(ConfigurableJoint j) => j == null ? "none"
            : $"x {Drive(j.xDrive)} y {Drive(j.yDrive)} z {Drive(j.zDrive)} slerp {Drive(j.slerpDrive)} angX {Drive(j.angularXDrive)} angYZ {Drive(j.angularYZDrive)} mode {j.rotationDriveMode} massScale {j.massScale:0.##}/{j.connectedMassScale:0.##} projection {j.projectionMode}";

        static void SwingDump(Club k, SwingTrack s)
        {
            var sb = new StringBuilder();
            var rb = k.Rb; var hand = s.Hand; var g = k.Grab;
            string side = s.Left ? "L" : "R";
            // Format of every drive: spring/damper/maxForce.
            try
            {
                sb.Append($"hold {side}: fixed step {Time.fixedDeltaTime * 1000f:0.#} ms, solver {Physics.defaultSolverIterations}/{Physics.defaultSolverVelocityIterations} (drives are spring/damper/maxForce)\n");
                if (Alive(rb))
                    sb.Append($"  club: {rb.mass:0.##} kg, inertia {V(rb.inertiaTensor)}, centre of mass {V(rb.centerOfMass)} (club space), drag {rb.linearDamping:0.##}/{rb.angularDamping:0.##}, max spin {rb.maxAngularVelocity:0.#} rad/s, {rb.interpolation}, {rb.collisionDetectionMode}, solver {rb.solverIterations}/{rb.solverVelocityIterations}\n");
                var hrb = hand.Rigidbody;
                if (Alive(hrb))
                    sb.Append($"  hand body: {hrb.mass:0.##} kg, inertia {V(hrb.inertiaTensor)}, drag {hrb.linearDamping:0.##}/{hrb.angularDamping:0.##}, max spin {hrb.maxAngularVelocity:0.#}, {hrb.interpolation}, solver {hrb.solverIterations}/{hrb.solverVelocityIterations}\n");
                var sh = hand.StrengthHandler;
                if (sh != null)
                {
                    sb.Append($"  hand strength now: {Pd(sh.CurrentStrength)}\n");
                    sb.Append($"  hand settings now: {Js(sh.CurrentSettings)}\n");
                    sb.Append($"  hand default: {Pd(sh.DefaultStrength)}; override {Pd(sh.StrengthOverride)}; grab override {Pd(sh.HandGrabOverride)}\n");
                    sb.Append($"  hand joint (chases the controller): {JointInfo(sh.Joint)}\n");
                }
                if (g != null)
                {
                    sb.Append($"  grabbable: one-hand strength {Pd(g.OneHandStrength)}\n");
                    sb.Append($"  grabbable: joint override {Js(g.JointOverride)}; one-hand joint {Js(g.OneHandJointSettings)}\n");
                }
                sb.Append($"  grab joint (hand to club): {JointInfo(hand.Joint)}");
            }
            catch (Exception e) { sb.Append($"  (dump stopped: {e.Message})"); }
            var text = sb.ToString();
            // A different grip with identical numbers only gets a one-liner.
            if (text == s.LastDump || text == LastSwingDump) { Log.Msg($"hold {side}: same physics as the last grip"); return; }
            s.LastDump = LastSwingDump = text;
            Log.Msg(text);
        }

        static string LastSwingDump = "";

        // ---------- flip trace: the club turns far away from your hand's line (a "full circle" around the hand) ----------

        static void FlipStep(SwingTrack s, SwSample sm)
        {
            if (!s.Flip)
            {
                if (sm.AxisErr < 30f) s.FlipLocked = false;
                if (sm.AxisErr <= 100f || s.FlipLocked) return;
                s.Flip = true; s.FlipStart = sm.T; s.FlipBackSince = -1f;
                s.FlipFrames.Clear();
                foreach (var x in s.H) if (x.T >= sm.T - 0.25f) s.FlipFrames.Add(x);
                return;
            }
            s.FlipFrames.Add(sm);
            if (sm.AxisErr < 30f) { if (s.FlipBackSince < 0f) s.FlipBackSince = sm.T; } else s.FlipBackSince = -1f;
            if ((s.FlipBackSince >= 0f && sm.T - s.FlipBackSince > 0.1f) || sm.T - s.FlipStart > 2.5f) { s.FlipLocked = s.FlipBackSince < 0f; FlipReport(s); }
        }

        // Called by the hit and collision hooks: the events a flip log lists around the turn.
        static void SwingNote(Club k, string what)
        {
            var s = k?.Sw;
            if (s == null) return;
            s.Events.Add((Time.unscaledTime, what));
            if (s.Events.Count > 40) s.Events.RemoveAt(0);
        }

        static void FlipReport(SwingTrack s)
        {
            s.Flip = false;
            var f = s.FlipFrames;
            if (f.Count < 3) return;
            float t0 = s.FlipStart, maxTwist = 0f, maxBody = 0f, maxSlip = 0f, wClub = 0f, wHand = 0f, wCtrl = 0f, gap = 0f, endT = f[f.Count - 1].T;
            bool jointLost = false, returning = false;
            foreach (var x in f)
            {
                maxTwist = Mathf.Max(maxTwist, x.AxisErr); maxBody = Mathf.Max(maxBody, x.BodyRot); maxSlip = Mathf.Max(maxSlip, x.Slip);
                wClub = Mathf.Max(wClub, x.ClubW); wHand = Mathf.Max(wHand, x.HandW); wCtrl = Mathf.Max(wCtrl, x.CtrlW); gap = Mathf.Max(gap, x.Gap);
                jointLost |= !x.JointOk; returning |= x.Returning;
            }
            string who = maxBody >= 0.7f * maxTwist ? "the HAND body turned (hand rotation drive)" : maxSlip >= 0.7f * maxTwist ? "the club slipped in the GRIP (grab joint)" : "mixed";
            var sb = new StringBuilder();
            sb.Append($"FLIP {(s.Left ? "L" : "R")}: club {maxTwist:0} deg off the hand's line for {(endT - t0) * 1000f:0} ms; hand body {maxBody:0} deg off the controller, club slipped {maxSlip:0} deg in the grip -> {who}; ");
            sb.Append($"peak spin club {wClub:0} / hand {wHand:0} / controller {wCtrl:0} rad/s, hand gap {gap * 100f:0} cm{(jointLost ? ", GRAB JOINT MISSING at times" : "")}{(returning ? ", hand returning to controller" : "")}");
            sb.Append("\n  trace: ms: club-twist / hand-turn / grip-slip deg | club / hand / controller spin rad/s | gap cm");
            int stride = Mathf.Max(1, f.Count / 22);
            for (int i = 0; i < f.Count; i += stride)
            {
                var x = f[i];
                sb.Append($"\n  {(x.T - t0) * 1000f:+0;-0} ms: {x.AxisErr:0} / {x.BodyRot:0} / {x.Slip:0} | {x.ClubW:0} / {x.HandW:0} / {x.CtrlW:0} | {x.Gap * 100f:0}");
            }
            foreach (var e in s.Events)
                if (e.t >= t0 - 0.5f && e.t <= endT) sb.Append($"\n  {(e.t - t0) * 1000f:+0;-0} ms: {e.what}");
            Log.Msg(sb.ToString());
        }

        internal static void ClubCollided(ANBSoundPhysicsItem item, Collision col)
        {
            var k = ClubOf(item);
            if (k == null || k.Sw == null) return;
            float v = col.relativeVelocity.magnitude;
            if (v < 2f) return;
            var other = col.collider;
            SwingNote(k, $"collision with '{(Alive(other) ? other.name : "?")}' at {v:0.0} m/s");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBSoundPhysicsItem), nameof(ANBSoundPhysicsItem.OnCollisionEnter))]
    static class ClubCollisionPatch
    {
        static void Postfix(ANBSoundPhysicsItem __instance, Collision collision)
        {
            try { BillyClubsMod.ClubCollided(__instance, collision); } catch { }
        }
    }
}
