using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    static partial class VanillaHolsters
    {
        static MelonPreferences_Entry<bool> directAdjustment, mirrorPairs;
        static HVRHandGrabber[] adjustmentHands;
        static readonly AdjustmentHold[] holds = { new(), new() };
        static Socket candidate, partner;
        static int pairIndex = -1; // mirrored with the candidate, even if its socket is missing in this scene
        static HVRHandGrabber adjustingHand;
        static float nextPulse, nextTick;
        static bool moving, dirty;
        static Vector3 handStart, offsetStart;
        // [0] the slot being moved or the nearest one, [1] its mirrored pair.
        static readonly GameObject[] markers = new GameObject[2];
        static readonly Material[] markerMaterials = new Material[2];
        static readonly Socket[] markerSlots = new Socket[2];
        static readonly Color NearColor = new(0.2f, 0.65f, 1f), HoldColor = new(1f, 0.65f, 0.1f), MoveColor = Color.green;
        const float Reach = 0.12f;

        static void InitAdjustment()
        {
            var c = MelonPreferences.CreateCategory("VRHolsters_Adjustment", "VR Holsters: move by hand");
            directAdjustment = c.CreateEntry("Enabled", false, display_name: "Move empty holsters by hand",
                description: "Enable to reposition the six GAME holsters. Empty your hand and holster, hold grip + trigger within 12 cm for 2 seconds until the marker turns green, then move. Release either button to save. Blue = nearby, amber = hold, green = moving; the holster's own hologram shows while you hold. Positions snap to 1 cm. Disable when finished. Mod-item back slots are separate.");
            mirrorPairs = c.CreateEntry("MirrorPairs", true, display_name: "Move hip and knife pairs together",
                description: "On: moving a hip or knife holster by hand moves the other side's one mirrored (same height and forward/back, opposite side). If the two were set apart, the other one jumps to the mirrored spot as soon as you start moving. Off: each holster moves on its own. Back holsters always move alone. Position rows and their Reset stay per holster.");
        }

        static bool Empty(Socket s) => s != null && VRHolsterCustomizationMod.Alive(s.Transform) &&
            s.Transform.gameObject.activeInHierarchy && VRHolsterCustomizationMod.Alive(s.Frame) &&
            VRHolsterCustomizationMod.Alive(s.Native) && !s.Native.IsGrabbing &&
            !VRHolsterCustomizationMod.Alive(s.Native.GrabbedTarget) && !VRHolsterCustomizationMod.Alive(s.Native.grabbedObject);

        static bool Free(HVRHandGrabber h) => VRHolsterCustomizationMod.Alive(h) && h.gameObject.activeInHierarchy &&
            !h.IsGrabbing && !VRHolsterCustomizationMod.Alive(h.GrabbedTarget) && !h.IsForceGrabbing;

        static Vector3 Palm(HVRHandGrabber h) => VRHolsterCustomizationMod.Alive(h.Palm) ? h.Palm.position : h.transform.position;
        static bool Held(HVRHandGrabber h) => h.Controller != null &&
            h.Controller.GripButtonState.Active && h.Controller.TriggerButtonState.Active;

        // Hip 0/1 and knife 2/3 are pairs; the back sockets move alone.
        static int Pair(int index) => index < 4 ? index ^ 1 : -1;

        static void AdjustByHand()
        {
            var game = ANBStaticGameManager.ANBmain;
            if (!directAdjustment.Value || Time.timeScale <= 0 || (VRHolsterCustomizationMod.Alive(game) && game.Paused))
            {
                EndAdjustment();
                return;
            }
            if (adjustmentHands == null || adjustmentHands.Length == 0 || !VRHolsterCustomizationMod.Alive(adjustmentHands[0]))
            {
                foreach (var pair in sockets)
                    if (VRHolsterCustomizationMod.Alive(pair.Value.Transform))
                    {
                        adjustmentHands = pair.Value.Transform.root.GetComponentsInChildren<HVRHandGrabber>(false);
                        break;
                    }
            }
            if (adjustmentHands == null) return;

            if (candidate != null)
            {
                if (!Empty(candidate) || !Free(adjustingHand) || !Held(adjustingHand)) { EndAdjustment(); return; }
                var palm = Palm(adjustingHand);
                int i = candidate.Index;
                if (!moving)
                {
                    if (Vector3.Distance(palm, candidate.Transform.position) > Reach) { EndAdjustment(); return; }
                    if (holds[adjustingHand.IsLeftHand ? 0 : 1].Mature(Time.unscaledTime))
                    {
                        moving = true;
                        handStart = candidate.Frame.InverseTransformPoint(palm);
                        offsetStart = new Vector3(along[i].Value, up[i].Value, forward[i].Value);
                        adjustingHand.Controller?.Vibrate(0.65f, 0.12f, 160f);
                        nextTick = Time.unscaledTime + 0.15f; // let the start pulse finish before the first tick
                        VRHolsterCustomizationMod.Log.Msg(pairIndex >= 0
                            ? $"Moving {keys[i]} with {keys[pairIndex]} mirrored: release grip or trigger to save"
                            : $"Moving {keys[i]}: release grip or trigger to save");
                    }
                    else if (Time.unscaledTime >= nextPulse)
                    {
                        nextPulse = Time.unscaledTime + 0.5f;
                        adjustingHand.Controller?.Vibrate(0.15f, 0.025f, 120f);
                    }
                }
                if (moving)
                {
                    var delta = candidate.Frame.InverseTransformPoint(palm) - handStart;
                    // Measure relative to the body frame, so locomotion/turning does not shift the calibration.
                    var desired = offsetStart + delta * 100f;
                    bool stepped = SetPosition(along[i], desired.x) | SetPosition(up[i], desired.y) | SetPosition(forward[i], desired.z);
                    // The pair always ends up mirrored, even if it was set apart before (it shows while you hold).
                    if (pairIndex >= 0) Mirror(i, pairIndex);
                    // A light tick per centimetre step, like a detent.
                    if (stepped && Time.unscaledTime >= nextTick)
                    {
                        nextTick = Time.unscaledTime + 0.04f;
                        adjustingHand.Controller?.Vibrate(0.1f, 0.01f, 200f);
                    }
                }
                ShowGesture(moving ? MoveColor : HoldColor);
                return;
            }

            Socket nearest = null;
            float nearestDistance = Reach;
            foreach (var h in adjustmentHands)
            {
                if (!VRHolsterCustomizationMod.Alive(h)) continue;
                int side = h.IsLeftHand ? 0 : 1;
                bool held = Held(h);
                if (!Free(h)) { holds[side].Cancel(); continue; }
                Socket near = null;
                float distance = Reach;
                foreach (var pair in sockets)
                {
                    var s = pair.Value;
                    if (!Empty(s)) continue;
                    float d = Vector3.Distance(Palm(h), s.Transform.position);
                    if (d < distance) { distance = d; near = s; }
                }
                if (near != null && distance < nearestDistance) { nearestDistance = distance; nearest = near; }
                if (holds[side].TryStart(held, near != null, Time.unscaledTime))
                {
                    candidate = near; adjustingHand = h;
                    pairIndex = mirrorPairs.Value ? Pair(near.Index) : -1;
                    partner = FindSlot(pairIndex);
                    nextPulse = Time.unscaledTime;
                    holds[1 - side].Cancel();
                    StartGesture();
                    ShowGesture(HoldColor);
                    return;
                }
            }
            HideGesture();
            if (nearest != null) ShowMarker(0, nearest, NearColor);
            else HideMarker(0);
        }

        // Preserve an existing manually configured value outside the usual range until moved inward.
        static bool SetPosition(MelonPreferences_Entry<int> entry, float cm) =>
            SetValue(entry, Mathf.RoundToInt(Mathf.Clamp(cm, Mathf.Min(-50f, entry.Value), Mathf.Max(50f, entry.Value))));

        static bool SetValue(MelonPreferences_Entry<int> entry, int value)
        {
            if (entry.Value == value) return false;
            entry.Value = value;
            dirty = true;
            return true;
        }

        // Mirrored across the belt: the same height and depth, the opposite side.
        static void Mirror(int from, int to)
        {
            SetValue(along[to], -along[from].Value);
            SetValue(up[to], up[from].Value);
            SetValue(forward[to], forward[from].Value);
        }

        internal static void EndAdjustment()
        {
            if (dirty)
            {
                dirty = false;
                try
                {
                    MelonPreferences.Save();
                    string saved = candidate == null ? "" : Offsets(candidate.Index) + (pairIndex >= 0 ? "; " + Offsets(pairIndex) : "");
                    VRHolsterCustomizationMod.Log.Msg($"Holster position saved: {saved}");
                }
                catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"Holster position save failed: {e.Message}"); }
            }
            candidate = partner = null; pairIndex = -1; adjustingHand = null; moving = false;
            foreach (var hold in holds) hold.Cancel();
            HideGesture();
            HideMarker(0);
        }

        static string Offsets(int i) => $"{keys[i]} left/right {along[i].Value}, height {up[i].Value}, forward {forward[i].Value} cm";

        static void ShowMarker(int which, Socket s, Color color)
        {
            if (s == null || !VRHolsterCustomizationMod.Alive(s.Transform)) { HideMarker(which); return; }
            if (!VRHolsterCustomizationMod.Alive(markers[which]))
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "HolsterAdjustmentMarker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                Object.DontDestroyOnLoad(marker);
                marker.transform.localScale = Vector3.one * 0.035f;
                markerMaterials[which] = new Material(shader);
                marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterials[which];
                markers[which] = marker;
            }
            markerSlots[which] = s;
            markers[which].transform.position = s.Transform.position;
            markerMaterials[which].SetColor("_BaseColor", color); markerMaterials[which].SetColor("_Color", color);
            markers[which].SetActive(true);
        }

        static void HideMarker(int which)
        {
            markerSlots[which] = null;
            if (VRHolsterCustomizationMod.Alive(markers[which])) markers[which].SetActive(false);
        }

        // After this frame's offsets are applied, so a marker never trails the socket it marks.
        static void FollowMarkers()
        {
            for (int i = 0; i < markers.Length; i++)
            {
                var s = markerSlots[i];
                if (s != null && VRHolsterCustomizationMod.Alive(markers[i]) && VRHolsterCustomizationMod.Alive(s.Transform))
                    markers[i].transform.position = s.Transform.position;
            }
        }
    }
}
