using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using Il2CppHurricaneVR.Framework.Core.Utils;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRHolsterCustomization
{
    // A kind of mod item that may go in the back holsters. ItemShape measures its shape from the first item
    // (longest box collider, grip end up), then the slot pins it and brings it back after a scene
    // load or a restart with Spawn.
    public sealed class HolsterKind
    {
        public string Id;                     // saved in the cfg; letters and digits
        public Func<GameObject, bool> IsMine; // is this object one of yours
        public Func<GameObject> Spawn;        // a new item for a restore; null if you can't make one now
        public bool Blade;                    // a sword or knife: hangs by its grip end in the blade pose (Blade* settings)
    }

    // Back holsters for mod items (Sep 29 2026, first version).
    //
    // They sit on the game's own back holsters (HVRShoulderSocket with leftShoulder / rightShoulder, the sockets the
    // game saves as holsterGunBackLeft / BackRight). Each side holds ONE weapon: a game gun (the game's socket) or a mod
    // item (ours). Ours is free only while the game's socket is empty, and the game's socket can't take a gun while ours
    // is full (HVRShoulderSocket.CanHover postfix). A mod item never enters the game's socket, so the game's loadout
    // save never sees a mod id (removing a mod can't break it).
    //
    // The pose comes from [VRHolsters_BackSlots], live (it is re-applied every frame), mirrored for the left side,
    // in the frame of the socket's parent (the body): out = away from the spine, up, back = behind you. The item hangs
    // grip up, far end (hook, tip) down.
    // Which side holds which kind is saved in SavedBackHolsters and restored after the game's own loadout on load.
    public static class Holsters
    {
        static readonly List<HolsterKind> kinds = new();
        static readonly Dictionary<string, Shape> shapes = new();
        struct Shape { public Vector3 Center, Axis, Flat; public float Length; }

        sealed class Slot
        {
            public string Name, Key;          // "back-left", "L"
            public bool Left;
            public HVRShoulderSocket Socket;
            public GameObject Item;
            public HolsterKind Kind;
        }

        static readonly Slot[] slots = { new() { Name = "back-left", Key = "L", Left = true }, new() { Name = "back-right", Key = "R", Left = false } };
        static readonly HashSet<IntPtr> ignoresPlayer = new();
        static readonly List<GameObject> tracked = new();   // items whose release this mod watches
        static readonly List<GameObject> drawn = new();     // back items that may still exist after a checkpoint reset
        static readonly Dictionary<IntPtr, bool> wasHeld = new();
        static readonly Dictionary<IntPtr, List<Collider>> ghosted = new(); // per holstered item: colliders we made non-solid
        static readonly Dictionary<IntPtr, bool> home = new(); // blade last drawn from the back: true = left side

        static MelonPreferences_Entry<bool> Enabled;
        static MelonPreferences_Entry<int> Out, Up, Back, Drop, Tilt, Lean, Spin, Snap, DrawReach, BladeGrip, BladeTilt, BladeLean, BladeSpin;
        static MelonPreferences_Entry<string> Saved;
        static MelonPreferences_Category cat;

        static float sceneStart, nextSearch;
        static float checkpointRestoreAt = float.PositiveInfinity;
        static bool restored, loggedSockets;

        // Pose defaults from the 0.2.2 test (~50 draws): the palm met the item ~22 cm nearer the spine, ~18 cm below and
        // ~5 cm behind its grip end (0.2.0 defaults put the grip ~15 cm above the shoulder socket, which sits at head
        // height, 35 cm out from the head). Moved about three quarters of that (the draws fire while the hand is still
        // moving in): grip at the socket point instead of above it, 13 cm in, 4 cm further back.
        internal static void Init()
        {
            cat = MelonPreferences.CreateCategory("VRHolsters_BackSlots", "VR Holster Customization: back slots");
            Enabled = cat.CreateEntry("BackHolsters", true, description: "Mod weapons (framework items, Billy Clubs) can go in the back holsters. Each side holds one weapon: a game gun or a mod item.");
            Out = cat.CreateEntry("OutCm", -13, description: "Moves the mod item in the back holster away from the spine (cm, both sides mirrored).");
            Up = cat.CreateEntry("UpCm", 0, description: "Moves it up (cm).");
            Back = cat.CreateEntry("BackCm", 7, description: "Moves it back, away from your body (cm).");
            Drop = cat.CreateEntry("DropCm", 30, description: "How far the item's centre hangs below the holster point (cm). More = the grip sits lower.");
            Tilt = cat.CreateEntry("TiltDeg", 20, description: "Tilts the lower end towards the spine (degrees). 0 = straight down, 45 = diagonal.");
            Lean = cat.CreateEntry("LeanDeg", 10, description: "Leans the lower end back, away from your body (degrees).");
            Spin = cat.CreateEntry("SpinDeg", 0, description: "Turns the item around its own length (degrees), e.g. to lay a crowbar's hook flat.");
            Snap = cat.CreateEntry("SnapCm", 30, description: "Let go of a mod item within this distance of a free back holster (slowly) and it goes in (cm).");
            DrawReach = cat.CreateEntry("DrawCm", 20, description: "Press grip with your hand within this distance of a mod item on your back and it comes out (cm). You can't see behind you, so this is wider than a normal grab.");
            Saved = cat.CreateEntry("SavedBackHolsters", "", description: "Managed by the mod: which mod item is in which back holster (L=kind;R=kind). Restored after every scene load.");
            // Blades hang by the grip end, so one pose fits a katana and a short knife; the anchor (OutCm, UpCm,
            // BackCm) is shared with the mod items. Default: grip where the crowbar's grip sits, diagonal across the back.
            BladeGrip = cat.CreateEntry("BladeGripCm", 0, description: "How far a katana's or knife's grip end hangs below the back holster point (cm). Negative = above it.");
            BladeTilt = cat.CreateEntry("BladeTiltDeg", 35, description: "Tilts a blade's tip towards the spine (degrees). 0 = straight down, 45 = diagonal across the back.");
            BladeLean = cat.CreateEntry("BladeLeanDeg", 10, description: "Leans a blade's tip back, away from your body (degrees).");
            BladeSpin = cat.CreateEntry("BladeSpinDeg", 0, description: "Turns a blade around its own length (degrees). 0 = the flat of the blade against your back.");
            BackBlades.Init(cat, Saved.Value);
        }

        public static void RegisterKind(HolsterKind k)
        {
            if (k == null || string.IsNullOrEmpty(k.Id) || k.IsMine == null) throw new ArgumentException("HolsterKind needs Id and IsMine");
            foreach (var o in kinds) if (o.Id == k.Id) return;
            kinds.Add(k);
            VRHolsterCustomizationMod.Log?.Msg($"holster kind '{k.Id}' registered");
        }

        // A mod calls this when the player lets go of its item. True = it is in a back holster now.
        const float MaxSnapSpeed = 5f;   // m/s; tests: normal put-backs over the shoulder were refused at 2.6 (limit 2.5), then 3.5-4.0 (limit 3.5)
        public static bool TryHolster(GameObject item)
        {
            try
            {
                if (!Enabled.Value || !VRHolsterCustomizationMod.Alive(item) || Holds(item)) return Holds(item);
                var kind = KindOf(item);
                if (kind == null) return false;
                var sh = ShapeOf(kind, item);
                var t = item.transform;
                var a = t.TransformPoint(sh.Center - sh.Axis * (sh.Length * 0.5f));
                var b = t.TransformPoint(sh.Center + sh.Axis * (sh.Length * 0.5f));
                float speed = 0f;
                var rb = item.GetComponent<Rigidbody>();
                if (rb != null) speed = rb.linearVelocity.magnitude;
                Slot best = null; float bestD = Snap.Value / 100f;
                foreach (var s in slots)
                {
                    if (!Free(s)) continue;
                    float d = DistToSegment(AnchorWorld(s), a, b);
                    if (d < bestD) { bestD = d; best = s; }
                }
                if (best == null || speed > MaxSnapSpeed)
                {
                    // Released at a back holster but not put in: say why (a release anywhere else stays quiet).
                    if (VRHolsterCustomizationMod.DebugOn)
                    {
                        Slot near = null; float nearD = Snap.Value / 100f;
                        foreach (var s in slots)
                        {
                            if (!VRHolsterCustomizationMod.Alive(s.Socket)) continue;
                            float d = DistToSegment(AnchorWorld(s), a, b);
                            if (d < nearD) { nearD = d; near = s; }
                        }
                        if (near != null)
                        {
                            string why = VRHolsterCustomizationMod.Alive(near.Item) ? $"it holds '{Label(near.Kind, near.Item)}'"
                                : !Free(near) ? $"the game's {GameGun(near)} is in it"
                                : $"moving too fast ({speed:0.0} m/s, max {MaxSnapSpeed})";
                            VRHolsterCustomizationMod.Log.Msg($"{near.Name}: '{Label(kind, item)}' let go {nearD * 100f:0} cm away, not holstered - {why}");
                        }
                    }
                    return false;
                }
                Put(best, item, kind);
                HolsterLog.ModHolster(best.Name, Label(kind, item), true);
                Save();
                return true;
            }
            catch (Exception e) { VRHolsterCustomizationMod.Log.Error($"back holster: {e.Message}"); return false; }
        }

        public static bool Holds(GameObject item)
        {
            if (!VRHolsterCustomizationMod.Alive(item)) return false;
            foreach (var s in slots) if (VRHolsterCustomizationMod.Alive(s.Item) && s.Item.Pointer == item.Pointer) return true;
            return false;
        }

        // The framework's own items (the test crowbar): it watches them for a release itself.
        public static void Watch(GameObject item)
        {
            if (!VRHolsterCustomizationMod.Alive(item)) return;
            foreach (var t in tracked) if (VRHolsterCustomizationMod.Alive(t) && t.Pointer == item.Pointer) return;
            tracked.Add(item);
        }

        internal static void Scene()
        {
            foreach (var s in slots)
            {
                if (!VRHolsterCustomizationMod.Alive(s.Socket)) s.Socket = null;
                if (!VRHolsterCustomizationMod.Alive(s.Item)) { s.Item = null; s.Kind = null; }
            }
            sceneStart = nextSearch = Time.time;
            restored = false;
            checkpointRestoreAt = float.PositiveInfinity;
            drawn.Clear();
            ignoresPlayer.Clear();
            ghosted.Clear();
            home.Clear();
        }

        internal static void PlayerLoadoutReset(ANBGameLogic game)
        {
            if (Enabled == null || !Enabled.Value || !VRHolsterCustomizationMod.Alive(game) || game.IsRangeScene) return;
            checkpointRestoreAt = Time.time + 1f;
        }

        internal static void Tick()
        {
            if (Enabled == null) return;
            if ((!VRHolsterCustomizationMod.Alive(slots[0].Socket) || !VRHolsterCustomizationMod.Alive(slots[1].Socket)) && Time.time >= nextSearch && Time.time - sceneStart < 60f)
            {
                nextSearch = Time.time + 1f;
                FindSockets();
            }

            // The framework's own items: a release near a free back holster puts them in.
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                var go = tracked[i];
                if (!VRHolsterCustomizationMod.Alive(go)) { tracked.RemoveAt(i); continue; }
                bool held = Dock.IsHeld(go);
                wasHeld.TryGetValue(go.Pointer, out bool was);
                if (was && !held) TryHolster(go);
                wasHeld[go.Pointer] = held;
            }

            DrawAssist();
            HoverHaptics();

            // Keep holstered items in place (the prop's scripts or physics may move them); this also applies setting
            // changes live.
            foreach (var s in slots)
            {
                if (s.Item == null) continue;
                if (!VRHolsterCustomizationMod.Alive(s.Item)) { s.Item = null; s.Kind = null; Save(); continue; }
                if (!VRHolsterCustomizationMod.Alive(s.Socket) || Dock.IsHeld(s.Item)) continue;
                if (!s.Item.activeSelf) s.Item.SetActive(true);
                if (s.Kind != null && s.Kind.Blade) BackBlades.KeepDocked(s.Item);
                KeepGhost(s.Item);
                PinIn(s);
            }

            // After the game has restored its own loadout (the load fold is over), bring back what was saved.
            if (!restored && VRHolsterCustomizationMod.Alive(slots[0].Socket) && !HolsterLog.Loading && Time.time - sceneStart > 3f)
            {
                restored = true;
                Restore();
            }
            if (Time.time >= checkpointRestoreAt && VRHolsterCustomizationMod.Alive(slots[0].Socket)
                && VRHolsterCustomizationMod.Alive(slots[1].Socket))
            {
                checkpointRestoreAt = float.PositiveInfinity;
                RestoreCheckpoint();
            }
        }

        // A hand (or anything) grabs an item in a back holster: out it comes.
        internal static void BeforeGrab(HVRGrabbable g)
        {
            if (!VRHolsterCustomizationMod.Alive(g)) return;
            var t = g.transform;
            foreach (var s in slots)
            {
                if (!VRHolsterCustomizationMod.Alive(s.Item) || !t.IsChildOf(s.Item.transform)) continue;
                var item = s.Item; var kind = s.Kind;
                if (kind != null) home[item.Pointer] = s.Left; // any kind: a mod's own return (Daredevil's clubs) uses it too
                if (!drawn.Exists(go => VRHolsterCustomizationMod.Alive(go) && go.Pointer == item.Pointer)) drawn.Add(item);
                s.Item = null; s.Kind = null;
                Ghost(item, false);
                Dock.Unpin(item);
                HolsterLog.ModHolster(s.Name, Label(kind, item), false);
                Save();
                return;
            }
        }

        // ---------- drawing ----------
        //
        // An item in a back holster rests kinematic, i.e. "docked": a hand hovers it only when its small native grab
        // sphere touches it (Grab Fix keeps docked items at native range on purpose, so a widened sphere can't pull a
        // holstered weapon off across the body). Behind your back you can't aim that precisely; the game's own guns
        // don't need to, the hand grabs them from the shoulder socket's big volume. 0.2.0 test: neither the crowbar
        // nor the club could be drawn. So: a grip press (or grip held up to DrawWindow after the press) with the
        // palm within DrawCm of the item's shaft, an empty hand and nothing else hovered = the hand takes it.
        // 0.2.1 test: draws fired at 13-15 cm (the edge, hand still moving in), misses were at 17-28 cm → 20 cm, 0.5 s.

        const float DrawWindow = 0.5f;
        static HVRHandGrabber[] hands;
        static readonly Dictionary<IntPtr, float> pressAt = new();
        static readonly Dictionary<IntPtr, bool> gripWas = new();

        static HVRHandGrabber[] Hands()
        {
            if (hands == null || hands.Length == 0 || !VRHolsterCustomizationMod.Alive(hands[0]))
            {
                var sock = VRHolsterCustomizationMod.Alive(slots[0].Socket) ? slots[0].Socket : slots[1].Socket;
                hands = VRHolsterCustomizationMod.Alive(sock) ? sock.transform.root.GetComponentsInChildren<HVRHandGrabber>(true) : null;
            }
            return hands;
        }

        // A held mod item that comes within SnapCm of a free back holster buzzes that hand once, like the game's guns
        // over a holster (their HVRSocket.GunHoverHaptics -> ANBHVRGunBase.HolsterHaptics). Tells you where to let go.
        static readonly Dictionary<IntPtr, string> hoverSlot = new();

        static void HoverHaptics()
        {
            if (!Enabled.Value || Hands() == null) return;
            foreach (var h in hands)
            {
                if (!VRHolsterCustomizationMod.Alive(h)) continue;
                string now = null;
                try
                {
                    var g = h.IsGrabbing ? h.GrabbedTarget : null;
                    GameObject item = null; HolsterKind kind = null;
                    for (var t = VRHolsterCustomizationMod.Alive(g) ? g.transform : null; t != null && kind == null; t = t.parent)
                        if ((kind = KindOf(t.gameObject)) != null) item = t.gameObject;
                    if (kind != null && !Holds(item))
                    {
                        var sh = ShapeOf(kind, item);
                        var it = item.transform;
                        var a = it.TransformPoint(sh.Center - sh.Axis * (sh.Length * 0.5f));
                        var b = it.TransformPoint(sh.Center + sh.Axis * (sh.Length * 0.5f));
                        float bestD = Snap.Value / 100f;
                        foreach (var s in slots)
                        {
                            if (!Free(s)) continue;
                            float d = DistToSegment(AnchorWorld(s), a, b);
                            if (d < bestD) { bestD = d; now = s.Name; }
                        }
                    }
                }
                catch { }
                hoverSlot.TryGetValue(h.Pointer, out var was);
                hoverSlot[h.Pointer] = now;
                if (now != null && now != was)
                    try { h.Controller?.Vibrate(0.35f, 0.06f, 150f); } catch { }
            }
        }

        static void DrawAssist()
        {
            bool any = false;
            foreach (var s in slots) if (VRHolsterCustomizationMod.Alive(s.Item) && VRHolsterCustomizationMod.Alive(s.Socket)) any = true;
            if (!any || Hands() == null) return;
            foreach (var h in hands)
            {
                if (!VRHolsterCustomizationMod.Alive(h) || !h.isActiveAndEnabled) continue;
                bool grip = false;
                try { grip = h.IsGripGrabActive; } catch { }
                gripWas.TryGetValue(h.Pointer, out bool was);
                gripWas[h.Pointer] = grip;
                if (grip && !was) { pressAt[h.Pointer] = Time.time; missWhy.Remove(h.Pointer); closest.Remove(h.Pointer); }
                if (!pressAt.TryGetValue(h.Pointer, out float at)) continue;
                var palm = VRHolsterCustomizationMod.Alive(h.Palm) ? h.Palm.position : h.transform.position;
                var best = Nearest(palm, out float bestD);
                // Closest approach during the press, and where the palm was then (for tuning the pose).
                if (best != null && (!closest.TryGetValue(h.Pointer, out var c0) || bestD < c0.D))
                    closest[h.Pointer] = new Approach { D = bestD, S = best, Where = Where(best, palm) };
                // The press is over without a draw: if it came near a back item, one line on why and where.
                if (!grip || Time.time - at > DrawWindow)
                {
                    pressAt.Remove(h.Pointer);
                    if (VRHolsterCustomizationMod.DebugOn && closest.TryGetValue(h.Pointer, out var c) && c.D < 2f * DrawReach.Value / 100f && Holds(c.S.Item))
                        VRHolsterCustomizationMod.Log.Msg($"{c.S.Name}: grip ({Hand(h)}) came within {c.D * 100f:0} cm of '{Label(c.S.Kind, c.S.Item)}' ({c.Where}), not drawn - " +
                            (missWhy.TryGetValue(h.Pointer, out var why) ? why : $"too far (DrawCm {DrawReach.Value})"));
                    closest.Remove(h.Pointer);
                    continue;
                }
                if (best == null || bestD > DrawReach.Value / 100f) continue;
                // The game's grab won (the hand took something, or grabs from a socket: a gun on that shoulder).
                if (h.IsGrabbing) { missWhy[h.Pointer] = $"the hand took '{GrabbedName(h)}'"; continue; }
                if (h.IsHoveringSocket) { missWhy[h.Pointer] = "the hand was over a game holster"; continue; }
                // The hand already hovers something else (a hip holster's gun, a prop): that wins.
                var hov = h.HoverTarget;
                if (VRHolsterCustomizationMod.Alive(hov) && !hov.transform.IsChildOf(best.Item.transform)) { missWhy[h.Pointer] = $"the hand hovered '{hov.gameObject.name}'"; continue; }

                var g = MainGrabbable(best.Item);
                if (g == null) continue;
                pressAt.Remove(h.Pointer); closest.Remove(h.Pointer);
                string label = Label(best.Kind, best.Item), side = best.Name, where = Where(best, palm);
                bool ok = false;
                try { ok = h.TryGrab(g, true); } catch (Exception e) { VRHolsterCustomizationMod.Log.Error($"back holster draw: {e.Message}"); }
                if (VRHolsterCustomizationMod.DebugOn)
                    VRHolsterCustomizationMod.Log.Msg($"{side}: '{label}' {(ok ? "drawn" : "NOT drawn (the game refused the grab)")} by the {Hand(h)} (draw assist, palm {bestD * 100f:0} cm, {where})");
            }
        }

        static readonly Dictionary<IntPtr, string> missWhy = new();
        struct Approach { public float D; public Slot S; public string Where; }
        static readonly Dictionary<IntPtr, Approach> closest = new();

        // Where the palm is relative to the item's grip end, in the body frame of the holster, mirrored per side:
        // "8 out, 12 above, 5 in front of the grip" (out = away from the spine). Tells which way to move the pose.
        static string Where(Slot s, Vector3 palm)
        {
            try
            {
                var parent = s.Socket.transform.parent;
                var sh = ShapeOf(s.Kind, s.Item);
                var grip = s.Item.transform.TransformPoint(sh.Center - sh.Axis * (sh.Length * 0.5f));
                var d = (parent != null ? parent.InverseTransformVector(palm - grip) : palm - grip) * 100f;
                float o = s.Left ? -d.x : d.x;
                return $"{Mathf.Abs(o):0} {(o >= 0 ? "out" : "in")}, {Mathf.Abs(d.y):0} {(d.y >= 0 ? "above" : "below")}, {Mathf.Abs(d.z):0} {(d.z >= 0 ? "in front of" : "behind")} the grip";
            }
            catch { return "?"; }
        }

        // The back item nearest to a point (distance to its shaft).
        static Slot Nearest(Vector3 p, out float dist)
        {
            Slot best = null; dist = float.MaxValue;
            foreach (var s in slots)
            {
                if (!VRHolsterCustomizationMod.Alive(s.Item)) continue;
                var sh = ShapeOf(s.Kind, s.Item);
                var t = s.Item.transform;
                float d = DistToSegment(p, t.TransformPoint(sh.Center - sh.Axis * (sh.Length * 0.5f)), t.TransformPoint(sh.Center + sh.Axis * (sh.Length * 0.5f)));
                if (d < dist) { dist = d; best = s; }
            }
            return best;
        }

        static string Hand(HVRHandGrabber h) => h.IsLeftHand ? "left hand" : "right hand";

        static string GrabbedName(HVRHandGrabber h)
        {
            try { var g = h.GrabbedTarget; return VRHolsterCustomizationMod.Alive(g) ? g.gameObject.name : "?"; } catch { return "?"; }
        }

        // The grabbable a hand should take: the one with the item's base grip point, else the first.
        static HVRGrabbable MainGrabbable(GameObject item)
        {
            HVRGrabbable first = null;
            foreach (var g in item.GetComponentsInChildren<HVRGrabbable>(true))
            {
                first ??= g;
                foreach (var t in g.GetComponentsInChildren<Transform>(true))
                    if (t.name == "GrabPoint_Base") return g;
            }
            return first;
        }

        // GrabGrabbable prefix: the game's back socket on a side that holds a mod item takes nothing. The socket's
        // grab calls the same virtual the hands use, so this catches every path in (hover, release-into-socket,
        // loadout restore).
        static float blockLogged;
        internal static bool BlocksGrab(HVRGrabberBase grabber, HVRGrabbable g)
        {
            if (!VRHolsterCustomizationMod.Alive(grabber)) return false;
            foreach (var s in slots)
            {
                if (!VRHolsterCustomizationMod.Alive(s.Item) || !VRHolsterCustomizationMod.Alive(s.Socket) || s.Socket.Pointer != grabber.Pointer) continue;
                if (VRHolsterCustomizationMod.DebugOn && Time.time - blockLogged > 1f)
                {
                    blockLogged = Time.time;
                    VRHolsterCustomizationMod.Log.Msg($"{s.Name}: holds '{Label(s.Kind, s.Item)}' - the game's holster refused '{(VRHolsterCustomizationMod.Alive(g) ? g.gameObject.name : "?")}'");
                }
                return true;
            }
            return false;
        }

        // The game's back socket must not take a gun (or our item) while ours holds something.
        internal static bool BlocksGameSocket(HVRShoulderSocket socket, HVRGrabbable g)
        {
            if (!VRHolsterCustomizationMod.Alive(socket)) return false;
            foreach (var s in slots)
            {
                if (s.Item != null && VRHolsterCustomizationMod.Alive(s.Item) && VRHolsterCustomizationMod.Alive(s.Socket) && s.Socket.Pointer == socket.Pointer) return true;
            }
            if (VRHolsterCustomizationMod.Alive(g))
                foreach (var k in kinds)
                {
                    try { if (k.IsMine(g.gameObject) || k.IsMine(g.transform.root.gameObject)) return true; } catch { }
                }
            return false;
        }

        // ---------- slots ----------

        static bool Free(Slot s)
        {
            if (!VRHolsterCustomizationMod.Alive(s.Socket) || VRHolsterCustomizationMod.Alive(s.Item)) return false;
            try { return s.Socket.GrabbedTarget == null; } catch { return true; }
        }

        static void Put(Slot s, GameObject item, HolsterKind kind)
        {
            s.Item = item; s.Kind = kind;
            home.Remove(item.Pointer);
            Dock.Unhang(item);
            Dock.Manage(item);
            var ato = item.GetComponent<Il2Cpp.ANBAssistedThrowingObject>();
            if (ato != null) { ato.StopAllCoroutines(); ato.homingTarget = null; }
            IgnorePlayer(item, s.Socket.transform.root);
            Ghost(item, true);
            PinIn(s);
        }

        // Pose in the frame of the socket's parent (the body), from the live settings.
        static void PinIn(Slot s)
        {
            var parent = s.Socket.transform.parent;
            if (parent == null) return;
            var sh = ShapeOf(s.Kind, s.Item);
            bool blade = s.Kind != null && s.Kind.Blade;
            float sign = s.Left ? -1f : 1f;
            Vector3 outDir = Vector3.right * sign, up = Vector3.up, back = Vector3.back;
            float tilt = (blade ? BladeTilt.Value : Tilt.Value) * Mathf.Deg2Rad, lean = (blade ? BladeLean.Value : Lean.Value) * Mathf.Deg2Rad;
            var tip = -up * Mathf.Cos(tilt) - outDir * Mathf.Sin(tilt);        // down, towards the spine
            tip = (tip * Mathf.Cos(lean) + back * Mathf.Sin(lean)).normalized;  // and away from the body
            var anchor = s.Socket.transform.localPosition + (outDir * Out.Value + up * Up.Value + back * Back.Value) / 100f;
            if (!blade)
            {
                var rot = Quaternion.AngleAxis(Spin.Value * sign, tip) * Quaternion.FromToRotation(sh.Axis, tip);
                var center = anchor + tip * (Drop.Value / 100f);
                Dock.Pin(s.Item, parent, center - rot * sh.Center, rot);
                return;
            }
            // Blade: grip end at the anchor (+ BladeGripCm along the blade), the flat of the blade against the back.
            var r0 = Quaternion.FromToRotation(sh.Axis, tip);
            float flat = Vector3.SignedAngle(Vector3.ProjectOnPlane(r0 * sh.Flat, tip), Vector3.ProjectOnPlane(back, tip), tip);
            var brot = Quaternion.AngleAxis(flat + BladeSpin.Value * sign, tip) * r0;
            var scale = s.Item.transform.localScale;
            float half = Vector3.Scale(scale, sh.Axis * sh.Length).magnitude * 0.5f;
            var bcenter = anchor + tip * (BladeGrip.Value / 100f + half);
            Dock.Pin(s.Item, parent, bcenter - brot * Vector3.Scale(scale, sh.Center), brot);
        }

        static Vector3 AnchorWorld(Slot s)
        {
            var parent = s.Socket.transform.parent;
            float sign = s.Left ? -1f : 1f;
            var local = s.Socket.transform.localPosition + (Vector3.right * sign * Out.Value + Vector3.up * Up.Value + Vector3.back * Back.Value) / 100f;
            return parent != null ? parent.TransformPoint(local) : s.Socket.transform.position;
        }

        static void FindSockets()
        {
            foreach (var o in Object.FindObjectsByType(Il2CppType.Of<HVRShoulderSocket>(), FindObjectsSortMode.None))
            {
                var sock = o.TryCast<HVRShoulderSocket>();
                if (sock == null || !sock.gameObject.activeInHierarchy) continue;
                if (sock.leftShoulder && !VRHolsterCustomizationMod.Alive(slots[0].Socket)) slots[0].Socket = sock;
                else if (sock.rightShoulder && !VRHolsterCustomizationMod.Alive(slots[1].Socket)) slots[1].Socket = sock;
            }
            if (loggedSockets || !VRHolsterCustomizationMod.Alive(slots[0].Socket) || !VRHolsterCustomizationMod.Alive(slots[1].Socket)) return;
            loggedSockets = true;
            // Once per game: where the sockets are and how their parent is turned, to calibrate the pose.
            var p = slots[1].Socket.transform.parent;
            var cam = Camera.main;
            string head = cam != null && p != null ? $", head fwd in parent {VRHolsterCustomizationMod.V(p.InverseTransformDirection(cam.transform.forward))}" : "";
            VRHolsterCustomizationMod.Log.Msg($"back holsters on '{Path(slots[0].Socket.transform)}' / '{slots[1].Socket.name}': local L {VRHolsterCustomizationMod.V(slots[0].Socket.transform.localPosition)} R {VRHolsterCustomizationMod.V(slots[1].Socket.transform.localPosition)}; parent '{(p != null ? p.name : "none")}' up {VRHolsterCustomizationMod.V(p != null ? p.up : Vector3.zero)}{head}");
        }

        // The body must not collide with an item riding on it.
        static void IgnorePlayer(GameObject item, Transform rigRoot)
        {
            if (!ignoresPlayer.Add(item.Pointer) || rigRoot == null) return;
            var mine = item.GetComponentsInChildren<Collider>(true);
            foreach (var pc in rigRoot.GetComponentsInChildren<Collider>(true))
            {
                if (pc.isTrigger) continue;
                if (pc.transform.IsChildOf(item.transform)) continue;
                // Other carried weapons are children of the rig too. Ignoring those pairs persists after a draw,
                // which made two clubs pass through each other for the rest of the session.
                if (pc.GetComponentInParent<HVRGrabbable>() != null) continue;
                bool modItem = false;
                for (var p = pc.transform; p != null && p.Pointer != rigRoot.Pointer; p = p.parent)
                    if (KindOf(p.gameObject) != null) { modItem = true; break; }
                if (modItem) continue;
                foreach (var c in mine) Physics.IgnoreCollision(c, pc, true);
            }
        }

        // A holstered item collides with nothing, like the game's socketed guns (HVRSocket.DisableCollision ->
        // HVRGrabbable.SetAllToTrigger): its solid colliders become triggers, so a gun or bow drawn from the other
        // shoulder passes through it (Oct 3 2026: the bow on the right caught on a katana on the left). The draw assist
        // and the hand's grab bag still find it. Drawn again: exactly those colliders turn solid. Same as Daredevil's belt clubs.
        static void Ghost(GameObject item, bool on)
        {
            if (!VRHolsterCustomizationMod.Alive(item)) return;
            if (!on)
            {
                if (ghosted.TryGetValue(item.Pointer, out var list))
                    foreach (var c in list) if (VRHolsterCustomizationMod.Alive(c)) c.isTrigger = false;
                ghosted.Remove(item.Pointer);
                return;
            }
            if (!ghosted.TryGetValue(item.Pointer, out var mine)) ghosted[item.Pointer] = mine = new List<Collider>();
            foreach (var c in item.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                var mesh = c.TryCast<MeshCollider>();
                if (mesh != null && !mesh.convex) continue; // a concave mesh can't be a trigger
                c.isTrigger = true;
                if (!mine.Exists(x => x.Pointer == c.Pointer)) mine.Add(c);
            }
            // A knife can arrive already non-solid: the game turns its colliders into triggers while it flies or sticks
            // in an enemy, and an auto-return (ReturnToBack) docks it in that state. Recording only the solid ones left
            // nothing to restore, so every katana drawn after a return passed through enemies, walls and floors
            // (Oct 3 2026). The knife's own list of normally solid colliders is restored on the draw as well.
            int already = 0;
            var knife = item.GetComponent<ANBKnife>();
            var native = knife != null ? knife.nonTriggerColliders : null;
            if (native != null)
                for (int i = 0; i < native.Count; i++)
                {
                    var c = native[i];
                    if (!VRHolsterCustomizationMod.Alive(c) || mine.Exists(x => x.Pointer == c.Pointer)) continue;
                    var mesh = c.TryCast<MeshCollider>();
                    if (mesh != null && !mesh.convex) continue;
                    c.isTrigger = true;
                    mine.Add(c); already++;
                }
            if (VRHolsterCustomizationMod.DebugOn && mine.Count > 0)
                VRHolsterCustomizationMod.Log.Msg($"'{item.name}': {mine.Count} collider(s) non-solid while holstered{(already > 0 ? $" ({already} already were, solid again on the draw)" : "")}");
        }

        // The item's own scripts may turn a collider solid again (a knife's switchCollisions): re-assert, cheaply.
        static void KeepGhost(GameObject item)
        {
            if (!ghosted.TryGetValue(item.Pointer, out var list)) return;
            foreach (var c in list) if (VRHolsterCustomizationMod.Alive(c) && !c.isTrigger) c.isTrigger = true;
        }

        // The game's knife auto-return (ANBKnife.returnKnife) only knows the belt knife holster and the wall spot. A blade
        // last drawn from the back goes back there instead: its own side if free, else the other free side, else the
        // game's return. True = it is on the back now. Public for mods that send their own items home (Daredevil's clubs).
        public static bool ReturnHome(GameObject item)
        {
            if (Enabled == null || !Enabled.Value || !VRHolsterCustomizationMod.Alive(item) || !home.TryGetValue(item.Pointer, out bool left)) return false;
            var kind = KindOf(item);
            if (kind == null) return false;
            var s = left ? slots[0] : slots[1];
            if (!Free(s)) s = left ? slots[1] : slots[0];
            if (!Free(s)) return false;
            Put(s, item, kind);
            HolsterLog.ModHolster(s.Name, Label(kind, item) + " (returned)", true);
            Save();
            return true;
        }

        // Taken from somewhere else (the belt knife holster, the wall): the game's return applies again.
        public static void ForgetHome(GameObject item)
        {
            if (VRHolsterCustomizationMod.Alive(item)) home.Remove(item.Pointer);
        }

        // ---------- save / restore ----------

        static void Save()
        {
            // A contract retry restores the loadout prepared in The Range. Drawing,
            // re-holstering or losing an item during combat changes only live slots.
            var game = ANBStaticGameManager.ANBmain;
            if (!VRHolsterCustomizationMod.Alive(game) || !game.gameStarted || !game.IsRangeScene) return;
            var parts = new List<string>();
            foreach (var s in slots)
            {
                if (s.Kind != null && VRHolsterCustomizationMod.Alive(s.Item)) { pending.Remove(s.Key); parts.Add($"{s.Key}={s.Kind.Id}"); continue; }
                if (!pending.TryGetValue(s.Key, out var id)) continue;
                // A waiting side the game has since put a gun on is given up.
                if (VRHolsterCustomizationMod.Alive(s.Socket) && !Free(s)) { pending.Remove(s.Key); continue; }
                parts.Add($"{s.Key}={id}");
            }
            var v = string.Join(";", parts);
            if (Saved.Value == v) return;
            Saved.Value = v;
            cat.SaveToFile(false);
        }

        static void Restore()
        {
            var want = Saved.Value ?? "";
            if (want.Length == 0) return;
            var done = new List<string>();
            foreach (var part in want.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                Slot s = kv[0] == "L" ? slots[0] : kv[0] == "R" ? slots[1] : null;
                HolsterKind kind = null;
                foreach (var k in kinds) if (k.Id == kv[1]) kind = k;
                if (s == null || VRHolsterCustomizationMod.Alive(s.Item)) continue;
                if (kind == null) { done.Add($"{s.Name} '{kv[1]}' skipped (its mod isn't loaded)"); continue; }
                if (!Free(s)) { pending.Remove(s.Key); done.Add($"{s.Name} '{kv[1]}' lost (the game put {GameGun(s)} there)"); continue; }
                GameObject item = null;
                try { item = kind.Spawn?.Invoke(); } catch (Exception e) { VRHolsterCustomizationMod.Log.Error($"'{kind.Id}' spawn: {e.Message}"); }
                if (!VRHolsterCustomizationMod.Alive(item))
                {
                    // Its template isn't there yet (the crowbar and the clubs are copied from The Range's crowbar, so a
                    // contract started straight from the main menu can't make them). Kept: it comes back in the first
                    // scene after a visit to The Range. 0.2.3 dropped it here, so the side stayed empty for good.
                    pending[s.Key] = kind.Id;
                    done.Add($"{s.Name} '{kind.Id}' waits (its mod can't make one before a visit to The Range)");
                    continue;
                }
                pending.Remove(s.Key);
                Put(s, item, kind);
                done.Add($"{s.Name} {Label(kind, item)}");
            }
            VRHolsterCustomizationMod.Log.Msg("back holsters restored: " + string.Join(", ", done));
            // What the game took is dropped; a kind whose mod isn't loaded and a kind that can't be made yet are kept.
            foreach (var part in want.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                bool known = false;
                foreach (var k in kinds) if (k.Id == kv[1]) known = true;
                if (!known) pending[kv[0]] = kv[1];
            }
            Save();
        }

        static void RestoreCheckpoint()
        {
            var want = Saved.Value ?? "";
            if (want.Length == 0) return;
            var done = new List<string>();
            foreach (var part in want.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                Slot s = kv[0] == "L" ? slots[0] : kv[0] == "R" ? slots[1] : null;
                if (s == null) continue;
                HolsterKind kind = null;
                foreach (var k in kinds) if (k.Id == kv[1]) { kind = k; break; }
                if (kind == null) { done.Add($"{s.Name} '{kv[1]}' unavailable"); continue; }
                if (VRHolsterCustomizationMod.Alive(s.Item))
                {
                    done.Add($"{s.Name} already holds {Label(s.Kind, s.Item)}");
                    continue;
                }
                s.Item = null; s.Kind = null;
                if (!Free(s)) { done.Add($"{s.Name} blocked by {GameGun(s)}"); continue; }

                GameObject item = Existing(kind);
                bool reused = VRHolsterCustomizationMod.Alive(item);
                if (!reused)
                    try { item = kind.Spawn?.Invoke(); }
                    catch (Exception e) { VRHolsterCustomizationMod.Log.Error($"checkpoint '{kind.Id}' spawn: {e.Message}"); }
                if (!VRHolsterCustomizationMod.Alive(item)) { done.Add($"{s.Name} '{kind.Id}' unavailable"); continue; }
                Put(s, item, kind);
                pending.Remove(s.Key);
                done.Add($"{s.Name} {kind.Id} {(reused ? "recalled" : "spawned")}");
            }
            VRHolsterCustomizationMod.Log.Msg("checkpoint back holsters: " + string.Join(", ", done));
        }

        static GameObject Existing(HolsterKind kind)
        {
            foreach (var item in drawn)
                if (Available(item, kind)) return item;
            foreach (var item in tracked)
                if (Available(item, kind)) return item;
            return null;
        }

        static bool Available(GameObject item, HolsterKind kind)
        {
            // Not one the game (a knife holster, a wall spot) or a hand holds.
            if (!VRHolsterCustomizationMod.Alive(item) || Holds(item) || Dock.IsHeld(item)) return false;
            try { return kind.IsMine(item); } catch { return false; }
        }

        // Saved sides whose item isn't on the back yet (not made yet, or its mod isn't loaded): key -> kind id.
        static readonly Dictionary<string, string> pending = new();

        static string GameGun(Slot s)
        {
            try { var g = s.Socket.GrabbedTarget; return g != null ? $"'{g.gameObject.name}'" : "a gun"; } catch { return "a gun"; }
        }

        // ---------- helpers ----------

        static HolsterKind KindOf(GameObject item)
        {
            foreach (var k in kinds)
            {
                try { if (k.IsMine(item)) return k; } catch { }
            }
            return null;
        }

        static Shape ShapeOf(HolsterKind kind, GameObject item)
        {
            if (kind != null && shapes.TryGetValue(kind.Id, out var s)) return s;
            Vector3 c, a, f = Vector3.forward; float l;
            ghosted.TryGetValue(item.Pointer, out var ghost);
            Func<Collider, bool> solid = col => !col.isTrigger || (ghost != null && ghost.Exists(x => x.Pointer == col.Pointer));
            bool ok = kind != null && kind.Blade ? ItemShape.MeasureBlade(item, out c, out a, out l, out f, solid)
                : ItemShape.Measure(item, out c, out a, out l, solid);
            if (!ok) VRHolsterCustomizationMod.Log.Warning($"'{Label(kind, item)}': no solid box collider to measure, using a default shape");
            s = new Shape { Center = c, Axis = a, Flat = f, Length = l };
            if (kind != null)
            {
                shapes[kind.Id] = s;
                if (VRHolsterCustomizationMod.DebugOn) VRHolsterCustomizationMod.Log.Msg($"'{kind.Id}' shape: {l:0.00} m long, centre {VRHolsterCustomizationMod.V(c)}, far end {VRHolsterCustomizationMod.V(a)} (item space)");
            }
            return s;
        }

        static string Label(HolsterKind k, GameObject item) => k != null ? k.Id : VRHolsterCustomizationMod.Alive(item) ? item.name : "?";

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(HVRShoulderSocket), nameof(HVRShoulderSocket.CanHover))]
    static class ShoulderSocketHoverPatch
    {
        static void Postfix(HVRShoulderSocket __instance, HVRGrabbable grabbable, ref bool __result)
        {
            if (!__result) return;
            try { if (Holsters.BlocksGameSocket(__instance, grabbable)) __result = false; } catch { }
        }
    }
}
