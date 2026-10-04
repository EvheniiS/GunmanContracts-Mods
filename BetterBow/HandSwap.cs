using System;
using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Shared;
using static BetterBow.BetterBowMod;

namespace BetterBow
{
    // Bow hand lock: while one hand holds the bow, the other hand can't take it, so reaching for the string
    // and gripping the riser by mistake no longer moves the bow to the drawing hand.
    //
    // The swap is HVR's own HoldType. The bow's grabbable is HVRHoldType.Swap: HVRHandGrabber.CheckSwapRelease
    // (@0x181d5bd10) makes the holding hand let go when another hand grabs a Swap item. HVRHandGrabber.CanGrab
    // (@0x181d5a2c0) refuses a OneHand item that already has a grabber, but only when the holder is a hand
    // (PrimaryGrabber not a socket), so taking the bow off a holster still works with either hand.
    // Enum values from the dump: OneHand 0, Swap 1, TwoHanded 2, ManyHands 3.
    internal static class HandSwap
    {
        // Bow grabbable pointer -> the hold type it had before this mod touched it.
        static readonly Dictionary<IntPtr, HVRHoldType> Original = new();

        internal static void OnScene() => Original.Clear();

        internal static void Update()
        {
            bool allow = Settings.BowHandSwap.Value;
            foreach (var l in Loaders)
            {
                var g = l.bow.Grabbable;
                if (!U.Alive(g)) continue;
                if (!Original.TryGetValue(g.Pointer, out var orig))
                {
                    orig = g.HoldType;
                    Original[g.Pointer] = orig;
                    if (U.Dbg) Log.Msg($"bow hold type: {orig}");
                }
                var want = allow || orig != HVRHoldType.Swap ? orig : HVRHoldType.OneHand;
                if (g.HoldType != want) g.HoldType = want;
            }
        }
    }
}
