using System;
using System.Collections.Generic;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.Sockets;
using Il2CppHurricaneVR.Framework.Core.Utils;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    // The game's real sockets are moved, so their grab volumes and holograms follow the setting.
    static partial class VanillaHolsters
    {
        static readonly string[] keys = { "LeftHip", "RightHip", "LeftKnife", "RightKnife", "LeftBack", "RightBack" };
        static readonly MelonPreferences_Entry<int>[] up = new MelonPreferences_Entry<int>[6];
        static readonly MelonPreferences_Entry<int>[] along = new MelonPreferences_Entry<int>[6];
        static readonly MelonPreferences_Entry<int>[] forward = new MelonPreferences_Entry<int>[6];
        static MelonPreferences_Entry<int> allUp;
        static MelonPreferences_Entry<string> color;

        sealed class Socket
        {
            public Transform Transform;
            public Transform Frame;
            public Vector3 Original;
            public int Index;
            public HVRSocket Native;
        }
        sealed class Fade
        {
            public HVRANBSocketHoverFade Component;
            public Color Base, Normal, Hover, Invisible;
        }

        static readonly Dictionary<IntPtr, Socket> sockets = new();
        static readonly Dictionary<IntPtr, Fade> fades = new();
        static float nextSearch, searchDeadline;
        static Color selectedColor;
        static bool originalColors;

        internal static void Init()
        {
            var positions = MelonPreferences.CreateCategory("VRHolsters_Positions", "VR Holster Customization: positions");
            allUp = positions.CreateEntry("AllUpCm", 0, display_name: "All holsters: height (cm)", description: "Move every game holster up or down in centimetres. Negative moves down.");
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                string label = (i % 2 == 0 ? "Left " : "Right ") + (i < 2 ? "hip" : i < 4 ? "knife" : "back");
                up[i] = positions.CreateEntry(key + "UpCm", 0, display_name: label + ": height (cm)", description: $"Move the {label} holster up (+) or down (-) from its original position. Centimetres; updates live.");
                along[i] = positions.CreateEntry(key + "AlongCm", 0, display_name: label + ": left/right (cm)", description: $"Move the {label} holster right (+) or left (-) along its body axis. Centimetres; updates live.");
                forward[i] = positions.CreateEntry(key + "ForwardCm", 0, display_name: label + ": forward/back (cm)", description: $"Move the {label} holster forward (+) or back (-) along its body axis. Centimetres; updates live.");
            }
            var visual = MelonPreferences.CreateCategory("VRHolsters_Visual", "VR Holster Customization: color");
            color = visual.CreateEntry("Color", "Default", description: "Holster hologram color: Default preserves the game's original colors; #RRGGBB applies a custom color. Updates live.");
            ReadColor();
            color.OnEntryValueChanged.Subscribe((_, _) =>
            {
                ReadColor();
                foreach (var pair in fades) Tint(pair.Value);
            });
            InitAdjustment();
        }

        internal static void Scene()
        {
            EndAdjustment();
            adjustmentHands = null;
            nextSearch = Time.time;
            searchDeadline = Time.time + 15f;
            // A rig may survive a scene load. Keep its original positions so offsets never stack.
            var dead = new List<IntPtr>();
            foreach (var pair in sockets) if (!VRHolsterCustomizationMod.Alive(pair.Value.Transform)) dead.Add(pair.Key);
            foreach (var id in dead) sockets.Remove(id);
            dead.Clear();
            foreach (var pair in fades) if (!VRHolsterCustomizationMod.Alive(pair.Value.Component)) dead.Add(pair.Key);
            foreach (var id in dead) fades.Remove(id);
        }

        internal static void Tick()
        {
            if (Time.time >= nextSearch && Time.time <= searchDeadline && (sockets.Count < 6 || fades.Count < 4))
            {
                nextSearch = Time.time + 1f;
                Discover();
            }
        }

        internal static void ApplyPositions()
        {
            AdjustByHand();
            // Live settings; reapply because the waist rig may adjust its children during a frame.
            foreach (var pair in sockets)
            {
                var s = pair.Value;
                if (!VRHolsterCustomizationMod.Alive(s.Transform)) continue;
                var parent = s.Transform.parent;
                if (!VRHolsterCustomizationMod.Alive(parent) || !VRHolsterCustomizationMod.Alive(s.Frame)) continue;
                float side = along[s.Index].Value / 100f;
                float height = (allUp.Value + up[s.Index].Value) / 100f;
                float depth = forward[s.Index].Value / 100f;
                var shift = s.Frame.Pointer == parent.Pointer
                    ? new Vector3(side, height, depth)
                    : parent.InverseTransformVector(s.Frame.TransformVector(new Vector3(side, height, depth)));
                var want = s.Original + shift;
                if ((s.Transform.localPosition - want).sqrMagnitude > 0.0000001f) s.Transform.localPosition = want;
            }
        }

        static void Discover()
        {
            foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<HVRANBSocketHoverFade>(), FindObjectsSortMode.None))
            {
                var fade = obj.TryCast<HVRANBSocketHoverFade>();
                if (!VRHolsterCustomizationMod.Alive(fade)) continue;
                var socket = fade.holster;
                if (!VRHolsterCustomizationMod.Alive(socket)) socket = fade.GetComponentInParent<HVRSocket>();
                if (!VRHolsterCustomizationMod.Alive(socket)) continue;
                int index = Classify(Path(socket.transform));
                if (index < 0) index = Classify(Path(fade.transform));
                if (index < 0 || index >= 4) continue;
                if (!HasSlot(index)) AddSocket(socket.transform, index);
                if (!fades.ContainsKey(fade.Pointer))
                {
                    var f = new Fade { Component = fade, Base = fade.colorBase, Normal = fade.colorNormal,
                        Hover = fade.colorHover, Invisible = fade.colorInvisible };
                    fades.Add(fade.Pointer, f);
                    Tint(f);
                }
            }
            // Some belt sockets have no hover fade component in a scene.
            foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<HVRSocket>(), FindObjectsSortMode.None))
            {
                var socket = obj.TryCast<HVRSocket>();
                if (!VRHolsterCustomizationMod.Alive(socket) || !socket.gameObject.activeInHierarchy) continue;
                int index = Classify(Path(socket.transform));
                if (index >= 0 && index < 4 && !HasSlot(index)) AddSocket(socket.transform, index);
            }
            foreach (var obj in Object.FindObjectsByType(Il2CppType.Of<HVRShoulderSocket>(), FindObjectsSortMode.None))
            {
                var socket = obj.TryCast<HVRShoulderSocket>();
                if (!VRHolsterCustomizationMod.Alive(socket) || !socket.gameObject.activeInHierarchy) continue;
                if (socket.leftShoulder) AddSocket(socket.transform, 4);
                else if (socket.rightShoulder) AddSocket(socket.transform, 5);
            }
        }

        static int Classify(string path)
        {
            int at = path.LastIndexOf("Holster", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            string slotPath = path.Substring(at);
            bool left = slotPath.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
            bool right = slotPath.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!left && !right)
            {
                left = path.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;
                right = path.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (!left && !right) return -1;
            if (path.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) >= 0) return left ? 4 : 5;
            if (path.IndexOf("Waist", StringComparison.OrdinalIgnoreCase) < 0) return -1;
            bool knife = path.IndexOf("Knife", StringComparison.OrdinalIgnoreCase) >= 0;
            return knife ? (left ? 2 : 3) : (left ? 0 : 1);
        }

        static void AddSocket(Transform transform, int index)
        {
            if (sockets.ContainsKey(transform.Pointer)) return;
            var frame = transform.parent;
            if (index < 4)
                for (var p = transform.parent; p != null; p = p.parent)
                    if (p.name == "Holsters" && p.parent != null && p.parent.name == "Waist") { frame = p; break; }
            sockets.Add(transform.Pointer, new Socket { Transform = transform, Frame = frame,
                Original = transform.localPosition, Index = index, Native = transform.GetComponent<HVRSocket>() });
            VRHolsterCustomizationMod.Log.Msg($"{keys[index]} holster at {Path(transform)}: original local {VRHolsterCustomizationMod.V(transform.localPosition)}");
        }

        static bool HasSlot(int index)
        {
            foreach (var pair in sockets)
                if (pair.Value.Index == index && VRHolsterCustomizationMod.Alive(pair.Value.Transform)) return true;
            return false;
        }

        static string Path(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        static void ReadColor()
        {
            originalColors = string.Equals(color.Value?.Trim(), "Default", StringComparison.OrdinalIgnoreCase);
            if (originalColors) return;
            if (!ColorUtility.TryParseHtmlString(color.Value?.Trim(), out selectedColor))
            {
                originalColors = true;
                VRHolsterCustomizationMod.Log.Warning($"Color '{color.Value}' is invalid; using original game colors");
            }
        }

        static Color Recolor(Color original, Color hue)
        {
            float brightness = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
            return new Color(hue.r * brightness, hue.g * brightness, hue.b * brightness, original.a);
        }

        internal static Color TintColor(Color original) => color == null || originalColors ? original : Recolor(original, selectedColor);

        static void Tint(Fade f)
        {
            if (!VRHolsterCustomizationMod.Alive(f.Component)) return;
            f.Component.colorBase = TintColor(f.Base);
            f.Component.colorNormal = TintColor(f.Normal);
            f.Component.colorHover = TintColor(f.Hover);
            f.Component.colorInvisible = TintColor(f.Invisible);
            var rend = f.Component.rend;
            if (!VRHolsterCustomizationMod.Alive(rend) || !VRHolsterCustomizationMod.Alive(rend.material)) return;
            var current = rend.material.color;
            // The fade script owns visibility. Only adjust hue, leaving its current brightness and alpha.
            if (originalColors)
            {
                // Restore hue immediately while preserving the fade's current brightness and alpha.
                float peak = Mathf.Max(f.Normal.r, Mathf.Max(f.Normal.g, f.Normal.b));
                rend.material.color = peak > 0 ? Recolor(current, f.Normal / peak) : f.Normal;
            }
            else rend.material.color = Recolor(current, selectedColor);
        }
    }

    // Small optional API for a mod that draws its own holster hologram.
    public static class HolsterColors
    {
        public static Color Tint(Color original) => VanillaHolsters.TintColor(original);
    }
}
