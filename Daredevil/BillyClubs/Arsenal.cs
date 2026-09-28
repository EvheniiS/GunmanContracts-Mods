using System;
using System.IO;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

// Weapon Framework is optional: without it the clubs come from the spawn key only.
[assembly: MelonOptionalDependencies("WeaponFramework")]

namespace BillyClubs
{
    // Billy Clubs on the game's arsenal panel (The Range), through the Weapon Framework mod: page to "Billy Clubs",
    // press Retrieve, and the pair hangs on the wall slot, tip down, ready to take. The wall shows the clubs you don't
    // already carry: loose clubs are brought back to it, new ones fill up to two. Clubs left there stay on the slot
    // (it slides back into the wall with them) and are there again on the next retrieve.
    public partial class BillyClubsMod
    {
        internal static MelonPreferences_Entry<string> WallLayout, WallShift;
        internal static MelonPreferences_Entry<float> WallAngle, WallSpacing;

        static readonly Slot[] WallSlots = { new() { Name = "wall-left", Wall = true }, new() { Name = "wall-right", Wall = true } };

        static void InitArsenal(MelonPreferences_Category c)
        {
            WallLayout = c.CreateEntry("WallLayout", "Diagonal", description: "How the pair hangs on the arsenal wall: Diagonal (two parallel clubs, one above the other, tilted by WallAngle), Upright (side by side, tip down) or Cross (an X). Used on the next retrieve.");
            WallAngle = c.CreateEntry("WallAngle", 45f, description: "Diagonal layout: tilt of the clubs from horizontal, in degrees (grip lower left, tip upper right).");
            WallSpacing = c.CreateEntry("WallSpacing", 0.1f, description: "Distance between the two clubs on the arsenal wall (Diagonal and Upright), in metres.");
            WallShift = c.CreateEntry("WallShift", "0,0,0", description: "Moves the pair on the arsenal wall, in metres, as right,up,out-from-the-board. Used on the next retrieve.");
            bool present = false;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "WeaponFramework") { present = true; break; }
            if (!present) { Log.Msg("Weapon Framework not installed - no arsenal entry (spawn key only)"); return; }
            try { RegisterArsenal(); }
            catch (Exception e) { Log.Warning($"arsenal entry failed: {e.Message}"); }
        }

        // Kept apart so nothing touches Weapon Framework types unless its DLL is loaded.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RegisterArsenal()
        {
            WeaponFramework.Arsenal.Register(new WeaponFramework.ArsenalWeapon
            {
                Id = "BillyClubs",
                DisplayName = "Billy Clubs",
                Description = "",   // the panel draws it over the name (the game's own weapons leave it empty)
                Icon = LoadIcon(),
                OnShown = ShowOnWall,
                OnSettled = WallSettled,
                OnHidden = () => { if (Dbg) Log.Msg("arsenal: another weapon retrieved - clubs left on the wall go in with the slot"); },
            });
        }

        static Texture2D LoadIcon()
        {
            try
            {
                using var stream = Asset("billy_clubs_icon.png");
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes.ToArray(), false)) return null;
                tex.name = "BillyClubs_icon"; tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception e) { Log.Warning($"arsenal icon: {e.Message}"); return null; }
        }

        // Retrieve pressed on the Billy Clubs entry: `mount` is the slot's gun position.
        static void ShowOnWall(Transform mount)
        {
            if (!Alive(Template)) { Log.Warning("arsenal: no club template yet (it is copied from The Range's crowbar)"); return; }
            PlaceWallAnchors(mount);

            int recalled = 0, n = 0;
            foreach (var k in Clubs)
            {
                if (k.In != null || !Alive(k.Go) || IsHandHeld(k)) continue;
                var free = FreeWallSlot();
                if (free == null) break;
                try { if (IsHeld(k.Grab)) k.Grab.ForceRelease(); } catch { }
                EndFlight(k, null, null); k.Held = false;
                Holster(k, free, false);
                recalled++;
            }
            int alive = 0;
            foreach (var k in Clubs) if (Alive(k.Go)) alive++;
            foreach (var s in WallSlots)
            {
                if (Alive(s.Club?.Go)) continue;
                if (alive >= 2) break;
                var k = NewClub("BillyClub-" + s.Name, s.Anchor.position, s.Anchor.rotation);
                Holster(k, s, false);
                n++; alive++;
            }
            int on = 0;
            foreach (var s in WallSlots) if (Alive(s.Club?.Go)) on++;
            Log.Msg($"arsenal: {on} club(s) on the wall ({n} new, {recalled} brought back)");
        }

        // Where the pair goes. The slot slides in with an animation after Retrieve (0.3.2 log: the gun position rests at
        // y 1.46, but is at 0.87 when Retrieve fires), so:
        // 1. At once (the clubs are spawned and slide in with it, as the slot's children): a layout in the slot's own
        //    frame. The gun position's axes are rigid with the wall: its up runs along the wall, its forward points up,
        //    its right is the wall normal (0.2.0 log: mount up (1,0,0) fwd (0,1,0); the player stood on -Z).
        // 2. When the wall's switch animation ends (Weapon Framework OnSettled; a 5 s timer if that never comes): the
        //    board depth. A ray along the wall normal counts only if it hits 0-15 cm behind the gun position; the
        //    wall face around the board window ('Plane', 4.5 x 2.55 m) is further out and is ignored. Otherwise the
        //    board is taken as BoardBehind behind the gun position (first test: an upright club 6 cm out from the gun
        //    position hung ~8 cm off the board).
        // History: 0.3.1 took "along the wall" from the line to the player's head (pair 43 deg off the board); 0.3.2
        // "fitted" 0.3 s after Retrieve, before the slide had even started, against that wall face: 17 cm out.
        const float ClubRadius = 0.0175f, BoardGap = 0.005f, BoardBehind = 0.02f;
        // The board's middle is a little above the gun position (first test: an upright club centred on it reached the
        // board's top edge and hung ~10 cm into the terminal below).
        const float PairLift = 0.05f;
        static Transform WallMount;
        static float WallSettleUntil;

        struct WallFrame { public Vector3 Right, Up, Out; }

        // The wall's axes from the gun position: Out = towards the player, Up = up the wall, Right = the viewer's right.
        static WallFrame FrameOf(Transform mount)
        {
            var f = new WallFrame { Out = -mount.right, Up = mount.forward };
            if (Mathf.Abs(Vector3.Dot(f.Up, Vector3.up)) < 0.7f) f.Up = Vector3.up;   // not the frame we measured
            else if (Vector3.Dot(f.Up, Vector3.up) < 0) f.Up = -f.Up;
            var cam = Camera.main;
            if (cam != null && Vector3.Dot(cam.transform.position - mount.position, f.Out) < 0) f.Out = -f.Out;
            f.Out = Vector3.ProjectOnPlane(f.Out, f.Up).normalized;
            f.Right = Vector3.Cross(f.Up, -f.Out);   // facing the wall (forward = -Out): right = up x forward
            return f;
        }

        static Vector3 PairCentre(Transform mount, WallFrame f, float behind)
        {
            var shift = ParseV(WallShift.Value, Vector3.zero);
            return mount.position - f.Out * behind + f.Right * shift.x + f.Up * (PairLift + shift.y) + f.Out * shift.z;
        }

        static void PlaceWallAnchors(Transform mount)
        {
            WallMount = mount;
            WallSettleUntil = Time.time + 5f;
            var f = FrameOf(mount);
            LayWallPair(mount, f, PairCentre(mount, f, BoardBehind));
            if (Dbg) Log.Msg($"arsenal: {WallLayout.Value} pair laid out on the slot, right {V(f.Right)} up {V(f.Up)} out {V(f.Out)}");
        }

        // Weapon Framework's OnSettled: the slot is in place.
        static void WallSettled(Transform mount)
        {
            WallSettleUntil = 0f;
            if (Alive(mount)) FitToBoard(mount, "settled");
        }

        // Fallback if OnSettled never comes (an older Weapon Framework).
        static void WatchWallSettle()
        {
            if (WallSettleUntil <= 0f || Time.time < WallSettleUntil) return;
            WallSettleUntil = 0f;
            if (Alive(WallMount)) FitToBoard(WallMount, "5 s after retrieve");
        }

        static void FitToBoard(Transform mount, string when)
        {
            var f = FrameOf(mount);
            float behind = BoardBehind;
            string what = null, seen = "";
            float bestBehind = float.MaxValue;
            foreach (var h in Physics.RaycastAll(mount.position + f.Out * 0.3f, -f.Out, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || IsClubCollider(h.collider)) continue;
                if (Alive(Belt) && h.collider.transform.IsChildOf(Belt.root)) continue;
                float b = Vector3.Dot(mount.position - h.point, f.Out);
                seen += $"{(seen.Length > 0 ? ", " : "")}'{h.collider.name}' {b * 100f:0}";
                if (Vector3.Dot(h.normal, f.Out) < 0.5f || b < -0.01f || b > 0.15f || b >= bestBehind) continue;
                bestBehind = b;
                what = $"board '{h.collider.name}' {b * 100f:0} cm behind the gun position";
            }
            if (what != null) behind = bestBehind;
            else what = $"no board surface 0-15 cm behind the gun position, used {BoardBehind * 100f:0} cm";
            LayWallPair(mount, f, PairCentre(mount, f, behind));
            Log.Msg($"arsenal: pair fitted ({when}) - {what}{(Dbg && seen.Length > 0 ? $" [ray hits, cm behind: {seen}]" : "")}");
        }

        // `centre` is on the board surface; the clubs lie on it, one radius (plus a hair) out.
        static void LayWallPair(Transform mount, WallFrame f, Vector3 centre)
        {
            string layout = (WallLayout.Value ?? "").Trim();
            float a = Mathf.Clamp(WallAngle.Value, 0f, 90f) * Mathf.Deg2Rad, gap = WallSpacing.Value;
            var surface = centre + f.Out * (ClubRadius + BoardGap);
            for (int i = 0; i < 2; i++)
            {
                var s = WallSlots[i];
                if (!Alive(s.Anchor)) s.Anchor = new GameObject("BillyClubWall-" + s.Name).transform;
                if (s.Anchor.parent == null || s.Anchor.parent.Pointer != mount.Pointer) s.Anchor.SetParent(mount, true);
                float side = i == 0 ? 0.5f : -0.5f;   // slot 0 = the upper / right club
                Vector3 tip, pos;
                if (layout.Equals("Upright", StringComparison.OrdinalIgnoreCase))
                {
                    tip = -f.Up;
                    pos = surface + f.Right * (gap * side);
                }
                else if (layout.Equals("Cross", StringComparison.OrdinalIgnoreCase))
                {
                    float t = 15f * Mathf.Deg2Rad;
                    tip = (f.Right * Mathf.Cos(t) * (i == 0 ? 1f : -1f) - f.Up * Mathf.Sin(t)).normalized;
                    pos = surface + f.Out * (i == 0 ? 2f * ClubRadius + 0.004f : 0f);
                }
                else // Diagonal
                {
                    tip = (f.Right * Mathf.Cos(a) + f.Up * Mathf.Sin(a)).normalized;
                    var perp = (f.Up * Mathf.Cos(a) - f.Right * Mathf.Sin(a)).normalized;   // in the board plane, "above"
                    pos = surface + perp * (gap * side);
                }
                s.Anchor.position = pos;
                s.Anchor.rotation = Quaternion.LookRotation(f.Out, -tip);   // PinToBelt points the tip along anchor-down
            }
        }

        static Slot FreeWallSlot()
        {
            foreach (var s in WallSlots) if (Alive(s.Anchor) && !Alive(s.Club?.Go)) return s;
            return null;
        }
    }
}
