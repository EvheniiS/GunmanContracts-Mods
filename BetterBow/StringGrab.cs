using System;
using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Weapons.Bow;
using static BetterBow.BetterBowMod;

namespace BetterBow
{
    // "Have to grip twice to get the next arrow" (from reading the game's HurricaneVR code):
    //  * HVRHandGrabber.CheckGrab only grabs on the grip-PRESS frame (IsGripGrabActivated).
    //  * HVRHandGrabber.CanHover refuses any NEW hover target while grip is HELD.
    //  So a grip press that lands a moment before the bow string becomes the hand's hover target
    //  is lost, and hover stays locked until the grip is released and pressed again.
    //  Fix 1: buffer the press - if grip is held, the hand is empty, the press was recent and the
    //         hand is near the string of a bow held in the other hand, call TryGrab on the string
    //         (the game's own path: CanGrab checks, grab events, HVRArrowLoader spawn).
    //  Fix 2: HVRPhysicsBow clears the previous arrow only after the next FixedUpdate, and
    //         HVRArrowLoader spawns a new one only if the slot is empty. If the string was grabbed
    //         inside that window, re-run the loader once the slot is empty.
    internal sealed class StringGrab
    {
        class HandState { public bool GripWasHeld; public double PressTime = -1; public long PressFrame; public bool Used; }
        class BowState { public bool NockWasHeld; public double GrabTime; public bool Retried; }

        readonly Dictionary<IntPtr, HandState> _handState = new();
        readonly Dictionary<IntPtr, BowState> _bowState = new();

        internal void OnScene()
        {
            _handState.Clear();
            _bowState.Clear();
        }

        internal void Update()
        {
            if (!Settings.StringGrab.Value) return;
            foreach (var hand in Hands)
            {
                try { UpdateHand(hand); }
                catch (Exception e) { if (U.Dbg) Log.Warning($"string grab: {e.GetType().Name}: {e.Message}"); }
            }
            if (!Settings.RetrySpawn.Value) return;
            foreach (var loader in Loaders)
            {
                try { UpdateBowSpawn(loader); }
                catch (Exception e) { if (U.Dbg) Log.Warning($"spawn retry: {e.GetType().Name}: {e.Message}"); }
            }
        }

        // ---- Fix 1: buffered grip press near the string -----------------------------------------
        void UpdateHand(HVRHandGrabber hand)
        {
            IntPtr hp = hand.Pointer;
            if (!_handState.TryGetValue(hp, out var st)) _handState[hp] = st = new HandState();

            bool held = hand.IsGripGrabActive;
            if (held && !st.GripWasHeld) { st.PressTime = U.Now; st.PressFrame = U.Frame; st.Used = false; }
            st.GripWasHeld = held;

            if (!held || st.Used || st.PressTime < 0) return;
            if (U.Frame == st.PressFrame) return;                       // give the game its own frame first
            if (U.Now - st.PressTime > Settings.StringGrabBuffer.Value) return;
            if (hand.IsGrabbing) { st.Used = true; return; }           // the game grabbed something itself

            var palm = hand.Palm;
            var handPos = U.Alive(palm) ? palm.position : hand.transform.position;

            HVRPhysicsBow bestBow = null;
            float bestDist = Settings.StringGrabRadius.Value;
            foreach (var loader in Loaders)
            {
                var bow = loader.bow;
                var nock = bow.NockGrabbable;
                if (!U.Alive(nock) || nock.IsBeingHeld) continue;
                // BowHand is set by HVRBowBase.OnHandGrabbed and cleared on release, so it means
                // "held in a hand" (Grabbable.IsBeingHeld would also count holster sockets).
                var bowHand = bow.BowHand;
                if (!U.Alive(bowHand) || bowHand.Pointer == hp) continue;
                float dist = U.Dist(handPos, nock.transform.position);
                if (dist < bestDist) { bestDist = dist; bestBow = bow; }
            }
            if (bestBow == null) return;

            // The bow learns its NockHand in BeforeNockHovered; this grab may never have hovered.
            bestBow.NockHand = hand;
            bool ok = hand.TryGrab(bestBow.NockGrabbable, false);
            st.Used = ok;
            if (U.Dbg)
                Log.Msg($"assisted string grab {(ok ? "OK" : "refused by CanGrab")} " +
                        $"({(U.Now - st.PressTime) * 1000:0} ms after press, {bestDist * 100:0.0} cm)");
        }

        // ---- Fix 2: arrow spawn retry -------------------------------------------------------------
        void UpdateBowSpawn(HVRArrowLoader loader)
        {
            var bow = loader.bow;
            IntPtr bp = bow.Pointer;
            if (!_bowState.TryGetValue(bp, out var st)) _bowState[bp] = st = new BowState();

            var nock = bow.NockGrabbable;
            if (!U.Alive(nock)) return;
            bool held = nock.IsBeingHeld;
            if (held && !st.NockWasHeld) { st.GrabTime = U.Now; st.Retried = false; }
            st.NockWasHeld = held;
            if (!held || st.Retried || U.Now - st.GrabTime > Settings.RetryWindow.Value) return;
            if (U.Alive(bow.Arrow)) return;                             // arrow is there - nothing to do

            var hand = nock.PrimaryGrabber?.TryCast<HVRHandGrabber>();  // PrimaryGrabber may be a socket
            if (!U.Alive(hand)) hand = bow.NockHand;
            if (!U.Alive(hand)) return;
            st.Retried = true;
            loader.OnStringGrabbed(hand, nock);                         // game's own spawn path (checks ammo)
            if (U.Dbg) Log.Msg($"arrow spawn retry -> arrow {(U.Alive(bow.Arrow) ? "created" : "not created (no ammo?)")}");
        }
    }
}
