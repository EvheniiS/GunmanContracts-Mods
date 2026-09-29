using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;

namespace WeaponFramework
{
    // Arsenal and stand events only. Holster logging lives in VR Holster Customization.
    static class ActionLog
    {
        const float LoadFold = 6f;   // seconds after a scene start that count as loading
        const float StepFold = 2f;   // extra seconds after each load step the game logs

        static float foldUntil;
        static bool foldPending;
        static int foldPut, foldTook;
        static string last;
        static float lastT;

        // Burst of stand events (real time, so slow motion doesn't stretch it).
        const float BurstGap = 0.2f;
        const int BurstMax = 3;
        static readonly List<string> burst = new();
        static int bPut, bTook;
        static float burstLast = -1f;

        static bool On => WeaponFrameworkMod.DebugOn;

        internal static void Scene()
        {
            foldUntil = Time.time + LoadFold;
            foldPending = true;
            foldPut = foldTook = 0;
        }

        // ANBGameLogic.LogLoadTime: the game is still loading (it builds the stands late, ~40 s into The Range).
        internal static void LoadStep()
        {
            foldUntil = Mathf.Max(foldUntil, Time.time + StepFold);
            foldPending = true;
        }

        internal static void Tick()
        {
            if (burst.Count > 0 && Time.realtimeSinceStartup - burstLast >= BurstGap) Flush();
            if (!foldPending || Time.time < foldUntil) return;
            foldPending = false;
            if (On && foldPut + foldTook > 0)
                Say($"after load: stands filled ({foldPut} put, {foldTook} taken)");
            foldPut = foldTook = 0;
        }

        static void Say(string s)
        {
            if (!On) return;
            if (s == last && Time.time - lastT < 0.5f) return;
            last = s; lastT = Time.time;
            WeaponFrameworkMod.Log.Msg(s);
        }

        static void Event(string line, int put, int took)
        {
            float now = Time.realtimeSinceStartup;
            if (burst.Count > 0 && now - burstLast >= BurstGap) Flush();
            burst.Add(line); bPut += put; bTook += took;
            burstLast = now;
        }

        static void Flush()
        {
            if (burst.Count <= BurstMax) foreach (var l in burst) Say(l);
            else Say($"{burst.Count} stand events at once ({bTook} taken, {bPut} put back) - scene change");
            burst.Clear(); bPut = bTook = 0;
        }

        // ---------- events ----------

        internal static void Retrieve(ANBGunwall w, IntPtr before, bool mod)
        {
            if (!On || !WeaponFrameworkMod.Alive(w)) return;
            var cur = w.currentWeapon;
            if (!WeaponFrameworkMod.Alive(cur) || cur.Pointer == before) return;
            int i = w.currentSpot, n = w.Items != null ? w.Items.Count : 0;
            string id = w.ItemsID != null && i >= 0 && i < w.ItemsID.Count ? w.ItemsID[i] : "?";
            if (burst.Count > 0) Flush();
            Say($"retrieve {WallName(w)} #{i + 1}/{n} '{id}'{(mod ? " (mod)" : "")}");
        }

        internal static void Stand(ANBGunwallSpot spot, bool took)
        {
            if (!On || !WeaponFrameworkMod.Alive(spot)) return;
            if (Time.time < foldUntil) { if (took) foldTook++; else foldPut++; return; }
            Event($"stand: {(took ? "took" : "put back")} '{SpotWeapon(spot)}'", took ? 0 : 1, took ? 1 : 0);
        }

        internal static void ModItemTaken(string id, HVRGrabbable g, HVRGrabberBase grabber)
        {
            if (!On) return;
            if (burst.Count > 0) Flush();
            Say($"stand: took '{id}' item '{Name(g)}' ({Name(grabber)})");
        }

        // ---------- names ----------

        static string SpotWeapon(ANBGunwallSpot s)
        {
            try
            {
                var p = s.WeaponPrefab;
                var t = WeaponFrameworkMod.Alive(p) ? p.GetComponent<ANBWeaponType>() : null;
                if (t != null && !string.IsNullOrEmpty(t.WeaponName)) return t.WeaponName;
                return s.SlotID ?? "?";
            }
            catch { return "?"; }
        }

        static string WallName(ANBGunwall w)
        {
            try
            {
                var big = ANBStaticGameManager.ANBmain?.ANBdataCollection?.gunWallLarge;
                if (WeaponFrameworkMod.Alive(big)) return big.Pointer == w.Pointer ? "big" : "pistol";
            }
            catch { }
            return w.gameObject.name;
        }

        static string Name(Component c) => WeaponFrameworkMod.Alive(c) ? c.gameObject.name : "?";
    }

    // ---------- hooks (logging only; they never change what the game does) ----------

    [HarmonyLib.HarmonyPatch(typeof(ANBGunwallSpot), nameof(ANBGunwallSpot.grabGunCall))]
    static class StandTakePatch
    {
        static void Postfix(ANBGunwallSpot __instance) { try { ActionLog.Stand(__instance, true); } catch { } }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGunwallSpot), nameof(ANBGunwallSpot.placeGunCall))]
    static class StandPutBackPatch
    {
        static void Postfix(ANBGunwallSpot __instance) { try { ActionLog.Stand(__instance, false); } catch { } }
    }

    // The holster mod owns blocking and undocking; this hook only logs wall takes.
    [HarmonyLib.HarmonyPatch(typeof(HVRGrabberBase), nameof(HVRGrabberBase.GrabGrabbable))]
    static class ModItemGrabPatch
    {
        static void Prefix(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            try { WeaponFrameworkMod.BeforeGrab(grabber, grabbable); } catch { }
        }
    }
}
