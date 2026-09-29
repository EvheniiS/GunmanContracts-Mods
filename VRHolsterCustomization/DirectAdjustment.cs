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
        static MelonPreferences_Entry<bool> directAdjustment;
        static HVRHandGrabber[] adjustmentHands;
        static readonly AdjustmentHold[] holds = { new(), new() };
        static Socket candidate;
        static HVRHandGrabber adjustingHand;
        static float nextPulse;
        static bool moving, dirty;
        static Vector3 handStart, offsetStart;
        static GameObject marker;
        static Material markerMaterial;
        const float Reach = 0.12f;

        static void InitAdjustment()
        {
            var c = MelonPreferences.CreateCategory("VRHolsters_Adjustment", "VR Holsters: move by hand");
            directAdjustment = c.CreateEntry("Enabled", false, display_name: "Move empty holsters by hand",
                description: "Enable to reposition the six GAME holsters. Empty your hand and holster, hold grip + trigger within 12 cm for 2 seconds until the marker turns green, then move. Release either button to save. Blue = nearby, amber = hold, green = moving. Positions snap to 1 cm. Disable when finished. Mod-item back slots are separate.");
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
                if (!moving)
                {
                    if (Vector3.Distance(palm, candidate.Transform.position) > Reach) { EndAdjustment(); return; }
                    if (holds[adjustingHand.IsLeftHand ? 0 : 1].Mature(Time.unscaledTime))
                    {
                        moving = true;
                        handStart = candidate.Frame.InverseTransformPoint(palm);
                        int i = candidate.Index;
                        offsetStart = new Vector3(along[i].Value, up[i].Value, forward[i].Value);
                        adjustingHand.Controller?.Vibrate(0.65f, 0.12f, 160f);
                        VRHolsterCustomizationMod.Log.Msg($"Moving {keys[i]}: release grip or trigger to save");
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
                    int i = candidate.Index;
                    SetPosition(along[i], desired.x);
                    SetPosition(up[i], desired.y);
                    SetPosition(forward[i], desired.z);
                }
                ShowMarker(candidate, moving ? Color.green : new Color(1f, 0.65f, 0.1f));
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
                    nextPulse = Time.unscaledTime;
                    holds[1 - side].Cancel();
                    ShowMarker(candidate, new Color(1f, 0.65f, 0.1f));
                    return;
                }
            }
            if (nearest != null) ShowMarker(nearest, new Color(0.2f, 0.65f, 1f));
            else if (VRHolsterCustomizationMod.Alive(marker)) marker.SetActive(false);
        }

        static void SetPosition(MelonPreferences_Entry<int> entry, float cm)
        {
            // Preserve an existing manually configured value outside the usual range until moved inward.
            int value = Mathf.RoundToInt(Mathf.Clamp(cm, Mathf.Min(-50f, entry.Value), Mathf.Max(50f, entry.Value)));
            if (entry.Value == value) return;
            entry.Value = value;
            dirty = true;
        }

        internal static void EndAdjustment()
        {
            if (dirty)
            {
                dirty = false;
                try { MelonPreferences.Save(); VRHolsterCustomizationMod.Log.Msg("Holster position saved"); }
                catch (Exception e) { VRHolsterCustomizationMod.Log.Warning($"Holster position save failed: {e.Message}"); }
            }
            candidate = null; adjustingHand = null; moving = false;
            foreach (var hold in holds) hold.Cancel();
            if (VRHolsterCustomizationMod.Alive(marker)) marker.SetActive(false);
        }

        static void ShowMarker(Socket s, Color color)
        {
            if (!VRHolsterCustomizationMod.Alive(marker))
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return;
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "HolsterAdjustmentMarker";
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                Object.DontDestroyOnLoad(marker);
                marker.transform.localScale = Vector3.one * 0.035f;
                markerMaterial = new Material(shader);
                marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;
            }
            marker.transform.position = s.Transform.position;
            markerMaterial.SetColor("_BaseColor", color); markerMaterial.SetColor("_Color", color);
            marker.SetActive(true);
        }
    }
}
