using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MeleeUnlocks.MeleeUnlocksMod), "Melee Unlocks", "0.2.1", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]

namespace MeleeUnlocks
{
    // Unlocks the game's knives on The Range knife wall, as if you had made the kills with them.
    //
    // How the game does it (game 0.3.1.1, il2cpp_tools):
    // - A knife unlocks by kills: ANBKnife.registerKnifeKill counts killsNeededToUnlock down and at 0 calls
    //   ANBDataCollection.makePurchase(knifeID, 0). makePurchase adds the id to purchasedContentWeapons (skipping
    //   duplicates), subtracts the price (0) and saves (ANBSaveData.SavePurchases). This mod makes that same call.
    // - The wall: each spot's checkKeepExec coroutine (started by name from initSlot2, and from checkKeep) sets the
    //   knife's ANBWeaponType.PurchaseID to its knifeID and asks ANBDataCollection.checkPurchaseDataWeapon. Not
    //   purchased and price > 0: the knife on the spot is destroyed. So the unlock is a prefix on that check, which
    //   every path (wall, loadout, contract holsters) goes through. (0.1.0 patched checkKeep, which the knife spots
    //   don't use, and never ran.)
    // - Changing the setting in The Range: locked knives are already destroyed, so their spots are refilled with the
    //   spot's own initSlot() (Instantiate WeaponPrefab, then the same check).
    // - makePurchase returns without doing anything while ANBGameLogic.gameStarted is false, which it is while The
    //   Range loads and the wall checks its spots (0.2.0 logged "unlocked" there and nothing happened). So before the
    //   game has started the id is only added to purchasedContentWeapons in memory (the wall check passes and the
    //   knife stays), and written through makePurchase once gameStarted is true.
    // - The double katana (Knife-Katana-double, knifeID "Katana2") has a wall spot but no contract map places one,
    //   so it can't be unlocked by playing.
    // Unlocks are written to the game save and stay after the mod is removed; Steam's Knifecollector stat is not added.
    public class MeleeUnlocksMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        static MelonPreferences_Entry<string> Unlock;
        static bool refill;
        static readonly List<string> pending = new();   // unlocked in memory, not saved yet
        static float pendingCheckAt;
        static float summaryAt = -1f;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var cat = MelonPreferences.CreateCategory("MeleeUnlocks", "Melee Unlocks");
            Unlock = cat.CreateEntry("Unlock", "Katana2", description: "Which knives on The Range knife wall to unlock, as if you had made the kills: Katana2 (the double katana, a second katana for dual wielding; no contract places one), All (every knife on the wall) or Off (nothing). Unlocks are saved in the game save and stay unlocked.");
            Unlock.OnEntryValueChanged.Subscribe((_, _) => refill = true);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            summaryAt = sceneName.StartsWith("The_Range", StringComparison.OrdinalIgnoreCase) ? Time.unscaledTime + 15f : -1f;
        }

        public override void OnUpdate()
        {
            if (refill) { refill = false; Refill(); }
            if (pending.Count > 0 && Time.unscaledTime >= pendingCheckAt) { pendingCheckAt = Time.unscaledTime + 1f; SavePending(); }
            if (summaryAt > 0 && Time.unscaledTime >= summaryAt) { summaryAt = -1f; Summary(); }
        }

        internal static bool Alive(UnityEngine.Object o)
        {
            try { return o != null && !o.WasCollected && o; } catch { return false; }
        }

        static bool Wanted(string id)
        {
            var v = (Unlock.Value ?? "").Trim();
            if (v.Equals("All", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("Off", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var part in v.Split(','))
                if (part.Trim().Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static ANBDataCollection Data()
        {
            var game = ANBStaticGameManager.ANBmain;
            var dc = Alive(game) ? game.ANBdataCollection : null;
            return Alive(dc) ? dc : null;
        }

        static string KnifeId(GameObject go)
        {
            var knife = Alive(go) ? go.GetComponent<ANBKnife>() : null;
            return Alive(knife) ? knife.knifeID : null;
        }

        static bool GameStarted()
        {
            var game = ANBStaticGameManager.ANBmain;
            return Alive(game) && game.gameStarted;
        }

        // Unlocks id if wanted and not owned yet. True if it is owned afterwards.
        static bool Ensure(ANBDataCollection dc, string id, string what)
        {
            var owned = dc.purchasedContentWeapons;
            if (owned == null || string.IsNullOrEmpty(id)) return false;
            if (owned.Contains(id)) return true;
            if (!Wanted(id)) return false;
            if (GameStarted())
            {
                dc.makePurchase(id, 0f);
                if (owned.Contains(id)) { Log.Msg($"unlocked '{id}' ({what}), saved to the game's purchases"); return true; }
            }
            owned.Add(id);
            if (!pending.Contains(id)) pending.Add(id);
            Log.Msg($"unlocked '{id}' ({what}) in memory; saved once the game has started");
            return true;
        }

        // Writes in-memory unlocks to the save through the game's own makePurchase, once its gate is open.
        static void SavePending()
        {
            try
            {
                var dc = Data();
                var owned = dc != null ? dc.purchasedContentWeapons : null;
                if (owned == null || !GameStarted()) return;
                foreach (var id in pending.ToArray())
                {
                    while (owned.Contains(id)) owned.Remove(id);
                    dc.makePurchase(id, 0f);
                    if (owned.Contains(id)) { pending.Remove(id); Log.Msg($"saved '{id}' to the game's purchases"); }
                    else { owned.Add(id); Log.Warning($"'{id}': the game did not save it, retrying"); pendingCheckAt = Time.unscaledTime + 30f; }
                }
            }
            catch (Exception e) { Log.Warning($"save: {e.Message}"); }
        }

        [HarmonyPatch(typeof(ANBDataCollection), nameof(ANBDataCollection.checkPurchaseDataWeapon))]
        static class CheckPurchasePatch
        {
            static void Prefix(ANBDataCollection __instance, ANBWeaponType tmpWPT)
            {
                try
                {
                    if (!Alive(tmpWPT) || string.IsNullOrEmpty(tmpWPT.PurchaseID)) return;
                    if (KnifeId(tmpWPT.gameObject) == null) return;   // guns and attachments: not ours
                    Ensure(__instance, tmpWPT.PurchaseID, tmpWPT.gameObject.name.Replace("(Clone)", ""));
                }
                catch (Exception e) { Log.Warning($"purchase check: {e.Message}"); }
            }
        }

        // After a setting change: unlock and refill wall spots whose knife the game already removed.
        static void Refill()
        {
            try
            {
                var dc = Data();
                var spots = dc != null ? dc.allKnifeSpots : null;
                if (spots == null) return;   // not in The Range: the next wall check applies it
                var refilled = new List<string>();
                foreach (var spot in spots)
                {
                    if (!Alive(spot) || Alive(spot.mygun)) continue;
                    var id = KnifeId(spot.WeaponPrefab);
                    if (id == null || !Wanted(id)) continue;
                    if (!Ensure(dc, id, spot.WeaponPrefab.name)) continue;
                    spot.initSlot();
                    refilled.Add(id);
                }
                if (refilled.Count > 0) Log.Msg($"wall refilled: {string.Join(", ", refilled)}");
            }
            catch (Exception e) { Log.Warning($"refill: {e.Message}"); }
        }

        // One line per visit to The Range: what the knife wall holds and what is still locked.
        static void Summary()
        {
            try
            {
                var dc = Data();
                var spots = dc != null ? dc.allKnifeSpots : null;
                if (spots == null) { Log.Msg("knife wall: not found"); return; }
                var owned = dc.purchasedContentWeapons;
                var shown = new List<string>();
                var locked = new List<string>();
                foreach (var spot in spots)
                {
                    if (!Alive(spot)) continue;
                    var id = KnifeId(spot.WeaponPrefab) ?? spot.SlotID;
                    if (owned != null && owned.Contains(id) || Alive(spot.mygun)) shown.Add(id);
                    else locked.Add(id);
                }
                Log.Msg($"knife wall (Unlock = {Unlock.Value}): {shown.Count} unlocked, locked: {(locked.Count > 0 ? string.Join(", ", locked) : "none")}");
            }
            catch (Exception e) { Log.Warning($"summary: {e.Message}"); }
        }
    }
}
