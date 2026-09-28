using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using UnityEngine;

namespace BillyClubs
{
    // Diagnostic: why a holstered club sometimes can't be drawn (0.5.1 test: after F8, no grab reached the club
    // for 1.5 minutes). A hand that stays near a holstered club without drawing it gets one line per approach
    // with what HVR sees: hover target, whether the club is in the hand's grab bags, and the club's grab state.
    public partial class BillyClubsMod
    {
        const float WatchNear = 0.2f, WatchFar = 0.3f, WatchDwell = 0.5f;
        static HVRHandGrabber[] Hands;
        static readonly Dictionary<string, float> NearSince = new();
        static readonly HashSet<string> Reported = new();
        static float NextWatch;

        static void WatchDraws()
        {
            if (!Dbg || !Alive(Belt) || Time.time < NextWatch) return;
            NextWatch = Time.time + 0.1f;
            if (Hands == null || Hands.Length == 0 || !Alive(Hands[0])) Hands = Belt.root.GetComponentsInChildren<HVRHandGrabber>(true);
            foreach (var h in Hands)
            {
                if (!Alive(h)) continue;
                foreach (var s in Slots)
                {
                    var k = s.Club;
                    if (k == null || !Alive(k.Go) || k.In != s) continue;
                    string key = h.name + "/" + s.Name;
                    var t = k.Go.transform;
                    float half = Length.Value * 0.5f;
                    float d = DistToSegment(h.transform.position, t.TransformPoint(ClubCenter - ClubAxis * half), t.TransformPoint(ClubCenter + ClubAxis * half));
                    if (d > WatchFar) { NearSince.Remove(key); Reported.Remove(key); continue; }
                    if (d > WatchNear) continue;
                    if (!NearSince.TryGetValue(key, out var since)) { NearSince[key] = Time.time; continue; }
                    if (Time.time - since < WatchDwell || Reported.Contains(key)) continue;
                    Reported.Add(key);
                    Log.Msg($"draw check: '{h.name}' {d:0.00} m from the {s.Name} club for {Time.time - since:0.0} s, not drawn - {GrabState(h, k)}");
                }
            }
        }

        static string GrabState(HVRHandGrabber h, Club k)
        {
            string hover = "?", inBag = "?", grab = "?", cols = "?";
            try { hover = h.IsHovering ? $"'{Name(h.HoverTarget)}'" : "nothing"; } catch { }
            try
            {
                inBag = "no";
                var bags = h.GrabBags;
                for (int i = 0; bags != null && i < bags.Count; i++)
                {
                    var v = bags[i]?.ValidGrabbables;
                    for (int j = 0; v != null && j < v.Count; j++)
                        if (Alive(v[j]) && v[j].Pointer == k.Grab.Pointer) inBag = "yes";
                }
            }
            catch { }
            try { grab = $"canBeGrabbed {k.Grab.CanBeGrabbed}, lineOfSight {k.Grab.RequireLineOfSight}, enabled {k.Grab.enabled}"; } catch { }
            try
            {
                int on = 0, all = 0; var layers = new SortedSet<int>();
                foreach (var c in k.Go.GetComponentsInChildren<Collider>(true)) { all++; if (c.enabled && c.gameObject.activeInHierarchy) on++; layers.Add(c.gameObject.layer); }
                cols = $"{on}/{all} colliders on, layers {string.Join(",", layers)}";
            }
            catch { }
            return $"hand hovers {hover}, club in its grab bag: {inBag}; {grab}; {cols}; kinematic {(Alive(k.Rb) && k.Rb.isKinematic)}";
        }
    }
}
