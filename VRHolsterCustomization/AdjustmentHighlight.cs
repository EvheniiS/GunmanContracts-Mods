using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRHolsterCustomization
{
    // While a move gesture is held, the slot (and its mirrored pair) shows the game's own holster hologram in its
    // hover colour, as when a gun is brought to it, so you see where the gun will sit. The hologram is a child of the
    // socket, so it is already at the new position. Not shown on a slot with a gun or knife in it.
    static partial class VanillaHolsters
    {
        sealed class Lit
        {
            public Material Material;
            public Color Before;
            public bool WasEnabled;
        }

        static readonly Dictionary<IntPtr, Lit> lit = new();
        static readonly List<IntPtr> unlit = new();

        static void StartGesture()
        {
            if (!VRHolsterCustomizationMod.DebugOn) return;
            int holograms = 0;
            foreach (var pair in fades)
                if (pair.Value.Index == candidate.Index || partner != null && pair.Value.Index == partner.Index) holograms++;
            string mirrored = pairIndex < 0 ? "" : partner != null ? $", {keys[pairIndex]} mirrored" : $", {keys[pairIndex]} mirrored (its socket isn't in this scene)";
            VRHolsterCustomizationMod.Log.Msg($"hold on {keys[candidate.Index]}{mirrored}: {holograms} game hologram(s)");
        }

        static void ShowGesture(Color color)
        {
            ShowMarker(0, candidate, color);
            if (partner != null) ShowMarker(1, partner, color);
            else HideMarker(1);
            foreach (var pair in fades)
            {
                var f = pair.Value;
                var s = f.Index == candidate.Index ? candidate : partner != null && f.Index == partner.Index ? partner : null;
                if (Empty(s)) Light(pair.Key, f);
                else Unlight(pair.Key);
            }
        }

        static void HideGesture()
        {
            HideMarker(1);
            if (lit.Count == 0) return;
            unlit.Clear();
            unlit.AddRange(lit.Keys);
            foreach (var id in unlit) Unlight(id);
        }

        static void Light(IntPtr id, Fade f)
        {
            if (!VRHolsterCustomizationMod.Alive(f.Component)) return;
            var rend = f.Component.rend;
            if (!VRHolsterCustomizationMod.Alive(rend)) return;
            if (!lit.TryGetValue(id, out var l))
            {
                var material = rend.material;
                if (!VRHolsterCustomizationMod.Alive(material)) return;
                l = new Lit { Material = material, Before = material.color, WasEnabled = rend.enabled };
                lit.Add(id, l);
                if (VRHolsterCustomizationMod.DebugOn && !rend.gameObject.activeInHierarchy)
                    VRHolsterCustomizationMod.Log.Msg($"{keys[f.Index]} hologram '{rend.name}' is switched off by the game; only the marker shows");
            }
            // Every frame: this runs after the fade's own coroutine, so it wins until the gesture ends.
            rend.enabled = true;
            l.Material.color = TintColor(f.Hover);
        }

        // Back to exactly what the game had before, so its fade carries on from its own state.
        static void Unlight(IntPtr id)
        {
            if (!lit.TryGetValue(id, out var l)) return;
            lit.Remove(id);
            if (VRHolsterCustomizationMod.Alive(l.Material)) l.Material.color = l.Before;
            if (fades.TryGetValue(id, out var f) && VRHolsterCustomizationMod.Alive(f.Component) &&
                VRHolsterCustomizationMod.Alive(f.Component.rend))
                f.Component.rend.enabled = l.WasEnabled;
        }
    }
}
