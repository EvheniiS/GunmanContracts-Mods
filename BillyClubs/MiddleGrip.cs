using Il2CppHurricaneVR.Framework.Core.HandPoser;
using MelonLoader;
using UnityEngine;

namespace BillyClubs
{
    // One grip: the hand always takes the club at its grip end. The crowbar has a second grab point mid-shaft
    // (GrabPoint_Additional, x 0.05), which is why a grab near the middle held the club there.
    //
    // It is switched OFF, never destroyed: 0.6.0 DestroyImmediate'd it and every grab then threw in
    // HVRGrabbable.GrabPointValid (HVR keeps grab points in more than GrabPoints). GrabPointValid (@0x181ce8d10) rejects
    // a point whose GameObject is inactive, whose component is disabled, or whose LeftHand (0x34) / RightHand (0x35)
    // flag is off, so an inactive point is simply never chosen.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<bool> MiddleGrip;
        const string BasePointPath = "Base - Grab/GrabPoints/GrabPoint_Base", MiddlePointPath = "Base - Grab/GrabPoints/GrabPoint_Additional";

        static void InitMiddleGripPref(MelonPreferences_Category c)
        {
            MiddleGrip = c.CreateEntry("MiddleGrip", false, description: "Keep the crowbar's second grab point in the middle of the club. Off = the hand always takes the club at its grip end. Applies to clubs not in a hand at once, and to new ones.");
            MiddleGrip.OnEntryValueChanged.Subscribe((_, on) =>
            {
                ApplyMiddleGrip(Template);
                int n = 0;
                foreach (var k in Clubs) if (!k.Held) { ApplyMiddleGrip(k.Go); n++; }
                Log.Msg($"middle grip {(on ? "on" : "off")}: {n} club(s) updated");
            });
        }

        // Called on the template (logs the grab points once) and on every new club.
        static void ApplyMiddleGrip(GameObject club, bool log = false)
        {
            if (!Alive(club)) return;
            var root = club.transform;
            var mid = root.Find(MiddlePointPath);
            var bas = root.Find(BasePointPath);
            if (mid == null) { if (log) Log.Warning("no middle grab point on the crowbar - nothing to switch off"); return; }
            var mp = mid.GetComponentInChildren<HVRPosableGrabPoint>(true);
            var bp = bas != null ? bas.GetComponentInChildren<HVRPosableGrabPoint>(true) : null;
            if (log) Log.Msg($"grab points: base {Flags(bp)}; middle {Flags(mp)}");
            bool on = MiddleGrip.Value;
            if (!on && bp != null && mp != null)
            {
                // Whatever the middle point allowed, the grip end now allows too (a distance grab, either hand).
                bp.IsForceGrabbable |= mp.IsForceGrabbable;
                bp.LeftHand |= mp.LeftHand;
                bp.RightHand |= mp.RightHand;
            }
            foreach (var p in mid.GetComponentsInChildren<HVRPosableGrabPoint>(true)) p.enabled = on;
            mid.gameObject.SetActive(on);
        }

        static string Flags(HVRPosableGrabPoint p) => p == null ? "none"
            : $"'{p.name}' force {(p.IsForceGrabbable ? "yes" : "no")}, hands {(p.LeftHand ? "L" : "")}{(p.RightHand ? "R" : "")}, one hand only {(p.OneHandOnly ? "yes" : "no")}";
    }
}
