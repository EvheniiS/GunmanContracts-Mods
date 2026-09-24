using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using Il2CppHurricaneVR.Framework.Core.Utils;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ArrowQuiver.ArrowQuiverMod), "ArrowQuiver", "0.3.4", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace ArrowQuiver
{
    // The game has no quiver: the only arrow source is HVRArrowLoader, which spawns an arrow into
    // the nock when the bow string is grabbed. This mod adds one without new assets:
    //  * Quiver zone = the socket the bow was holstered in. While the bow is in hand that socket is
    //    guaranteed empty. Fallback when the bow was never holstered: a point beside the head.
    //  * Draw: grip press with the free hand inside the zone -> HVRArrowLoader.CreateArrow(false)
    //    (a pure spawn: no nock, no ammo change) and the hand grabs it on its nock-end grip point.
    //  * Nock: bring the carried arrow to the string -> it is destroyed and the hand grabs the
    //    string through the game's own path (OnStringGrabbed -> CreateArrow(true)), so the nocked
    //    arrow and its ammo accounting are exactly what a normal string grab produces.
    //  * Release away from the string -> the arrow drops and is cleaned up after a while (it can be
    //    picked up again). Released back at the quiver -> removed at once.
    public class ArrowQuiverMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> Debug;
        internal static IntPtr CarriedKnife;               // ANBKnife of the carried arrow, for the stab log

        MelonPreferences_Entry<bool> _enabled, _headFallback, _haptics, _gripSwitch;
        MelonPreferences_Entry<float> _quiverRadius, _nockRadius, _buffer, _dropLifetime, _slipRadius, _daggerFromNock;
        MelonPreferences_Entry<string> _daggerMode;
        static readonly string[] DaggerModes = { "Knuckles", "KnucklesReverse", "FlipX", "FlipY" };

        // Filled by Harmony postfixes on Start (LoaderStartPatch / HandStartPatch) - no scene-wide searches.
        internal static readonly List<HVRArrowLoader> Loaders = new();
        internal static readonly List<HVRHandGrabber> Hands = new();
        readonly Dictionary<IntPtr, BowState> _bowState = new();
        readonly Dictionary<IntPtr, HandState> _handState = new();
        // Where the hand grabbed the bow, in the holster's local space, keyed by holster name so it
        // survives scene changes (the rig and its sockets are rebuilt per scene).
        readonly Dictionary<string, Vector3> _grabSpot = new();
        readonly List<Dropped> _dropped = new();
        Carry _carry;
        PendingNock _pending;

        readonly Stopwatch _clock = Stopwatch.StartNew();
        double _fallbackScanAt = -1;
        long _frame;
        int _lastAmmo = int.MinValue;
        bool _inspected;
        // DebugLog only: cost of this mod's own OnUpdate
        readonly Stopwatch _perf = new();
        double _perfSum, _perfMax, _perfNext; int _perfFrames;

        class BowState
        {
            public HVRSocket Home;
            public bool WasSocketed;
            public IntPtr LoggedSocket = (IntPtr)(-1), LoggedBowHand = (IntPtr)(-1);
        }
        class HandState { public bool GripWasHeld; public double PressTime = -1; public long PressFrame; public bool Used, Logged; }
        class Carry
        {
            public HVRArrow Arrow; public HVRHandGrabber Hand; public HVRPhysicsBow Bow; public HVRArrowLoader Loader;
            public bool Grabbed; public int GrabTries; public long Frame; public bool PoseLogged;
            public long GraceUntil;                        // frame until which "not in hand" is expected
            // grip switch (A / X)
            public HVRPosableGrabPoint BackPoint, DaggerPoint;
            public bool Dagger, PrimaryWasDown, SecondaryWasDown;
        }
        class PendingNock { public HVRHandGrabber Hand; public HVRPhysicsBow Bow; public int Tries; public bool Grabbed; }
        class Dropped { public HVRArrow Arrow; public HVRPhysicsBow Bow; public HVRArrowLoader Loader; public double Since; }

        double Now => _clock.Elapsed.TotalSeconds;
        bool Dbg => Debug.Value;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var cat = MelonPreferences.CreateCategory("ArrowQuiver");
            _enabled = cat.CreateEntry("Enabled", true, description: "Master switch.");
            _quiverRadius = cat.CreateEntry("QuiverRadius", 0.20f, description: "Metres around the bow's holster (the quiver) within which a grip press draws an arrow.");
            _nockRadius = cat.CreateEntry("NockRadius", 0.15f, description: "Metres from the bow string at which a carried arrow is nocked.");
            _buffer = cat.CreateEntry("PressBufferSeconds", 0.35f, description: "How long after pressing grip (while still holding it) a draw may still happen.");
            _headFallback = cat.CreateEntry("UseHeadFallback", true, description: "If the bow was never holstered, put the quiver beside the head on the drawing hand's side.");
            _dropLifetime = cat.CreateEntry("DroppedArrowLifetime", 10f, description: "Seconds before a dropped quiver arrow is removed (the timer pauses while it is held).");
            _haptics = cat.CreateEntry("HapticOnDraw", true, description: "Short controller pulse when an arrow is drawn.");
            _slipRadius = cat.CreateEntry("SlipNockRadius", 0.30f, description: "If the arrow slips out of the hand while grip is still held this close to the string, nock it anyway.");
            _gripSwitch = cat.CreateEntry("GripSwitch", true, description: "A (right hand) / X (left hand) switches a quiver arrow between the nocking grip and a reverse dagger grip.");
            _daggerFromNock = cat.CreateEntry("DaggerGripFromNock", 0.25f, description: "Dagger grip: metres from the nock end towards the tip where the hand holds the arrow.");
            _daggerMode = cat.CreateEntry("DaggerOrientation", "Knuckles", description: "Dagger grip: Knuckles = shaft along the knuckle line, tip out past the little finger; KnucklesReverse = tip out past the index finger; FlipX / FlipY = the old 180-degree flips. With DebugLog on, B / Y cycles it in game.");
            Debug = cat.CreateEntry("DebugLog", false, description: "Log holster tracking, draws, nocks, drops and ammo to the MelonLoader console.");
            LoggerInstance.Msg("loaded - draw arrows from the bow's holster.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (_carry != null && Alive(_carry.Arrow)) Object.Destroy(_carry.Arrow.gameObject);
            SetCarry(null);
            _pending = null;
            _dropped.Clear();
            _bowState.Clear();
            _handState.Clear();
            Prune(Loaders); Prune(Hands);
            _fallbackScanAt = Now + 3.0;
            if (!_warmed) { _warmed = true; WarmUp(); }
        }

        // First-use costs (JIT of this mod's methods, interop type setup) otherwise land on the first
        // bow grab / draw as a visible hitch (perf log: 39-55 ms once). Pay them during a scene load.
        bool _warmed;
        void WarmUp()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (var m in typeof(ArrowQuiverMod).GetMethods(all))
                    if (!m.IsAbstract && !m.ContainsGenericParameters)
                        try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
                foreach (var t in new[] { typeof(HVRArrow), typeof(HVRArrowLoader), typeof(HVRPhysicsBow), typeof(HVRBowBase),
                                          typeof(HVRHandGrabber), typeof(HVRGrabbable), typeof(HVRSocket), typeof(HVRShoulderSocket),
                                          typeof(HVRPosableGrabPoint), typeof(Il2CppHurricaneVR.Framework.Shared.HVRController),
                                          typeof(ANBKnife), typeof(ANBGameLogic), typeof(ANBStaticGameManager), typeof(Collider), typeof(Renderer) })
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                    Il2CppType.From(t);
                }
            }
            catch (Exception e) { LoggerInstance.Warning($"warm-up incomplete: {e.Message}"); }
            if (Dbg) LoggerInstance.Msg($"warm-up done in {sw.Elapsed.TotalMilliseconds:0} ms");
        }

        public override void OnUpdate()
        {
            if (!_enabled.Value) return;
            _frame++;
            bool dbg = Dbg;
            if (dbg) _perf.Restart();
            try
            {
                // Safety net only: one search per scene, in case a Start ran before the patches.
                if (_fallbackScanAt >= 0 && Now >= _fallbackScanAt) { _fallbackScanAt = -1; FallbackScan(); }
                if (Loaders.Count == 0) return;

                for (int i = 0; i < Loaders.Count; i++)
                {
                    if (!Alive(Loaders[i]) || !Alive(Loaders[i].bow)) { Loaders.RemoveAt(i--); continue; }
                    TrackBow(Loaders[i]);
                }
                if (dbg) LogAmmo();

                UpdatePendingNock();
                UpdateDropped();
                if (_carry != null) UpdateCarry();
                else if (_pending == null)
                    for (int i = 0; i < Hands.Count; i++)
                    {
                        if (!Alive(Hands[i])) { Hands.RemoveAt(i--); continue; }
                        UpdateHand(Hands[i]);
                    }
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"update failed, resetting: {e.GetType().Name}: {e.Message}");
                SetCarry(null); _pending = null;
            }
            finally
            {
                if (dbg) PerfSample();
            }
        }

        void PerfSample()
        {
            double ms = _perf.Elapsed.TotalMilliseconds;
            _perfSum += ms; _perfFrames++;
            if (ms > _perfMax) _perfMax = ms;
            if (Now < _perfNext) return;
            if (_perfFrames > 0 && _perfNext > 0)
                LoggerInstance.Msg($"perf: OnUpdate avg {_perfSum / _perfFrames * 1000:0} us, max {_perfMax * 1000:0} us over {_perfFrames} frames");
            _perfSum = _perfMax = 0; _perfFrames = 0; _perfNext = Now + 30;
        }

        // ---- holster tracking -----------------------------------------------------------------
        void TrackBow(HVRArrowLoader loader)
        {
            var bow = loader.bow;
            var st = StateOf(bow);
            var g = bow.Grabbable;

            var sock = g.Socket;
            if (!Alive(sock) && g.IsSocketed) sock = g.PrimaryGrabber?.TryCast<HVRSocket>();
            if (Alive(sock)) st.Home = sock;

            IntPtr sp = Alive(sock) ? sock.Pointer : IntPtr.Zero;
            if (Dbg && sp != st.LoggedSocket)
            {
                st.LoggedSocket = sp;
                LoggerInstance.Msg($"bow socket: Socket={Describe(g.Socket)} Linked={Describe(g.LinkedSocket)} " +
                                   $"Starting={Describe(g.StartingSocket)} IsSocketed={g.IsSocketed} -> home={Describe(st.Home)}");
            }
            var bh = bow.BowHand;
            IntPtr hp = Alive(bh) ? bh.Pointer : IntPtr.Zero;
            // Taken out of the holster by hand: remember where the hand was, relative to the holster.
            // That is where the player physically reaches for it - the socket pivot is not
            // (first test: every press started 32-42 cm from the pivot).
            if (st.WasSocketed && hp != IntPtr.Zero && hp != st.LoggedBowHand && Alive(st.Home))
            {
                var local = st.Home.transform.InverseTransformPoint(bh.Palm.position);
                float offset = Dist(bh.Palm.position, st.Home.transform.position);
                if (offset < 0.5f) _grabSpot[st.Home.name] = local;
                if (Dbg) LoggerInstance.Msg($"bow taken from '{st.Home.name}': hand {offset * 100:0.0} cm from the holster pivot" +
                                            (offset < 0.5f ? " - quiver centred there" : " - too far, ignored"));
            }
            st.WasSocketed = Alive(sock);
            if (hp != st.LoggedBowHand)
            {
                st.LoggedBowHand = hp;
                if (Dbg) LoggerInstance.Msg($"bow hand: {(hp == IntPtr.Zero ? "none" : Side(bh))}, home={Describe(st.Home)}");
            }
        }

        BowState StateOf(HVRPhysicsBow bow)
        {
            if (!_bowState.TryGetValue(bow.Pointer, out var st)) _bowState[bow.Pointer] = st = new BowState();
            return st;
        }

        // Where the quiver is for this bow and drawing hand, or null if there is none.
        Vector3? Zone(HVRPhysicsBow bow, HVRHandGrabber drawHand, out string source)
        {
            source = null;
            var st = StateOf(bow);
            var mainCam = Camera.main;
            var cam = Alive(mainCam) ? mainCam.transform : null;
            if (Alive(st.Home))
            {
                var ht = st.Home.transform;
                var p = ht.position;
                // A bow taken from a wall rack or shop slot leaves a "home" that isn't on the body.
                if (!Alive(cam) || Dist(p, cam.position) < 1.2f)
                {
                    if (_grabSpot.TryGetValue(st.Home.name, out var local)) { source = "grab spot"; return ht.TransformPoint(local); }
                    source = "holster pivot"; return p;
                }
            }
            if (!_headFallback.Value || !Alive(cam)) return null;
            float side = drawHand.IsLeftHand ? -1f : 1f;
            var c = cam.position; var r = cam.right; var f = cam.forward;
            float fl = MathF.Sqrt(f.x * f.x + f.z * f.z);
            float fx = fl > 1e-4f ? f.x / fl : 0f, fz = fl > 1e-4f ? f.z / fl : 0f;
            source = "head";
            return new Vector3(c.x + side * r.x * 0.15f - fx * 0.10f,
                               c.y + side * r.y * 0.15f - 0.20f,
                               c.z + side * r.z * 0.15f - fz * 0.10f);
        }

        // ---- draw -----------------------------------------------------------------------------
        void UpdateHand(HVRHandGrabber hand)
        {
            if (!_handState.TryGetValue(hand.Pointer, out var hs)) _handState[hand.Pointer] = hs = new HandState();
            bool held = hand.IsGripGrabActive;
            if (held && !hs.GripWasHeld) { hs.PressTime = Now; hs.PressFrame = _frame; hs.Used = hs.Logged = false; }
            hs.GripWasHeld = held;

            if (!held || hs.Used || hs.PressTime < 0) return;
            if (_frame == hs.PressFrame) return;                        // the game gets the press frame first
            if (Now - hs.PressTime > _buffer.Value) return;
            if (hand.IsGrabbing) { hs.Used = true; return; }           // the game grabbed something itself

            HVRArrowLoader loader = null;
            foreach (var l in Loaders)
            {
                var bh = l.bow.BowHand;
                if (Alive(bh) && bh.Pointer != hand.Pointer) { loader = l; break; }
            }
            if (loader == null) return;                                 // no bow in the other hand
            var bow = loader.bow;
            if (Alive(bow.Arrow)) return;                               // an arrow is already nocked

            var zone = Zone(bow, hand, out var source);
            if (zone == null) return;
            float d = Dist(hand.Palm.position, zone.Value);
            if (Dbg && !hs.Logged)
            {
                hs.Logged = true;
                var home = StateOf(bow).Home;
                string pivot = source == "grab spot" && Alive(home) ? $", {Dist(hand.Palm.position, home.transform.position) * 100:0.0} cm from the pivot" : "";
                LoggerInstance.Msg($"{Side(hand)} grip press {d * 100:0.0} cm from quiver ({source}{pivot}), radius {_quiverRadius.Value * 100:0} cm");
            }
            if (d > _quiverRadius.Value) return;                       // may still arrive within the buffer
            hs.Used = true;
            if (Dbg && _frame - hs.PressFrame > 1)
                LoggerInstance.Msg($"  reached the quiver {(Now - hs.PressTime) * 1000:0} ms after the press");

            var game = ANBStaticGameManager.ANBmain;
            if (game != null && game.AmmoArrows <= 0) { if (Dbg) LoggerInstance.Msg("no arrow ammo - no draw"); return; }

            Draw(loader, hand);
        }

        void Draw(HVRArrowLoader loader, HVRHandGrabber hand)
        {
            var arrow = loader.CreateArrow(false);                      // spawn only: no nock, no ammo change
            if (!Alive(arrow)) { LoggerInstance.Warning("CreateArrow returned nothing"); return; }
            var palm = hand.Palm;
            arrow.transform.SetPositionAndRotation(palm.position, palm.rotation);
            var c = new Carry { Arrow = arrow, Hand = hand, Bow = loader.bow, Loader = loader, Frame = _frame };
            SetCarry(c);
            if (!TryGrabCarried(c)) return;
            if (_haptics.Value) try { hand.Controller.Vibrate(0.35f, 0.06f, 150f); } catch { }
            if (Dbg)
            {
                LoggerInstance.Msg($"drew arrow into {Side(hand)} hand, grab {(c.Grabbed ? "OK" : "pending")}");
                if (!_inspected) { _inspected = true; Inspect(arrow); }
            }
        }

        // false = the grab threw and the arrow was removed (never leave an untracked arrow behind).
        bool TryGrabCarried(Carry c)
        {
            c.GrabTries++;
            try
            {
                // Always hand the grab an explicit grip point. A forced TryGrab lets the hand reuse its
                // cached PosableGrabPoint (HVRHandGrabber+0x288). Right after a dropped arrow, that
                // cache can still point at a grip whose grabbable is gone, and OnGrabbed then throws
                // in GetGrabbableRelativeRotation (3 of 49 draws in 0.3.3, every time 0.3-1.5 s after
                // a drop). Grab(grabbable, trigger, point) is the game's put-this-in-the-hand call: it
                // orients the arrow to the hand instead of pulling it in, and grabs next update.
                var point = NockEndPoint(c.Arrow);
                if (Alive(point))
                {
                    c.Hand.Grab(c.Arrow.Grabbable, Il2CppHurricaneVR.Framework.Shared.HVRGrabTrigger.Active, point);
                    c.BackPoint = point;
                    c.Grabbed = true;
                    c.GraceUntil = _frame + 30;
                }
                else c.Grabbed = c.Hand.TryGrab(c.Arrow.Grabbable, true);   // force: it was never hovered
                return true;
            }
            catch (Exception e)
            {
                LoggerInstance.Warning($"grabbing the drawn arrow failed ({e.GetType().Name}) - removed it, press again");
                if (Dbg) LoggerInstance.Warning(e.ToString());
                try { c.Hand.ForceRelease(); } catch { }
                Object.Destroy(c.Arrow.gameObject);
                SetCarry(null);
                return false;
            }
        }

        // The arrow's grip at the nock end: the posable grab point closest to the arrow's origin,
        // which is the notch (logged: GrabPoint at local (0,0,0), the other at z=0.257).
        static HVRPosableGrabPoint NockEndPoint(HVRArrow arrow)
        {
            HVRPosableGrabPoint best = null;
            float bestD = float.MaxValue;
            var at = arrow.transform;
            foreach (var pg in arrow.GetComponentsInChildren<HVRPosableGrabPoint>(false))
            {
                if (pg.name == "QuiverDaggerGrip") continue;
                var p = at.InverseTransformPoint(pg.transform.position);
                float d = p.x * p.x + p.y * p.y + p.z * p.z;
                if (d < bestD) { bestD = d; best = pg; }
            }
            return best;
        }


        // ---- carrying -------------------------------------------------------------------------
        void UpdateCarry()
        {
            var c = _carry;
            if (!Alive(c.Arrow)) { if (Dbg) LoggerInstance.Msg("carried arrow is gone"); SetCarry(null); return; }
            if (!Alive(c.Hand)) { DropCarried(c, "hand gone"); return; }

            if (!c.Grabbed)
            {
                if (c.GrabTries >= 5)
                {
                    LoggerInstance.Warning("could not put the arrow in the hand - removed it");
                    Object.Destroy(c.Arrow.gameObject); SetCarry(null); return;
                }
                if (!TryGrabCarried(c)) return;
                if (Dbg && c.Grabbed) LoggerInstance.Msg($"arrow grabbed after {c.GrabTries} tries");
                return;
            }

            var bow = c.Bow;
            // The game's own nock socket may take the arrow out of the hand (native nocking).
            if (Alive(bow) && Alive(bow.Arrow) && bow.Arrow.Pointer == c.Arrow.Pointer)
            {
                LoggerInstance.Msg("the game nocked the carried arrow itself (native nocking) - leaving it to the game");
                SetCarry(null); return;
            }

            bool bowReady = Alive(bow) && Alive(bow.BowHand) && bow.BowHand.Pointer != c.Hand.Pointer;
            bool inHand = c.Hand.IsGrabbing && Alive(c.Hand.GrabbedTarget) && c.Hand.GrabbedTarget.Pointer == c.Arrow.Grabbable.Pointer;
            if (!inHand)
            {
                if (_frame - c.Frame < 3 || _frame < c.GraceUntil) return;   // a (re)grab may take frames to register
                bool grip = c.Hand.IsGripGrabActive;
                float ds = bowReady ? StringDistance(bow, c.Hand) : float.MaxValue;
                // Grip still held = the hand lost the arrow, the player didn't let go. Near the
                // string that was a nock attempt: finish it.
                if (grip && ds < _slipRadius.Value && !Alive(bow.Arrow) && !bow.NockGrabbable.IsBeingHeld)
                {
                    LoggerInstance.Msg($"arrow slipped from the hand {ds * 100:0.0} cm from the string with grip held - nocking it anyway");
                    StartNock(c, bow, ds);
                    return;
                }
                DropCarried(c, $"left the hand (grip {(grip ? "HELD - the hand lost it" : "released")}, " +
                               $"{(ds < 10f ? $"{ds * 100:0} cm from the string" : "bow not ready")})");
                return;
            }
            if (!c.PoseLogged && _frame - c.Frame >= 5)
            {
                c.PoseLogged = true;
                if (!Alive(c.BackPoint)) c.BackPoint = c.Hand.PosableGrabPoint;
                if (Dbg) LoggerInstance.Msg($"held on grab point '{Name(c.Hand.PosableGrabPoint)}' (GrabPoint '{Name(c.Hand.GrabPoint)}')");
            }
            if (_gripSwitch.Value && c.PoseLogged) UpdateGripSwitch(c);

            if (!bowReady)
            {
                if (Dbg) LoggerInstance.Msg("bow left the other hand - removing the carried arrow");
                c.Hand.ForceRelease(); Object.Destroy(c.Arrow.gameObject); SetCarry(null); return;
            }
            if (Alive(bow.Arrow) || !c.Hand.IsGripGrabActive) return;
            if (bow.NockGrabbable.IsBeingHeld) return;
            float d = StringDistance(bow, c.Hand);
            if (d > _nockRadius.Value) return;
            StartNock(c, bow, d);
        }

        float StringDistance(HVRPhysicsBow bow, HVRHandGrabber hand)
        {
            var nock = bow.NockGrabbable;
            return Alive(nock) ? Dist(hand.Palm.position, nock.transform.position) : float.MaxValue;
        }

        // Swap to the proven string path: the nocked arrow comes from OnStringGrabbed.
        void StartNock(Carry c, HVRPhysicsBow bow, float dist)
        {
            if (c.Hand.IsGrabbing) c.Hand.ForceRelease();
            Object.Destroy(c.Arrow.gameObject);
            SetCarry(null);
            _pending = new PendingNock { Hand = c.Hand, Bow = bow };
            if (Dbg) LoggerInstance.Msg($"nocking: carried arrow {dist * 100:0.0} cm from the string");
            UpdatePendingNock();
        }

        // ---- grip switch: A / X spins the arrow between the nocking grip and a dagger grip -------
        // Swapping is done by SwapGrip (not the knife swapper's ChangeGrabPoint - see there). The arrow
        // prefab has no dagger grip point, so one is cloned from the grip in use, moved along the
        // shaft and turned so the shaft runs along the knuckle line (through the curled fingers,
        // not through the palm) with the tip out past the little finger.
        void UpdateGripSwitch(Carry c)
        {
            var ctrl = c.Hand.Controller;
            if (!Alive(ctrl)) return;
            bool primary = ctrl.PrimaryButtonState.Active;
            bool pressed = primary && !c.PrimaryWasDown;
            c.PrimaryWasDown = primary;

            if (Dbg)
            {
                // Test aid: B / Y cycles the dagger orientation. In the dagger grip it applies at once.
                bool secondary = ctrl.SecondaryButtonState.Active;
                if (secondary && !c.SecondaryWasDown)
                {
                    int i = Array.IndexOf(DaggerModes, DaggerMode());
                    _daggerMode.Value = DaggerModes[(i + 1) % DaggerModes.Length];
                    LoggerInstance.Msg($"dagger orientation -> {_daggerMode.Value}");
                    var old = c.DaggerPoint;
                    c.DaggerPoint = null;
                    if (c.Dagger)
                    {
                        var fresh = MakeDaggerPoint(c);
                        if (Alive(fresh)) { c.DaggerPoint = fresh; SwapGrip(c, fresh); }
                    }
                    if (Alive(old) && (!Alive(c.DaggerPoint) || old.Pointer != c.DaggerPoint.Pointer)) Object.Destroy(old.gameObject);
                }
                c.SecondaryWasDown = secondary;
            }
            if (!pressed) return;

            try
            {
                if (!Alive(c.BackPoint)) { LoggerInstance.Warning("grip switch: no grip point to start from"); return; }
                if (!Alive(c.DaggerPoint)) c.DaggerPoint = MakeDaggerPoint(c);
                if (!Alive(c.DaggerPoint)) return;
                var target = c.Dagger ? c.BackPoint : c.DaggerPoint;
                SwapGrip(c, target);
                c.Dagger = !c.Dagger;
                if (Dbg) LoggerInstance.Msg($"grip -> {(c.Dagger ? $"dagger ({DaggerMode()}, {_daggerFromNock.Value * 100:0} cm from the nock)" : "nocking")}");
            }
            catch (Exception e) { LoggerInstance.Warning($"grip switch failed: {e.GetType().Name}: {e.Message}"); }
        }

        // Move the hand onto another grip point without moving the hand.
        // HVRHandGrabber.ChangeGrabPoint (the knife swapper's call) animates the object around ONE
        // fixed axis (Quaternion.AngleAxis about GetVector(axis)) and then re-poses the hand. That lands
        // only if the two grip points differ by a turn about that axis - true for a 180-degree flip,
        // false for the knuckle-aligned dagger grip, where the hand got dragged far off to the side.
        // Instead: let go, place the arrow so the target point sits exactly where the held point is
        // (the hand pose is fixed relative to its point, so the hand needn't move), grab that point.
        void SwapGrip(Carry c, HVRPosableGrabPoint target)
        {
            var hand = c.Hand;
            var arrow = c.Arrow;
            var held = hand.PosableGrabPoint;
            if (!Alive(held)) held = c.Dagger ? c.DaggerPoint : c.BackPoint;
            var ht = held.transform;
            var tt = target.transform;
            var at = arrow.transform;
            var hp = ht.position;
            var hr = ht.rotation;

            c.GraceUntil = _frame + 30;                                 // don't treat the release as a drop
            hand.ForceRelease();
            // A' = H * inverse(D) * A: afterwards the target point's frame equals the held point's.
            at.rotation = hr * Quaternion.Inverse(tt.rotation) * at.rotation;
            var tp = tt.position;
            var ap = at.position;
            at.position = new Vector3(ap.x + hp.x - tp.x, ap.y + hp.y - tp.y, ap.z + hp.z - tp.z);
            var rb = arrow.Rigidbody;
            if (Alive(rb))
            {
                rb.position = at.position;
                rb.rotation = at.rotation;
                rb.linearVelocity = new Vector3(0f, 0f, 0f);
                rb.angularVelocity = new Vector3(0f, 0f, 0f);
            }
            hand.Grab(arrow.Grabbable, Il2CppHurricaneVR.Framework.Shared.HVRGrabTrigger.Active, target);
            if (Dbg)
            {
                var e = Dist(tt.position, hp);
                LoggerInstance.Msg($"grip swap: target point {e * 100:0.0} cm from the old grip frame, grabbing={hand.IsGrabbing}");
            }
        }

        string DaggerMode()
        {
            var v = (_daggerMode.Value ?? "").Trim();
            foreach (var m in DaggerModes) if (string.Equals(m, v, StringComparison.OrdinalIgnoreCase)) return m;
            return DaggerModes[0];
        }

        HVRPosableGrabPoint MakeDaggerPoint(Carry c)
        {
            var back = c.BackPoint;
            var bt = back.transform;
            var go = Object.Instantiate(back.gameObject, bt.parent);
            go.name = "QuiverDaggerGrip";
            // The grip point carries the hand poser's preview hand (RightHand_Gloves_LOD0 ...). It
            // stays hidden on the prefab's own points but shows on a runtime clone as a ghost glove
            // stuck to the arrow. The pose itself is data, so the meshes can go.
            // Renderers alone weren't enough: a UI part of that preview (the wrist health display)
            // still floated on the arrow. So every child of the clone is switched off, except the
            // path down to its HVRHandPoser, whose pose data the grab needs.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Object.Destroy(r);
            var pg = go.GetComponent<HVRPosableGrabPoint>();
            if (!Alive(pg)) { Object.Destroy(go); LoggerInstance.Warning("grip switch: cloned grip point has no HVRPosableGrabPoint"); return null; }
            pg.Grabbable = c.Arrow.Grabbable;
            var keep = new List<IntPtr>();
            var poser = pg.HandPoser;
            if (Alive(poser))
            {
                var t = poser.transform;
                for (int guard = 0; guard < 16 && Alive(t) && t.Pointer != go.transform.Pointer; guard++, t = t.parent) keep.Add(t.Pointer);
            }
            int hidden = HideChildren(go.transform, keep);
            if (Dbg) LoggerInstance.Msg($"dagger grip clone: hid {hidden} preview object(s), poser {(Alive(poser) ? $"'{poser.name}'" : "none")}");

            // Direction nock -> tip: from the grip towards the middle of the shaft mesh ('Arrow01 (1)').
            // The prefab also carries hand-pose preview meshes (RightHand_Gloves, RightHandFinalPalm...).
            var from = bt.position;
            Vector3 mid = from; float best = -1f;
            foreach (var r in c.Arrow.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                if (n.Contains("hand") || n.Contains("palm") || n.Contains("wrist") || n.Contains("glove")) continue;
                var s = r.bounds.size;
                float len = MathF.Max(s.x, MathF.Max(s.y, s.z)) + (n.Contains("arrow") ? 10f : 0f);
                if (len > best) { best = len; mid = r.bounds.center; }
            }
            float dx = mid.x - from.x, dy = mid.y - from.y, dz = mid.z - from.z;
            float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dl > 1e-3f)
            {
                float k = _daggerFromNock.Value / dl;
                go.transform.position = new Vector3(from.x + dx * k, from.y + dy * k, from.z + dz * k);
            }
            go.transform.rotation = bt.rotation;
            var mode = DaggerMode();
            string how = mode;
            if (mode.StartsWith("Knuckles"))
            {
                // Shaft along the knuckle line. The hand pose is fixed relative to the grip point it
                // holds, so the knuckle line measured in the HELD point's frame is where it will be in
                // the new point's frame too. R turns that line onto the arrow's tip direction:
                // holding a point rotated by R from the back grip puts the tip along the knuckles.
                var knuckles = KnuckleLineInHeldPoint(c.Hand, out var span);
                if (knuckles is Vector3 kLocal && dl > 1e-3f)
                {
                    if (mode == "KnucklesReverse") kLocal = new Vector3(-kLocal.x, -kLocal.y, -kLocal.z);
                    var tipLocal = bt.InverseTransformDirection(new Vector3(dx / dl, dy / dl, dz / dl));
                    var r = Quaternion.FromToRotation(kLocal, tipLocal);
                    go.transform.Rotate(r.eulerAngles, Space.Self);
                    float cos = Math.Clamp(kLocal.x * tipLocal.x + kLocal.y * tipLocal.y + kLocal.z * tipLocal.z, -1f, 1f);
                    how = $"{mode} (shaft was {MathF.Acos(cos) * 180f / MathF.PI:0} deg off the knuckle line, knuckle span {span * 100:0.0} cm)";
                }
                else
                {
                    go.transform.Rotate(new Vector3(1f, 0f, 0f), 180f, Space.Self);
                    how = $"{mode} unavailable (no hand bones) - used FlipX";
                }
            }
            else go.transform.Rotate(mode == "FlipY" ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f), 180f, Space.Self);
            if (Dbg) LoggerInstance.Msg($"dagger grip built: {dl * 100:0.0} cm grip->shaft middle, moved {_daggerFromNock.Value * 100:0} cm, {how}");
            return pg;
        }

        static int HideChildren(Transform t, List<IntPtr> keep)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                if (keep.Contains(ch.Pointer)) n += HideChildren(ch, keep);
                else if (ch.gameObject.activeSelf) { ch.gameObject.SetActive(false); n++; }
            }
            return n;
        }

        // Index-knuckle -> little-finger-knuckle direction, in the frame of the grip point the hand holds.
        Vector3? KnuckleLineInHeldPoint(HVRHandGrabber hand, out float span)
        {
            span = 0f;
            try
            {
                var held = hand.PosableGrabPoint;
                var ph = hand._posableHand;
                if (!Alive(ph)) ph = hand.GetComponentInChildren<HVRPosableHand>(true);
                if (!Alive(held) || !Alive(ph)) return null;
                var i = FingerBase(ph.Index);
                var p = FingerBase(ph.Pinky);
                if (i == null || p == null) return null;
                float x = p.Value.x - i.Value.x, y = p.Value.y - i.Value.y, z = p.Value.z - i.Value.z;
                span = MathF.Sqrt(x * x + y * y + z * z);
                if (span < 0.01f || span > 0.2f) return null;               // not a hand-sized reading
                return held.transform.InverseTransformDirection(new Vector3(x / span, y / span, z / span));
            }
            catch { return null; }
        }

        static Vector3? FingerBase(HVRPosableFinger f)
        {
            if (f == null) return null;
            if (Alive(f.Root)) return f.Root.position;
            var bones = f.Bones;
            if (bones != null && bones.Count > 0 && bones[0] != null && Alive(bones[0].Transform)) return bones[0].Transform.position;
            return null;
        }

        void DropCarried(Carry c, string why)
        {
            SetCarry(null);
            var g = c.Arrow.Grabbable;
            string where = null;
            if (g.IsSocketed) where = "socketed";                       // never let it occupy a holster
            else if (Alive(c.Bow) && Alive(c.Hand) && Zone(c.Bow, c.Hand, out _) is Vector3 z
                     && Dist(c.Arrow.transform.position, z) < _quiverRadius.Value) where = "back in the quiver";
            if (where != null)
            {
                if (Dbg) LoggerInstance.Msg($"carried arrow {why}: {where} - removed");
                Object.Destroy(c.Arrow.gameObject);
                return;
            }
            _dropped.Add(new Dropped { Arrow = c.Arrow, Bow = c.Bow, Loader = c.Loader, Since = Now });
            if (Dbg) LoggerInstance.Msg($"carried arrow {why}: dropped, removed after {_dropLifetime.Value:0} s unless picked up");
        }

        // A dropped quiver arrow: removed after the lifetime; picked up by a hand -> carried again.
        void UpdateDropped()
        {
            for (int i = 0; i < _dropped.Count; i++)
            {
                var d = _dropped[i];
                if (!Alive(d.Arrow)) { _dropped.RemoveAt(i--); continue; }
                var g = d.Arrow.Grabbable;
                if (g.IsSocketed) { Object.Destroy(d.Arrow.gameObject); _dropped.RemoveAt(i--); continue; }
                if (g.IsBeingHeld)
                {
                    d.Since = Now;
                    var hand = g.PrimaryGrabber?.TryCast<HVRHandGrabber>();
                    if (_carry == null && _pending == null && Alive(hand) && Alive(d.Bow))
                    {
                        _dropped.RemoveAt(i--);
                        SetCarry(new Carry { Arrow = d.Arrow, Hand = hand, Bow = d.Bow, Loader = d.Loader, Grabbed = true, Frame = _frame });
                        if (Dbg) LoggerInstance.Msg($"dropped quiver arrow picked up by {Side(hand)} hand");
                    }
                    continue;
                }
                if (Now - d.Since > _dropLifetime.Value) { Object.Destroy(d.Arrow.gameObject); _dropped.RemoveAt(i--); }
            }
        }

        void UpdatePendingNock()
        {
            var p = _pending;
            if (p == null) return;
            if (!Alive(p.Hand) || !Alive(p.Bow)) { _pending = null; return; }
            if (p.Grabbed)
            {
                // One frame after the string grab: did the loader put an arrow on the string?
                _pending = null;
                if (Dbg) LoggerInstance.Msg($"string grabbed -> arrow {(Alive(p.Bow.Arrow) ? "nocked" : "NOT created")}" +
                                            (Alive(p.Bow.Arrow) ? $", createdNok={p.Bow.createdNok}" : ""));
                return;
            }
            var nock = p.Bow.NockGrabbable;
            if (nock.IsBeingHeld || Alive(p.Bow.Arrow)) { p.Grabbed = true; return; }
            if (++p.Tries > 5)
            {
                LoggerInstance.Warning("string grab after nocking failed 5 times - grip the string to load");
                _pending = null; return;
            }
            p.Bow.NockHand = p.Hand;                                    // normally set in BeforeNockHovered
            p.Grabbed = p.Hand.TryGrab(nock, false);
        }

        void SetCarry(Carry c)
        {
            _carry = c;
            try { CarriedKnife = c != null ? (c.Arrow.GetComponent<ANBKnife>()?.Pointer ?? IntPtr.Zero) : IntPtr.Zero; }
            catch { CarriedKnife = IntPtr.Zero; }
        }

        // ---- diagnostics ----------------------------------------------------------------------
        void LogAmmo()
        {
            var game = ANBStaticGameManager.ANBmain;
            if (game == null) return;
            int a = game.AmmoArrows;
            if (a == _lastAmmo) return;
            LoggerInstance.Msg($"AmmoArrows {(_lastAmmo == int.MinValue ? "" : _lastAmmo + " -> ")}{a} (max {game.AmmoArrowsMax})");
            _lastAmmo = a;
        }

        // First draw only: what the arrow prefab offers for grip positions (QUIVER_PLAN 3.8).
        void Inspect(HVRArrow arrow)
        {
            try
            {
                var t = arrow.transform;
                LoggerInstance.Msg($"[inspect] arrow '{arrow.name}' Notch={(Alive(arrow.Notch) ? V(arrow.Notch.localPosition) : "none")} " +
                                   $"NotchPointLocal={V(arrow.NotchPointLocal)} ForwardGrabbable={Alive(arrow.ForwardGrabbable)} lossyScale={V(t.lossyScale)}");
                var g = arrow.Grabbable;
                LoggerInstance.Msg($"[inspect] grabbable '{g.name}' on '{g.gameObject.name}', GrabPoints={g.GrabPoints?.Count ?? -1}");
                if (g.GrabPoints != null)
                    foreach (var gp in g.GrabPoints)
                        if (Alive(gp)) LoggerInstance.Msg($"[inspect]   grab point '{gp.name}' local pos {V(Local(t, gp.position))} rot {V(gp.localEulerAngles)} posable={Alive(gp.GetComponent<HVRPosableGrabPoint>())}");
                foreach (var pg in arrow.GetComponentsInChildren<HVRPosableGrabPoint>(true))
                    LoggerInstance.Msg($"[inspect]   posable '{pg.name}' L={pg.LeftHand} R={pg.RightHand} line={pg.IsLineGrab} group={pg.Group} poser={Alive(pg.HandPoser)} parent='{Name(pg.transform.parent)}'");
                LoggerInstance.Msg($"[inspect] grab-point swapper on prefab: {Alive(arrow.GetComponentInChildren<HVRGrabPointSwapper>(true))}");
                foreach (var r in arrow.GetComponentsInChildren<Renderer>(true))
                    LoggerInstance.Msg($"[inspect]   renderer '{r.name}' localBounds center {V(r.localBounds.center)} size {V(r.localBounds.size)}");
                var k = arrow.GetComponent<ANBKnife>() ?? arrow.GetComponentInChildren<ANBKnife>(true);
                if (Alive(k))
                    LoggerInstance.Msg($"[inspect] knife: isArrow={k.isArrow} stabActive={k.stabActive} stabber={(Alive(k.Stabber) ? k.Stabber.enabled.ToString() : "none")} " +
                                       $"damage={k.knifeDamage} momentum={k.stabThresholdMomentum} angle={k.allowedAngle} killOnHuman={k.killArrowOnHumanHit} canSlash={k.canSlash} isHeld={k.isHeld}");
                else LoggerInstance.Msg("[inspect] no ANBKnife on the arrow");
            }
            catch (Exception e) { LoggerInstance.Warning($"[inspect] failed: {e.Message}"); }
        }

        // ---- scanning / helpers ---------------------------------------------------------------
        void FallbackScan()
        {
            int before = Loaders.Count + Hands.Count;
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRArrowLoader>(), FindObjectsSortMode.None))
                Register(Loaders, o.TryCast<HVRArrowLoader>());
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRHandGrabber>(), FindObjectsSortMode.None))
                Register(Hands, o.TryCast<HVRHandGrabber>());
            int found = Loaders.Count + Hands.Count - before;
            if (Dbg) LoggerInstance.Msg($"scene check: {Loaders.Count} arrow loader(s), {Hands.Count} hand(s)" +
                                        (found > 0 ? $" - {found} were missed by registration" : ""));
        }

        internal static void Register<T>(List<T> list, T o) where T : Object
        {
            if (!Alive(o)) return;
            foreach (var x in list) if (x.Pointer == o.Pointer) return;
            list.Add(o);
        }

        static void Prune<T>(List<T> list) where T : Object
        {
            for (int i = 0; i < list.Count; i++) if (!Alive(list[i])) list.RemoveAt(i--);
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }

        static float Dist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static Vector3 Local(Transform t, Vector3 world) => t.InverseTransformPoint(world);
        static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
        static string Name(Object o) => Alive(o) ? o.name : "none";
        static string Side(HVRHandGrabber h) => h.IsLeftHand ? "left" : "right";

        static string Describe(HVRSocket s)
        {
            if (!Alive(s)) return "none";
            var sh = s.TryCast<HVRShoulderSocket>();
            string kind = sh != null ? $"shoulder L={sh.leftShoulder} R={sh.rightShoulder}" : s.GetIl2CppType().Name;
            return $"'{s.name}' ({kind})";
        }
    }

    // Objects register themselves when they start, so the mod never searches the scene.
    [HarmonyLib.HarmonyPatch(typeof(HVRArrowLoader), nameof(HVRArrowLoader.Start))]
    internal static class LoaderStartPatch
    {
        static void Postfix(HVRArrowLoader __instance)
        {
            try { ArrowQuiverMod.Register(ArrowQuiverMod.Loaders, __instance); } catch { }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRHandGrabber), nameof(HVRHandGrabber.Start))]
    internal static class HandStartPatch
    {
        static void Postfix(HVRHandGrabber __instance)
        {
            try { ArrowQuiverMod.Register(ArrowQuiverMod.Hands, __instance); } catch { }
        }
    }

    // Debug only: shows whether a hand-held quiver arrow stabs (QUIVER_PLAN 3.7, test 6b).
    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.stabEnemy))]
    internal static class StabLogPatch
    {
        static void Prefix(ANBKnife __instance)
        {
            try
            {
                if (ArrowQuiverMod.Debug == null || !ArrowQuiverMod.Debug.Value) return;
                if (__instance != null && __instance.Pointer == ArrowQuiverMod.CarriedKnife)
                    ArrowQuiverMod.Log.Msg($"carried quiver arrow STABBED (damage {__instance.knifeDamage})");
            }
            catch { }
        }
    }
}
