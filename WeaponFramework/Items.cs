using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WeaponFramework
{
    // Weapon templates and compatibility helpers; docking is owned by VR Holster Customization.
    public static class Items
    {
        static GameObject holder;
        // A copy of `source` that never wakes: parented under an inactive DontDestroyOnLoad holder. Spawn from it.
        public static GameObject MakeTemplate(GameObject source, string name)
        {
            if (!WeaponFrameworkMod.Alive(holder))
            {
                holder = new GameObject("WeaponFramework-Templates");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            var t = Object.Instantiate(source, holder.transform);
            t.name = name;
            t.transform.localPosition = Vector3.zero;
            t.transform.localRotation = Quaternion.identity;
            return t;
        }

        // A live copy of a template, grabbable, out of the optimiser, skipped by prop resets.
        public static GameObject Spawn(GameObject template, string name, Vector3 pos, Quaternion rot)
        {
            var go = Object.Instantiate(template, pos, rot);
            go.name = name;
            VRHolsterCustomization.Dock.Manage(go);
            return go;
        }

        public static void Manage(GameObject go) => VRHolsterCustomization.Dock.Manage(go);
        public static bool IsManaged(GameObject go) => VRHolsterCustomization.Dock.IsManaged(go);
        public static bool IsHeld(GameObject go) => VRHolsterCustomization.Dock.IsHeld(go);
        public static void Hang(GameObject go, Transform parent, Vector3 pos, Quaternion rot) => VRHolsterCustomization.Dock.Hang(go, parent, pos, rot);
        public static bool IsHung(GameObject go) => VRHolsterCustomization.Dock.IsHung(go);
        public static bool Measure(GameObject go, out Vector3 center, out Vector3 axis, out float length) => VRHolsterCustomization.ItemShape.Measure(go, out center, out axis, out length);
        public static Vector3 Widest(GameObject go, Vector3 axis) => VRHolsterCustomization.ItemShape.Widest(go, axis);
    }
}
