using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppHurricaneVR.Framework.Components;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Bags;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using UnityEngine;
using static GrabLog.GrabLogMod;

namespace GrabLog;

internal static class Details
{
    internal readonly record struct IndicatorKey(int Id, bool Active, bool Enabled);
    internal readonly record struct PromptKey(bool Grip, bool Dynamic, IndicatorKey GripObject, IndicatorKey TriggerObject,
        IndicatorKey DynamicObject, IndicatorKey ForceObject, int Hover, int TriggerHover);
    static IndicatorKey IndicatorSignature(HVRGrabbableHoverBase i) => !Alive(i) ? default : new(Id(i), i.gameObject.activeInHierarchy, i.enabled);
    internal static PromptKey PromptSignature(HVRHandGrabber h) => new(h._grabIndicatorEnabled, h._dynamicIndicatorEnabled,
        IndicatorSignature(h._grabIndicator), IndicatorSignature(h._triggerIndicator), IndicatorSignature(h.DynamicPoseIndicator),
        Alive(h.ForceGrabber) ? IndicatorSignature(h.ForceGrabber._grabIndicator) : default, Id(h.HoverTarget), Id(h.TriggerHoverTarget));
    // Only the small hand detector hierarchy is cached. Item collider / grab-point lists
    // are read live so thrown items and modded grips retain their current configuration.
    static readonly Dictionary<int, (double at, Collider[] colliders)> bagShapes = new();
    internal static void Reset() => bagShapes.Clear();
    static Vector3 Palm(HVRHandGrabber h) => Alive(h.Palm) ? h.Palm.position : h.transform.position;
    static object TransformInfo(Transform t) => !Alive(t) ? null : new { id = Id(t), position = V(t.position), rotation = Q(t.rotation) };

    static object Indicator(HVRGrabbableHoverBase i) => !Alive(i) ? null : new
    {
        id = Id(i), active = i.gameObject.activeInHierarchy, enabled = i.enabled
    };

    // These are the actual instantiated indicators, not the template fields GrabIndicator
    // and TriggerGrabIndicator. Logical enabled flags are distinct from hierarchy state.
    internal static object Prompt(HVRHandGrabber h) => new
    {
        gripEnabled = h._grabIndicatorEnabled,
        dynamicEnabled = h._dynamicIndicatorEnabled,
        grip = Indicator(h._grabIndicator), trigger = Indicator(h._triggerIndicator),
        dynamicPose = Indicator(h.DynamicPoseIndicator),
        force = Alive(h.ForceGrabber) ? Indicator(h.ForceGrabber._grabIndicator) : null,
        hover = Id(h.HoverTarget), triggerHover = Id(h.TriggerHoverTarget)
    };

    internal static object Hand(HVRHandGrabber h)
    {
        var s = State(h);
        var c = h.Controller;
        return new
        {
            id = Id(h), side = Side(h), active = h.isActiveAndEnabled,
            gripHeld = h.IsGripGrabActive, gripActivated = h.IsGripGrabActivated,
            triggerHeld = h.IsTriggerGrabActive, triggerActivated = h.IsTriggerGrabActivated,
            gripValue = Alive(c) ? (float?)c.Grip : null, triggerValue = Alive(c) ? (float?)c.Trigger : null,
            press = s.PressAt >= 0 ? (long?)s.PressId : null,
            pressAge = s.PressAt >= 0 ? (double?)(Now - s.PressAt) : null,
            grabbing = h.IsGrabbing, held = Id(h.GrabbedTarget), hover = Id(h.HoverTarget), triggerHover = Id(h.TriggerHoverTarget),
            allowGrabbing = h.AllowGrabbing, allowHovering = h.AllowHovering,
            menuBlocksDetection = h.NoDetectionDueToMenu, releaseWait = h.releaseWait,
            grabTrigger = h.GrabTrigger.ToString(), toggleActive = h.GrabToggleActive,
            hand = TransformInfo(h.transform), palm = TransformInfo(h.Palm), controllerTarget = TransformInfo(h.ControllerHandTarget),
            controllerRotation = Q(h.ControllerRotation), handWorldRotation = Q(h.HandWorldRotation), handModel = TransformInfo(h.HandModel),
            selectedGrabPoint = TransformInfo(h.GrabPoint), selectedPosablePoint = Id(h.PosableGrabPoint),
            prompt = Prompt(h), bags = Bags(h, null), overlapSphere = Shape(h._overlapCollider),
            force = Alive(h.ForceGrabber) ? new
            {
                active = h.ForceGrabber.isActiveAndEnabled, hover = Id(h.ForceGrabber.HoverTarget),
                grabbing = h.ForceGrabber.IsForceGrabbing, autoGrabDistance = h.ForceGrabber.AutoGrabDistance,
                maxRaycastDistance = h.ForceGrabber.MaxRayCastDistance, requireLineOfSight = h.ForceGrabber.RequireLineOfSight,
                bags = Bags(h.ForceGrabber, null)
            } : null
        };
    }

    internal static List<HVRGrabbable> Candidates(HVRHandGrabber h)
    {
        var result = new List<HVRGrabbable>();
        void Add(HVRGrabbable g) { if (Alive(g) && !result.Any(x => Id(x) == Id(g))) { Register(g); result.Add(g); } }
        Add(h.HoverTarget); Add(h.TriggerHoverTarget); Add(h.GrabbedTarget);
        if (Alive(h.ForceGrabber)) Add(h.ForceGrabber.HoverTarget);
        var p = Palm(h);
        float radius = Clamp(ObservationRadius.Value, .75f, .1f, 3);
        // Include nearby items even when absent from every HVR bag: essential for diagnosing
        // dropped items whose colliders stopped entering the detector. No eligibility filtering.
        var nearest = new List<(HVRGrabbable item, float distance)>();
        foreach (var g in Items.Values)
        {
            if (!Alive(g) || !g.gameObject.activeInHierarchy) continue;
            float distance = Distance(g, p);
            if (distance <= radius) nearest.Add((g, distance));
        }
        nearest.Sort((a, b) => a.distance.CompareTo(b.distance));
        foreach (var entry in nearest.Take(3)) Add(entry.item);
        return result;
    }

    static float Distance(HVRGrabbable g, Vector3 p)
    {
        float d = Vector3.Distance(p, g.transform.position);
        if (g.Colliders != null)
            foreach (var c in g.Colliders)
                if (Alive(c) && c.enabled && c.gameObject.activeInHierarchy)
                    // Bounds distance is cheap for discovering candidates. Detailed rows below
                    // use Collider.ClosestPoint only where Unity supports the shape.
                    d = Math.Min(d, Vector3.Distance(p, c.bounds.ClosestPoint(p)));
        return d;
    }

    static object ColliderDistance(Collider c, Vector3 p)
    {
        var mesh = c.TryCast<MeshCollider>();
        bool exact = Alive(c.TryCast<SphereCollider>()) || Alive(c.TryCast<BoxCollider>()) ||
                     Alive(c.TryCast<CapsuleCollider>()) || (Alive(mesh) && mesh.convex);
        bool active = c.enabled && c.gameObject.activeInHierarchy;
        return new
        {
            shape = Shape(c),
            distance = active ? (float?)Vector3.Distance(p, exact ? c.ClosestPoint(p) : c.bounds.ClosestPoint(p)) : null,
            metric = exact ? "collider closest point" : "world AABB estimate"
        };
    }

    internal static object Item(HVRHandGrabber h, HVRGrabbable g)
    {
        if (!Alive(g)) return null;
        Register(g);
        var p = Palm(h);
        var points = new List<object>();
        if (g.GrabPoints != null)
            foreach (var t in g.GrabPoints)
            {
                if (!Alive(t)) continue;
                var point = t.GetComponent<HVRPosableGrabPoint>();
                points.Add(new
                {
                    id = Id(t), name = t.name, active = t.gameObject.activeInHierarchy,
                    position = V(t.position), palmDistance = Vector3.Distance(p, t.position),
                    selected = Id(t) == Id(h.GrabPoint),
                    pose = Alive(point) ? new
                    {
                        enabled = point.enabled, poserIndex = point.PoserIndex, handPoserIndex = h.PoserIndex,
                        leftAllowed = point.LeftHand, rightAllowed = point.RightHand,
                        checkDistance = point.CheckDistance, maxDistance = point.MaxDistance,
                        allowedAngle = point.AllowedAngleDifference, oneHandOnly = point.OneHandOnly,
                        lineGrab = point.IsLineGrab,
                        position = V(point.GetPoseWorldPosition(h.HandSide)),
                        rotation = Q(point.GetPoseWorldRotation(h.HandSide)),
                        handWorldAngle = Quaternion.Angle(h.HandWorldRotation, point.GetPoseWorldRotation(h.HandSide)),
                        handModelToPose = Alive(h.HandModel) ? (float?)Vector3.Distance(h.HandModel.position, point.GetPoseWorldPosition(h.HandSide)) : null,
                        observedNormalPointValid = GateFor(h, g, "GrabPointValid:" + Id(t) + ":0"),
                        controllerAngle = Quaternion.Angle(h.ControllerRotation, point.GetPoseWorldRotation(h.HandSide)),
                        palmAngle = Alive(h.Palm) ? (float?)Quaternion.Angle(h.Palm.rotation, point.GetPoseWorldRotation(h.HandSide)) : null
                    } : null
                });
            }
        var colliders = new List<object>();
        if (g.Colliders != null) foreach (var c in g.Colliders) if (Alive(c)) colliders.Add(ColliderDistance(c, p));
        var rb = g.Rigidbody;
        Releases.TryGetValue(Id(g), out var release);
        var anb = g.TryCast<ANBHVRGrabbable>();
        return new
        {
            id = Id(g), name = g.name, active = g.isActiveAndEnabled, position = V(g.transform.position),
            palmToOrigin = Vector3.Distance(p, g.transform.position),
            controllerToOrigin = Alive(h.ControllerHandTarget) ? (float?)Vector3.Distance(h.ControllerHandTarget.position, g.transform.position) : null,
            held = g.IsBeingHeld, socketed = g.IsSocketed, primaryGrabber = Id(g.PrimaryGrabber), canBeGrabbed = g.CanBeGrabbed,
            lockGrab = g.LockGrab, forceGrabbable = g.ForceGrabbable, forceGrabbing = g.IsBeingForcedGrabbed,
            requireLineOfSight = g.RequireLineOfSight, requiresGrabbable = g.RequiresGrabbable, requiredGrabbable = g.RequiresGrabbable ? Id(g.RequiredGrabbable) : 0,
            stabbing = g.IsStabbing, stabbed = g.IsStabbed, beingDestroyed = g.BeingDestroyed,
            holdType = g.HoldType.ToString(), grabControl = g.GrabControl.ToString(), poseType = g.PoseType.ToString(),
            overrideGrabTrigger = g.OverrideGrabTrigger, grabTrigger = g.GrabTrigger.ToString(),
            considerGrabPointAngle = g.ConsiderGrabPointAngle, useColliderClosestPoint = g.UseColliderClosestPoint,
            showGrabIndicator = g.ShowGrabIndicator, showTriggerIndicator = g.ShowTriggerGrabIndicator,
            elapsedSinceReleased = g.ElapsedSinceReleased,
            release = release == null ? null : new { age = Now - release.Time, frame = release.Frame, hand = release.Hand, count = release.Count },
            physics = Alive(rb) ? new { velocity = V(rb.linearVelocity), speed = rb.linearVelocity.magnitude, angularVelocity = V(rb.angularVelocity), kinematic = rb.isKinematic, detectCollisions = rb.detectCollisions } : null,
            anb = Alive(anb) ? new { grace = anb.handGrabGraceTime, justHandGrabbed = anb.justHandGrabbed, autoGrabbed = anb.AutoGrabbed, autoGrabOnHover = anb.AutoGrabOnHover } : null,
            observedCanHover = GateFor(h, g, "CanHover"), observedCanGrab = GateFor(h, g, "CanGrab"),
            observedLineOfSight = GateFor(h, g, "CheckLineOfSight"),
            bags = Bags(h, g), grabPoints = points, colliders
        };
    }

    static object[] Bags(HVRGrabberBase h, HVRGrabbable g)
    {
        var list = new List<object>();
        var seen = new HashSet<int>();
        void Add(HVRGrabbableBag b)
        {
            if (!Alive(b) || !seen.Add(Id(b))) return;
            var trigger = b.TryCast<HVRTriggerGrabbableBag>();
            var anb = b.TryCast<HVRANBTriggerGrabbableBag>();
            if (!bagShapes.TryGetValue(Id(b), out var shapes) || Now - shapes.at > 2)
            {
                if (bagShapes.Count > 32) bagShapes.Clear();
                shapes = (Now, b.GetComponentsInChildren<Collider>(true).ToArray());
                bagShapes[Id(b)] = shapes;
            }
            list.Add(new
            {
                id = Id(b), name = b.name, active = b.isActiveAndEnabled,
                maxDistanceAllowed = b.MaxDistanceAllowed, distanceSource = TransformInfo(b.DistanceSource),
                sourceToItemOrigin = Alive(g) && Alive(b.DistanceSource) ? (float?)Vector3.Distance(b.DistanceSource.position, g.transform.position) : null,
                sortMode = b.hvrSortMode.ToString(), closest = Id(b.ClosestGrabbable),
                validCount = b.ValidGrabbables?.Count ?? 0, allCount = b._allGrabbables?.Count ?? 0,
                targetInAll = Alive(g) ? (bool?)(b._allGrabbables?.Contains(g) ?? false) : null,
                targetValid = Alive(g) ? (bool?)(b.ValidGrabbables?.Contains(g) ?? false) : null,
                targetIgnored = Alive(g) ? (bool?)(b.IgnoredGrabbables?.Contains(g) ?? false) : null,
                useColliderDistance = Alive(trigger) ? (bool?)trigger.UseColliderDistance : null,
                anb = Alive(anb) ? new { fixTimer = anb.grabbableFixTimer, fixTimerStarted = anb.fixTimerStarted, releaseFixDistance = anb.releaseFixDistance, ghostHandsTime = anb.ghostHandsTime, ghostHandsStarted = anb.ghostHandsStarted } : null,
                // These colliders are attached to the bag hierarchy. Do not assume every
                // one is a detection volume; isTrigger, enabled and active are recorded.
                hierarchyColliders = Alive(g) ? null : shapes.colliders.Where(Alive).Select(Shape).ToArray()
            });
        }
        Add(h._grabBag);
        if (h.GrabBags != null) foreach (var bag in h.GrabBags) Add(bag);
        return list.ToArray();
    }

    internal static object Shape(Collider c)
    {
        if (!Alive(c)) return null;
        var sphere = c.TryCast<SphereCollider>();
        var box = c.TryCast<BoxCollider>();
        var capsule = c.TryCast<CapsuleCollider>();
        var scale = c.transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return new
        {
            id = Id(c), name = c.name, enabled = c.enabled, active = c.gameObject.activeInHierarchy, trigger = c.isTrigger,
            layer = c.gameObject.layer, scale = V(scale), rotation = Q(c.transform.rotation),
            boundsCenter = V(c.bounds.center), boundsExtents = V(c.bounds.extents),
            sphere = Alive(sphere) ? new { center = V(c.transform.TransformPoint(sphere.center)), localRadius = sphere.radius, worldRadius = sphere.radius * maxScale } : null,
            box = Alive(box) ? new { center = V(c.transform.TransformPoint(box.center)), localSize = V(box.size) } : null,
            capsule = Alive(capsule) ? new { center = V(c.transform.TransformPoint(capsule.center)), localRadius = capsule.radius, localHeight = capsule.height, axis = capsule.direction } : null
        };
    }
}
