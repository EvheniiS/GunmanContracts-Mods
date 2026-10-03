using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.Player;
using Il2CppHurricaneVR.Framework.Core.ScriptableObjects;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(SlowMotionHands.SlowMotionHandsMod), "Slow Motion Hands", "0.1.3", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace SlowMotionHands
{
    // Your hands keep up with your controllers during slow motion.
    //
    // Why they lag (GameAssembly.dll + the game's logs, Oct 2 2026):
    // - toggleSlowMotion only sets Time.timeScale. The physics step is the game's adaptive one
    //   (ANBGameLogic.FixedTimeTest, clamped by its own min/max): at x0.22 the logs show 7.3 ms of game time,
    //   about 33 ms of real time, so physics (and the hand body, which has no interpolation) runs near
    //   30 Hz in real time instead of 90.
    // - The physics hand is a 20 kg body pulled to the controller by a ConfigurableJoint (spring 9000, damper 900,
    //   torque 500/50, set by HVRHandStrengthHandler.UpdateStrength). That is a time-domain system: at time scale s
    //   it catches up 1/s times slower in real time, because your hand moves in real time and the world doesn't
    //   (measured by Daredevil's HandProbe: 88-96 ms at x0.22 against 16-24 ms normally).
    //
    // The fix is the same two things: (1) FullRatePhysics keeps the physics step at the normal real-time rate
    // (frame time x timeScale), and (2) HandResponse scales the hand drives the way time scaling needs:
    // spring and force by (1/s)^2, damper by 1/s, so the hand follows in real time like at normal speed.
    // The hand-to-weapon joint (HVRHandGrabber.Joint) is scaled the same way (GripResponse).
    public class SlowMotionHandsMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Enabled, FullRatePhysics, RaiseSpinLimit, DebugLog;
        internal static MelonPreferences_Entry<float> HandResponse, GripResponse, MaxStrengthBoost, SlowMotionSnap;

        const float SlowBelow = 0.97f, PausedBelow = 0.02f;

        // Drive multiplier right now (spring and force; the damper gets its square root). 1 = off.
        internal static float Boost = 1f;
        internal static float DamperBoost = 1f;
        // Same for the joint that holds the weapon to the hand.
        static float gripBoost = 1f, gripDamper = 1f;

        static float appliedScale = 1f;        // timeScale the boost was computed for (1 = normal speed)
        static bool slowOn, forceRefresh;
        static float slowStart;
        static float frameEma;                 // smoothed real frame time, seconds

        // Physics step bookkeeping, for the log.
        static bool stepOverriding;
        static float gameStep, ourStep;

        class Meter
        {
            public int N; public double Sum; public float Max;
            public void Add(float v) { N++; Sum += v; if (v > Max) Max = v; }
            public void Clear() { N = 0; Sum = 0; Max = 0; }
            public string Show => N < 10 ? "n/a" : $"mean {Sum / N * 100:0.#} / max {Max * 100:0} cm ({N})";
        }

        class Hand
        {
            public HVRJointHand Jh;
            public string Name;
            public Transform Body, Target;
            public Vector3 Rest, PrevRel;
            public bool HaveRest, HavePrev;
            public float StillT;
            public HVRHandGrabber Grabber;
            public IntPtr ItemPtr;
            public Vector3 ItemRest;
            public bool HaveItemRest;
            public readonly Meter Slow = new(), Norm = new(), ISlow = new(), INorm = new();
        }

        static readonly List<Hand> Hands = new();
        static float scanAt = -1f;
        static Camera head;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("SlowMotionHands", "Slow Motion Hands");
            Enabled = c.CreateEntry("Enabled", true, description: "Master switch. Off = the game's own slow motion feel.");
            HandResponse = c.CreateEntry("HandResponse", 1f, description: "How much of the slow-motion hand lag to cancel (0 to 1). 1 = the hands follow your controllers as fast as at normal speed. 0 = the game's value. Lower it if the hands jitter or push things around too hard in slow motion. Live.");
            GripResponse = c.CreateEntry("GripResponse", 1f, description: "Same as HandResponse for the joint that holds a weapon or item to your hand (0 to 1). 1 = the held weapon follows the hand as fast as at normal speed. 0 = the game value. Live.");
            FullRatePhysics = c.CreateEntry("FullRatePhysics", true, description: "Keep the physics step at the normal real-time rate during slow motion (the game lets it drop to about 30 per real second, so the hands update in jumps). Costs the CPU time of normal play, not more.");
            RaiseSpinLimit = c.CreateEntry("RaiseSpinLimit", true, description: "Raise the hand's and the held item's spin speed limit by the slow-motion factor, so fast wrist flicks are not capped in slow motion (the limit is in game time: the hand's 150 rad/s becomes 33 rad/s real at x0.22). Live.");
            SlowMotionSnap = c.CreateEntry("SlowMotionSnap", 2f, description: "Hands SNAPPIER than at normal speed during slow motion (1 to 4). 1 = the same lag as normal play; 2 = about 30% less lag; 4 = about half. Only in slow motion, the hand joint only. Lower it if hands jitter or shove things. Live.");
            MaxStrengthBoost = c.CreateEntry("MaxStrengthBoost", 30f, description: "Advanced: the most the hand spring and force are multiplied by (damper: square root of it). x0.22 slow motion needs 20.7. Live.");
            DebugLog = c.CreateEntry("DebugLog", true, description: "One line when slow motion starts and one summary when it ends (physics step, hand gap while moving, with and without the fix). Turn off once it feels right.");
            foreach (var e in new MelonPreferences_Entry[] { Enabled, HandResponse, GripResponse, FullRatePhysics, RaiseSpinLimit, MaxStrengthBoost, SlowMotionSnap })
                e.OnEntryValueChangedUntyped.Subscribe((_, __) => forceRefresh = true);
            LoggerInstance.Msg("loaded - hands keep up with the controllers in slow motion.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            Hands.Clear();
            scanAt = Time.unscaledTime + 3f;
        }

        static bool Alive(Object o) => o != null;

        static bool IsSlow(float ts) => Enabled.Value && ts > PausedBelow && ts < SlowBelow;

        // ---------- per frame ----------

        public override void OnUpdate()
        {
            try
            {
                float udt = Time.unscaledDeltaTime;
                if (udt > 0f && udt < 0.25f) frameEma = frameEma <= 0f ? udt : Mathf.Lerp(frameEma, udt, 0.1f);

                float ts = Time.timeScale;
                bool slow = IsSlow(ts);
                float want = slow ? ts : 1f;
                if (forceRefresh || slow != slowOn || (slow && Mathf.Abs(want - appliedScale) > 0.03f * appliedScale))
                    Transition(slow, want);

                if (scanAt >= 0f && Time.unscaledTime >= scanAt) { scanAt = -1f; Scan(); }
                else if (Hands.Count == 0 && scanAt < 0f) scanAt = Time.unscaledTime + 5f;
            }
            catch (Exception e) { Log.Error($"update: {e}"); }
        }

        public override void OnLateUpdate()
        {
            if (!DebugLog.Value || Hands.Count == 0) return;
            try { MeterTick(Time.unscaledDeltaTime); }
            catch (Exception e) { Log.Error($"meter stopped: {e.Message}"); Hands.Clear(); }
        }

        static void Transition(bool slow, float want)
        {
            bool was = slowOn;
            float oldScale = appliedScale;
            forceRefresh = false;
            slowOn = slow;
            appliedScale = want;

            float r = Mathf.Clamp01(HandResponse.Value);
            float m = 1f;
            if (slow && r > 0f) m = Mathf.Min(Mathf.Pow(1f / want, 2f * r), Mathf.Max(1f, MaxStrengthBoost.Value));
            if (slow) m *= Mathf.Clamp(SlowMotionSnap.Value, 1f, 4f);   // on top of the time-scale cap
            Boost = m;
            DamperBoost = Mathf.Sqrt(m);
            float rg = Mathf.Clamp01(GripResponse.Value);
            gripBoost = slow && rg > 0f ? Mathf.Min(Mathf.Pow(1f / want, 2f * rg), Mathf.Max(1f, MaxStrengthBoost.Value)) : 1f;
            gripDamper = Mathf.Sqrt(gripBoost);
            spinBoost = slow && RaiseSpinLimit.Value ? 1f / want : 1f;
            RefreshHands();
            PollJoints();

            if (slow && !was)
            {
                slowStart = Time.unscaledTime;
                if (DebugLog.Value) { Log.Msg($"slow motion on x{want:0.00}: hand spring/force x{Boost:0.#}, damper x{DamperBoost:0.#}; grip x{gripBoost:0.#}; spin limit x{spinBoost:0.#}"); LogHolds(); }
            }
            else if (slow && DebugLog.Value) Log.Msg($"slow motion x{oldScale:0.00} -> x{want:0.00}: hand spring/force x{Boost:0.#}");
            else if (!slow && was)
            {
                stepOverriding = false;
                float dur = Time.unscaledTime - slowStart;
                if (DebugLog.Value && dur >= 0.5f) LogSummary(dur, oldScale);
                foreach (var h in Hands) { h.Slow.Clear(); h.Norm.Clear(); h.ISlow.Clear(); h.INorm.Clear(); }
            }
        }

        static void LogSummary(float dur, float ts)
        {
            var sb = new StringBuilder($"slow motion over after {dur:0.0} s (x{ts:0.00}); ");
            if (gameStep > 0f)
                sb.Append($"physics step game {gameStep * 1000f:0.#} ms ({gameStep / ts * 1000f:0} real)" +
                          (ourStep > 0f ? $" -> ours {ourStep * 1000f:0.#} ms ({ourStep / ts * 1000f:0} real); " : " (not changed); "));
            sb.Append("hand gap while moving >1 m/s: ");
            foreach (var h in Hands)
            {
                sb.Append($"{h.Name} slow {h.Slow.Show} vs normal {h.Norm.Show}; ");
                if (h.ISlow.N >= 10) sb.Append($"held item slow {h.ISlow.Show} vs normal {h.INorm.Show}; ");
            }
            Log.Msg(sb.ToString().TrimEnd(' ', ';'));
            gameStep = ourStep = 0f;
        }

        // ---------- physics step ----------

        // Runs right after the game has set its own step for this frame (ANBGameLogic.Update -> FixedTimeTest).
        internal static void AdjustFixedStep()
        {
            float ts = Time.timeScale;
            if (!IsSlow(ts) || !FullRatePhysics.Value) { stepOverriding = false; return; }
            float real = Mathf.Clamp(frameEma > 0f ? frameEma : 1f / 90f, 1f / 144f, 1f / 45f);
            float want = real * ts;
            float cur = Time.fixedDeltaTime;
            if (!stepOverriding) { stepOverriding = true; gameStep = cur; }
            ourStep = want;
            if (!Mathf.Approximately(cur, want)) Time.fixedDeltaTime = want;
        }

        // ---------- joint scaling (hand joint and the hand-to-weapon joint) ----------
        // Per joint and drive: the game's latest value (base) and what we wrote (base x boost). A drive that is not
        // what we wrote last is a new game value. 0.1.0 instead compared the drives before and after UpdateStrength
        // and scaled only "changed" ones; when slow motion started the game rewrote the same base value, so nothing
        // was scaled while you held a weapon (log: hand joint 9000/900/9000 during x0.22).

        static JointDrive GetDrive(ConfigurableJoint j, int i) => i switch
        {
            0 => j.xDrive, 1 => j.yDrive, 2 => j.zDrive, 3 => j.angularXDrive, 4 => j.angularYZDrive, _ => j.slerpDrive
        };

        static void SetDrive(ConfigurableJoint j, int i, JointDrive d)
        {
            switch (i)
            {
                case 0: j.xDrive = d; break;
                case 1: j.yDrive = d; break;
                case 2: j.zDrive = d; break;
                case 3: j.angularXDrive = d; break;
                case 4: j.angularYZDrive = d; break;
                default: j.slerpDrive = d; break;
            }
        }

        struct DriveState { public bool Has; public float BS, BD, BF, WS, WD, WF; }
        static Dictionary<IntPtr, DriveState[]> scaled = new(), scaledNext = new();

        // Writes base x f (spring, force) and base x df (damper). Keeps the joint's state in `into` while f != 1.
        static void ScaleJoint(ConfigurableJoint j, float f, float df, Dictionary<IntPtr, DriveState[]> into)
        {
            if (!Alive(j)) return;
            scaled.TryGetValue(j.Pointer, out var st);
            if (st == null) { if (f == 1f) return; st = new DriveState[6]; }
            for (int i = 0; i < 6; i++)
            {
                var d = GetDrive(j, i);
                ref var s = ref st[i];
                if (!(s.Has && d.positionSpring == s.WS && d.positionDamper == s.WD && d.maximumForce == s.WF))
                    s = new DriveState { Has = true, BS = d.positionSpring, BD = d.positionDamper, BF = d.maximumForce };   // new base
                float ws = s.BS * f, wd = s.BD * df, wf = s.BF < 1e20f ? s.BF * f : s.BF;   // "unlimited" stays unlimited
                if (ws != d.positionSpring || wd != d.positionDamper || wf != d.maximumForce)
                {
                    d.positionSpring = ws; d.positionDamper = wd; d.maximumForce = wf;
                    SetDrive(j, i, d);
                }
                s.WS = ws; s.WD = wd; s.WF = wf;
            }
            if (f != 1f) into[j.Pointer] = st;
            else scaled.Remove(j.Pointer);
        }

        static ConfigurableJoint HandJoint(Hand h)
        {
            if (!Alive(h.Jh)) return null;
            var sh = h.Jh.StrengthHandler;
            return Alive(sh) && Alive(sh.Joint) ? sh.Joint : h.Jh.Joint;
        }

        // Right after the game writes the hand drives (grab, release, override, slow motion start/end).
        internal static void AfterStrength(HVRHandStrengthHandler sh)
        {
            if (sh == null) return;
            var j = sh.Joint;
            if (!Alive(j) || (Boost == 1f && !scaled.ContainsKey(j.Pointer))) return;
            ScaleJoint(j, Boost, DamperBoost, scaled);
        }

        // Every physics step: both joints of every hand. Catches drives the game writes outside UpdateStrength.
        static void PollJoints()
        {
            if (Hands.Count == 0 || (Boost == 1f && gripBoost == 1f && spinBoost == 1f && scaled.Count == 0 && spun.Count == 0)) return;
            scaledNext.Clear(); spunNext.Clear();
            foreach (var h in Hands)
            {
                ScaleJoint(HandJoint(h), Boost, DamperBoost, scaledNext);
                ScaleSpin(Alive(h.Jh) ? h.Jh.RigidBody : null);
                var g = h.Grabber;
                if (Alive(g) && g.IsGrabbing)
                {
                    ScaleJoint(g.Joint, gripBoost, gripDamper, scaledNext);
                    if (g.GrabbedTarget != null) ScaleSpin(g.GrabbedTarget.Rigidbody);
                }
            }
            (scaled, scaledNext) = (scaledNext, scaled);
            (spun, spunNext) = (spunNext, spun);
        }

        // ---------- spin limit (Rigidbody.maxAngularVelocity) ----------
        // It is in game time: the hand's 150 rad/s (HVRJointHand.Awake) is 33 rad/s of real turning at x0.22, and your
        // logged flicks peak at 20-38 rad/s. Items can have their own, lower limit (HVRRigidBodyOverrides, which can
        // rewrite it every physics step). Same base/written tracking as the drives; scaled by 1/timeScale.
        static float spinBoost = 1f;
        static Dictionary<IntPtr, float[]> spun = new(), spunNext = new();

        static void ScaleSpin(Rigidbody rb)
        {
            if (!Alive(rb)) return;
            spun.TryGetValue(rb.Pointer, out var st);
            if (st == null) { if (spinBoost == 1f) return; st = new float[2] { -1f, -1f }; }   // [base, written]
            float cur = rb.maxAngularVelocity;
            if (cur != st[1]) st[0] = cur;   // a new game value
            float w = st[0] * spinBoost;
            if (w != cur) rb.maxAngularVelocity = w;
            st[1] = w;
            if (spinBoost != 1f) spunNext[rb.Pointer] = st;
        }

        public override void OnFixedUpdate()
        {
            try { PollJoints(); }
            catch (Exception e) { Log.Error($"joint scaling stopped: {e.Message}"); Boost = DamperBoost = gripBoost = gripDamper = spinBoost = 1f; scaled.Clear(); spun.Clear(); }
        }

        static string Spin(Rigidbody rb) => spun.TryGetValue(rb.Pointer, out var st) ? $"{st[0]:0.#} -> {rb.maxAngularVelocity:0.#}" : $"{rb.maxAngularVelocity:0.#}";

        static string Dr(JointDrive d) => $"{d.positionSpring:0}/{d.positionDamper:0}/{(d.maximumForce > 1e20f ? "inf" : d.maximumForce.ToString("0"))}";

        // What the hand and its hold look like when slow motion starts (spring/damper/maxForce, after scaling).
        static void LogHolds()
        {
            foreach (var h in Hands)
            {
                try
                {
                    var hj = HandJoint(h);
                    var hb = Alive(h.Jh) ? h.Jh.RigidBody : null;
                    string hand = (Alive(hj) ? $"hand joint x {Dr(hj.xDrive)} slerp {Dr(hj.slerpDrive)}" : "no hand joint") + (Alive(hb) ? $", spin limit {Spin(hb)}" : "");
                    var g = h.Grabber;
                    if (!Alive(g) || !g.IsGrabbing || g.GrabbedTarget == null) { Log.Msg($"{h.Name} empty; {hand}"); continue; }
                    var rb = g.GrabbedTarget.Rigidbody;
                    var gj = g.Joint;
                    Log.Msg($"{h.Name} holds '{g.GrabbedTarget.name}' ({(Alive(rb) ? $"{rb.mass:0.##} kg, spin limit {Spin(rb)}" : "no body")}); {hand}; " +
                            (Alive(gj) ? $"grab joint x {Dr(gj.xDrive)} slerp {Dr(gj.slerpDrive)} motion {gj.xMotion}/{gj.angularXMotion}" : "no grab joint"));
                }
                catch (Exception e) { Log.Warning($"hold log: {e.Message}"); }
            }
        }

        // Make every hand rewrite its drives from the game's own values (the patch scales them if slow motion is on).
        static void RefreshHands()
        {
            foreach (var h in Hands)
            {
                try
                {
                    var sh = Alive(h.Jh) ? h.Jh.StrengthHandler : null;
                    if (Alive(sh)) sh.UpdateJoint();
                }
                catch (Exception e) { Log.Warning($"refresh {h.Name}: {e.Message}"); }
            }
        }

        static void Scan()
        {
            Hands.Clear();
            head = Camera.main;
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRJointHand>(), FindObjectsSortMode.None))
            {
                var jh = o.TryCast<HVRJointHand>();
                if (!Alive(jh)) continue;
                var body = Alive(jh.RigidBody) ? jh.RigidBody.transform : jh.transform;
                Hands.Add(new Hand { Jh = jh, Name = jh.name.Replace("Physics ", ""), Body = body, Grabber = Alive(jh.Grabber) ? jh.Grabber : null, Target = Alive(jh.Target) ? jh.Target : null });
            }
            if (Hands.Count == 0) return;
            if (DebugLog.Value)
            {
                var sb = new StringBuilder("hands:");
                foreach (var h in Hands) sb.Append($" {h.Name} (follows {(h.Target != null ? h.Target.name : "nothing")})");
                Log.Msg(sb.ToString());
            }
            RefreshHands();
        }

        // ---------- measuring: how far the physics hand trails the point it chases, while the hand moves ----------

        static void MeterTick(float udt)
        {
            if (udt <= 0f || udt > 0.1f) return;
            Vector3 headPos = Alive(head) ? head.transform.position : Vector3.zero;
            bool slow = slowOn;
            foreach (var h in Hands)
            {
                if (!Alive(h.Body) || !Alive(h.Target)) continue;
                Vector3 rel = h.Target.position - headPos;
                float speed = h.HavePrev ? (rel - h.PrevRel).magnitude / udt : 0f;
                h.PrevRel = rel; h.HavePrev = true;
                Vector3 local = h.Target.InverseTransformPoint(h.Body.position);
                if (speed < 0.15f)
                {
                    h.StillT += udt;
                    if (h.StillT > 0.25f) { h.Rest = local; h.HaveRest = true; }
                }
                else h.StillT = 0f;
                Transform item = null;
                var g = h.Grabber;
                if (Alive(g) && g.IsGrabbing && g.GrabbedTarget != null) item = g.GrabbedTarget.transform;
                if (item == null) h.HaveItemRest = false;
                else
                {
                    if (item.Pointer != h.ItemPtr) { h.ItemPtr = item.Pointer; h.HaveItemRest = false; }
                    if (h.StillT > 0.25f) { h.ItemRest = h.Target.InverseTransformPoint(item.position); h.HaveItemRest = true; }
                }
                if (!h.HaveRest || speed < 1f) continue;
                float gap = h.Target.TransformVector(local - h.Rest).magnitude;
                var m = slow ? h.Slow : h.Norm;
                m.Add(gap);
                if (!slow && h.Norm.N > 2500) h.Norm.Clear();
                if (item != null && h.HaveItemRest)
                {
                    var im = slow ? h.ISlow : h.INorm;
                    im.Add(h.Target.TransformVector(h.Target.InverseTransformPoint(item.position) - h.ItemRest).magnitude);
                    if (!slow && h.INorm.N > 2500) h.INorm.Clear();
                }
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.FixedTimeTest))]
    internal static class FixedStepPatch
    {
        static void Postfix() => SlowMotionHandsMod.AdjustFixedStep();
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandStrengthHandler), nameof(HVRHandStrengthHandler.UpdateStrength), new[] { typeof(PDStrength) })]
    internal static class StrengthPdPatch
    {
        static void Postfix(HVRHandStrengthHandler __instance) => SlowMotionHandsMod.AfterStrength(__instance);
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandStrengthHandler), nameof(HVRHandStrengthHandler.UpdateStrength), new[] { typeof(HVRJointSettings) })]
    internal static class StrengthSettingsPatch
    {
        static void Postfix(HVRHandStrengthHandler __instance) => SlowMotionHandsMod.AfterStrength(__instance);
    }
}
