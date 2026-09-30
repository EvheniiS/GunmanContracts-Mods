using System;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.HandPoser;
using Il2CppHurricaneVR.Framework.Core.UI;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModSettings
{
    // Reuse the game's menu pose, but keep our ray/rendering and input scoped to this board.
    // showVRpointer also swaps hand models and changes global locomotion flags, so do not call it.
    internal static class VRInteraction
    {
        static HVRHandPoser menuPose;
        static float nextSearch;
        static readonly HVRHandGrabber[] posed = new HVRHandGrabber[2];
        static readonly HVRHandPoser[] previous = new HVRHandPoser[2];
        static readonly LineRenderer[] lasers = new LineRenderer[2];

        internal static bool Free(HVRHandGrabber h) => Panel.Alive(h) && h.gameObject.activeInHierarchy && !h.IsGrabbing &&
            !Panel.Alive(h.GrabbedTarget) && !h.IsForceGrabbing;

        internal static Vector3 Direction(HVRHandGrabber h)
        {
            var game = ANBStaticGameManager.ANBmain;
            var ui = Panel.Alive(game) ? game.UIManager : null;
            var line = Panel.Alive(ui) ? (h.IsLeftHand ? ui.PointerLeft : ui.PointerRight) : null;
            var pointer = Panel.Alive(line) ? line.GetComponent<HVRUIPointer>() : null;
            if (Panel.Alive(pointer) && Panel.Alive(pointer.Camera)) return pointer.Camera.transform.forward;
            return Panel.Alive(h.TrackedController) ? h.TrackedController.forward : h.transform.forward;
        }

        internal static void Show(int i, HVRHandGrabber h, bool active, Vector3 start, Vector3 end, Shader shader)
        {
            active &= Free(h);
            if (active && !Panel.Alive(menuPose) && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 2f;
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<ANBVrHandProximityPoser>()))
                {
                    var p = o.TryCast<ANBVrHandProximityPoser>();
                    if (Panel.Alive(p) && p.isVRHandPointer && Panel.Alive(p.Poser)) { menuPose = p.Poser; break; }
                }
                ModSettingsMod.Dbg(menuPose != null ? "using native menu pointing pose" : "native menu pose not found yet");
            }
            if (Panel.Alive(posed[i]) && (!active || posed[i].Pointer != h.Pointer)) ReleasePose(i);
            if (active && Panel.Alive(menuPose))
            {
                if (!Panel.Alive(posed[i]))
                {
                    posed[i] = h;
                    previous[i] = Panel.Alive(h.HandAnimator) ? h.HandAnimator.OverridePoser : null;
                }
                h.SetAnimatorOverridePose(menuPose);
            }
            if (active && !Panel.Alive(lasers[i]))
            {
                var go = new GameObject("ModSettingsLaser");
                Object.DontDestroyOnLoad(go);
                var line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = new Material(shader);
                line.sharedMaterial.SetColor("_BaseColor", new Color(0.3f, 0.8f, 1f));
                line.sharedMaterial.SetColor("_Color", new Color(0.3f, 0.8f, 1f));
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.0015f; line.endWidth = 0.0025f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                lasers[i] = line;
            }
            if (Panel.Alive(lasers[i]))
            {
                lasers[i].enabled = active;
                if (active) { lasers[i].SetPosition(0, start); lasers[i].SetPosition(1, end); }
            }
        }

        static void ReleasePose(int i)
        {
            var h = posed[i];
            posed[i] = null;
            if (Panel.Alive(h) && Panel.Alive(h.HandAnimator) &&
                Panel.Alive(h.HandAnimator.OverridePoser) && Panel.Alive(menuPose) &&
                h.HandAnimator.OverridePoser.Pointer == menuPose.Pointer)
            {
                var game = ANBStaticGameManager.ANBmain;
                var native = Panel.Alive(game) ? (h.IsLeftHand ? game.PoserOverrideLeft : game.PoserOverrideRight) : previous[i];
                h.SetAnimatorOverridePose(Panel.Alive(native) ? native : null);
            }
            previous[i] = null;
        }

        internal static void Close()
        {
            for (int i = 0; i < 2; i++)
            {
                ReleasePose(i);
                if (Panel.Alive(lasers[i])) lasers[i].enabled = false;
            }
        }
    }
}
