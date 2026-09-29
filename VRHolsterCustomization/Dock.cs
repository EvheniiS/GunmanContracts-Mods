using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    // Shared lifetime and docking support for holstered mod items and arsenal mounts.
    // - GD_HVROptimiser collects every grabbable once (inactive ones too), switches them all off and later re-enables
    //   only those near and in front of the player. Copies of a template start disabled and aren't in its list;
    //   items that are in it switch off behind the player (a back holster!). Managed items are taken out of its list
    //   at spawn and again once a second.
    // Also: the game's physics-prop reset (ANBGeneratePhysics.resetMe, on wave start) teleports copies of a prop back to
    // their spawn spot; managed items are skipped. Hung items (on a wall mount) stay pinned until grabbed.
    public static class Dock
    {
        static readonly List<GameObject> managed = new();
        static readonly List<Hung> hung = new();
        static float nextOptimiser;

        sealed class Hung { public GameObject Go; public Transform Parent; public Vector3 Pos; public Quaternion Rot; }

        // Adopt an object the mod made itself (the optimiser and reset rules apply to it from now on).
        public static void Manage(GameObject go)
        {
            if (!VRHolsterCustomizationMod.Alive(go)) return;
            bool known = false;
            foreach (var m in managed) if (VRHolsterCustomizationMod.Alive(m) && m.Pointer == go.Pointer) { known = true; break; }
            if (!known) managed.Add(go);
            FreeFromOptimiser(go);
        }

        public static bool IsManaged(GameObject go)
        {
            if (!VRHolsterCustomizationMod.Alive(go)) return false;
            foreach (var m in managed) if (VRHolsterCustomizationMod.Alive(m) && m.Pointer == go.Pointer) return true;
            return false;
        }

        public static bool IsHeld(GameObject go)
        {
            try
            {
                if (!VRHolsterCustomizationMod.Alive(go)) return false;
                foreach (var g in go.GetComponentsInChildren<HVRGrabbable>(true)) if (g.IsBeingHeld) return true;
            }
            catch { }
            return false;
        }

        // Hang on a transform (a wall mount): parented, kinematic, put back every frame until a hand takes it.
        public static void Hang(GameObject go, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            if (!VRHolsterCustomizationMod.Alive(go) || !VRHolsterCustomizationMod.Alive(parent)) return;
            Unhang(go);
            hung.Add(new Hung { Go = go, Parent = parent, Pos = localPos, Rot = localRot });
            Pin(go, parent, localPos, localRot);
        }

        public static bool IsHung(GameObject go)
        {
            foreach (var h in hung) if (VRHolsterCustomizationMod.Alive(h.Go) && h.Go.Pointer == go.Pointer) return true;
            return false;
        }

        public static void Unhang(GameObject go)
        {
            for (int i = hung.Count - 1; i >= 0; i--)
                if (!VRHolsterCustomizationMod.Alive(hung[i].Go) || hung[i].Go.Pointer == go.Pointer) hung.RemoveAt(i);
        }

        // Parent, kinematic, exact local pose.
        public static void Pin(GameObject go, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            var t = go.transform;
            if (t.parent == null || t.parent.Pointer != parent.Pointer) t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
        }

        // Out of a slot or off a wall: into the world with physics back.
        public static void Unpin(GameObject go)
        {
            go.transform.SetParent(null, true);
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        internal static void Tick()
        {
            for (int i = hung.Count - 1; i >= 0; i--)
            {
                var h = hung[i];
                if (!VRHolsterCustomizationMod.Alive(h.Go) || !VRHolsterCustomizationMod.Alive(h.Parent)) { hung.RemoveAt(i); continue; }
                // The prop's own scripts turn physics back on a frame after a spawn (ANBGeneratePhysics.Init).
                if (!IsHeld(h.Go)) Pin(h.Go, h.Parent, h.Pos, h.Rot);
            }
            if (Time.time < nextOptimiser) return;
            nextOptimiser = Time.time + 1f;
            for (int i = managed.Count - 1; i >= 0; i--)
            {
                if (!VRHolsterCustomizationMod.Alive(managed[i])) { managed.RemoveAt(i); continue; }
                FreeFromOptimiser(managed[i]);
            }
        }

        // A hand takes a hung item off the wall.
        public static void BeforeGrab(HVRGrabbable g)
        {
            if (!VRHolsterCustomizationMod.Alive(g)) return;
            var t = g.transform;
            foreach (var h in hung)
            {
                if (!VRHolsterCustomizationMod.Alive(h.Go) || !t.IsChildOf(h.Go.transform)) continue;
                var go = h.Go;
                Unhang(go);
                Unpin(go);
                return;
            }
        }

        static void FreeFromOptimiser(GameObject go)
        {
            GD_HVROptimiser opt = null;
            try { opt = GD_HVROptimiser.instance; } catch { }
            var list = VRHolsterCustomizationMod.Alive(opt) ? opt._grabbables : null;
            foreach (var g in go.GetComponentsInChildren<HVRGrabbable>(true))
            {
                if (!g.enabled) g.enabled = true;
                list?.Remove(g);
            }
        }


    }

    // The Range resets physics props on a wave start (ANBGeneratePhysics.resetMe teleports them to their spawn spot).
    [HarmonyLib.HarmonyPatch(typeof(ANBGeneratePhysics), nameof(ANBGeneratePhysics.resetMe))]
    static class ItemResetPatch
    {
        static bool Prefix(ANBGeneratePhysics __instance)
        {
            try { return !Dock.IsManaged(__instance.gameObject); }
            catch { return true; }
        }
    }
}
