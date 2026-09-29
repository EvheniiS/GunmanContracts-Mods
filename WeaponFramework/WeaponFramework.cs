using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(WeaponFramework.WeaponFrameworkMod), "Weapon Framework", "0.3.0", "Evgeeso")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]
[assembly: MelonAdditionalDependencies("VRHolsterCustomization")]

namespace WeaponFramework
{
    // A mod's own weapon on the game's arsenal panel (The Range, "Big Guns" wall, next to the rifles, shotguns and the bow).
    public sealed class ArsenalWeapon
    {
        // Unique id, letters and digits (it becomes the wall entry id and the slot name).
        public string Id;
        // Name on the panel. The game prints it upper case.
        public string DisplayName;
        public string Description = "";
        // Big picture on the panel. The game's own are white shaded renders on a transparent background, about 2.15:1
        // (the bow's is 1364x635). Null = no picture.
        public Texture2D Icon;
        // "Retrieve" was pressed on this entry: its wall slot is out, and `mount` is where the bow would hang (the slot's
        // gun position). Hang your items on it (parent them, kinematic); they move with the slot. Called again after
        // every retrieve, so fill only what is missing.
        public Action<Transform> OnShown;
        // The slot has finished sliding in (the wall's switch animation ended): positions are final now. Measure or
        // align against the wall here, not in OnShown, which runs while the slot is still moving.
        public Action<Transform> OnSettled;
        // Another entry was retrieved: this slot slides back into the wall and is switched off (with anything on it).
        public Action OnHidden;
    }

    public static class Arsenal
    {
        internal static readonly List<ArsenalWeapon> Weapons = new();

        // Call once, e.g. from OnInitializeMelon. Entries appear the next time The Range builds its wall.
        public static void Register(ArsenalWeapon w)
        {
            if (w == null || string.IsNullOrEmpty(w.Id)) throw new ArgumentException("ArsenalWeapon needs an Id");
            foreach (var o in Weapons)
                if (o.Id == w.Id) { WeaponFrameworkMod.Log?.Warning($"'{w.Id}' is already registered - ignored"); return; }
            Weapons.Add(w);
            WeaponFrameworkMod.Log?.Msg($"registered '{w.Id}' ({w.DisplayName})");
        }
    }

    // How the game builds the arsenal (ANBGameLogic.LoadAssetLoop, "Processed Filling Other Slots"): for each prefab in
    // ANBDataCollection.allOthers (not a knife) it instantiates othersSlotPrefab (`WeaponSlot_Big`) under
    // ANBGameLogic.WeaponFolder, sets the ANBGunwallSpot inside it (SlotID "Othersspot_<WeaponName>", WeaponPrefab, pose
    // from the weapon's ANBWeaponType.gunWallPosition1/Rotation1) and appends the slot to gunWallLarge.Items and the
    // weapon name to gunWallLarge.ItemsID. The panel (ANBGunwall.printInfo) reads name, description, icon and price from
    // the WeaponPrefab's ANBWeaponType; ANBGunwall.pickWeapon ("Retrieve") moves Items[currentSpot] to the wall's mainSpot
    // and switches it on, and endAnimation switches the previous one off.
    //
    // This mod appends one more slot per registered weapon the same way, after the game logs "Processed Completed
    // Gunwall". Differences from the game's slots:
    // - WeaponPrefab is an inactive display object that only carries an ANBWeaponType (never instantiated, never awake).
    // - The slot's gun socket (`gunstorage`, a DemoHolster that throws on anything but a gun or knife) is switched off
    //   before the slot ever wakes, and ANBGunwallSpot.initSlot is never called for it (only LoadAssetLoop calls it),
    //   so nothing is spawned into it. The weapon mod hangs its own items on a mount at the socket's gun position.
    // - Not added to ANBDataCollection.allGunSpots / allGunSpotObjects (the loadout code walks those).
    // - Always "owned" (ANBDataCollection.checkPurchaseDataWeapon prefix), without the game's free auto-purchase,
    //   which would write the id into the save.
    // - Retrieving writes the wall index to the save (pickWeapon -> saveSpotLarge -> SaveWeapons, key "saveSpotLarge" in
    //   SaveData_WeaponSetups). A mod index there would point past the list once the mod is removed, so SaveWeapons always
    //   sees the last game index instead. (0.1.1 guarded SavePurchases, which never writes it.)
    public class WeaponFrameworkMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        static MelonPreferences_Entry<bool> DebugLog, TestEntries;
        static bool Dbg => DebugLog.Value;
        internal static bool DebugOn => DebugLog != null && DebugLog.Value;

        const string GunwallDoneLog = "Processed Completed Gunwall";

        class Entry
        {
            public ArsenalWeapon W;
            public GameObject Slot;
            public Transform Mount;
            public bool Shown;
        }

        static GameObject DisplayHolder;                         // DontDestroyOnLoad, inactive
        static readonly Dictionary<string, GameObject> Displays = new();
        static readonly List<Entry> Entries = new();              // this scene's slots
        static ANBGunwall Wall;
        static ANBDataCollection Data;
        static int VanillaCount = -1, LastVanillaSpot;
        static float SceneStart;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var c = MelonPreferences.CreateCategory("WeaponFramework", "Weapon Framework");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log arsenal entries, wall retrieves and stand take / put-back events. Holster events are logged by VR Holster Customization.");
            TestEntries = c.CreateEntry("TestEntries", false, description: "Adds the framework's test weapon, a Crowbar, to the arsenal panel. Restart the game after changing this.");
            if (TestEntries.Value) TestCrowbar.Init();
            Log.Msg(TestEntries.Value ? "loaded (test entries on)" : "loaded");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (!Alive(Wall)) { Wall = null; Data = null; Entries.Clear(); VanillaCount = -1; }
            SceneStart = Time.time;
            ActionLog.Scene();
            if (TestEntries.Value) { try { TestCrowbar.Scene(); } catch (Exception e) { Log.Error($"test crowbar: {e.Message}"); } }
        }

        // Fallback if the load-log hook never fires: look for a built wall for a while after each scene load.
        static float nextPoll;
        public override void OnUpdate()
        {
            ActionLog.Tick();
            if (Arsenal.Weapons.Count == 0 || Alive(Wall) || Time.time < nextPoll || Time.time - SceneStart > 60f) return;
            nextPoll = Time.time + 2f;
            if (Time.time - SceneStart < 8f) return;
            try
            {
                var gl = Object.FindObjectOfType<ANBGameLogic>();
                if (gl != null && gl.gameStarted) Inject(gl, "poll");
            }
            catch (Exception e) { Log.Error($"poll: {e.Message}"); }
        }

        internal static void GunwallBuilt(ANBGameLogic gl)
        {
            try { Inject(gl, "load"); }
            catch (Exception e) { Log.Error($"adding arsenal entries failed: {e}"); }
        }

        static void Inject(ANBGameLogic gl, string how)
        {
            if (Arsenal.Weapons.Count == 0) return;
            var dc = gl != null ? gl.ANBdataCollection : null;
            var wall = dc != null ? dc.gunWallLarge : null;
            if (!Alive(wall) || wall.Items == null || wall.ItemsID == null) return;   // no arsenal in this scene
            if (Alive(Wall) && Wall.Pointer == wall.Pointer) return;                 // already done
            if (wall.Items.Count == 0 || !Alive(dc.othersSlotPrefab)) { if (Dbg) Log.Msg($"({how}) wall not built yet"); return; }

            // The bow's slot is the model for ours (same prefab; its spot pose and gun position).
            var refSlot = FindReferenceSlot(wall, dc, out var refType);
            Wall = wall; Data = dc; Entries.Clear();
            VanillaCount = wall.Items.Count;
            LastVanillaSpot = Mathf.Clamp(dc.saveSpotLarge, 0, VanillaCount - 1);

            foreach (var w in Arsenal.Weapons)
            {
                if (wall.ItemsID.Contains(w.Id)) continue;
                try { Entries.Add(AddSlot(gl, dc, wall, w, refSlot, refType)); }
                catch (Exception e) { Log.Error($"'{w.Id}': {e}"); }
            }
            Log.Msg($"arsenal ({how}): {Entries.Count} mod entr{(Entries.Count == 1 ? "y" : "ies")} added after the game's {VanillaCount} ({string.Join(", ", Entries.ConvertAll(e => e.W.Id))})");
        }

        static GameObject FindReferenceSlot(ANBGunwall wall, ANBDataCollection dc, out ANBWeaponType type)
        {
            type = null;
            GameObject first = null;
            for (int i = 0; i < wall.Items.Count; i++)
            {
                var slot = wall.Items[i];
                var spot = Alive(slot) ? slot.GetComponentInChildren<ANBGunwallSpot>(true) : null;
                if (spot == null || !Alive(spot.WeaponPrefab)) continue;
                var t = spot.WeaponPrefab.GetComponent<ANBWeaponType>();
                if (t == null) continue;
                if (first == null) { first = slot; type = t; }
                if (t.ammoType == ANBWeaponType.AmmoType.Arrows) { type = t; return slot; }   // the bow
            }
            return first;
        }

        static Entry AddSlot(ANBGameLogic gl, ANBDataCollection dc, ANBGunwall wall, ArsenalWeapon w, GameObject refSlot, ANBWeaponType refType)
        {
            var display = GetDisplay(w, refType);

            // Build under an inactive parent: nothing in the copy wakes until the gun socket is off.
            var tmp = new GameObject("WeaponFramework-build");
            tmp.SetActive(false);
            var slot = Object.Instantiate(dc.othersSlotPrefab, tmp.transform);
            slot.name = "WeaponSlot_" + w.Id;
            var spot = slot.GetComponentInChildren<ANBGunwallSpot>(true);
            if (spot == null) { Object.Destroy(tmp); throw new Exception("slot prefab has no ANBGunwallSpot"); }

            // Same pose as the reference slot (root where the game's slots wait, spot where the bow sits).
            var root = slot.transform;
            var refSpot = refSlot != null ? refSlot.GetComponentInChildren<ANBGunwallSpot>(true) : null;
            root.localScale = Vector3.one;
            if (refSpot != null)
            {
                spot.transform.localPosition = refSpot.transform.localPosition;
                spot.transform.localRotation = refSpot.transform.localRotation;
            }

            // Our mount: where the socket holds a gun. Made before the socket (its parent) is switched off.
            var mount = new GameObject("WeaponFramework-Mount").transform;
            var gunPos = spot.Hanger != null ? spot.Hanger.transform.Find("gunPos") : null;
            var anchor = gunPos != null ? gunPos : spot.Hanger != null ? spot.Hanger.transform : spot.transform;
            mount.SetParent(spot.transform, false);
            mount.position = anchor.position;
            mount.rotation = anchor.rotation;
            if (spot.Hanger != null) spot.Hanger.gameObject.SetActive(false);

            spot.SlotID = "Othersspot_" + w.Id;
            spot.WeaponPrefab = display;

            // Into the game's weapon folder, like its own slots (this wakes the slot).
            var folder = gl.WeaponFolder;
            if (refSlot != null && refSlot.transform.parent != null && folder != null && refSlot.transform.parent.Pointer == folder.Pointer)
            {
                root.SetParent(folder, false);
                root.localPosition = refSlot.transform.localPosition;
                root.localRotation = refSlot.transform.localRotation;
            }
            else
            {
                root.SetParent(folder, true);
                root.position = Vector3.zero;
                root.rotation = Quaternion.identity;
            }
            Object.Destroy(tmp);

            wall.Items.Add(slot);
            wall.ItemsID.Add(w.Id);
            if (Dbg) Log.Msg($"'{w.Id}' slot #{wall.Items.Count} under '{Name(root.parent)}', spot {V(spot.transform.localPosition)}, mount {V(spot.transform.InverseTransformPoint(mount.position))} (spot space), reference '{(refType != null ? refType.WeaponName : "none")}'");
            return new Entry { W = w, Slot = slot, Mount = mount };
        }

        // The object the panel reads: an inactive GameObject with only an ANBWeaponType.
        static GameObject GetDisplay(ArsenalWeapon w, ANBWeaponType refType)
        {
            if (Displays.TryGetValue(w.Id, out var d) && Alive(d)) return d;
            if (!Alive(DisplayHolder))
            {
                DisplayHolder = new GameObject("WeaponFramework-Displays");
                DisplayHolder.SetActive(false);
                Object.DontDestroyOnLoad(DisplayHolder);
            }
            d = new GameObject("Arsenal-" + w.Id);
            d.transform.SetParent(DisplayHolder.transform, false);
            var t = d.AddComponent<ANBWeaponType>();
            t.WeaponName = w.Id;
            t.WeaponID = w.Id;
            t.WeaponDisplayName = w.DisplayName ?? w.Id;
            t.WeaponDescription = w.Description ?? "";
            t.PurchaseID = "mod_" + w.Id;
            t.WeaponPrice = 0;
            t.WeaponMagCap = 0;
            t.ammoType = ANBWeaponType.AmmoType.Arrows;
            t.CanHaveSilencer = t.CanHaveLaserlight = t.CanHaveCompensator = t.CanHaveReddot = t.CanHaveScope = false;
            t.AttachmentPrice_AT_1 = t.AttachmentPrice_AT_2 = t.AttachmentPrice_AT_3 = t.AttachmentPrice_AT_4 = t.AttachmentPrice_AT_5 = t.AttachmentPrice_AT_6 = 0;
            if (refType != null)
            {
                t.overrideWallPos = refType.overrideWallPos;
                t.gunWallPosition1 = refType.gunWallPosition1;
                t.gunWallRotation1 = refType.gunWallRotation1;
                t.gunWallPosition2 = refType.gunWallPosition2;
            }
            if (Alive(w.Icon))
            {
                var tex = w.Icon;
                t.WeaponIcon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                t.WeaponIcon.name = "gunicon_mod_" + w.Id;
                Object.DontDestroyOnLoad(t.WeaponIcon);
            }
            Displays[w.Id] = d;
            return d;
        }

        // ---------- game hooks ----------

        internal static bool IsOurs(ANBWeaponType t)
        {
            if (t == null) return false;
            foreach (var d in Displays.Values) if (Alive(d) && d.Pointer == t.gameObject.Pointer) return true;
            return false;
        }

        static Entry EntryFor(GameObject slot)
        {
            if (!Alive(slot)) return null;
            foreach (var e in Entries) if (Alive(e.Slot) && e.Slot.Pointer == slot.Pointer) return e;
            return null;
        }

        static bool IsWall(ANBGunwall w) => Alive(Wall) && Alive(w) && w.Pointer == Wall.Pointer;

        internal static void AfterPick(ANBGunwall w, IntPtr before)
        {
            ActionLog.Retrieve(w, before, IsWall(w) && EntryFor(w.currentWeapon) != null);
            if (!IsWall(w)) return;
            if (w.currentSpot >= 0 && w.currentSpot < VanillaCount) LastVanillaSpot = w.currentSpot;
            var cur = w.currentWeapon;
            if (!Alive(cur) || cur.Pointer == before) return;   // nothing was retrieved
            foreach (var e in Entries)
            {
                bool now = Alive(e.Slot) && e.Slot.Pointer == cur.Pointer;
                if (now)
                {
                    e.Shown = true;
                    try { e.W.OnShown?.Invoke(e.Mount); } catch (Exception ex) { Log.Error($"'{e.W.Id}' OnShown: {ex}"); }
                }
                else if (e.Shown)
                {
                    e.Shown = false;
                    try { e.W.OnHidden?.Invoke(); } catch (Exception ex) { Log.Error($"'{e.W.Id}' OnHidden: {ex}"); }
                }
            }
        }

        // ANBGunwall.endAnimation: the switch animation is over (it switches the previous slot off here).
        internal static void AfterEndAnimation(ANBGunwall w)
        {
            if (!IsWall(w)) return;
            var cur = w.currentWeapon;
            if (!Alive(cur)) return;
            foreach (var e in Entries)
            {
                if (!e.Shown || !Alive(e.Slot) || e.Slot.Pointer != cur.Pointer) continue;
                if (Dbg) Log.Msg($"'{e.W.Id}' settled - mount at {V(e.Mount.position)}, facing {V(e.Mount.forward)}");
                try { e.W.OnSettled?.Invoke(e.Mount); } catch (Exception ex) { Log.Error($"'{e.W.Id}' OnSettled: {ex}"); }
            }
        }

        // A grab of something hanging on one of our mounts = a mod item taken off the stand (logging only).
        internal static void BeforeGrab(Il2CppHurricaneVR.Framework.Core.Grabbers.HVRGrabberBase grabber, Il2CppHurricaneVR.Framework.Core.HVRGrabbable g)
        {
            if (!Alive(g)) return;
            var t = g.transform;
            if (Dbg)
                foreach (var e in Entries)
                    if (Alive(e.Mount) && t.IsChildOf(e.Mount)) { ActionLog.ModItemTaken(e.W.Id, g, grabber); break; }
        }

        // Our entries have no ammo, no attachments and nothing to buy.
        internal static void AfterPrintInfo(ANBGunwall w)
        {
            if (!IsWall(w) || w.currentSpot < VanillaCount || w.currentSpot >= w.Items.Count) return;
            if (EntryFor(w.Items[w.currentSpot]) == null) return;
            if (Alive(w.ammoObj)) w.ammoObj.SetActive(false);
            if (Alive(w.modifyButton)) w.modifyButton.SetActive(false);
            if (Alive(w.buyButton)) w.buyButton.SetActive(false);
        }

        static int savedSpot = -1;
        internal static void BeforeSave()
        {
            savedSpot = -1;
            if (!Alive(Data) || VanillaCount <= 0) return;
            if (Data.saveSpotLarge < VanillaCount) return;
            savedSpot = Data.saveSpotLarge;
            Data.saveSpotLarge = Mathf.Clamp(LastVanillaSpot, 0, VanillaCount - 1);
            if (Dbg) Log.Msg($"save: wall index {savedSpot} is a mod entry - saved as {Data.saveSpotLarge}");
        }

        internal static void AfterSave()
        {
            if (savedSpot >= 0 && Alive(Data)) Data.saveSpotLarge = savedSpot;
            savedSpot = -1;
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }

        static string Name(Object o) => Alive(o) ? o.name : "none";
        internal static string V(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.LogLoadTime))]
    static class LogLoadTimePatch
    {
        static void Postfix(ANBGameLogic __instance, string text)
        {
            ActionLog.LoadStep();
            if (text == "Processed Completed Gunwall") WeaponFrameworkMod.GunwallBuilt(__instance);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBDataCollection), nameof(ANBDataCollection.checkPurchaseDataWeapon))]
    static class OwnedPatch
    {
        static bool Prefix(ANBWeaponType tmpWPT, ref bool __result)
        {
            try
            {
                if (!WeaponFrameworkMod.IsOurs(tmpWPT)) return true;
                __result = true;
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGunwall), nameof(ANBGunwall.pickWeapon))]
    static class PickPatch
    {
        static void Prefix(ANBGunwall __instance, out IntPtr __state)
        {
            __state = IntPtr.Zero;
            try { var c = __instance.currentWeapon; if (c != null) __state = c.Pointer; } catch { }
        }

        static void Postfix(ANBGunwall __instance, IntPtr __state)
        {
            try { WeaponFrameworkMod.AfterPick(__instance, __state); }
            catch (Exception e) { MelonLogger.Error($"[WeaponFramework] pickWeapon: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGunwall), nameof(ANBGunwall.endAnimation))]
    static class EndAnimationPatch
    {
        static void Postfix(ANBGunwall __instance)
        {
            try { WeaponFrameworkMod.AfterEndAnimation(__instance); }
            catch (Exception e) { MelonLogger.Error($"[WeaponFramework] endAnimation: {e.Message}"); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGunwall), nameof(ANBGunwall.printInfo))]
    static class PrintInfoPatch
    {
        static void Postfix(ANBGunwall __instance)
        {
            try { WeaponFrameworkMod.AfterPrintInfo(__instance); }
            catch (Exception e) { MelonLogger.Error($"[WeaponFramework] printInfo: {e.Message}"); }
        }
    }

    // SaveWeapons writes saveSpotLarge/saveSpotSmall. Callers: pickWeapon, gun editing (ANBWeaponAttachments),
    // revertAllWeaponData, LoadWeapons.
    [HarmonyLib.HarmonyPatch(typeof(ANBSaveData), nameof(ANBSaveData.SaveWeapons))]
    static class SavePatch
    {
        static void Prefix() { try { WeaponFrameworkMod.BeforeSave(); } catch { } }
        static void Postfix() { try { WeaponFrameworkMod.AfterSave(); } catch { } }
    }
}
