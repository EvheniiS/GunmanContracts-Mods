using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using Il2CppHurricaneVR.Framework.Core.Utils;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using UnityEngine;
using static BetterBow.BetterBowMod;
using Object = UnityEngine.Object;

namespace BetterBow
{
    // The game has no quiver: the only arrow source is HVRArrowLoader, which spawns an arrow into
    // the nock when the bow string is grabbed. This adds one without new assets:
    //  * Quiver = the holster the bow came from, centred on where the hand grabbed the bow out of it
    //    (the holster pivot sits 30-40 cm off). While the bow is in hand that holster is empty.
    //    Fallback when the bow was never holstered: a point beside the head.
    //  * Draw: grip press with the free hand at the quiver -> HVRArrowLoader.CreateArrow(false)
    //    (a pure spawn: no nock, no ammo change) and the hand grabs it on its nock-end grip point.
    //  * Nock: bring the carried arrow to the string -> it is destroyed and the hand grabs the string
    //    through the game's own path (OnStringGrabbed -> CreateArrow(true)), so the nocked arrow and
    //    its ammo accounting are exactly what a normal string grab produces.
    //  * A / X switches the carried arrow to a dagger grip and back.
    //  * Released away from the string -> the arrow drops and is cleaned up after a while (it can be
    //    picked up again). Released back at the quiver -> removed at once.
    internal sealed class Quiver
    {
        internal static IntPtr CarriedKnife;               // ANBKnife of the carried arrow, for the stab log

        readonly Dictionary<IntPtr, BowState> _bowState = new();
        readonly Dictionary<IntPtr, HandState> _handState = new();
        // Where the hand grabbed the bow, in the holster's local space, keyed by holster name so it
        // survives scene changes (the rig and its sockets are rebuilt per scene).
        readonly Dictionary<string, Vector3> _grabSpot = new();
        readonly List<Dropped> _dropped = new();
        Carry _carry;
        PendingNock _pending;
        int _lastAmmo = int.MinValue;
        // DebugLog only: cost of the quiver's own update
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

        internal void OnScene()
        {
            if (_carry != null && U.Alive(_carry.Arrow)) Object.Destroy(_carry.Arrow.gameObject);
            SetCarry(null);
            _pending = null;
            _dropped.Clear();
            _bowState.Clear();
            _handState.Clear();
        }

        internal void Update()
        {
            if (!Settings.Quiver.Value) return;
            bool dbg = U.Dbg;
            if (dbg) _perf.Restart();
            try
            {
                foreach (var loader in Loaders) TrackBow(loader);
                if (dbg) LogAmmo();

                UpdatePendingNock();
                UpdateDropped();
                if (_carry != null) UpdateCarry();
                else if (_pending == null)
                    foreach (var hand in Hands) UpdateHand(hand);
            }
            catch (Exception e)
            {
                Log.Warning($"quiver update failed, resetting: {e.GetType().Name}: {e.Message}");
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
            if (U.Now < _perfNext) return;
            if (_perfFrames > 0 && _perfNext > 0)
                Log.Msg($"perf: quiver update avg {_perfSum / _perfFrames * 1000:0} us, max {_perfMax * 1000:0} us over {_perfFrames} frames");
            _perfSum = _perfMax = 0; _perfFrames = 0; _perfNext = U.Now + 30;
        }

        // ---- holster tracking -----------------------------------------------------------------
        void TrackBow(HVRArrowLoader loader)
        {
            var bow = loader.bow;
            var st = StateOf(bow);
            var g = bow.Grabbable;

            var sock = g.Socket;
            if (!U.Alive(sock) && g.IsSocketed) sock = g.PrimaryGrabber?.TryCast<HVRSocket>();
            if (U.Alive(sock)) st.Home = sock;

            IntPtr sp = U.Alive(sock) ? sock.Pointer : IntPtr.Zero;
            if (U.Dbg && sp != st.LoggedSocket)
            {
                st.LoggedSocket = sp;
                Log.Msg($"bow socket: Socket={Describe(g.Socket)} IsSocketed={g.IsSocketed} -> home={Describe(st.Home)}");
            }
            var bh = bow.BowHand;
            IntPtr hp = U.Alive(bh) ? bh.Pointer : IntPtr.Zero;
            // Taken out of the holster by hand: remember where the hand was, relative to the holster.
            // That is where the player physically reaches for it - the socket pivot is not
            // (first test: every press started 32-42 cm from the pivot).
            if (st.WasSocketed && hp != IntPtr.Zero && hp != st.LoggedBowHand && U.Alive(st.Home))
            {
                var local = st.Home.transform.InverseTransformPoint(bh.Palm.position);
                float offset = U.Dist(bh.Palm.position, st.Home.transform.position);
                if (offset < 0.5f) _grabSpot[st.Home.name] = local;
                if (U.Dbg) Log.Msg($"bow taken from '{st.Home.name}': hand {offset * 100:0.0} cm from the holster pivot" +
                                   (offset < 0.5f ? " - quiver centred there" : " - too far, ignored"));
            }
            st.WasSocketed = U.Alive(sock);
            if (hp != st.LoggedBowHand)
            {
                st.LoggedBowHand = hp;
                if (U.Dbg) Log.Msg($"bow hand: {(hp == IntPtr.Zero ? "none" : U.Side(bh))}, home={Describe(st.Home)}");
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
            var cam = U.Alive(mainCam) ? mainCam.transform : null;
            if (U.Alive(st.Home))
            {
                var ht = st.Home.transform;
                var p = ht.position;
                // A bow taken from a wall rack or shop slot leaves a "home" that isn't on the body.
                if (!U.Alive(cam) || U.Dist(p, cam.position) < 1.2f)
                {
                    if (_grabSpot.TryGetValue(st.Home.name, out var local)) { source = "grab spot"; return ht.TransformPoint(local); }
                    source = "holster pivot"; return p;
                }
            }
            if (!Settings.HeadFallback.Value || !U.Alive(cam)) return null;
            float side = drawHand.IsLeftHand ? -1f : 1f;
            var c = cam.position; var r = cam.right; var f = cam.forward;
            float fl = MathF.Sqrt(f.x * f.x + f.z * f.z);
            float fx = fl > 1e-4f ? f.x / fl : 0f, fz = fl > 1e-4f ? f.z / fl : 0f;
            source = "head";
            return new Vector3(c.x + side * r.x * 0.15f - fx * 0.10f,
                               c.y + side * r.y * 0.15f - 0.20f,
                               c.z + side * r.z * 0.15f - fz * 0.10f);
        }

        // Where the palm would be if nothing blocked the hand. The in-game hand is a physics body
        // chasing ControllerHandTarget (PhysicsHandTarget ?? TrackedController): reaching back to the
        // shoulder next to a wall, the physics hand stops at the wall while the real controller
        // carries on, so the physics palm can fail to reach the quiver ("sometimes I hit a wall and
        // it's stuck"). Map the palm's offset in the hand onto the target's pose instead.
        static Vector3 ReachPoint(HVRHandGrabber hand, Vector3 palm)
        {
            var target = hand.ControllerHandTarget;
            if (!U.Alive(target)) return palm;
            return target.TransformPoint(hand.transform.InverseTransformPoint(palm));
        }

        // ---- draw -----------------------------------------------------------------------------
        void UpdateHand(HVRHandGrabber hand)
        {
            if (!_handState.TryGetValue(hand.Pointer, out var hs)) _handState[hand.Pointer] = hs = new HandState();
            bool held = hand.IsGripGrabActive;
            if (held && !hs.GripWasHeld) { hs.PressTime = U.Now; hs.PressFrame = U.Frame; hs.Used = hs.Logged = false; }
            hs.GripWasHeld = held;

            if (!held || hs.Used || hs.PressTime < 0) return;
            if (U.Frame == hs.PressFrame) return;                       // the game gets the press frame first
            if (U.Now - hs.PressTime > Settings.QuiverBuffer.Value) return;
            if (hand.IsGrabbing) { hs.Used = true; return; }           // the game grabbed something itself

            HVRArrowLoader loader = null;
            foreach (var l in Loaders)
            {
                var bh = l.bow.BowHand;
                if (U.Alive(bh) && bh.Pointer != hand.Pointer) { loader = l; break; }
            }
            if (loader == null) return;                                 // no bow in the other hand
            var bow = loader.bow;
            if (U.Alive(bow.Arrow)) return;                             // an arrow is already nocked

            var zone = Zone(bow, hand, out var source);
            if (zone == null) return;
            var palm = hand.Palm.position;
            var reach = ReachPoint(hand, palm);
            float dPalm = U.Dist(palm, zone.Value), dReach = U.Dist(reach, zone.Value);
            float d = MathF.Min(dPalm, dReach);
            if (U.Dbg && !hs.Logged)
            {
                hs.Logged = true;
                Log.Msg($"{U.Side(hand)} grip press {d * 100:0.0} cm from quiver ({source}): palm {dPalm * 100:0.0} cm, " +
                        $"controller {dReach * 100:0.0} cm (hand held back {U.Dist(palm, reach) * 100:0.0} cm), radius {Settings.QuiverRadius.Value * 100:0} cm");
            }
            if (d > Settings.QuiverRadius.Value) return;               // may still arrive within the buffer
            hs.Used = true;
            if (U.Dbg && U.Frame - hs.PressFrame > 1)
                Log.Msg($"  reached the quiver {(U.Now - hs.PressTime) * 1000:0} ms after the press");

            var game = ANBStaticGameManager.ANBmain;
            if (game != null && game.AmmoArrows <= 0) { if (U.Dbg) Log.Msg("no arrow ammo - no draw"); return; }

            Draw(loader, hand);
        }

        void Draw(HVRArrowLoader loader, HVRHandGrabber hand)
        {
            var arrow = loader.CreateArrow(false);                      // spawn only: no nock, no ammo change
            if (!U.Alive(arrow)) { Log.Warning("CreateArrow returned nothing"); return; }
            var palm = hand.Palm;
            arrow.transform.SetPositionAndRotation(palm.position, palm.rotation);
            var c = new Carry { Arrow = arrow, Hand = hand, Bow = loader.bow, Loader = loader, Frame = U.Frame };
            SetCarry(c);
            if (!TryGrabCarried(c)) return;
            if (Settings.Haptics.Value) try { hand.Controller.Vibrate(0.35f, 0.06f, 150f); } catch { }
            if (U.Dbg) Log.Msg($"drew arrow into {U.Side(hand)} hand");
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
                // in GetGrabbableRelativeRotation (3 of 49 draws, every time 0.3-1.5 s after a drop).
                // Grab(grabbable, trigger, point) is the game's put-this-in-the-hand call: it orients
                // the arrow to the hand instead of pulling it in, and grabs next update.
                var point = NockEndPoint(c.Arrow);
                if (U.Alive(point))
                {
                    c.Hand.Grab(c.Arrow.Grabbable, Il2CppHurricaneVR.Framework.Shared.HVRGrabTrigger.Active, point);
                    c.BackPoint = point;
                    c.Grabbed = true;
                    c.GraceUntil = U.Frame + 30;
                }
                else c.Grabbed = c.Hand.TryGrab(c.Arrow.Grabbable, true);   // force: it was never hovered
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"grabbing the drawn arrow failed ({e.GetType().Name}) - removed it, press again");
                if (U.Dbg) Log.Warning(e.ToString());
                try { c.Hand.ForceRelease(); } catch { }
                Object.Destroy(c.Arrow.gameObject);
                SetCarry(null);
                return false;
            }
        }

        // The arrow's grip at the nock end: the posable grab point closest to the arrow's origin,
        // which is the notch (the prefab has one at local (0,0,0) and one at z=0.257).
        static HVRPosableGrabPoint NockEndPoint(HVRArrow arrow)
        {
            HVRPosableGrabPoint best = null;
            float bestD = float.MaxValue;
            var at = arrow.transform;
            foreach (var pg in arrow.GetComponentsInChildren<HVRPosableGrabPoint>(false))
            {
                if (pg.name == DaggerGripName) continue;
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
            if (!U.Alive(c.Arrow)) { if (U.Dbg) Log.Msg("carried arrow is gone"); SetCarry(null); return; }
            if (!U.Alive(c.Hand)) { DropCarried(c, "hand gone"); return; }

            if (!c.Grabbed)
            {
                if (c.GrabTries >= 5)
                {
                    Log.Warning("could not put the arrow in the hand - removed it");
                    Object.Destroy(c.Arrow.gameObject); SetCarry(null); return;
                }
                TryGrabCarried(c);
                return;
            }

            var bow = c.Bow;
            // The game's own nock socket may take the arrow out of the hand (native nocking).
            if (U.Alive(bow) && U.Alive(bow.Arrow) && bow.Arrow.Pointer == c.Arrow.Pointer)
            {
                Log.Msg("the game nocked the carried arrow itself (native nocking) - leaving it to the game");
                SetCarry(null); return;
            }

            bool bowReady = U.Alive(bow) && U.Alive(bow.BowHand) && bow.BowHand.Pointer != c.Hand.Pointer;
            bool inHand = c.Hand.IsGrabbing && U.Alive(c.Hand.GrabbedTarget) && c.Hand.GrabbedTarget.Pointer == c.Arrow.Grabbable.Pointer;
            if (!inHand)
            {
                bool grip = c.Hand.IsGripGrabActive;
                // Thrown: grip let go and the arrow is flying. Checked before the grace wait so the
                // assist starts at once; a re-grab (the reason for the wait) always has grip held.
                if (!grip && U.Frame - c.Frame >= 3 && ThrowAssist.IsThrow(c.Arrow))
                {
                    if (DropCarried(c, "thrown", thrown: true)) ThrowAssist.Throw(c.Arrow);
                    return;
                }
                if (U.Frame - c.Frame < 3 || U.Frame < c.GraceUntil) return;   // a (re)grab may take frames to register
                float ds = bowReady ? StringDistance(bow, c.Hand) : float.MaxValue;
                // Grip still held = the hand lost the arrow, the player didn't let go. Near the
                // string that was a nock attempt: finish it.
                if (grip && ds < Settings.SlipNockRadius.Value && !U.Alive(bow.Arrow) && !bow.NockGrabbable.IsBeingHeld)
                {
                    Log.Msg($"arrow slipped from the hand {ds * 100:0.0} cm from the string with grip held - nocking it anyway");
                    StartNock(c, bow, ds);
                    return;
                }
                DropCarried(c, $"left the hand (grip {(grip ? "HELD - the hand lost it" : "released")}, " +
                               $"{(ds < 10f ? $"{ds * 100:0} cm from the string" : "bow not ready")})");
                return;
            }
            if (!c.PoseLogged && U.Frame - c.Frame >= 5)
            {
                c.PoseLogged = true;
                if (!U.Alive(c.BackPoint)) c.BackPoint = c.Hand.PosableGrabPoint;
            }
            if (Settings.GripSwitch.Value && c.PoseLogged) UpdateGripSwitch(c);

            if (!bowReady)
            {
                if (U.Dbg) Log.Msg("bow left the other hand - removing the carried arrow");
                c.Hand.ForceRelease(); Object.Destroy(c.Arrow.gameObject); SetCarry(null); return;
            }
            if (U.Alive(bow.Arrow) || !c.Hand.IsGripGrabActive) return;
            if (bow.NockGrabbable.IsBeingHeld) return;
            float d = StringDistance(bow, c.Hand);
            if (d > Settings.NockRadius.Value) return;
            StartNock(c, bow, d);
        }

        static float StringDistance(HVRPhysicsBow bow, HVRHandGrabber hand)
        {
            var nock = bow.NockGrabbable;
            return U.Alive(nock) ? U.Dist(hand.Palm.position, nock.transform.position) : float.MaxValue;
        }

        // Swap to the proven string path: the nocked arrow comes from OnStringGrabbed.
        void StartNock(Carry c, HVRPhysicsBow bow, float dist)
        {
            if (c.Hand.IsGrabbing) c.Hand.ForceRelease();
            Object.Destroy(c.Arrow.gameObject);
            SetCarry(null);
            _pending = new PendingNock { Hand = c.Hand, Bow = bow };
            if (U.Dbg) Log.Msg($"nocking: carried arrow {dist * 100:0.0} cm from the string");
            UpdatePendingNock();
        }

        // ---- grip switch: A / X spins the arrow between the nocking grip and a dagger grip -------
        // The arrow prefab has no dagger grip point, so one is cloned from the grip in use, moved
        // along the shaft and turned so the shaft runs along the knuckle line (through the curled
        // fingers, not through the palm) with the tip out past the little finger. The swap itself is
        // SwapGrip, not the knife swapper's ChangeGrabPoint (see there).
        const string DaggerGripName = "QuiverDaggerGrip";

        void UpdateGripSwitch(Carry c)
        {
            var ctrl = c.Hand.Controller;
            if (!U.Alive(ctrl)) return;
            bool primary = ctrl.PrimaryButtonState.Active;
            bool pressed = primary && !c.PrimaryWasDown;
            c.PrimaryWasDown = primary;

            if (U.Dbg)
            {
                // Test aid: B / Y cycles the dagger orientation. In the dagger grip it applies at once.
                bool secondary = ctrl.SecondaryButtonState.Active;
                if (secondary && !c.SecondaryWasDown)
                {
                    Settings.DaggerTip.Value = DaggerTip() == "Down" ? "Up" : "Down";
                    Log.Msg($"dagger tip -> {Settings.DaggerTip.Value}");
                    var old = c.DaggerPoint;
                    c.DaggerPoint = null;
                    if (c.Dagger)
                    {
                        var fresh = MakeDaggerPoint(c);
                        if (U.Alive(fresh)) { c.DaggerPoint = fresh; SwapGrip(c, fresh); }
                    }
                    if (U.Alive(old) && (!U.Alive(c.DaggerPoint) || old.Pointer != c.DaggerPoint.Pointer)) Object.Destroy(old.gameObject);
                }
                c.SecondaryWasDown = secondary;
            }
            if (!pressed) return;

            try
            {
                if (!U.Alive(c.BackPoint)) { Log.Warning("grip switch: no grip point to start from"); return; }
                if (!U.Alive(c.DaggerPoint)) c.DaggerPoint = MakeDaggerPoint(c);
                if (!U.Alive(c.DaggerPoint)) return;
                SwapGrip(c, c.Dagger ? c.BackPoint : c.DaggerPoint);
                c.Dagger = !c.Dagger;
                if (U.Dbg) Log.Msg($"grip -> {(c.Dagger ? $"dagger (tip {DaggerTip()}, {Settings.DaggerFromNock.Value * 100:0} cm from the nock)" : "nocking")}");
            }
            catch (Exception e) { Log.Warning($"grip switch failed: {e.GetType().Name}: {e.Message}"); }
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
            if (!U.Alive(held)) held = c.Dagger ? c.DaggerPoint : c.BackPoint;
            var ht = held.transform;
            var tt = target.transform;
            var at = arrow.transform;
            var hp = ht.position;
            var hr = ht.rotation;

            c.GraceUntil = U.Frame + 30;                                // don't treat the release as a drop
            hand.ForceRelease();
            // A' = H * inverse(D) * A: afterwards the target point's frame equals the held point's.
            at.rotation = hr * Quaternion.Inverse(tt.rotation) * at.rotation;
            var tp = tt.position;
            var ap = at.position;
            at.position = new Vector3(ap.x + hp.x - tp.x, ap.y + hp.y - tp.y, ap.z + hp.z - tp.z);
            var rb = arrow.Rigidbody;
            if (U.Alive(rb))
            {
                rb.position = at.position;
                rb.rotation = at.rotation;
                rb.linearVelocity = new Vector3(0f, 0f, 0f);
                rb.angularVelocity = new Vector3(0f, 0f, 0f);
            }
            hand.Grab(arrow.Grabbable, Il2CppHurricaneVR.Framework.Shared.HVRGrabTrigger.Active, target);
            if (U.Dbg) Log.Msg($"grip swap: target point {U.Dist(tt.position, hp) * 100:0.0} cm from the old grip frame");
        }

        static string DaggerTip() =>
            string.Equals((Settings.DaggerTip.Value ?? "").Trim(), "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down";

        HVRPosableGrabPoint MakeDaggerPoint(Carry c)
        {
            var back = c.BackPoint;
            var bt = back.transform;
            // The clone's preview hand carries an ANBWristHud, and ANBWristHud.Awake writes itself into
            // the game's global ANBwristHudLeft/Right with no check. Once the arrow is destroyed that
            // slot is dead, and the next pause (phone Settings, menu button) throws half-way: hands
            // left on the controllers, no menu. Put the game's own HUDs back and remove the clone's.
            var game = ANBStaticGameManager.ANBmain;
            var hudL = game?.ANBwristHudLeft; var hudR = game?.ANBwristHudRight;
            var go = Object.Instantiate(back.gameObject, bt.parent);
            go.name = DaggerGripName;
            if (game != null)
            {
                bool tookL = !Same(game.ANBwristHudLeft, hudL), tookR = !Same(game.ANBwristHudRight, hudR);
                if (tookL) game.ANBwristHudLeft = hudL;
                if (tookR) game.ANBwristHudRight = hudR;
                if ((tookL || tookR) && U.Dbg) Log.Msg($"dagger grip clone took the game's {(tookL ? "left " : "")}{(tookR ? "right " : "")}wrist HUD slot - given back");
            }
            foreach (var w in go.GetComponentsInChildren<ANBWristHud>(true)) Object.Destroy(w);
            // The grip point carries the hand poser's preview hand (RightHand_Gloves_LOD0 ...) and its
            // wrist health display (UI, not a Renderer). Hidden on the prefab's own points, visible on
            // a runtime clone as a ghost glove on the arrow. The pose itself is data: destroy the
            // renderers and switch off every child except the path down to the HVRHandPoser.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Object.Destroy(r);
            var pg = go.GetComponent<HVRPosableGrabPoint>();
            if (!U.Alive(pg)) { Object.Destroy(go); Log.Warning("grip switch: cloned grip point has no HVRPosableGrabPoint"); return null; }
            pg.Grabbable = c.Arrow.Grabbable;
            var keep = new List<IntPtr>();
            var poser = pg.HandPoser;
            if (U.Alive(poser))
            {
                var t = poser.transform;
                for (int guard = 0; guard < 16 && U.Alive(t) && t.Pointer != go.transform.Pointer; guard++, t = t.parent) keep.Add(t.Pointer);
            }
            HideChildren(go.transform, keep);

            // Direction nock -> tip: from the grip towards the middle of the shaft mesh.
            var from = bt.position;
            var mid = ShaftMiddle(c.Arrow, from);
            float dx = mid.x - from.x, dy = mid.y - from.y, dz = mid.z - from.z;
            float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dl > 1e-3f)
            {
                float k = Settings.DaggerFromNock.Value / dl;
                go.transform.position = new Vector3(from.x + dx * k, from.y + dy * k, from.z + dz * k);
            }
            go.transform.rotation = bt.rotation;
            var tip = DaggerTip();
            string how;
            // Shaft along the knuckle line, through the curled fingers. The hand pose is fixed relative
            // to the grip point it holds, so the knuckle line measured in the HELD point's frame is
            // where it will be in the new point's frame too. R turns that line onto the arrow's tip
            // direction: holding a point rotated by R from the back grip puts the tip along it.
            // Direction: index->little finger = tip out past the little finger = Down in a fist.
            // (1.0.0 had this backwards: the "tip points up" report was made with the old
            // DaggerOrientation = KnucklesReverse, i.e. the little->index direction.)
            var knuckles = KnuckleLineInHeldPoint(c.Hand, out var span);
            if (knuckles is Vector3 kLocal && dl > 1e-3f)
            {
                if (tip == "Up") kLocal = new Vector3(-kLocal.x, -kLocal.y, -kLocal.z);
                var tipLocal = bt.InverseTransformDirection(new Vector3(dx / dl, dy / dl, dz / dl));
                var r = Quaternion.FromToRotation(kLocal, tipLocal);
                go.transform.Rotate(r.eulerAngles, Space.Self);
                float cos = Math.Clamp(kLocal.x * tipLocal.x + kLocal.y * tipLocal.y + kLocal.z * tipLocal.z, -1f, 1f);
                how = $"tip {tip} (shaft was {MathF.Acos(cos) * 180f / MathF.PI:0} deg off the knuckle line, knuckle span {span * 100:0.0} cm)";
            }
            else
            {
                go.transform.Rotate(new Vector3(1f, 0f, 0f), 180f, Space.Self);
                how = "no hand bones found - plain 180-degree flip";
            }
            if (U.Dbg) Log.Msg($"dagger grip built: {Settings.DaggerFromNock.Value * 100:0} cm from the nock, {how}");
            return pg;
        }

        // Centre of the shaft mesh ('Arrow01 (1)'). The prefab also carries hand-pose preview meshes
        // (RightHand_Gloves, RightHandFinalPalm...), which are skipped.
        static Vector3 ShaftMiddle(HVRArrow arrow, Vector3 fallback)
        {
            Vector3 mid = fallback; float best = -1f;
            foreach (var r in arrow.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                if (n.Contains("hand") || n.Contains("palm") || n.Contains("wrist") || n.Contains("glove")) continue;
                var s = r.bounds.size;
                float len = MathF.Max(s.x, MathF.Max(s.y, s.z)) + (n.Contains("arrow") ? 10f : 0f);
                if (len > best) { best = len; mid = r.bounds.center; }
            }
            return mid;
        }

        // World direction from a point at the nock end towards the tip.
        internal static Vector3 TipDirection(HVRArrow arrow, Vector3 from)
        {
            var mid = ShaftMiddle(arrow, from);
            float dx = mid.x - from.x, dy = mid.y - from.y, dz = mid.z - from.z;
            float dl = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            return dl > 1e-3f ? new Vector3(dx / dl, dy / dl, dz / dl) : arrow.transform.forward;
        }

        static bool Same(Object a, Object b) => (a == null ? IntPtr.Zero : a.Pointer) == (b == null ? IntPtr.Zero : b.Pointer);

        static void HideChildren(Transform t, List<IntPtr> keep)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                if (keep.Contains(ch.Pointer)) HideChildren(ch, keep);
                else if (ch.gameObject.activeSelf) ch.gameObject.SetActive(false);
            }
        }

        // Index-knuckle -> little-finger-knuckle direction, in the frame of the grip point the hand holds.
        static Vector3? KnuckleLineInHeldPoint(HVRHandGrabber hand, out float span)
        {
            span = 0f;
            try
            {
                var held = hand.PosableGrabPoint;
                var ph = hand._posableHand;
                if (!U.Alive(ph)) ph = hand.GetComponentInChildren<HVRPosableHand>(true);
                if (!U.Alive(held) || !U.Alive(ph)) return null;
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
            if (U.Alive(f.Root)) return f.Root.position;
            var bones = f.Bones;
            if (bones != null && bones.Count > 0 && bones[0] != null && U.Alive(bones[0].Transform)) return bones[0].Transform.position;
            return null;
        }

        // ---- dropping -------------------------------------------------------------------------
        // false = the arrow was removed.
        bool DropCarried(Carry c, string why, bool thrown = false)
        {
            SetCarry(null);
            var g = c.Arrow.Grabbable;
            string where = null;
            if (g.IsSocketed) where = "socketed";                       // never let it occupy a holster
            else if (!thrown && U.Alive(c.Bow) && U.Alive(c.Hand) && Zone(c.Bow, c.Hand, out _) is Vector3 z
                     && U.Dist(c.Arrow.transform.position, z) < Settings.QuiverRadius.Value) where = "back in the quiver";
            if (where != null)
            {
                if (U.Dbg) Log.Msg($"carried arrow {why}: {where} - removed");
                Object.Destroy(c.Arrow.gameObject);
                return false;
            }
            _dropped.Add(new Dropped { Arrow = c.Arrow, Bow = c.Bow, Loader = c.Loader, Since = U.Now });
            if (U.Dbg) Log.Msg($"carried arrow {why}: {(thrown ? "flying" : "dropped")}, removed after {Settings.DropLifetime.Value:0} s unless picked up");
            return true;
        }

        // A dropped quiver arrow: removed after the lifetime; picked up by a hand -> carried again.
        void UpdateDropped()
        {
            for (int i = 0; i < _dropped.Count; i++)
            {
                var d = _dropped[i];
                if (!U.Alive(d.Arrow)) { _dropped.RemoveAt(i--); continue; }
                var g = d.Arrow.Grabbable;
                if (g.IsSocketed) { Object.Destroy(d.Arrow.gameObject); _dropped.RemoveAt(i--); continue; }
                if (g.IsBeingHeld)
                {
                    d.Since = U.Now;
                    var hand = g.PrimaryGrabber?.TryCast<HVRHandGrabber>();
                    if (_carry == null && _pending == null && U.Alive(hand) && U.Alive(d.Bow))
                    {
                        _dropped.RemoveAt(i--);
                        SetCarry(new Carry { Arrow = d.Arrow, Hand = hand, Bow = d.Bow, Loader = d.Loader, Grabbed = true, Frame = U.Frame });
                        if (U.Dbg) Log.Msg($"dropped quiver arrow picked up by {U.Side(hand)} hand");
                    }
                    continue;
                }
                if (U.Now - d.Since > Settings.DropLifetime.Value) { Object.Destroy(d.Arrow.gameObject); _dropped.RemoveAt(i--); }
            }
        }

        void UpdatePendingNock()
        {
            var p = _pending;
            if (p == null) return;
            if (!U.Alive(p.Hand) || !U.Alive(p.Bow)) { _pending = null; return; }
            if (p.Grabbed)
            {
                // One frame after the string grab: did the loader put an arrow on the string?
                _pending = null;
                if (U.Dbg) Log.Msg($"string grabbed -> arrow {(U.Alive(p.Bow.Arrow) ? "nocked" : "NOT created")}");
                return;
            }
            var nock = p.Bow.NockGrabbable;
            if (nock.IsBeingHeld || U.Alive(p.Bow.Arrow)) { p.Grabbed = true; return; }
            if (++p.Tries > 5)
            {
                Log.Warning("string grab after nocking failed 5 times - grip the string to load");
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
            Log.Msg($"AmmoArrows {(_lastAmmo == int.MinValue ? "" : _lastAmmo + " -> ")}{a} (max {game.AmmoArrowsMax})");
            _lastAmmo = a;
        }

        static string Describe(HVRSocket s)
        {
            if (!U.Alive(s)) return "none";
            var sh = s.TryCast<HVRShoulderSocket>();
            string kind = sh != null ? $"shoulder L={sh.leftShoulder} R={sh.rightShoulder}" : s.GetIl2CppType().Name;
            return $"'{s.name}' ({kind})";
        }
    }
}
