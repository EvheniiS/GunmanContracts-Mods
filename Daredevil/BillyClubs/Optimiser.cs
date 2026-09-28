using Il2CppHurricaneVR.Framework.Core;
using UnityEngine;

namespace BillyClubs
{
    // The game's performance optimiser switches club grabbables off (found Sep 28 2026, arsenal wall test).
    //
    // GD_HVROptimiser (Il2CppGD_Game) collects every ANBHVRGrabbable / HVRGrabbable / HVRGrabbableBag ONCE, with
    // FindObjectsByType(FindObjectsInactive.Include), and switches each one off at once (AddToGrabbablesList<T>).
    // Its Update then re-enables, round-robin, only the ones within _grabbableRange and in front of _player.
    // - The club template sits in an inactive holder ~20 m away: collected, switched off, never back on. Every club
    //   Instantiate'd from it afterwards starts with its grabbable OFF and isn't in the list, so nothing turns it on:
    //   the wall club, and the old "F8-spawned clubs can't be drawn for a minute" (Billy Clubs 0.5-0.6 notes).
    // - A club that already existed at collection time is in the list, so it switches off whenever it is behind you
    //   or out of range: the belt club that sometimes wouldn't draw ("enabled False while holstered").
    // Fix: every club's grabbables are switched on at spawn and taken out of the optimiser's list. Checked once a
    // second, since the list is filled some time after a scene load (not at a hookable call: it's inlined in Update).
    public partial class BillyClubsMod
    {
        static float NextOptimiserCheck;
        static int OptimiserFreed;

        static void FreeFromOptimiser(GameObject go)
        {
            if (!Alive(go)) return;
            Il2Cpp.GD_HVROptimiser opt = null;
            try { opt = Il2Cpp.GD_HVROptimiser.instance; } catch { }
            var list = Alive(opt) ? opt._grabbables : null;
            foreach (var g in go.GetComponentsInChildren<HVRGrabbable>(true))
            {
                if (!g.enabled) g.enabled = true;
                if (list != null && list.Remove(g)) OptimiserFreed++;
            }
        }

        // Once a second: clubs (and the template, so later copies start enabled) out of the optimiser's reach.
        static void WatchOptimiser()
        {
            if (Time.time < NextOptimiserCheck) return;
            NextOptimiserCheck = Time.time + 1f;
            int before = OptimiserFreed;
            if (Alive(Template)) FreeFromOptimiser(Template);
            foreach (var k in Clubs) if (Alive(k.Go)) FreeFromOptimiser(k.Go);
            if (Dbg && OptimiserFreed > before) Log.Msg($"took {OptimiserFreed - before} club grabbable(s) out of the game's grabbable optimiser (it switches them off)");
        }
    }
}
