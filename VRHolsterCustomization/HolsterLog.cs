using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Weapons.Guns;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    static class HolsterLog
    {
        static readonly SortedDictionary<string, string> loadout = new();
        static readonly List<string> burst = new();
        static float foldUntil, burstAt, lastAt;
        static float saveAt = float.PositiveInfinity;
        static bool pending;
        static string last;

        internal static bool Loading => Time.time < foldUntil;
        internal static void Scene()
        {
            foldUntil = Time.time + 6f;
            pending = true;
            loadout.Clear();
            burst.Clear();
            saveAt = float.PositiveInfinity;
        }

        internal static void LoadStep()
        {
            foldUntil = Mathf.Max(foldUntil, Time.time + 2f);
            pending = true;
        }

        internal static void Tick()
        {
            if (burst.Count > 0 && Time.realtimeSinceStartup - burstAt >= 0.2f) Flush();
            if (Time.time >= saveAt && !Loading)
            {
                saveAt = float.PositiveInfinity;
                try
                {
                    var game = Object.FindObjectOfType<ANBGameLogic>();
                    if (VRHolsterCustomizationMod.Alive(game) && game.gameStarted && game.IsRangeScene)
                    {
                        game.SaveContractHolsters();
                        if (VRHolsterCustomizationMod.DebugOn) Say("Range loadout saved after holster change");
                    }
                }
                catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"could not save game loadout: {e.Message}"); }
            }
            if (!pending || Loading) return;
            pending = false;
            if (loadout.Count == 0) return;
            var names = new List<string>();
            foreach (var pair in loadout) names.Add($"{pair.Key} {pair.Value}");
            Say("after load: loadout " + string.Join(", ", names));
        }

        internal static void Holster(string side, string item, bool into)
        {
            side = string.IsNullOrEmpty(side) || side == "none" ? "stand" : side;
            if (into) loadout[side] = item;
            else { if (item == null) loadout.TryGetValue(side, out item); loadout.Remove(side); }
            if (Loading) return;
            Event($"holster {side}: '{item ?? "?"}' {(into ? "in" : "out")}");
        }

        internal static void SaveSoon()
        {
            var game = ANBStaticGameManager.ANBmain;
            // SaveContractHolsters writes the persistent loadout, not merely the current contract.
            // A knife put away in combat must not erase the prepared pistol slot while that pistol is drawn.
            if (!Loading && VRHolsterCustomizationMod.Alive(game) && game.gameStarted && game.IsRangeScene)
                saveAt = Time.time + 1f;
        }

        internal static void ModHolster(string slot, string item, bool into) =>
            Event($"holster {slot}: '{item}' {(into ? "in" : "out")} (mod)");

        static void Event(string line)
        {
            if (!VRHolsterCustomizationMod.DebugOn) return;
            if (burst.Count > 0 && Time.realtimeSinceStartup - burstAt >= 0.2f) Flush();
            burst.Add(line);
            burstAt = Time.realtimeSinceStartup;
        }

        static void Flush()
        {
            if (burst.Count < 4) foreach (var line in burst) Say(line);
            else Say($"{burst.Count} holster events at once - scene change");
            burst.Clear();
        }

        static void Say(string line)
        {
            if (!VRHolsterCustomizationMod.DebugOn || line == last && Time.time - lastAt < 0.5f) return;
            last = line; lastAt = Time.time;
            VRHolsterCustomizationMod.Log.Msg(line);
        }

        internal static string Gun(ANBHVRGunBase gun)
        {
            try
            {
                if (!VRHolsterCustomizationMod.Alive(gun)) return "?";
                var type = gun.ANBwpt;
                return VRHolsterCustomizationMod.Alive(type) && !string.IsNullOrEmpty(type.WeaponName) ? type.WeaponName : gun.gameObject.name;
            }
            catch { return "?"; }
        }

        internal static string Knife(ANBKnife knife)
        {
            try
            {
                if (!VRHolsterCustomizationMod.Alive(knife)) return "?";
                var type = knife.WPT;
                if (!VRHolsterCustomizationMod.Alive(type)) type = knife.GetComponent<ANBWeaponType>();
                if (!VRHolsterCustomizationMod.Alive(type)) type = knife.GetComponentInParent<ANBWeaponType>();
                if (VRHolsterCustomizationMod.Alive(type) && !string.IsNullOrEmpty(type.WeaponName)) return type.WeaponName;
                return string.IsNullOrEmpty(knife.knifeID) ? knife.gameObject.name.Replace("(Clone)", "") : knife.knifeID;
            }
            catch { return "?"; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.LogLoadTime))]
    static class HolsterLoadPatch
    {
        static void Postfix() { try { HolsterLog.LoadStep(); } catch { } }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.holsterGun))]
    static class HolsterGunPatch
    {
        static void Postfix(string side, ANBHVRGunBase tmpGBS) { try { HolsterLog.Holster(side, HolsterLog.Gun(tmpGBS), true); HolsterLog.SaveSoon(); } catch { } }
    }
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.unholsterGun))]
    static class UnholsterGunPatch
    {
        static void Postfix(string side, ANBHVRGunBase tmpGBS) { try { HolsterLog.Holster(side, HolsterLog.Gun(tmpGBS), false); } catch { } }
    }
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.holsterKnife))]
    static class HolsterKnifePatch
    {
        static void Postfix(string side, ANBKnife knf) { try { HolsterLog.Holster("knife-" + side, HolsterLog.Knife(knf), true); HolsterLog.SaveSoon(); } catch { } }
    }
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.unholsterKnife))]
    static class UnholsterKnifePatch
    {
        static void Postfix(string side) { try { HolsterLog.Holster("knife-" + side, null, false); } catch { } }
    }
}
