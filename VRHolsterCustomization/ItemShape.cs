using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    // Geometry shared by back holsters and arsenal mounts.
    public static class ItemShape
    {
        // The item's long axis, measured on its longest (non-trigger) box collider: the grip end is the one nearer its
        // main grab point, `axis` points to the other end (a crowbar's hook, a club's tip). Item-local space.
        public static bool Measure(GameObject go, out Vector3 center, out Vector3 axis, out float length)
        {
            center = Vector3.zero; axis = Vector3.forward; length = 0.5f;
            if (!VRHolsterCustomizationMod.Alive(go)) return false;
            var root = go.transform;
            BoxCollider shaft = null;
            foreach (var bc in go.GetComponentsInChildren<BoxCollider>(true))
            {
                if (bc.isTrigger) continue;
                var bt = bc.transform;
                for (int i = 0; i < 3; i++)
                {
                    var ax = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                    var w = root.InverseTransformVector(bt.TransformVector(ax * bc.size[i]));
                    if (w.magnitude > length || shaft == null) { length = w.magnitude; shaft = bc; axis = w.normalized; }
                }
            }
            if (shaft == null) return false;
            center = root.InverseTransformPoint(shaft.transform.TransformPoint(shaft.center));
            Transform grip = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name == "GrabPoint_Base") { grip = t; break; }
            if (grip == null)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("GrabPoint")) { grip = t; break; }
            if (grip != null && Vector3.Dot(root.InverseTransformPoint(grip.position) - center, axis) > 0) axis = -axis;
            return true;
        }

        // The direction across `axis` in which the item's meshes reach furthest (a crowbar's hook, a blade's edge),
        // pointing to the side the bulk sticks out to. Item-local. Lay it along a wall to hang the item flat.
        public static Vector3 Widest(GameObject go, Vector3 axis)
        {
            var root = go.transform;
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var b = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    pts.Add(root.InverseTransformPoint(mf.transform.TransformPoint(c)));
                }
            }
            var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var v = Vector3.Cross(axis, u);
            if (pts.Count == 0) return u;
            Vector3 best = u; float bestSpan = -1f;
            for (int k = 0; k < 18; k++)
            {
                float a = k * Mathf.PI / 18f;
                var d = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
                foreach (var p in pts) { float x = Vector3.Dot(p, d); lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); sum += x; }
                if (hi - lo <= bestSpan) continue;
                bestSpan = hi - lo;
                best = sum / pts.Count > (lo + hi) * 0.5f ? d : -d;
            }
            return best;
        }
    }
}
