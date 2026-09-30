using System;
using MelonLoader;
using UnityEngine;

namespace WeaponFramework
{
    // The framework's own test weapon (Sep 29 2026): a plain copy of The Range's crowbar (`Prop-Bluntweapon-Crowbar`,
    // blunt damage + the game's throw assist) as an arsenal entry, built only from the framework's own parts (Items,
    // Holsters). It is the reference for weapon mods and a second entry for testing. Off by default:
    // [WeaponFramework] TestEntries = true, then restart.
    // At most one crowbar exists: Retrieve brings a loose one back to the wall, or makes one if there is none.
    static class TestCrowbar
    {
        const string Id = "Crowbar";
        const string SourceName = "Prop-Bluntweapon-Crowbar";
        const float Mass = 3f;   // kg, as Billy Clubs

        static GameObject template;
        static GameObject item;

        internal static void Init()
        {
            Arsenal.Register(new ArsenalWeapon
            {
                Id = Id,
                DisplayName = "Crowbar",
                Description = "",
                Icon = LoadIcon(),
                OnShown = Show,
            });
            VRHolsterCustomization.Holsters.RegisterKind(new VRHolsterCustomization.HolsterKind
            {
                Id = Id,
                IsMine = go => WeaponFrameworkMod.Alive(go) && WeaponFrameworkMod.Alive(item) && go.Pointer == item.Pointer,
                Spawn = () => Make(Vector3.zero, Quaternion.identity),
            });
        }

        // Panel picture: grey render with an outline on transparent, 1364x635 like the game's gunicon_* sprites
        // (BlenderRefs/out/crowbar_icon.png, embedded).
        static Texture2D LoadIcon()
        {
            try
            {
                using var stream = typeof(TestCrowbar).Assembly.GetManifestResourceStream("WeaponFramework.crowbar_icon.png");
                if (stream == null) return null;
                using var bytes = new System.IO.MemoryStream(); stream.CopyTo(bytes);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes.ToArray(), false)) return null;
                tex.name = "Crowbar_icon"; tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception e) { WeaponFrameworkMod.Log.Warning($"test crowbar icon: {e.Message}"); return null; }
        }

        // The template is copied the first time The Range loads (the crowbar exists only there).
        internal static void Scene()
        {
            if (WeaponFrameworkMod.Alive(template)) return;
            var src = GameObject.Find(SourceName);
            if (src == null) return;
            template = Items.MakeTemplate(src, "WF-Crowbar");
            WeaponFrameworkMod.Log.Msg("test crowbar: template copied from The Range's crowbar");
        }

        static GameObject Make(Vector3 pos, Quaternion rot)
        {
            if (!WeaponFrameworkMod.Alive(template)) return null;
            if (WeaponFrameworkMod.Alive(item)) return null;   // one crowbar at a time
            item = Items.Spawn(template, "WF-Crowbar", pos, rot);
            // The Range's crowbar weighs 8 kg: HVR's hand joint can't keep up with it, so it swims behind the hand
            // ("floaty", 0.2.1 test). Billy Clubs use 3 kg and feel right. The prop's physics script re-applies its own
            // mass (ANBGeneratePhysics.RBMass), so set both.
            var rb = item.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = Mass;
            var phys = item.GetComponent<Il2Cpp.ANBGeneratePhysics>();
            if (phys != null) phys.RBMass = Mass;
            VRHolsterCustomization.Holsters.Watch(item);
            return item;
        }

        // Retrieve: the crowbar lies across the slot like the rifles (0.2.0 hung it upright and it stuck out past the
        // slot), hook along the wall's up so the bend lies flat on the board, 3 cm in front of it.
        static void Show(Transform mount)
        {
            if (!WeaponFrameworkMod.Alive(template)) { WeaponFrameworkMod.Log.Warning("test crowbar: no template yet"); return; }
            string how;
            if (!WeaponFrameworkMod.Alive(item)) { Make(mount.position, mount.rotation); how = "new"; }
            else if (Items.IsHeld(item) || VRHolsterCustomization.Holsters.Holds(item)) { WeaponFrameworkMod.Log.Msg("test crowbar: you carry it - nothing hung on the wall"); return; }
            else how = Items.IsHung(item) ? "still on the wall" : "brought back";
            if (!WeaponFrameworkMod.Alive(item)) return;

            Items.Measure(item, out var c, out var axis, out _);
            var bend = Items.Widest(item, axis);
            // Mount axes (measured): up = along the wall, forward = up, right = into the wall (the board is ~6 cm in).
            // Item space -> mount space: shaft along the wall, hook bending up.
            var rot = Quaternion.Inverse(Quaternion.LookRotation(bend, axis));   // bend -> forward, shaft -> up
            var pos = Vector3.right * 0.03f - rot * c;
            Items.Hang(item, mount, pos, rot);
            var it = item.transform;
            WeaponFrameworkMod.Log.Msg($"test crowbar on the wall ({how}): hook end points {WeaponFrameworkMod.V(it.TransformDirection(axis))}, bend {WeaponFrameworkMod.V(it.TransformDirection(bend))} (world; the settled line follows)");
        }
    }
}
