using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Bags;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using static GrabFix.GrabFixMod;
using Object = UnityEngine.Object;

namespace GrabFix;

// Detector geometry only. Never rotate the controller, physical hand, held object,
// grip points or an ancestor of the hand. Native detectors keep their aim; "Both"
// adds a palm-aimed copy of each distance capsule next to it. Everything is undone
// by Restore (native radius/centre back, copies removed from the grabber and destroyed).
internal sealed class Geometry
{
    sealed class Near
    {
        internal SphereCollider Collider;
        internal Vector3 Center;
        internal float Radius;
    }
    sealed class Far
    {
        internal CapsuleCollider Collider;
        internal Transform Transform;
        internal HVRForceGrabber Owner;
        internal Vector3 HandPosition;
        internal Quaternion HandRotation;
        internal float Radius;
        internal GameObject Copy;
        internal CapsuleCollider CopyCollider;
        internal HVRGrabbableBag CopyBag;
        internal bool CopyFailed;
    }
    readonly List<Near> near = new();
    readonly List<Far> far = new();
    readonly HashSet<int> known = new();
    double discoverAt;
    bool changed;

    void Discover(HVRHandGrabber h)
    {
        if (Now < discoverAt) return;
        discoverAt = Now + 2;
        int before = near.Count + far.Count;
        void Local(HVRGrabbableBag b)
        {
            if (!Alive(b) || !Alive(b.TryCast<HVRTriggerGrabbableBag>())) return;
            foreach (var c in b.GetComponents<SphereCollider>())
                if (Alive(c) && c.isTrigger && known.Add(c.GetInstanceID()))
                    near.Add(new Near { Collider = c, Center = c.center, Radius = c.radius });
        }
        Local(h._grabBag);
        if (h.GrabBags != null) foreach (var b in h.GrabBags) Local(b);
        if (Alive(h.ForceGrabber))
        {
            var fg = h.ForceGrabber;
            void Distance(HVRGrabbableBag b)
            {
                if (!Alive(b) || !Alive(b.TryCast<HVRTriggerGrabbableBag>())) return;
                var t = b.transform;
                if (t.Pointer == h.transform.Pointer || h.transform.IsChildOf(t) ||
                    (Alive(h.HandModel) && h.HandModel.IsChildOf(t)) ||
                    (Alive(h.Palm) && h.Palm.IsChildOf(t)) ||
                    t.Pointer == fg.transform.Pointer) return;
                var c = b.GetComponent<CapsuleCollider>();
                // Palm copies are pre-registered in known, so they are never treated as native.
                if (!Alive(c) || !c.isTrigger || !known.Add(c.GetInstanceID())) return;
                far.Add(new Far { Collider = c, Transform = t, Owner = fg, Radius = c.radius,
                    HandPosition = h.transform.InverseTransformPoint(t.position), HandRotation = Quaternion.Inverse(h.transform.rotation) * t.rotation });
            }
            Distance(fg._grabBag);
            if (fg.GrabBags != null) foreach (var b in fg.GrabBags) Distance(b);
        }
        if (before != near.Count + far.Count && DebugLog.Value)
            Log.Msg($"{(h.IsLeftHand ? "L" : "R")} detectors: {near.Count} local sphere(s), {far.Count} distance capsule(s); direction={(BothMode ? "Both" : "Finger")}");
    }

    // True if g is within the LOCAL sphere's native (pre-widen) radius, using each sphere's
    // stored original centre/radius rather than the currently-applied (possibly widened) ones.
    // Used to keep a docked item (holstered, socketed, wall-mounted) reachable at native range
    // without letting the widened sphere reach it early.
    internal bool NativeContains(HVRGrabbable g)
    {
        if (!Alive(g)) return false;
        foreach (var n in near)
        {
            if (!Alive(n.Collider)) continue;
            var t = n.Collider.transform;
            var scale = t.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (maxScale < .0001f) continue;
            var center = t.TransformPoint(n.Center);
            float radius = n.Radius * maxScale;
            if (SurfaceDistance(g, center) <= radius) return true;
        }
        return false;
    }

    // Same idea as NativeContains, but for a distance/force-grab capsule: rebuilds its native
    // (pre-DistanceWidth, finger-aimed - never the Both-mode palm copy) world-space segment from
    // the stored original radius and the untouched collider centre/transform, and tests the
    // candidate against that segment instead of the currently-applied (possibly widened, and
    // possibly copy-only) geometry.
    internal bool NativeContainsFar(HVRGrabbable g)
    {
        if (!Alive(g)) return false;
        foreach (var f in far)
        {
            if (!Alive(f.Collider) || !Alive(f.Transform)) continue;
            var c = f.Collider;
            var t = f.Transform;
            var scale = t.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (maxScale < .0001f) continue;
            Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            float half = Mathf.Max(0, c.height * .5f - f.Radius) * maxScale;
            Vector3 centerWorld = t.TransformPoint(c.center);
            Vector3 axisWorld = t.TransformDirection(axis);
            Vector3 p0 = centerWorld - axisWorld * half, p1 = centerWorld + axisWorld * half;
            float radius = f.Radius * maxScale;
            Vector3 gPos = Alive(g.Rigidbody) ? g.Rigidbody.worldCenterOfMass : g.transform.position;
            var seg = p1 - p0;
            float segLenSq = seg.sqrMagnitude;
            Vector3 nearest = segLenSq < 1e-8f ? p0 : p0 + seg * Mathf.Clamp01(Vector3.Dot(gPos - p0, seg) / segLenSq);
            if (SurfaceDistance(g, nearest) <= radius) return true;
        }
        return false;
    }

    internal void Apply(HVRHandGrabber h)
    {
        if (!Active || !Alive(h) || !h.isActiveAndEnabled) { Restore(); return; }
        Discover(h);
        bool both = BothMode && Alive(h.Palm);
        var palmPoint = both ? Palm(h) + h.Palm.forward * Limit(PalmOffset.Value, .03f, 0, .08f) : Vector3.zero;
        foreach (var n in near)
        {
            if (!Alive(n.Collider)) continue;
            var t = n.Collider.transform;
            var scale = t.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (maxScale < .0001f) continue;
            float radius = Limit(NearRadius.Value, .12f, 0, .25f);
            if (radius <= 0) radius = n.Radius * maxScale;
            var center = n.Center;
            if (both)
            {
                // One sphere spanning the finger-side native centre and the palm face.
                var native = t.TransformPoint(n.Center);
                center = t.InverseTransformPoint((native + palmPoint) * .5f);
                radius = Mathf.Min(.25f, Mathf.Max(radius, Vector3.Distance(native, palmPoint) * .5f + n.Radius * maxScale));
            }
            float localRadius = radius / maxScale;
            if (!Mathf.Approximately(n.Collider.radius, localRadius)) n.Collider.radius = localRadius;
            if ((n.Collider.center - center).sqrMagnitude > .00000001f) n.Collider.center = center;
        }
        // Palm forward is hand +/-X (mirrored per side); use the real palm transform.
        var turn = both ? Quaternion.FromToRotation(h.transform.forward, h.Palm.forward) : Quaternion.identity;
        float width = Limit(DistanceWidth.Value, 1.2f, 1, 2);
        foreach (var f in far)
        {
            if (!Alive(f.Transform) || !Alive(f.Collider)) continue;
            float radius = f.Radius * width;
            if (!Mathf.Approximately(f.Collider.radius, radius)) f.Collider.radius = radius;
            if (!both) { RemoveCopy(f); continue; }
            if (!Alive(f.Copy) && !MakeCopy(f)) continue;
            if (!Mathf.Approximately(f.CopyCollider.radius, radius)) f.CopyCollider.radius = radius;
            var ct = f.Copy.transform;
            var position = Palm(h) + turn * (h.transform.TransformPoint(f.HandPosition) - h.transform.position);
            var rotation = turn * h.transform.rotation * f.HandRotation;
            if ((ct.position - position).sqrMagnitude > .00000001f) ct.position = position;
            if (Quaternion.Angle(ct.rotation, rotation) > .01f) ct.rotation = rotation;
        }
        changed = true;
    }

    bool MakeCopy(Far f)
    {
        if (f.CopyFailed || !Alive(f.Owner) || f.Owner.GrabBags == null) return false;
        // The bag object holds only the bag + its capsule in the tested build. Refuse
        // anything with children: Instantiate wakes every component it copies.
        if (f.Transform.childCount != 0) { f.CopyFailed = true; Log.Warning($"'{f.Transform.name}' has children; no palm copy."); return false; }
        var go = Object.Instantiate(f.Transform.gameObject, f.Transform.parent);
        go.name = f.Transform.name + " (GrabFix palm)";
        var bag = go.GetComponent<HVRGrabbableBag>();
        var col = go.GetComponent<CapsuleCollider>();
        if (!Alive(bag) || !Alive(col)) { Object.Destroy(go); f.CopyFailed = true; Log.Warning($"'{f.Transform.name}' copy incomplete; no palm copy."); return false; }
        known.Add(col.GetInstanceID());
        // Appended after the native bags, so a finger-aimed target keeps priority.
        if (!f.Owner.GrabBags.Contains(bag)) f.Owner.GrabBags.Add(bag);
        f.Copy = go; f.CopyCollider = col; f.CopyBag = bag;
        return true;
    }

    static void RemoveCopy(Far f)
    {
        if (Alive(f.Owner) && f.Owner.GrabBags != null && Alive(f.CopyBag)) f.Owner.GrabBags.Remove(f.CopyBag);
        if (Alive(f.Copy)) Object.Destroy(f.Copy);
        f.Copy = null; f.CopyCollider = null; f.CopyBag = null;
    }

    internal void Restore()
    {
        if (!changed) return;
        foreach (var n in near) if (Alive(n.Collider)) { n.Collider.center = n.Center; n.Collider.radius = n.Radius; }
        foreach (var f in far)
        {
            if (Alive(f.Collider)) f.Collider.radius = f.Radius;
            RemoveCopy(f);
        }
        changed = false;
    }
}
