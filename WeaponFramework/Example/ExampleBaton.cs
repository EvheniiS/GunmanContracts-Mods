using System;
using System.Runtime.CompilerServices;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(ExampleBaton.ExampleBatonMod), "Example Baton", "0.1.0", "YourName")]
[assembly: MelonGame("ANB_Seth", "GunmanContracts")]
// Weapon Framework is optional for this mod: without it the mod loads and simply has no arsenal entry.
// For a hard requirement use [assembly: MelonAdditionalDependencies("WeaponFramework")] instead.
[assembly: MelonOptionalDependencies("WeaponFramework")]

namespace ExampleBaton
{
    // The smallest complete Weapon Framework user: one "baton" (a copy of The Range's crowbar) on the arsenal panel.
    // Page to "Example Baton", press Retrieve, take it off the wall. See WeaponFramework/MODDING_GUIDE.md.
    public class ExampleBatonMod : MelonMod
    {
        static MelonLogger.Instance Log;
        static GameObject Template;          // inactive copy of the crowbar, kept across scenes
        static GameObject OnWall;            // the baton currently hanging on the wall slot (null when taken)

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            if (!FrameworkLoaded()) { Log.Msg("Weapon Framework not installed - no arsenal entry"); return; }
            try { RegisterArsenal(); } catch (Exception e) { Log.Warning($"arsenal entry failed: {e.Message}"); }
        }

        static bool FrameworkLoaded()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "WeaponFramework") return true;
            return false;
        }

        // Only this method touches Weapon Framework types, so the JIT never needs its DLL when it isn't installed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RegisterArsenal()
        {
            WeaponFramework.Arsenal.Register(new WeaponFramework.ArsenalWeapon
            {
                Id = "ExampleBaton",
                DisplayName = "Example Baton",
                Description = "",          // leave empty: the panel draws it over the name
                Icon = null,               // a Texture2D (see the guide); null = no picture
                OnShown = Hang,
                OnSettled = mount => Log.Msg($"baton slot settled at {mount.position}"),
                OnHidden = () => Log.Msg("another weapon retrieved - the baton goes back into the wall"),
            });
        }

        // The template: The Range's crowbar, copied under an INACTIVE holder, so nothing in the copy runs Awake
        // (game components register themselves in Awake - see the guide).
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (Template != null) return;
            var crowbar = GameObject.Find("Prop-Bluntweapon-Crowbar");
            if (crowbar == null) return;
            var holder = new GameObject("ExampleBaton-Template");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            Template = Object.Instantiate(crowbar, holder.transform);
            Log.Msg("template copied from the crowbar");
        }

        // Retrieve pressed: hang one baton on the slot (only if the last one was taken).
        static void Hang(Transform mount)
        {
            if (Template == null) { Log.Warning("no template yet - it is copied in The Range"); return; }
            if (OnWall != null) return;
            var go = Object.Instantiate(Template, mount.position, mount.rotation);
            go.name = "ExampleBaton";
            MakeGrabbable(go);
            go.transform.SetParent(mount, true);     // rides the slot's slide animation
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; }
            OnWall = go;
            Log.Msg("baton hung on the wall");
        }

        // The game's GD_HVROptimiser switches every grabbable it found (inactive ones too, so the template's) off, and
        // a copy of a disabled template starts ungrabbable. Switch them on and keep them out of its list.
        static void MakeGrabbable(GameObject go)
        {
            var list = GD_HVROptimiser.instance != null ? GD_HVROptimiser.instance._grabbables : null;
            foreach (var g in go.GetComponentsInChildren<HVRGrabbable>(true))
            {
                g.enabled = true;
                list?.Remove(g);
            }
        }

        // Taken off the wall: unparent and give physics back before HVR sets up the grab.
        internal static void BeforeGrab(HVRGrabbable g)
        {
            if (OnWall == null || g == null || g.gameObject.Pointer != OnWall.Pointer) return;
            OnWall.transform.SetParent(null, true);
            var rb = OnWall.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            OnWall = null;
            Log.Msg("baton taken");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRGrabberBase), nameof(HVRGrabberBase.GrabGrabbable))]
    static class GrabPatch
    {
        static void Prefix(HVRGrabbable grabbable)
        {
            try { ExampleBatonMod.BeforeGrab(grabbable); } catch { }
        }
    }
}
