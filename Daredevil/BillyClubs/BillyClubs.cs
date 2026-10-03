using System;
using System.Collections.Generic;
using System.Globalization;
using Il2Cpp;
using Il2CppHurricaneVR.Framework.Core;
using Il2CppHurricaneVR.Framework.Core.Grabbers;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace BillyClubs
{
    // A pair of Daredevil-style billy clubs, built from the game's own crowbar, with their own holsters.
    //
    // Why the crowbar (`Prop-Bluntweapon-Crowbar`, only placed in The Range): it is the one item that
    // carries both ANBBluntWeapon (speed-tier blunt damage, canKill, head x10) and
    // ANBAssistedThrowingObject (homing throw, wired to its release event in the scene). A copy keeps
    // both, plus its HVR grabbable and grab points.
    //
    // - Template: the first time The Range loads, the crowbar is copied under an inactive
    //   DontDestroyOnLoad holder. Copying under an inactive parent runs no Awake, so the copy never
    //   registers itself anywhere. Its crowbar meshes are hidden and the bundled textured club is
    //   placed along the crowbar's long axis (cylinders are the fallback). The colliders stay (the same
    //   length). ANBGeneratePhysics.useRandomDesign is turned off so Start doesn't show a random
    //   crowbar design again.
    // - Holsters: the game's own holsters can't take a club (tag filters, and DemoHolster/HVRShoulderSocket
    //   OnGrabbed throw or call holsterGun without a gun or knife component). And a contract starts from the
    //   saved loadout in a freshly loaded scene, so nothing physical carries over anyway. So the mod has its
    //   own two slots on the player's belt (`Waist/Holsters`, next to the hip and knife holsters):
    //   release a club near a free slot and it snaps in (kinematic, parented to the belt); grab it and it
    //   comes out (HVRGrabberBase.GrabGrabbable prefix). Which slots are full is remembered across scene
    //   loads and game restarts, and the clubs are respawned into them once the player rig exists.
    // - Spawn key (default F8): fills the empty slots. Without a player rig, a pair floats in front of you.
    // - Throw assist: dontUse off, longer search and fly distances than the crowbar's 6 m / 4 m.
    public partial class BillyClubsMod : MelonMod
    {
        internal static MelonPreferences_Entry<string> SpawnKey, BodyColor, SlotLeft, SlotRight, SavedSlots;
        internal static MelonPreferences_Entry<float> Length, Radius, Mass, ThrowSpeed, MinSteerSpeed, ThrowSearch, ThrowMaxFly, SnapDistance;
        internal static MelonPreferences_Entry<bool> DebugLog, UseCustomModel, ClubDoorKick;
        static MelonPreferences_Category Cat;

        const string CrowbarName = "Prop-Bluntweapon-Crowbar";
        const string VisualName = "BillyClubVisual";
        const float HolsterMaxSpeed = 2.5f; // m/s at release; faster is a throw, not a holster

        static GameObject Holder, Template;
        static Vector3 TemplateAxis = Vector3.right; // club's long axis in its own space, towards the tip
        static Vector3 TemplateCenter;               // centre of the club in its own space
        static readonly List<Club> Clubs = new();

        // Every live club (belt, wall, dropped). Radar Sense reads them here instead of searching the scene.
        internal static void CopyClubs(List<GameObject> into)
        {
            into.Clear();
            foreach (var k in Clubs) if (Alive(k.Go)) into.Add(k.Go);
        }

        class Club
        {
            public GameObject Go;
            public Rigidbody Rb;
            public HVRGrabbable Grab;
            public bool Held, Floating, IgnoresPlayer;
            public float SpawnedAt;
            public int HideChecks;
            public Slot In;
            public string LastPull; // last way the game pulled it off the belt (logged once per kind)
            public Flight Fly;
            public Settle Calm;
            public float FlightEndedAt = -99f;
            public ANBBasicNPC LastHitNpc;  // for the club's own hit cooldown (the game's is 0.6 s per weapon)
            public float LastHitAt = -99f;
            public Transform HandT;         // the hand holding it (follow-through)
            public Vector3 HandPrev, HandV;
            public bool HandOk;
            public SwingTrack Sw;           // swing diagnostics while a hand holds it (SwingLog.cs)
            public bool InertiaSet;         // ClubInertiaScale has been applied to this club's rigidbody
            public List<Collider> Ghosted = new(); // solid colliders made triggers while holstered
            public bool? SavedLos;          // the grabbable's RequireLineOfSight, switched off while holstered
            public float LooseAt = -99f, RestAt = -99f, StillFor; // return timer (ClubReturn.cs)
        }

        class Slot
        {
            public string Name;
            public MelonPreferences_Entry<string> Pos;
            public Transform Anchor;
            public bool Full;   // remembered across scenes; cleared only when the club is taken out
            public Club Club;
            public Ghost Ghost; // the visible holster tube
            public bool Wall;   // a spot on the arsenal wall (Arsenal.cs), not a belt holster
        }

        static readonly Slot[] Slots = { new() { Name = "left" }, new() { Name = "right" } };
        static Transform Belt;
        static float SceneStart, NextBeltSearch;
        static bool Restored;
        static float CheckpointRestoreAt = float.PositiveInfinity;

        static MelonLogger.Instance Log;
        static bool Dbg => DebugLog.Value;

        // Implemented in Daredevil.cs, which runs Radar Sense from here.
        static partial void PackageInit();
        static partial void PackageScene();
        static partial void PackageUpdate();

        public override void OnInitializeMelon()
        {
            PackageInit();
            Log = LoggerInstance;
            var c = Cat = MelonPreferences.CreateCategory("BillyClubs", "Daredevil: Clubs");
            SpawnKey = c.CreateEntry("SpawnKey", "F8", description: "Keyboard key that brings your clubs back into the club holsters (from wherever they are) and spawns new ones if you have fewer than two (Input System key name, e.g. F8, B, Numpad1). Visit The Range once per game start first: the clubs are copied from its crowbar.");
            Length = c.CreateEntry("Length", 0.6f, description: "Club length in metres (visual only; the grip and hit shape stay the crowbar's, about 0.6 m).");
            Radius = c.CreateEntry("Radius", 0.018f, description: "Primitive fallback radius in metres. The custom model uses its authored 34 mm grip diameter.");
            Mass = c.CreateEntry("Mass", 3f, description: "Club mass in kg (the crowbar is 8). Applies live to every club, including one in your hand.");
            BodyColor = c.CreateEntry("BodyColor", "#5A080A", description: "Primitive fallback body colour as #RRGGBB. The custom model uses its baked burgundy texture.");
            UseCustomModel = c.CreateEntry("UseCustomModel", true, description: "Use the bundled textured billy club model. Off uses the old cylinder visuals. Restart the game after changing this.");
            ThrowSpeed = c.CreateEntry("ThrowSpeed", 18f, description: "Top speed (m/s) of a club the mod steers (throw assist and ricochets). It flies at your throw speed, between MinSteerSpeed and this. Hand throws log 3-12 m/s; the crowbar's own assist used 20. Full damage needs only 3.5.");
            MinSteerSpeed = c.CreateEntry("MinSteerSpeed", 13f, description: "Slowest speed (m/s) of a steered club, so a soft throw or a ricochet off a wall still reaches its target.");
            ThrowSearch = c.CreateEntry("ThrowSearchDistance", 15f, description: "How far the throw looks for a target, in metres (crowbar 6).");
            ThrowMaxFly = c.CreateEntry("ThrowMaxFlyDistance", 20f, description: "How far a homing throw flies before it gives up, in metres (crowbar 4).");
            SlotLeft = c.CreateEntry("HolsterLeft", "-0.23,-0.12,-0.10", description: "Left club holster: centre of the club relative to the belt, in metres, as right,up,forward. The game's hip holsters sit at +-0.27,0,0 and its knife holsters at +-0.17,0.05,0.13. The club hangs tip down.");
            SlotRight = c.CreateEntry("HolsterRight", "0.23,-0.12,-0.10", description: "Right club holster, same format.");
            SnapDistance = c.CreateEntry("HolsterSnapDistance", 0.4f, description: "Let go of a club within this many metres of a free club holster and it snaps in.");
            SavedSlots = c.CreateEntry("SavedHolsters", "", description: "Managed by the mod: which club holsters are full (L, R). Restored after every scene load.");
            ClubDoorKick = c.CreateEntry("ClubDoorKick", true, description: "Press A (right hand) / X (left hand) while holding a Billy Club to kick open a marked door. Uses the game's VR door-kick setting and range.");
            DebugLog = c.CreateEntry("DebugLog", false, description: "Log template building, spawns, holstering, grabs, throws, ricochets and unsuccessful door-kick presses.");
            InitThrowPrefs(c);
            InitMiddleGripPref(c);
            InitDamagePrefs(c);
            InitHolsterPrefs(c);
            InitReturnPrefs(c);
            InitFollowPrefs(c);
            InitSwingLogPrefs(c);
            InitHandProbePrefs(c);
            Mass.OnEntryValueChanged.Subscribe((_, kg) => ApplyMassLive(kg));
            InitArsenal(c);
            Slots[0].Pos = SlotLeft; Slots[1].Pos = SlotRight;
            var saved = SavedSlots.Value ?? "";
            Slots[0].Full = saved.Contains('L');
            Slots[1].Full = saved.Contains('R');
            Log.Msg($"loaded - spawn key {SpawnKey.Value}, club holsters full: {(saved.Length > 0 ? saved : "none")}");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            PackageScene();
            HandProbeScene();
            DoorButtonWasDown.Clear();
            // Scenes may load additively: keep whatever is still alive, reset only what died with the old scene.
            for (int i = 0; i < Clubs.Count; i++) if (!Alive(Clubs[i].Go)) Clubs.RemoveAt(i--);
            foreach (var s in Slots)
            {
                if (!Alive(s.Anchor)) s.Anchor = null;
                if (!Alive(s.Club?.Go)) s.Club = null;
            }
            foreach (var s in WallSlots)
            {
                if (!Alive(s.Anchor)) s.Anchor = null;
                if (!Alive(s.Club?.Go)) s.Club = null;
            }
            if (!Alive(Belt))
            {
                Belt = null;
                // A new player rig is a new loadout. Contract draws change the live slots,
                // but the saved Range loadout is what a retry must restore.
                var saved = SavedSlots.Value ?? "";
                Slots[0].Full = saved.Contains('L');
                Slots[1].Full = saved.Contains('R');
                Restored = false;
                SceneStart = NextBeltSearch = Time.time;
            }
            CheckpointRestoreAt = float.PositiveInfinity;
            if (Alive(Template)) return;
            var crowbar = GameObject.Find(CrowbarName);
            if (crowbar == null) return;
            try { BuildTemplate(crowbar); }
            catch (Exception e) { Log.Error($"template build failed: {e}"); }
        }

        public override void OnUpdate()
        {
            PackageUpdate();
            if (KeyPressed()) FillHolsters(true);
            TuningKeys();
            WatchDraws();
            WatchOptimiser();
            WatchWallSettle();
            UpdateGhosts();

            // The player rig can appear after the scene is initialized: look for the belt once a second.
            if (!Alive(Belt) && Time.time >= NextBeltSearch && Time.time - SceneStart < 30f)
            {
                NextBeltSearch = Time.time + 1f;
                FindBelt();
            }
            // Give the game's own loadout a moment, then put back the clubs that were holstered.
            if (!Restored && Alive(Belt) && Time.time - SceneStart > 2f)
            {
                Restored = true;
                if (Slots[0].Full || Slots[1].Full) FillHolsters(false);
            }
            if (Time.time >= CheckpointRestoreAt && Alive(Belt))
            {
                CheckpointRestoreAt = float.PositiveInfinity;
                try { RestoreCheckpointClubs(); }
                catch (Exception e) { Log.Error($"checkpoint club restore failed: {e}"); }
            }

            for (int i = 0; i < Clubs.Count; i++)
            {
                var k = Clubs[i];
                if (!Alive(k.Go)) { Clubs.RemoveAt(i--); continue; }
                // useRandomDesign is off, but hide the crowbar meshes again for a few frames in case
                // something in Start re-enables one.
                if (k.HideChecks > 0) { k.HideChecks--; HideCrowbarMeshes(k.Go); }

                bool held = IsHeld(k.Grab);
                if (k.Floating && (held || Time.time - k.SpawnedAt > 30f))
                {
                    k.Floating = false;
                    if (Alive(k.Rb)) k.Rb.useGravity = true;
                }
                if (k.Held && !held && k.In == null)
                {
                    TryHolster(k);
                    if (k.In == null && !FwTryHolster(k.Go)) StartFlight(k);
                }
                if (k.In != null && !held) KeepOnBelt(k);
                k.Held = held;
                WatchReturn(k, held);
            }

            UpdateDoorKicks();
        }

        static bool KeyPressed()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return false;
                var key = kb.FindKeyOnCurrentKeyboardLayout(SpawnKey.Value) ?? kb[SpawnKey.Value]?.TryCast<UnityEngine.InputSystem.Controls.KeyControl>();
                return key != null && key.wasPressedThisFrame;
            }
            catch { return false; }
        }

        static bool IsHeld(HVRGrabbable g)
        {
            try { return Alive(g) && g.IsBeingHeld; } catch { return false; }
        }

        // ---------- holsters ----------

        static void FindBelt()
        {
            var hr = GameObject.Find("HolsterRight");
            if (hr == null || hr.transform.parent == null || hr.transform.parent.name != "Holsters") return;
            Belt = hr.transform.parent;
            foreach (var s in Slots)
            {
                var a = new GameObject("BillyClubHolster-" + s.Name).transform;
                a.SetParent(Belt, false);
                a.localPosition = ParseV(s.Pos.Value, s == Slots[0] ? new Vector3(-0.23f, -0.12f, -0.1f) : new Vector3(0.23f, -0.12f, -0.1f));
                a.localRotation = Quaternion.identity;
                s.Anchor = a;
            }
            BuildGhosts();
            if (Dbg)
            {
                var cam = Camera.main;
                string h = cam != null ? $", belt {Belt.position.y - cam.transform.position.y:+0.00;-0.00} m from the head" : "";
                Log.Msg($"club holsters on '{Path(Belt)}'{h}; game hip holster R at {V(Belt.InverseTransformPoint(hr.transform.position))}, clubs at L {V(Slots[0].Anchor.localPosition)} R {V(Slots[1].Anchor.localPosition)} (belt space)");
            }
        }

        // Spawn clubs into the holsters that are empty (fromKey) or remembered as full (after a scene load).
        static void FillHolsters(bool fromKey)
        {
            if (!Alive(Template)) { Log.Warning("no club template yet - visit The Range once (the clubs are copied from its crowbar)"); return; }
            if (!Alive(Belt)) { if (fromKey) SpawnFloating(); return; }

            // Clubs are never destroyed here: destroying one that a hand or the force grab still points at
            // left the hands unable to grab (0.3.0 test). Loose clubs are brought back instead.
            int recalled = 0, n = 0;
            if (fromKey)
            {
                foreach (var k in Clubs)
                {
                    if ((k.In != null && !k.In.Wall) || !Alive(k.Go) || IsHandHeld(k) || OnBack(k.Go)) continue;
                    var free = FreeSlot();
                    if (free == null) break;
                    SendToSlot(k, free);
                    recalled++;
                }
            }
            int alive = 0;
            foreach (var k in Clubs) if (Alive(k.Go)) alive++;
            foreach (var s in Slots)
            {
                if (Alive(s.Club?.Go)) continue;
                if (!fromKey && !s.Full) continue;
                if (fromKey && alive >= 2) break;
                var k = NewClub("BillyClub-" + s.Name, s.Anchor.position, s.Anchor.rotation);
                Holster(k, s, false);
                n++; alive++;
            }
            SaveSlots();
            if (!fromKey) Log.Msg($"restored {n} holstered club(s)");
            else Log.Msg(recalled + n == 0 ? "clubs: nothing to do (both holstered, or held in your hands)" : $"clubs: {recalled} brought back to the holsters, {n} new");
        }

        // The game can restart a contract in the same scene. Its player loadout is reloaded,
        // but loose clubs are still where they fell; no OnSceneWasInitialized runs then.
        internal static void PlayerLoadoutReset(ANBGameLogic game)
        {
            if (!Alive(game) || game.IsRangeScene) return;
            CheckpointRestoreAt = Time.time + 1f;
        }

        static void RestoreCheckpointClubs()
        {
            if (!Alive(Template) || !Alive(Belt)) return;
            var saved = SavedSlots.Value ?? "";
            int recalled = 0, spawned = 0;
            foreach (var s in Slots)
            {
                bool wanted = saved.Contains(s == Slots[0] ? 'L' : 'R');
                if (!wanted) continue;
                if (Alive(s.Club?.Go)) { s.Full = true; continue; }
                s.Club = null;
                Club k = null;
                foreach (var candidate in Clubs)
                {
                    if (!Alive(candidate.Go) || candidate.In != null || OnBack(candidate.Go)) continue;
                    k = candidate;
                    break;
                }
                if (k != null)
                {
                    try { if (IsHeld(k.Grab)) k.Grab.ForceRelease(); } catch { }
                    k.Held = false;
                    recalled++;
                }
                else
                {
                    k = NewClub("BillyClub-" + s.Name, s.Anchor.position, s.Anchor.rotation);
                    spawned++;
                }
                Holster(k, s, false);
            }
            Log.Msg($"checkpoint loadout: {recalled} loose club(s) recalled, {spawned} spawned into leg holsters");
        }

        static Slot FreeSlot()
        {
            foreach (var s in Slots) if (Alive(s.Anchor) && !Alive(s.Club?.Go)) return s;
            return null;
        }

        static void TryHolster(Club k)
        {
            if (!Alive(Belt) || !Alive(k.Rb)) return;
            float speed = k.Rb.linearVelocity.magnitude;
            var t = k.Go.transform;
            float half = Length.Value * 0.5f;
            var a = t.TransformPoint(ClubCenter - ClubAxis * half);
            var b = t.TransformPoint(ClubCenter + ClubAxis * half);
            Slot best = null; float bestD = SnapDistance.Value;
            foreach (var s in Slots)
            {
                if (!Alive(s.Anchor) || Alive(s.Club?.Go)) continue;
                float d = DistToSegment(s.Anchor.position, a, b);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null)
            {
                // Only slow releases: a fast one is a throw, not a holster attempt.
                if (Dbg && speed <= HolsterMaxSpeed)
                {
                    float near = float.MaxValue; Slot ns = null;
                    foreach (var s in Slots)
                        if (Alive(s.Anchor) && !Alive(s.Club?.Go)) { float d = DistToSegment(s.Anchor.position, a, b); if (d < near) { near = d; ns = s; } }
                    if (ns != null && near < SnapDistance.Value * 1.5f)
                    {
                        // Where the club really was, as the holster setting would need it (belt space, club centre).
                        var c = Belt.InverseTransformPoint(t.TransformPoint(ClubCenter));
                        var tip = Belt.InverseTransformDirection(t.TransformDirection(ClubAxis));
                        Log.Msg($"club let go {near:0.00} m from the free {ns.Name} holster at {speed:0.0} m/s (snaps within {SnapDistance.Value:0.00}) - not holstered; club centre at {V(c)} vs holster {V(ns.Anchor.localPosition)}, tip {V(tip)} (belt space)");
                    }
                }
                return;
            }
            if (speed > HolsterMaxSpeed)
            {
                if (Dbg) Log.Msg($"club let go {bestD:0.00} m from the {best.Name} holster at {speed:0.0} m/s - a throw, not holstered");
                return;
            }
            Holster(k, best, true);
            SaveSlots();
        }

        // Bring a loose club (or one on the arsenal wall) back to a belt slot, from wherever it is: F8 and the return timer.
        static void SendToSlot(Club k, Slot s)
        {
            try { if (IsHeld(k.Grab)) k.Grab.ForceRelease(); } catch { }
            EndFlight(k, null, null); k.Held = false;
            if (k.In != null) { k.In.Club = null; k.In = null; }   // off the arsenal wall
            Holster(k, s, false);
        }

        static void Holster(Club k, Slot s, bool log)
        {
            FwForgetHome(k.Go); // a belt or wall slot is its home now, not the back
            EndFlight(k, null, null);
            var rb = k.Rb;
            if (Alive(rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = true;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
            k.Floating = false;
            // A gentle release can still start the homing throw; stop it so it doesn't resume on the draw.
            var ato = k.Go.GetComponent<ANBAssistedThrowingObject>();
            if (ato != null) { ato.StopAllCoroutines(); ato.homingTarget = null; }
            PinToBelt(k.Go.transform, s.Anchor);
            IgnorePlayer(k);
            SetGhost(k, true);
            k.In = s; s.Club = k; s.Full = true;
            if (log) Log.Msg($"club holstered ({s.Name})");
            if (Dbg && log)
            {
                var t = k.Go.transform; var cam = Camera.main;
                float dy = t.TransformPoint(ClubCenter).y - (cam != null ? cam.transform.position.y : 0f);
                var tip = t.TransformDirection(ClubAxis);
                Log.Msg($"  {s.Name} club centre {dy:+0.00;-0.00} m from the head, tip {(tip.y < -0.9f ? "down" : "NOT down " + V(tip))}");
            }
        }

        static void PinToBelt(Transform t, Transform anchor)
        {
            if (t.parent == null || t.parent.Pointer != anchor.Pointer) t.SetParent(anchor, false);
            var rot = Quaternion.FromToRotation(ClubAxis, Vector3.down); // tip down, grip up
            t.localRotation = rot;
            t.localPosition = -(rot * ClubCenter);
        }

        // The crowbar's own scripts run after a spawn (ANBGeneratePhysics.Init turns physics back on and
        // changes the layer) and on resets. While a club is holstered, put it back every frame.
        static void KeepOnBelt(Club k)
        {
            var s = k.In;
            if (!Alive(s.Anchor)) return;
            var t = k.Go.transform;
            string pull = null;
            if (!k.Go.activeSelf) { k.Go.SetActive(true); pull = "deactivated"; }
            if (t.parent == null || t.parent.Pointer != s.Anchor.Pointer) pull ??= $"moved to '{(t.parent != null ? Path(t.parent) : "the scene root")}'";
            if (Alive(k.Rb) && !k.Rb.isKinematic) { k.Rb.isKinematic = true; pull ??= "given physics again"; }
            if (pull == null && (t.localPosition + t.localRotation * ClubCenter).sqrMagnitude > 0.0001f) pull = "moved on the belt";
            if (pull == null) return;
            PinToBelt(t, s.Anchor);
            SetGhost(k, true); // the game's physics setup may have rebuilt or reset the colliders
            if (Dbg && pull != k.LastPull) Log.Msg($"club ({s.Name}) was {pull} by the game - put back on the belt");
            k.LastPull = pull;
        }

        internal static bool IsClub(GameObject go)
        {
            if (!Alive(go)) return false;
            foreach (var k in Clubs) if (Alive(k.Go) && k.Go.Pointer == go.Pointer) return true;
            return false;
        }

        static float LastResetLog = -99f;
        internal static void SkippedReset()
        {
            if (Dbg && Time.time - LastResetLog > 5f) Log.Msg("kept the clubs out of the game's physics-prop reset (it teleports props back to their spawn spot)");
            LastResetLog = Time.time;
        }

        // Called before any grabber takes a grabbable: a holstered club leaves its holster first.
        internal static void BeforeGrab(HVRGrabbable g, HVRGrabberBase grabber)
        {
            if (!Alive(g)) return;
            foreach (var k in Clubs)
            {
                if (!Alive(k.Grab) || k.Grab.Pointer != g.Pointer) continue;
                NoteGrabber(k, grabber);
                if (k.In == null) { if (k.Fly != null) EndFlight(k, null, null); if (Dbg) Log.Msg($"club '{k.Go.name}' grabbed by '{Name(grabber)}'"); return; }
                var s = k.In;
                k.Go.transform.SetParent(null, true);
                SetGhost(k, false);
                if (Alive(k.Rb))
                {
                    k.Rb.isKinematic = false;
                    k.Rb.interpolation = RigidbodyInterpolation.Interpolate;
                }
                k.In = null; s.Club = null; s.Full = false;
                SaveSlots();
                if (Dbg) Log.Msg($"club drawn ({s.Name}) by '{Name(grabber)}'");
                return;
            }
        }

        // The club rides on the player's belt: it must not push the player's body or hands.
        static void IgnorePlayer(Club k)
        {
            if (k.IgnoresPlayer || !Alive(Belt)) return;
            k.IgnoresPlayer = true;
            var mine = k.Go.GetComponentsInChildren<Collider>(true);
            int pairs = 0;
            foreach (var pc in Belt.root.GetComponentsInChildren<Collider>(true))
            {
                if (pc.isTrigger || IsClubCollider(pc)) continue;
                foreach (var c in mine) { Physics.IgnoreCollision(c, pc, true); pairs++; }
            }
            if (Dbg) Log.Msg($"club ignores {pairs / Math.Max(1, mine.Length)} player colliders");
        }

        // A holstered or wall-mounted club collides with nothing, like the game's socketed guns
        // (HVRSocket.DisableCollision -> HVRGrabbable.SetAllToTrigger). Triggers still reach the
        // hand's grab bag, so the draw works. Drawn again: the same colliders turn solid.
        // Line of sight must go off too: HVRHandGrabber.CheckLineOfSight raycasts grabbable.Colliders with
        // QueryTriggerInteraction.Ignore, so it never hits a ghosted club and CanGrab/CanHover fail (1.1.1 bug:
        // the club sat in the grab bag but was never hovered).
        static void SetGhost(Club k, bool ghost)
        {
            if (!Alive(k.Go)) return;
            if (!ghost)
            {
                foreach (var c in k.Ghosted) if (Alive(c)) c.isTrigger = false;
                k.Ghosted.Clear();
                if (k.SavedLos.HasValue && Alive(k.Grab)) k.Grab.RequireLineOfSight = k.SavedLos.Value;
                k.SavedLos = null;
                // Belt-and-braces: re-assert the player ignore pairs on the solid colliders.
                k.IgnoresPlayer = false;
                IgnorePlayer(k);
                return;
            }
            if (Alive(k.Grab) && k.Grab.RequireLineOfSight) { k.SavedLos ??= true; k.Grab.RequireLineOfSight = false; }
            int n = 0;
            foreach (var c in k.Go.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                var mesh = c.TryCast<MeshCollider>();
                if (Alive(mesh) && !mesh.convex) continue; // a concave mesh can't be a trigger
                c.isTrigger = true;
                k.Ghosted.Add(c);
                n++;
            }
            if (Dbg && n > 0) Log.Msg($"club '{k.Go.name}': {n} collider(s) made non-solid while holstered");
        }

        static bool IsClubCollider(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null) return false;
            foreach (var k in Clubs) if (Alive(k.Rb) && k.Rb.Pointer == rb.Pointer) return true;
            return false;
        }

        static void SaveSlots()
        {
            // Match the game's loadout policy: contract combat must not overwrite
            // the equipment prepared in The Range before entering the level.
            var game = ANBStaticGameManager.ANBmain;
            if (!Alive(game) || !game.gameStarted || !game.IsRangeScene) return;
            var v = (Slots[0].Full ? "L" : "") + (Slots[1].Full ? "R" : "");
            if (SavedSlots.Value == v) return;
            SavedSlots.Value = v;
            Cat.SaveToFile(false);
        }

        // ---------- template ----------

        static void BuildTemplate(GameObject crowbar)
        {
            Holder = new GameObject("BillyClubs-Template");
            Holder.SetActive(false);
            Object.DontDestroyOnLoad(Holder);
            Template = Object.Instantiate(crowbar, Holder.transform);
            Template.name = "BillyClub";
            var root = Template.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;

            // Visible crowbar meshes, measured in the club's own space.
            var mrs = Template.GetComponentsInChildren<MeshRenderer>(true);
            Material source = null;
            bool any = false;
            Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            foreach (var mr in mrs)
            {
                if (!ActiveUnder(mr.transform, root) || !mr.enabled) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                source ??= mr.sharedMaterial;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mr.transform.TransformPoint(corner));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                any = true;
            }
            if (!any) { Log.Error("crowbar has no visible mesh - template not built"); Object.Destroy(Holder); Template = null; return; }

            var size = max - min;
            var center = (min + max) * 0.5f;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            var dir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

            // The hand poses hold the crowbar's shaft, and the mesh bounds are pulled sideways by the hooks.
            // The longest box collider is the shaft: centre the club on its line.
            BoxCollider shaft = null; float shaftLen = 0f; Vector3 shaftDir = dir;
            foreach (var bc in Template.GetComponentsInChildren<BoxCollider>(true))
            {
                var bt = bc.transform;
                for (int i = 0; i < 3; i++)
                {
                    var ax = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                    var w = root.InverseTransformVector(bt.TransformVector(ax * bc.size[i]));
                    if (w.magnitude > shaftLen) { shaftLen = w.magnitude; shaft = bc; shaftDir = w.normalized; }
                }
            }
            if (shaft != null)
            {
                if (Vector3.Dot(shaftDir, dir) < 0) shaftDir = -shaftDir;
                var sc = root.InverseTransformPoint(shaft.transform.TransformPoint(shaft.center));
                var off = sc - center; off -= Vector3.Project(off, shaftDir);
                if (Dbg) Log.Msg($"shaft collider {shaftLen:0.00} m long; club moved {off.magnitude * 100f:0.0} cm off the mesh-bounds centre onto it, axis tilt {Vector3.Angle(shaftDir, dir):0.0} deg");
                center = sc; dir = shaftDir;
            }

            // The silver end goes away from the main grip point.
            float gripAlong = 0f;
            var gp = root.Find("Base - Grab/GrabPoints/GrabPoint_Base");
            Vector3 gripLocal = center, gripFwd = Vector3.forward;
            if (gp != null) { gripLocal = root.InverseTransformPoint(gp.position); gripFwd = root.InverseTransformDirection(gp.forward); }
            if (gp != null)
            {
                var g = root.InverseTransformPoint(gp.position) - center;
                gripAlong = Vector3.Dot(g, dir);
                if (Dbg) Log.Msg($"grip point {gripAlong:0.00} m along the shaft, {(g - dir * gripAlong).magnitude * 100f:0.0} cm off its line");
            }
            var tipDir = gripAlong <= 0 ? dir : -dir;

            TemplateAxis = tipDir;
            TemplateCenter = center;
            InitGrip(gripLocal, gripFwd);
            ApplyMiddleGrip(Template, true);
            HideCrowbarMeshes(Template);
            BuildVisual(root, center, tipDir, source);

            var rb = Template.GetComponent<Rigidbody>();
            if (rb != null) rb.mass = Mass.Value;
            var phys = Template.GetComponent<ANBGeneratePhysics>();
            if (phys != null)
            {
                phys.useRandomDesign = false;
                phys.RBMass = Mass.Value;
            }

            var t = Template.GetComponent<ANBAssistedThrowingObject>();
            if (t != null)
            {
                t.dontUse = false;
                t.speed = ThrowSpeed.Value;
                t.targetSearchDistanceOverride = ThrowSearch.Value;
                t.maxFlyDistanceOverride = ThrowMaxFly.Value;
            }
            var blunt = Template.GetComponent<ANBBluntWeapon>();

            if (Dbg) Log.Msg(GripInfo());
            Log.Msg($"template built from the crowbar: axis {"xyz"[axis]}, crowbar length {size[axis]:0.00} m, grip at {gripAlong:0.00} m from centre, material '{Name(source)}' shader '{(source != null ? source.shader.name : "none")}'");
            if (Dbg)
            {
                if (t != null) Log.Msg($"throw assist: speed {t.speed}, stop {t.stopDistance}, search {t.targetSearchDistanceOverride}, max fly {t.maxFlyDistanceOverride}, rotate {t.changeRotation}, torque {t.torque}, spin axis {t.spinAxis}");
                else Log.Warning("no ANBAssistedThrowingObject on the crowbar copy");
                if (blunt != null) Log.Msg($"blunt: canKill {blunt.canKill}, speeds {blunt.slowSpeed}/{blunt.mediumSpeed}/{blunt.fastSpeed} m/s, damage {blunt.damageOnSlow}/{blunt.damageOnMedium}/{blunt.damageOnFast}, head x{blunt.headMultiplier}, limb x{blunt.limbMultiplier}, impact {blunt.impactStrength}");
                else Log.Warning("no ANBBluntWeapon on the crowbar copy");
            }
        }

        static void ApplyMassLive(float kg)
        {
            if (kg < 0.05f) return;
            int n = 0;
            if (Alive(Template))
            {
                var trb = Template.GetComponent<Rigidbody>(); if (trb != null) trb.mass = kg;
                var tp = Template.GetComponent<ANBGeneratePhysics>(); if (tp != null) tp.RBMass = kg;
            }
            foreach (var k in Clubs)
            {
                if (!Alive(k.Go)) continue;
                if (Alive(k.Rb)) k.Rb.mass = kg;
                var p = k.Go.GetComponent<ANBGeneratePhysics>(); if (p != null) p.RBMass = kg;
                n++;
            }
            Log.Msg($"club mass {kg:0.##} kg ({n} club(s) updated)");
        }

        static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        static void HideCrowbarMeshes(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!IsVisualPart(r.transform)) r.enabled = false;
        }

        static bool IsVisualPart(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name == VisualName) return true;
            return false;
        }

        // Red knurled body with a silver tip section and silver end caps (like the reference art).
        static void BuildVisual(Transform root, Vector3 center, Vector3 tipDir, Material source)
        {
            if (UseCustomModel.Value && TryBuildCustomVisual(root, center, tipDir, source)) return;

            var vis = new GameObject(VisualName).transform;
            vis.SetParent(root, false);
            vis.localPosition = center;
            vis.localRotation = Quaternion.FromToRotation(Vector3.up, tipDir); // local +Y = towards the tip
            vis.gameObject.layer = root.gameObject.layer;

            if (!ColorUtility.TryParseHtmlString(BodyColor.Value, out var bodyColor)) bodyColor = new Color(0.35f, 0.03f, 0.04f, 1f);
            var red = MakeMaterial(source, bodyColor, 0.35f, 0.35f);
            var silver = MakeMaterial(source, new Color(0.75f, 0.75f, 0.77f, 1f), 0.9f, 0.75f);

            float L = Length.Value, r = Radius.Value;
            float tip = L * 0.25f, body = L - tip;
            // Body from the grip end (-L/2) to where the tip starts.
            Part(vis, "Body", -L * 0.5f + body * 0.5f, body, r, red);
            Part(vis, "Tip", L * 0.5f - tip * 0.5f, tip, r * 0.9f, silver);
            Part(vis, "Band", -L * 0.5f + body, 0.012f, r * 1.08f, silver);
            Part(vis, "CapGrip", -L * 0.5f + 0.005f, 0.01f, r * 1.05f, silver);
            Part(vis, "CapTip", L * 0.5f - 0.005f, 0.01f, r * 0.95f, silver);
        }

        static void Part(Transform parent, string name, float along, float length, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.name = name;
            go.layer = parent.gameObject.layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = new Vector3(0, along, 0);
            t.localRotation = Quaternion.identity;
            // Unity's cylinder is 2 m tall and 1 m wide.
            t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Material MakeMaterial(Material source, Color color, float metallic, float smoothness)
        {
            Material m;
            if (source != null) m = new Material(source);
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
            }
            foreach (var tex in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            foreach (var kw in new[] { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_EMISSION" }) m.DisableKeyword(kw);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        // ---------- spawn ----------

        static Club NewClub(string name, Vector3 pos, Quaternion rot)
        {
            var go = Object.Instantiate(Template, pos, rot);
            go.name = name;
            FreeFromOptimiser(go);   // copies of the template start with the grabbable switched off (Optimiser.cs)
            ApplyGrip(go);
            ApplyMiddleGrip(go);
            var k = new Club
            {
                Go = go,
                Rb = go.GetComponent<Rigidbody>(),
                Grab = go.GetComponent<HVRGrabbable>(),
                SpawnedAt = Time.time,
                HideChecks = 10,
            };
            Clubs.Add(k);
            ApplyInertia(k);
            return k;
        }

        // No player belt found (shouldn't happen in game scenes): a pair floats in front of you.
        static void SpawnFloating()
        {
            var cam = Camera.main;
            if (cam == null) { Log.Warning("no camera - can't place the clubs"); return; }
            for (int i = 0; i < Clubs.Count; i++)
                if (Clubs[i].In == null && !Clubs[i].Held && !OnBack(Clubs[i].Go)) { Object.Destroy(Clubs[i].Go); Clubs.RemoveAt(i--); }

            var head = cam.transform;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var basePos = head.position + fwd * 0.45f - Vector3.up * 0.35f;
            // Lying flat, tip pointing forward, so the two clubs sit side by side and don't overlap.
            var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.FromToRotation(ClubAxis, Vector3.forward);

            for (int s = -1; s <= 1; s += 2)
            {
                var k = NewClub(s < 0 ? "BillyClub-L" : "BillyClub-R", basePos + right * (0.12f * s), rot);
                k.Floating = true;
                if (Alive(k.Rb))
                {
                    k.Rb.useGravity = false;
                    k.Rb.linearVelocity = Vector3.zero;
                    k.Rb.angularVelocity = Vector3.zero;
                }
            }
            Log.Msg($"no player belt found - spawned a pair of clubs in front of you at {V(basePos)}");
        }

        // ---------- helpers ----------

        static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        static Vector3 ParseV(string s, Vector3 fallback)
        {
            var p = (s ?? "").Split(',');
            if (p.Length != 3) return fallback;
            var ci = CultureInfo.InvariantCulture;
            return float.TryParse(p[0], NumberStyles.Float, ci, out var x) && float.TryParse(p[1], NumberStyles.Float, ci, out var y)
                && float.TryParse(p[2], NumberStyles.Float, ci, out var z) ? new Vector3(x, y, z) : fallback;
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        internal static bool Alive(Object o)
        {
            try { return o != null && !o.WasCollected && o; }
            catch { return false; }
        }

        static string Name(Object o) => Alive(o) ? o.name : "none";
        static string V(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
    }

    // Every grab (hand or socket) goes through here before the grab is set up.
    [HarmonyLib.HarmonyPatch(typeof(HVRGrabberBase), nameof(HVRGrabberBase.GrabGrabbable))]
    static class GrabGrabbablePatch
    {
        static void Prefix(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            try { BillyClubsMod.BeforeGrab(grabbable, grabber); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] BeforeGrab: {e.Message}"); }
        }
    }

    // The Range resets every physics prop when a wave starts (ANBGameLogic.resetPhysicObjects ->
    // ANBGeneratePhysics.resetMe -> teleport to the spawn spot). The clubs inherited that from the crowbar.
    [HarmonyLib.HarmonyPatch(typeof(ANBGeneratePhysics), nameof(ANBGeneratePhysics.resetMe))]
    static class ResetMePatch
    {
        static bool Prefix(ANBGeneratePhysics __instance)
        {
            try
            {
                if (!BillyClubsMod.IsClub(__instance.gameObject)) return true;
                BillyClubsMod.SkippedReset();
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ANBGameLogic), nameof(ANBGameLogic.resetPlayerLoadout))]
    static class PlayerLoadoutResetPatch
    {
        static void Postfix(ANBGameLogic __instance)
        {
            try { BillyClubsMod.PlayerLoadoutReset(__instance); }
            catch (Exception e) { MelonLogger.Error($"[BillyClubs] checkpoint reset: {e}"); }
        }
    }
}
