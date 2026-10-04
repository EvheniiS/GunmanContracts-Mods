# VR Holster Customization — split plan (Sep 29 2026)

Historical design notes. The implemented settings and install steps are in [README.md](README.md).

Working plan for a new mod that takes over **everything about holsters** from Weapon Framework, and then adds the
highly requested feature: **move and resize the game's own holsters from the Mod Settings board**. Written at the end of
the Weapon Framework 0.2.x session so a new chat can start from this file alone.

- Folder: `VRHolsterCustomization/` (repo rule: one folder per mod, no spaces). Display name **VR Holster Customization**.
- DLL / assembly / namespace: `VRHolsterCustomization`. Log prefix `[VR_Holster_Customization]` (MelonLoader turns
  spaces into underscores).
- Branch: `dev`, same repo. Never a feature branch.

---

## 1. The rule: who controls what

**VR Holster Customization = where things sit on your body, and how they get in and out.**
**Weapon Framework = which mod weapons exist, and how you get them.**

| Question | Owner |
|---|---|
| Where is a holster, how big is its range, which side holds what? | **VR Holster Customization** |
| Putting an item in (snap on release, hover buzz), taking it out (draw assist), refusing a second weapon | **VR Holster Customization** |
| Saving and restoring what's holstered across scenes and restarts | **VR Holster Customization** |
| Keeping a *docked* item in place: pinned, kinematic, not colliding with you, not switched off by the game's optimiser, not teleported by the prop reset | **VR Holster Customization** (the "dock" layer, section 4) |
| Measuring an item's shape (long axis, grip end, widest side) | **VR Holster Customization** (holsters need it; the wall uses it too) |
| Moving the game's own hip, knife and back holsters | **VR Holster Customization** (new, phase 2) |
| Arsenal terminal entries, the save guard (`saveSpotLarge`), paging, Retrieve | **Weapon Framework** |
| Making mod items: templates (`MakeTemplate`), `Spawn` | **Weapon Framework** |
| The wall: hanging items on the mount, `OnShown/OnSettled/OnHidden`, putting items back on the wall (roadmap) | **Weapon Framework** (uses the dock layer to pin) |
| JSON weapon packs, runtime panel pictures, the pistol wall | **Weapon Framework** |
| Test crowbar (the framework's example weapon) | **Weapon Framework** (registers itself with the holsters API like any mod) |
| Log lines: `holster …` / `after load: loadout …` | **VR Holster Customization** |
| Log lines: `retrieve …`, `stand: took/put back …`, `after load: stands filled …`, `save: …` | **Weapon Framework** |

Tie-breaker for anything new: **if it would still make sense with zero mod weapons installed (vanilla guns only), it
belongs to VR Holster Customization.** If it only makes sense because a mod added a weapon, it belongs to Weapon
Framework.

---

## 2. Dependencies

```
VR Holster Customization   (base layer, works alone: vanilla players can just rearrange holsters)
        ▲            ▲
        │ requires   │ requires
Weapon Framework     │
        ▲            │
        │ requires   │
     Daredevil ──────┘   (also Gloves, Throw Assist, Mod Settings, as today)
```

- **VR Holster Customization depends on nothing** of ours (Mod Settings is optional: its settings are plain
  MelonPreferences, the board just shows them).
- **Weapon Framework hard-requires VR Holster Customization.** It references the DLL (`<Reference>` with
  `<Private>false</Private>`) and calls it directly: the dock layer for wall hanging, `RegisterKind` for the test
  crowbar. Add `[assembly: MelonAdditionalDependencies("VRHolsterCustomization")]` so MelonLoader loads it first and
  names it when it's missing.
- **Daredevil calls VR Holster Customization directly** for the back holsters (today it goes through
  `WeaponFramework.Holsters`). Keep its current optional-call pattern (`FrameworkPresent` + `[MethodImpl(NoInlining)]`
  wrappers) so a missing DLL can never throw on load; add VR Holster Customization to its "required mods not installed"
  warning list in `Daredevil.cs`.
- No upgrade path (settled by Evhenii: nobody downloaded a 0.2.x). Config sections get new names; copy his tuned values
  by hand once (section 6).

---

## 3. Exactly what moves (from Weapon Framework 0.2.4)

State at the time of writing: Weapon Framework **0.2.4** installed (hash `60CF4D28…`), 0.2.1–0.2.3 tested in game (see
`HISTORY.md` (Weapon Framework section), `GAME_KNOWLEDGE.md` §5 and `WeaponFramework/TESTING.md`). 0.2.4 adds
hover haptics, the new pose defaults and the "waiting side" restore fix, **untested**. Commit 0.2.4 on `dev` before
starting, so the move is a clean diff.

### 3.1 `WeaponFramework/Holsters.cs` → `VRHolsterCustomization/BackSlots.cs` (whole file)

Moves as-is, then gets its dependencies rewired:

| Piece | Notes |
|---|---|
| `public sealed class HolsterKind { Id, IsMine, Spawn }` | Public API. Keep the name. |
| `public static class Holsters`: `RegisterKind`, `TryHolster`, `Holds` | Public API. Keep the names (Daredevil + crowbar only change the namespace). |
| `Holsters.Track` (internal today) | Make **public**, rename `Watch(GameObject)`: "the holster mod watches this item for a release itself" (for mods that don't call `TryHolster`, e.g. the test crowbar). |
| `Slot` (back-left / back-right on `HVRShoulderSocket.leftShoulder/rightShoulder`), `FindSockets` + the one-time calibration log line | |
| Pose (`PinIn`, `AnchorWorld`), settings `BackHolsters OutCm UpCm BackCm DropCm TiltDeg LeanDeg SpinDeg SnapCm DrawCm SavedBackHolsters` | Category `[WeaponFrameworkHolsters]` → `[VRHolsters_BackSlots]`, display "VR Holster Customization: back slots for mod items". |
| Draw assist (`DrawAssist`, `Hands`, `Nearest`, `Where`, `MainGrabbable`, miss/draw log lines) | |
| Hover haptics (`HoverHaptics`, `Controller.Vibrate(0.35, 0.06, 150)`) | |
| `BlocksGrab` (refuses the game's back socket on a full side) + `BlocksGameSocket` | |
| `Save` / `Restore` / `pending` (a side that can't be made yet waits) | |
| `ShoulderSocketHoverPatch` (`HVRShoulderSocket.CanHover` postfix) | |
| `IgnorePlayer` | Moves to the dock layer (3.2), both need it. |
| `MaxSnapSpeed = 5`, `DrawWindow = 0.5` | |

### 3.2 `WeaponFramework/Items.cs` → split

**To VR Holster Customization, as `Dock.cs` (public static class `Dock`) + `ItemShape.cs`:**

| From `Items` | To | Why it moves |
|---|---|---|
| `Pin`, `Unpin` (internal today) | `Dock.Pin`, `Dock.Unpin` (public) | Holsters and the wall both pin. |
| `hung` list, `Hang` / `IsHung` / `Unhang`, `Tick` re-pinning, `BeforeGrab` unhang | **`Dock` with an owner tag**: `Dock.Hold(go, parent, localPos, localRot, owner)`, `Dock.Release(go)`, `Dock.OwnerOf(go)` | One item = one owner. Holding it somewhere new (wall → back) releases the old owner, which replaces today's `Items.Unhang` call inside `Holsters.Put`. The wall becomes owner `"wall"`, the back slots owner `"back-left"` etc. |
| `Manage`, `IsManaged`, `managed` list, `FreeFromOptimiser`, once-a-second re-check | `Dock.Manage` | A docked item behind you is exactly what `GD_HVROptimiser` switches off. |
| `ItemResetPatch` (`ANBGeneratePhysics.resetMe` prefix) | `Dock` | The wave-start prop reset would yank a holstered item. |
| `IsHeld` | `Dock.IsHeld` | |
| `Measure`, `Widest` | `ItemShape.Measure`, `ItemShape.Widest` | Holsters need the long axis and grip end; the wall uses `Widest` to lay items flat. |
| `IgnorePlayer` (from `Holsters.cs`) | `Dock.IgnorePlayer` | Any item riding on the body. |

**Stays in Weapon Framework `Items.cs`:** `MakeTemplate`, the inactive `holder`, `Spawn` (now calls `Dock.Manage`),
and `Hang` as a thin wrapper: `Dock.Hold(go, mount, pos, rot, "wall")`.

### 3.3 `WeaponFramework/ActionLog.cs` → split

**To VR Holster Customization, as `HolsterLog.cs`:**
- The game-holster patches: `HolsterGunPatch`, `UnholsterGunPatch` (`ANBGameLogic.holsterGun/unholsterGun`),
  `HolsterKnifePatch`, `UnholsterKnifePatch` (`holsterKnife/unholsterKnife`, keyed `knife-<side>`).
- `Holster(...)`, the `Loadout` dictionary, `ModHolster(...)`, the name helpers `Gun(...)` and `Knife(...)` (WPT →
  `ANBWeaponType` on self/parent → `knifeID` → object name).
- Its **own** load fold: a `ANBGameLogic.LogLoadTime` postfix and the 6 s + 2 s/step window, exposed as
  `HolsterLog.Loading` (replaces `ActionLog.Loading`, which the restore waits on). Its own burst folding (4+ holster
  events in 0.2 s = one "scene change" line) and 0.5 s duplicate filter.
- The `after load` line becomes holster-only: `after load: loadout backLeft AB15, knife-left …`.

**Stays in Weapon Framework `ActionLog.cs`:** `Retrieve`, `Stand` (+ `StandTakePatch`/`StandPutBackPatch`),
`ModItemTaken`, its own fold/burst for stand events, `after load: stands filled (N put, M taken)`.

### 3.4 The grab hook (`HVRGrabberBase.GrabGrabbable` prefix, in `ActionLog.cs` today)

Both mods keep **their own** prefix on the same method (Harmony allows several):
- **VR Holster Customization:** `static bool Prefix(...)`: `BackSlots.BlocksGrab` → `return false`; else
  `Dock.BeforeGrab(grabbable)` (a hand takes a docked item: release its owner, unpin) and the back slots' "out" line.
- **Weapon Framework:** `static void Prefix(...)`: only its `stand: took '<Id>' item` log line for items on its mounts.
- ⚠ When one prefix returns `false`, Harmony still runs the other prefixes; WF's prefix must not assume the grab
  happens (it only logs, so fine). Give the holster prefix `[HarmonyPriority(Priority.High)]` so the block runs first.

### 3.5 `WeaponFramework/WeaponFramework.cs` wiring to remove

- `Holsters.Init()`, `Holsters.Scene()`, `Holsters.Tick()`, `Items.Tick()` (re-pinning moves to `Dock`, driven by VR
  Holster Customization's own `OnUpdate`), `Holsters.BeforeGrab(g)` / `Items.BeforeGrab(g)` in `BeforeGrab`.
- Keep `ActionLog.Scene/Tick/LoadStep`, `Arsenal`, the save guard, `TestCrowbar.Init/Scene`.
- `csproj`: drop nothing else; add the `VRHolsterCustomization` reference.

### 3.6 Callers to update

| File | Change |
|---|---|
| `WeaponFramework/TestCrowbar.cs` | `Holsters.RegisterKind/Track/Holds` → `VRHolsterCustomization.Holsters.RegisterKind/Watch/Holds`; `Items.IsHeld/Measure/Widest/IsHung` → `Dock` / `ItemShape`. |
| `Daredevil/BillyClubs/Arsenal.cs` (lines ~52-68) | `WeaponFramework.Holsters.*` / `WeaponFramework.HolsterKind` → `VRHolsterCustomization.*`. Keep `FwTryHolster`/`OnBack` names or rename to `HolsterTry`/`OnBack`; keep the NoInlining wrappers and a separate "holster mod present" flag. |
| `Daredevil/Daredevil.cs` Required list | add `("VRHolsterCustomization", "VR Holster Customization (clubs on your back)")`. |
| `WeaponFramework/Example/ExampleBaton.cs` + `MODDING_GUIDE.md` | point to the new API for holstering. |
| `WeaponFramework/Check-WFSave.ps1` | it greps `[Weapon_Framework]`; holster lines now say `[VR_Holster_Customization]`. |

---

## 4. The dock layer (the one shared mechanism)

A "docked" item = pinned under a transform, kinematic, re-pinned every frame until a hand takes it. Wall mounts, back
slots, later belt slots and any custom slot are all docks. Every trap already paid for lives here, once:

- **`GD_HVROptimiser`** collects grabbables once and switches off those behind or far from you; docked items are taken
  out of its `_grabbables` list and re-checked once a second.
- **`ANBGeneratePhysics.resetMe`** (wave start) teleports copies of a prop to its spawn spot; managed items are skipped.
- **The prop's own physics script turns physics back on a frame after spawn** (`ANBGeneratePhysics.Init`), so pinning
  is re-applied every frame, not once.
- **Grab Fix treats kinematic items as docked** and only lets a hand hover them inside its small native sphere (on
  purpose). Docked items behind you therefore need the draw assist; anything docked in plain view (wall) doesn't.
- **The player's own colliders** must ignore a docked item riding on the body (`Physics.IgnoreCollision` against the
  rig's non-trigger colliders).
- **One owner per item** (new): `Dock.Hold` by a new owner releases the old one; `Dock.OwnerOf` answers "is it on the
  wall / on my back?" (replaces `Items.IsHung` + `Holsters.Holds` checks in callers).

---

## 5. Game facts the new mod builds on

| Holster | Game object | Where | Saved as |
|---|---|---|---|
| Back guns (2) | `HVRShoulderSocket` (`leftShoulder` / `rightShoulder` flags), `…/Camera/HeadRelativeInventory/LeftShoulder`, `RightShoulder` | local L (-0.36, 0, -0.09), R (0.33, 0, -0.09) in `HeadRelativeInventory`; that frame **follows head yaw and partly pitch** (its up was (-0.05, 0.95, -0.32) one session, (-0.05, 0.98, 0.21) the next) | `holsterGunBackLeft/Right` |
| Hip guns (2) | `HurricaneVR.TechDemo.Scripts.DemoHolster` (`leftHolster` / `rightHolster`) under `PlayerObject/TechDemoXRRigOpenXR/Waist/Holsters` | hip R at (0.27, 0, 0) belt space | loadout `left` / `right` |
| Knives (2) | same `DemoHolster` class (`leftKnifeHolster` / `rightKnifeHolster`, `knifeScript`), e.g. `…/Waist/Holsters/HolsterKnifeRight` | | loadout knife sides |
| Daredevil clubs (2, mod) | Daredevil's own belt slots under `…/Waist/Holsters` | L (-0.23, -0.12, -0.1), R (0.23, -0.12, -0.1) belt space; **too close to the hip pistols** (Evhenii: "basically unusable with pistols") | `[BillyClubs] SavedHolsters` |

Code paths (from the binary, `il2cpp_tools`):
- `HVRShoulderSocket.OnGrabbed` and `DemoHolster.OnGrabbed` both call `ANBGameLogic.holsterGun(side, gun)`;
  `DemoHolster.OnGrabbed` also calls `holsterKnife`. Side strings: `left/right/backLeft/backRight`; knives reuse
  `left/right`.
- **Every grab ends in the virtual `HVRGrabberBase.GrabGrabbable`**, declared only on the base class (`TryGrab(g, force)`
  = `CanGrab` unless forced, then `GrabGrabbable` via vtable 0x3c8). That's the one watertight place to refuse a socket.
  `HVRShoulderSocket.CanHover` alone did NOT stop the game (0.2.0 test).
- Hover haptics for guns: `HVRSocket.GunHoverHaptics` → `ANBHVRGunBase.HolsterHaptics`. Ours:
  `HVRHandGrabber.Controller.Vibrate(amplitude, duration, frequency)`.
- Grip state: `HVRHandGrabber.IsGripGrabActive` (held), `TryGrab(grabbable, force: true)` to draw.
- Loadout restore: `ANBGameLogic.LoadContractHolstersSingle` matches ids against prefab lists; the load finishes late
  (The Range builds its stands ~40 s in), hence restore waits for the load fold.

**Unknowns to settle first in phase 2 (read the code before theorising, repo rule):**
1. Does anything rewrite the hip/knife holster `localPosition` each frame (a waist-follow script on `Waist`)? If yes,
   apply the offset after it (LateUpdate postfix) instead of once.
2. What decides a holster's range: the trigger collider radius on the socket object, `HVRSocket.Size`, or a
   `HVRSocketBag` distance (`MaxDistanceAllowed`)? Dump `HVRSocket` / `DemoHolster` / `HVRShoulderSocket` fields and
   the colliders on those objects before choosing the "range" setting.
3. Does the rig (`PlayerObject`) survive scene loads (DontDestroyOnLoad)? Decides whether offsets are applied once per
   game or once per scene.
4. The holster visuals (`holster_holograph` material, `Rim Dissolve` shader, seen by Daredevil): are they children of
   the socket (move with it) or separate?

---

## 6. Phases

### Phase 0 — before starting (this chat's leftovers)
- Test Weapon Framework 0.2.4 (hover buzz, new pose, "waits" restore). Optional: the move keeps behaviour identical,
  so 0.2.4 results carry over.
- Commit on `dev`: WF 0.2.4, Daredevil 0.3.5, docs.

### Phase 1 — the move, no behaviour change
1. `VRHolsterCustomization/VRHolsterCustomization.csproj` (copy `WeaponFramework.csproj`: net6.0, MelonLoader 0.7.3
   references, `Il2CppGD_Game`, `Il2CppHurricaneVR.Framework`, `UnityEngine.PhysicsModule`), `VRHolsterCustomization.cs`
   (`MelonMod`: `OnInitializeMelon` → settings + Harmony; `OnSceneWasInitialized` → `BackSlots.Scene`,
   `HolsterLog.Scene`; `OnUpdate` → `HolsterLog.Tick`, `Dock.Tick`, `BackSlots.Tick`). Version **0.1.0**.
2. Move per section 3. Weapon Framework → **0.3.0**, Daredevil → **0.4.0**.
3. Build all three plus `Example/ExampleBaton.csproj`, 0 warnings.
4. Install all three with the game closed (back up DLLs + `MelonPreferences.cfg` to `feature/backup-before-VRHC-0.1.0-*`),
   copy Evhenii's tuned values into the new section: `OutCm -13`, `BackCm 7`, `DropCm 30`, `DrawCm 20`, and his current
   `SavedBackHolsters`.
5. **Acceptance = the 0.2.3/0.2.4 test again, same log lines under the new prefix:** crowbar + clubs into both back
   sides, drawn with either hand, a rifle refused on a full side, carried into a contract, "waits" when a contract is
   loaded before The Range. Plus: disable `VRHolsterCustomization.dll` → Weapon Framework and Daredevil log the missing
   dependency and nothing throws.

### Phase 2 — move the game's own holsters (the requested feature)
**Built** (per-slot settings, `AllUpCm`, move by hand). Open follow-ups (mirror/multi-slot move, highlight the real holster
while moving) are in the root [ROADMAP.md](../ROADMAP.md) §1.
- Settings per holster group, all live on the Mod Settings board (ints, as today, since free-form strings are read-only
  there): `[VRHolsters_Hip]`, `[VRHolsters_Knife]`, `[VRHolsters_Back]` with `OutCm UpCm ForwardCm` (mirrored L/R),
  `TurnDeg`, and a range setting once unknown 2 is answered. Default all 0 = vanilla.
- While a setting changes, show the holster (the game's own holograph material) so you can see where it went.
- Log once per scene: where each holster ended up (belt/head space), so positions can be tuned from the log like the
  back slots were.

### Phase 3 — generic extra slots, then Daredevil's belt
**Dropped Oct 4 2026:** moving the game's gun holsters already fixes "clubs too close to the pistols".
- The back slots become one instance of a generic "mod slot" (body part: belt / chest / back, pose, which kinds fit).
- Migrate Daredevil's two belt club slots onto it (they already duplicate pin/optimiser/restore code: `BillyClubs.cs`
  `Slot`, `SaveSlots`, `Holster`, `HolsterVisual.cs`, `Optimiser.cs`, `DrawWatch.cs`). Fixes "clubs too close to the
  pistols" by making their position a setting.

### Later
- **Custom holster sounds** (idea, Oct 3 2026): own click and sheath sounds for the katana and clubs on the back, instead of borrowing the
  belt knife holster's clip (0.3.8). Needs sound files (embedded WAV via `AudioClip.Create`, or AudioImportLib; GAME_KNOWLEDGE §7).
- Flat mode: the holster mod does nothing without a VR rig (check before touching sockets).
- The template limit (a contract loaded before visiting The Range has no crowbar/clubs to put on your back) belongs to
  Weapon Framework, not here: the holster mod only waits for `Spawn` to succeed (`pending`).

---

## 7. References

- History and test results: `HISTORY.md` (Weapon Framework, VR Holster Customization) and `GAME_KNOWLEDGE.md` §5.
- Weapon Framework: `WeaponFramework/README.md` (0.2.x section, known limit), `ROADMAP.md`, `TESTING.md`.
- The request ("HIGHLY REQUESTED: holster management", Sep 29 2026): players want to choose where holsters sit; built as this mod.
- Binary tools: `il2cpp_tools/` (`dumpt.py <Type>`, `disa.py <Namespace.Type::Method | 0xADDR>`, `xref.py`, `slot.py`);
  set `PYTHONIOENCODING=utf-8` first. `il2cpp_tools/sockets.txt` = earlier socket field dump.
- Logging rule: one short line per event, fold load-time noise (see `VR/CLAUDE.md` "Working rules for mod projects").
