using System;
using System.Collections.Generic;
using System.Text;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;

namespace VRHolsterCustomization
{
    // The game's own blades (katanas, knives) in the back slots (0.3.0).
    //
    // Game facts (il2cpp_tools, Oct 3 2026):
    // - A katana is an ANBKnife with a knifeID, like every knife. Arrows (isArrow) and pens (isPen) are ANBKnife too and
    //   are never accepted (arrows are drawn over the shoulder).
    // - ANBKnife.checkAutoReturn: while the game runs, a knife that is neither held nor socketed counts
    //   autoReturnAfterCurrent (set on release) down and then returnKnife() sends it to its holster or wall spot. A knife
    //   docked on our back is neither, so the timer is kept at 0 while it is there.
    // - ANBGameLogic.LoadContractHolsterKnife takes the knife from the scene's wall spot (ANBdataCollection.allKnifeSpots,
    //   SlotID == knifeID, spot.mygun), releases it from the spot's Hanger socket and puts it in the holster. Knives are
    //   moved, never instantiated, so a restore onto the back does the same.
    //
    // One HolsterKind per knifeID ("Knife-<id>"), registered when a hand first takes that knife, or at start for ids
    // already saved in SavedBackHolsters.
    internal static class BackBlades
    {
        static MelonPreferences_Entry<string> Mode;
        static readonly HashSet<string> registered = new();
        // Per object: its clean knife id and whether it is a katana; null = not a back-slot blade. Cleared per scene.
        static readonly Dictionary<IntPtr, Info> info = new();
        sealed class Info { public string Id; public bool Katana; }
        static float spotsLogAt = float.PositiveInfinity;

        internal static void Init(MelonPreferences_Category cat, string saved)
        {
            Mode = cat.CreateEntry("BackBlades", "All", description: "Which of the game's blades fit the back slots: All (katanas and knives), Katana (katanas only) or Off (none). A blade on your back is carried into contracts like a mod item.");
            foreach (var part in (saved ?? "").Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[1].StartsWith("Knife-")) Register(kv[1].Substring(6));
            }
        }

        internal static void Scene()
        {
            info.Clear();
            spotsLogAt = Time.time + 10f;
        }

        internal static void Tick()
        {
            if (Time.time < spotsLogAt) return;
            spotsLogAt = float.PositiveInfinity;
            if (!VRHolsterCustomizationMod.DebugOn) return;
            try
            {
                var spots = Spots();
                var ids = new List<string>();
                if (spots != null) foreach (var s in spots) if (VRHolsterCustomizationMod.Alive(s)) ids.Add(s.SlotID);
                VRHolsterCustomizationMod.Log.Msg($"knife spots: {ids.Count} ({string.Join(", ", ids)})");
            }
            catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"knife spots: {e.Message}"); }
        }

        // A hand takes a knife: the back slots watch it for the release.
        internal static void OnGrab(HVRGrabberBase grabber, HVRGrabbable g)
        {
            if (Mode == null || Mode.Value == "Off" || !VRHolsterCustomizationMod.Alive(g) || grabber == null || grabber.TryCast<HVRHandGrabber>() == null) return;
            var knife = g.GetComponentInParent<ANBKnife>();
            if (!VRHolsterCustomizationMod.Alive(knife)) return;
            // Taken out of a socket (the belt knife holster, its wall spot), not from our back or caught in the air.
            // HVR's IsSocketed, not ANBKnife.socketed/inWall: grabKnife never clears those, so they go stale.
            var main = knife.GrabbableScript;
            if (g.IsSocketed || (VRHolsterCustomizationMod.Alive(main) && main.IsSocketed)) Holsters.ForgetHome(knife.gameObject);
            var i = InfoOf(knife.gameObject);
            if (i == null || !Fits(i)) return;
            Register(i.Id);
            Holsters.Watch(knife.gameObject);
        }

        // Every tick while a blade is on the back: no auto-return to its holster or wall spot.
        internal static void KeepDocked(GameObject item)
        {
            var knife = item.GetComponent<ANBKnife>();
            if (knife != null && knife.autoReturnAfterCurrent != 0f) knife.autoReturnAfterCurrent = 0f;
        }

        // ANBKnife.returnKnife (auto-return after a throw or a drop): a blade drawn from the back goes back there.
        // returnKnife itself unstabs (HVRStabber.ForceUnstab(false)) before its holster grab; so do we.
        internal static bool ReturnToBack(ANBKnife knife)
        {
            if (Mode == null || Mode.Value == "Off" || !VRHolsterCustomizationMod.Alive(knife) || knife.isHeld) return false;
            var go = knife.gameObject;
            if (InfoOf(go) == null) return false;
            var stabber = knife.Stabber;
            if (VRHolsterCustomizationMod.Alive(stabber) && stabber.IsStabbing) stabber.ForceUnstab(false);
            knife.abortHoming();
            if (!Holsters.ReturnHome(go)) return false;
            knife.autoReturnAfterCurrent = 0f;
            return true;
        }

        static void Register(string id)
        {
            if (string.IsNullOrEmpty(id) || !registered.Add(id)) return;
            Holsters.RegisterKind(new HolsterKind
            {
                Id = "Knife-" + id,
                Blade = true,
                IsMine = go => { var i = InfoOf(go); return i != null && i.Id == id && Fits(i); },
                Spawn = () => TakeFromSpot(id),
            });
        }

        static bool Fits(Info i) => Mode.Value == "All" || (Mode.Value == "Katana" && i.Katana);

        static Info InfoOf(GameObject go)
        {
            if (!VRHolsterCustomizationMod.Alive(go)) return null;
            if (info.TryGetValue(go.Pointer, out var i)) return i;
            i = null;
            try
            {
                var knife = go.GetComponent<ANBKnife>();
                if (knife != null && !knife.isArrow && !knife.isPen)
                {
                    var id = Clean(knife.knifeID);
                    if (id.Length > 0)
                        i = new Info { Id = id, Katana = id.IndexOf("katana", StringComparison.OrdinalIgnoreCase) >= 0
                            || go.name.IndexOf("katana", StringComparison.OrdinalIgnoreCase) >= 0 };
                }
            }
            catch { }
            info[go.Pointer] = i;
            return i;
        }

        // The knife hanging on this scene's wall spot for the id, freed from the spot's socket; null if there is none
        // or it is already in use (in a holster, in a hand).
        static GameObject TakeFromSpot(string id)
        {
            var spots = Spots();
            if (spots == null) return null;
            foreach (var spot in spots)
            {
                if (!VRHolsterCustomizationMod.Alive(spot) || Clean(spot.SlotID) != id) continue;
                var go = spot.mygun;
                if (!VRHolsterCustomizationMod.Alive(go) || Holsters.Holds(go)) continue;
                var hanger = spot.Hanger;
                HVRGrabbable onWall = null;
                try { onWall = VRHolsterCustomizationMod.Alive(hanger) ? hanger.GrabbedTarget : null; } catch { }
                if (VRHolsterCustomizationMod.Alive(onWall) && onWall.transform.IsChildOf(go.transform)) hanger.ForceRelease();
                else if (Dock.IsHeld(go)) continue;
                return go;
            }
            return null;
        }

        static Il2CppSystem.Collections.Generic.List<ANBGunwallSpot> Spots()
        {
            var game = ANBStaticGameManager.ANBmain;
            var dc = VRHolsterCustomizationMod.Alive(game) ? game.ANBdataCollection : null;
            return VRHolsterCustomizationMod.Alive(dc) ? dc.allKnifeSpots : null;
        }

        // Letters, digits, '-' and '_' (the id goes into "L=kind;R=kind").
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new StringBuilder(s.Length);
            foreach (var c in s) if (char.IsLetterOrDigit(c) || c == '-' || c == '_') b.Append(c);
            return b.ToString();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBKnife), nameof(ANBKnife.returnKnife))]
    static class KnifeReturnPatch
    {
        static bool Prefix(ANBKnife __instance)
        {
            try { return !BackBlades.ReturnToBack(__instance); }
            catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"knife return: {e.Message}"); return true; }
        }
    }

    // The player put the knife in a belt knife holster: from now on the game's return (to that holster) applies.
    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.holsterKnife))]
    static class KnifeHolsteredPatch
    {
        static void Postfix(ANBKnife knf)
        {
            try { if (VRHolsterCustomizationMod.Alive(knf)) Holsters.ForgetHome(knf.gameObject); } catch { }
        }
    }
}
