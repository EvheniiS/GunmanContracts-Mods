# Quiver for Gunman Contracts — implementation plan

> **For the next session:** read this file, then [../GUNMAN_CONTRACTS.md](../GUNMAN_CONTRACTS.md)
> (root causes, offsets, tooling). Everything below was checked against the game binary on
> Sep 24 2026 (game 0.3.1.0, MelonLoader 0.7.3). "✅ verified" = read in the code.
> "❓ test" = must be confirmed in game before relying on it.

## 0. Status

### v0.1.0 tested Sep 24 2026 — WORKS. Evhenii: "everything works", but "a bit laggy"
Log results (19 draws, 13 nocks, 6 stabs, 0 warnings):
- **Home socket:** `Socket` is the populated property (`LinkedSocket` always none; `StartingSocket`
  = the world `gunstorage` DemoHolster). His holster is `'RightShoulder'` (`HVRShoulderSocket`,
  `rightShoulder=True`). §3.3 ❓ answered.
- **Draw → nock → shoot:** every nock gave `createdNok=True`, 0 failed string grabs.
- **Ammo (6a): `AmmoArrows` stayed 150/150 across ~13 shots even with `createdNok=True`.** Arrows are
  infinite in practice. Nothing to build.
- **Stab (6b): works out of the box**, 6/6, `knifeDamage 100`. The arrow is destroyed on a human
  hit (`killArrowOnHumanHit=True`), which is probably the "looks a bit weird". `ArmHeldArrowAsKnife`
  is not needed. He uses the quiver arrow as a separate melee weapon.
- **Grip (6c.1):** the arrow has 2 posable grab points (group 0 at local z=0 = the notch end, group 1
  at z=0.257), **no `HVRGrabPointSwapper`**, mesh spans z 0 → −0.69. The hand took the z=0 point,
  and Evhenii confirmed "the grab on the end is correct". §3.8 dagger grip still unbuilt (not
  requested after the test).
- **★ Lag cause 1 — draw latency: all 19 presses started 32–42 cm from the socket PIVOT** and only
  drew once the hand drifted inside the 20 cm radius, **110–190 ms after the press**. The pivot is
  not where the hand goes. → v0.2.0 centres the quiver on **where the hand grabbed the bow out of
  that holster** (holster-local, remembered per holster name across scenes).
- **★ Lag cause 2 — both mods ran 3 × `FindObjectsOfType` every 2 s, on the same frame** (a
  scene-wide search, sorted by instance ID). A periodic one-frame hitch = VR reprojection stutter.
  → v0.2.0 (and ArrowGrabAssist 1.1.1) register loaders/hands via **Harmony postfixes on
  `HVRArrowLoader.Start` / `HVRHandGrabber.Start`**, plus one unsorted `FindObjectsByType` per
  scene as a safety net. Head fallback uses `Camera.main` instead of searching for the rig.
- Debug logging was never a meaningful cost (~150 lines per session).

### Versioning (Sep 24 2026)
`GunmanContracts/` is a git repo (local identity `evgeeso <evhenii.soroka@gmail.com>`; the global
git email is the work one, so never commit here without the local config). **`main` = release**:
ArrowGrabAssist 1.1.1 + ArrowQuiver 0.2.0, tagged, DLLs in `release/`, and those are deployed to the
game. **`feature/grip-switch` = ongoing work**, built to `feature/ArrowQuiver.dll` (not deployed).
The v0.2.0 source was rebuilt by reversing the 0.3.0 edits. The rebuilt DLL came out at exactly
27,136 bytes, the same as the tested v0.2.0 build.

### v0.3.4 (feature branch) — draw crash root cause; deployed with ArrowGrabAssist 1.2.0
0.3.3 test: **"generally it feels great, the hand switch position is right."** Remaining:
- **Draw crash, root cause found:** 3 of 49 draws threw, **each 0.3–1.5 s after a dropped arrow**
  (v0.2.0's single crash followed a drop too). The failing read in `HVRHandGrabber.OnGrabbed` is
  `[hand+0x288]` = `<PosableGrabPoint>k__BackingField`. It is alive (it passes `op_Implicit`), but its
  `Grabbable` is dead. A forced `TryGrab` lets the hand reuse that stale cache. Fix: the draw now
  always passes an explicit point, `Grab(grabbable, Active, NockEndPoint(arrow))` (the grab point
  closest to the arrow origin = the notch). It also orients the arrow into the hand instead of
  pulling it in. Exceptions crossing from IL2CPP into managed code are expensive (stack-trace
  build + log), so the crashes were likely also some of the reported hitches.
- ❓ **Hands drifting with the left stick** ("at some point my hands started moving together with my
  left stick; when I run forward the hands go ahead"). Not in the log; cause unknown. Hypotheses:
  a held item (dagger arrow held tip-down, or the bow) colliding with the player's own body collider
  while moving, or HVR hand state left behind by a release/re-grab. Needs: when it started (after a
  grip switch? a drop?), whether it persists after a scene change, and whether the release build
  (`main`) ever does it.
- Hitches: steady perf max 2–5 ms per 30 s window = the arrow `Instantiate` on draw (the game
  instantiates a second arrow on nock). If hitches remain after 0.3.4, reuse the carried arrow as the
  nocked arrow (`bow.NockArrow(carried)` + `createdNok`) to halve the instantiations.

### v0.3.3 (feature branch) — grip swap without the hand flying off
0.3.2 test: **the knuckle alignment is correct**, but (1) **after a swap the hand flew far off to
the right**, and (2) a dropped dagger arrow still showed the preview's **wrist health display**.
- (1) Cause, from the disassembly of `<SwapGrabPoint>d__380::MoveNext`: `ChangeGrabPoint` removes the
  joint, rotates the object with `Quaternion.AngleAxis(angle, GetVector(axis))` around **one fixed
  axis**, then `PoseHand`s. That only lands when the two grip points differ by a turn about that axis
  (the 180° flip did, the knuckle-aligned `FromToRotation` doesn't), so the hand ended up dragged to
  wherever the grip point expected it. **★ Rule: `ChangeGrabPoint` only works between points related
  by a rotation about a principal axis.** Fix: `SwapGrip` sets the arrow to
  `A' = H · D⁻¹ · A` (target point D lands exactly on the held point's frame H, so the hand pose
  doesn't move), then `HVRHandGrabber.Grab(grabbable, HVRGrabTrigger.Active, point)`. That overload
  force-releases, calls `OrientGrabbable` and completes the grab via `ExecuteNextUpdate`, so the carry
  has a 30-frame grace (`GraceUntil`) before a missing grab counts as a drop.
- (2) The health display is UI (CanvasRenderer, not a `Renderer`), so destroying renderers missed it.
  Every child of the clone is now deactivated except the path to its `HVRHandPoser`.
- Evhenii also couldn't press **Settings on the in-game phone**. Parked. Neither mod does anything
  while no bow is in hand (the screenshot shows the bow on the floor), so it's probably unrelated.
  To rule it out: set `Enabled = false` for both mods and retry.

### v0.3.2 (feature branch) — dagger grip along the knuckle line
Evhenii: with the FlipX dagger grip the arrow **goes through the palm**. It should lie along the
knuckle line (through the curled fingers), about 90° further round. No axis guessing this time:
the hand model exposes `HVRHandGrabber._posableHand` → `HVRPosableHand.Index/Pinky` →
`HVRPosableFinger.Root`. The index→little-finger direction is measured in the frame of the grip
point currently held. The hand pose is fixed relative to its grip point, so that vector is the same
in any grip point sharing the pose. The dagger point = back point rotated by
`R = FromToRotation(knuckleLocal, tipLocal)`. Holding it puts the tip along the knuckle line.
Config `DaggerOrientation` = `Knuckles` (tip past the little finger, default) | `KnucklesReverse` |
`FlipX` | `FlipY`. B/Y cycles it live (debug). The log line `dagger grip built: … shaft was N deg
off the knuckle line, knuckle span X cm` sanity-checks the measurement (span should be about 6–8 cm).
❓ Test: does the shaft now run through the fingers? Is 25 cm from the nock the right spot?

### v0.3.1 (feature branch) — fixes after testing 0.3.0
0.3.0 test: the grip switch works (dagger grip with flip X built and toggled 4×; in Evhenii's
screenshot the tip comes out of the little-finger side). Problems found:
- **Ghost glove on a dropped dagger arrow** (screenshot): cloning the grip point also cloned the
  hand poser's preview meshes (`RightHand_Gloves_LOD0`, …), which are hidden on the prefab's own
  points but visible on the clone. → the clone's renderers are destroyed.
- **"Grabbing got glitchier, gets stuck, less seamless."** 0.3.0 changed the draw path with
  `Physics.IgnoreCollision` for arrow × every bow collider plus `bowHand.UpdateCollision`. Changing
  PhysX ignore pairs on a jointed, held bow is the prime suspect. It also bought nothing: **all 5
  drops in the session were "grip released"**, i.e. the player let go, never the hand losing the
  arrow. `RepairGrabPoints` was also removed (never fired). → The draw path is back to 0.2.0's,
  plus only the try/catch that removes the arrow if `TryGrab` throws. ❓ Retest whether it's
  seamless again.
- Still in: grip switch, slip-nock rescue, warm-up (it ran in 51 ms; perf max was still
  14.7 / 26.8 ms in the first two 30 s windows, with debug `[inspect]` on the first draw).
- Two hands on one arrow (screenshot 2): HVR allows a second hand on any grabbable. Not handled
  specially, not obviously a bug. ❓ Ask.

### v0.3.0 (Sep 24 2026) — grip switch + robustness
- **§3.8 built.** A/X (edge on `Controller.PrimaryButtonState.Active`) calls
  `hand.ChangeGrabPoint(point, 0.15, axis)`, the knife swapper's call. The dagger grip is a runtime
  **clone of the grip point in use** (`hand.PosableGrabPoint`, taken 5 frames after the grab),
  moved `DaggerGripFromNock` (0.25 m) towards the middle of the shaft renderer (`Arrow01 (1)`; the
  prefab also carries hand-preview meshes that have to be skipped), then rotated 180° about local
  `DaggerFlipAxis` (X). ❓ The right flip axis isn't known: **with DebugLog on, B/Y cycles X → Y →
  None in game** and the choice is saved to the cfg.
- **Draw crash (1 of 52 in v0.2.0):** `TryGrab` threw an NRE in
  `HVRPosableGrabPoint.GetGrabbableRelativeRotation` → `get_transform` on the grab point's
  `Grabbable` (offset 0x70), i.e. a grab point pointing at a dead grabbable. The outer catch then
  left the spawned arrow orphaned. Now each drawn arrow's grab points are pointed at its own
  grabbable before the grab (logs `repaired N grab point(s)` if that was ever needed), and a
  throwing grab destroys the arrow.
- **"Nocking sometimes glitches":** 3 of ~55 carried arrows left the hand ~0.7–1 s after the draw,
  on the way to the string. v0.2.0 logged no reason. Now: (1) the carried arrow ignores collisions
  with the bow body and the bow hand (`HVRHandGrabber.UpdateCollision(arrow, false)` is what the game
  does for its nocked arrow); (2) if the arrow leaves the hand **while grip is still held** within
  `SlipNockRadius` (0.30 m) of the string, it's nocked anyway; (3) otherwise the drop line logs
  whether grip was held (the hand lost it) or released (the player let go), plus the distance to
  the string.
- **Hitches:** v0.2.0 perf log showed avg 11–48 µs/frame, steady max ~1.5–2.7 ms (frames with a
  draw: an arrow `Instantiate`), plus one-offs of **39 ms** (first scene) and **55 ms** (first draw,
  including `[inspect]`). Those are first-use costs: JIT and interop type setup. v0.3.0 pays them in
  a warm-up during the first scene load (`warm-up done in N ms`).
- v0.2.0 result: **52 draws, all fired on the press itself** (4–20 cm from the learned grab spot,
  0 `reached the quiver` delays). The grab-spot fix worked. No more periodic stutter.

### v0.2.0 (Sep 24 2026) — tested, works
Look for: `bow taken from 'RightShoulder': hand X cm from the holster pivot - quiver centred there`,
then `grip press X cm from quiver (grab spot, Y cm from the pivot)` → X should now usually be under
20 cm at the press itself, with no `reached the quiver … ms after the press` line. `perf: OnUpdate avg
… us, max … us` every 30 s = the mod's own cost. `scene check: … - N were missed by registration`
should say 0 missed.

### v0.1.0 — first build
**Sep 24 2026 — v0.1.0 built and deployed.** Source:
[ArrowQuiver/ArrowQuiver.cs](ArrowQuiver/ArrowQuiver.cs); deployed at `<game>\Mods\ArrowQuiver.dll`,
`[ArrowQuiver] DebugLog = true` set in `UserData\MelonPreferences.cfg`.
- Build steps 1–6 of §7 are implemented in one go, all behind debug logging, so one play session
  answers every ❓ in §7 steps 2–6b. §3.8 (grip positions / A-X switch) is **not built**: step 6c.1
  (prefab inspection) is, and its `[inspect]` log lines decide how to build the rest.
- **Fully typed** — every member needed turned out to be public in the interop, including the
  game-protected `HVRHandGrabber.IsGripGrabActive`. No reflection at all.
- Deviations from the plan, on purpose:
  - Dropped arrows (§3.7): tracked in a list instead of `Destroy(go, t)`; the timer **pauses
    while held**, and a dropped quiver arrow picked up again becomes the carried arrow (can still
    be nocked). Released **inside the quiver zone** or **socketed anywhere** → removed at once, so
    an arrow can never occupy the bow's holster.
  - Home socket fallback chain: `Socket`, then `PrimaryGrabber` cast to `HVRSocket` if
    `IsSocketed`. A home further than 1.2 m from the head (wall rack, shop slot) is ignored and the
    head fallback is used instead.
  - If the game's own `NockSocket` takes the carried arrow (native nocking), the mod logs it and
    leaves the arrow alone instead of destroying it.
  - `ArmHeldArrowAsKnife` not added yet: a Harmony prefix on `ANBKnife.stabEnemy` logs
    `carried quiver arrow STABBED` instead, so test 6b says whether it's needed.

**What to look for in `MelonLoader\Latest.log` (`[ArrowQuiver]`):**
`bow socket: … -> home=` (§3.3 — which property is populated, which shoulder) · `grip press X cm
from quiver (holster|head)` (step 3 radius tuning) · `drew arrow … grab OK|pending` · `held on grab
point '…'` · `nocking: …` → `string grabbed -> arrow nocked, createdNok=True` · `AmmoArrows a -> b`
(6a) · `carried quiver arrow STABBED` (6b) · `[inspect] …` (6c.1).

## 1. What Evhenii wants (the spec)

- **Two separate downloads:** keep **Arrow Grab Assist** as it is (grab buffer + barrel fix), and
  ship the quiver as **its own mod**. Don't fold it into the grab-assist DLL.
- **The quiver lives where the bow was holstered.** His setup: bow on the **right shoulder**, taken
  with the **left hand** (the bow hand). The **right hand** draws arrows **from the right shoulder**.
  The left shoulder stays free for a shotgun or rifle.
- So the rule is **"quiver = the socket the bow came from"**, not "the shoulder opposite the bow
  hand". That also avoids clashing with a weapon in the other holster. While the bow is in hand,
  its own holster is guaranteed empty, so that spot is free to act as the quiver.
- Arrows still come from the string as they do now (grab-assist behaviour). The quiver is an
  **additional** way to take an arrow, not a replacement.

## 2. Findings from the binary (what the plan is built on)

| Fact | Where | Status |
|---|---|---|
| The only arrow source is `HVRArrowLoader.OnStringGrabbed(hand, nock)`. It requires `bow.Arrow == null` and `ANBStaticGameManager.ANBmain.AmmoArrows > 0`, then calls `CreateArrow(true)` | disasm of `OnStringGrabbed` | ✅ verified |
| `HVRArrowLoader.CreateArrow(bool nokking)`: **Instantiate(ArrowPrefab)** → `GetComponent<HVRArrow>`. If `nokking`: `bow.NockArrow(arrow)` (vtable +0x278) + `bow.createdNok = true`. With `false` it's a **pure spawn**: no nock, no ammo change, no positioning | disasm of `CreateArrow` | ✅ verified |
| Ammo is subtracted **on shot**, in `HVRPhysicsBow.ShootArrow`, only if `createdNok` (then `ANBGameLogic.substractAmmo`, `createdNok = false`) | disasm of `ShootArrow` | ✅ verified |
| Arrow ammo = `ANBGameLogic.AmmoArrows` (int, 0x1070), `AmmoArrowsMax` 0x1074. Global instance = static `ANBStaticGameManager.ANBmain` | field + static scan | ✅ verified |
| The loader sits on the **same GameObject as the bow** (`Start` → `GetComponent<HVRPhysicsBow>`) | disasm of `Start` | ✅ verified |
| Shoulder holsters are `HVRShoulderSocket : HVRSocket` with `leftShoulder` (0x1d5) / `rightShoulder` (0x1d6) bools, `VelocityCutoff`, `storeID` | type dump | ✅ verified (fields exist; which one the bow uses ❓ test) |
| A grabbable knows its socket: `HVRGrabbable.Socket` (backing field), `LinkedSocket`, `StartingSocket`, `IsSocketed` (0x1b8) | type dump | ✅ fields exist; ❓ which is set for a holstered bow |
| `HVRSocket.AutoSpawnPrefab` (0xd0) + `CheckAutoSpawn()` = stock HVR auto-refill: a socket respawns a prefab **into itself** | type dump | ✅ exists, **rejected**, see §4 |
| Head transform: `HVRCameraRig.Camera` (Transform, 0x20) | type dump | ✅ |
| Grab API: `HVRGrabberBase.TryGrab(grabbable, bool force)` → `CanGrab` unless `force` → `GrabGrabbable`. `ForceRelease()` exists | disasm | ✅ |
| Grip state: `HVRHandGrabber.IsGripGrabActive` (**protected** field 0x3ce), `IsGrabbing` (0x72), `Palm` | disasm | ✅ (already used by Arrow Grab Assist) |
| Bow held-by-hand: `HVRBowBase.BowHand` (set in `OnHandGrabbed`, cleared in `OnHandReleased`). `NockHand` set in `BeforeNockHovered` | disasm | ✅ |
| Game's own manual nocking (bring a held arrow to the nock → `NockSocket` socket → `OnArrowSocketed` → `NockArrow`) exists in the code (`HVRNockingPoint`, `OnArrowSocketed`, `OnArrowNocked`) | type dump | ❓ test. May be disabled on this bow prefab, and it would **bypass ammo** (`createdNok` stays false) |

## 3. Design

### 3.1 Two DLLs
- `ArrowGrabAssist.dll`: unchanged.
- **`ArrowQuiver.dll`**: new project `GunmanContracts/ArrowQuiver/`, standalone, and works with or
  without Arrow Grab Assist installed. **Don't share code at runtime** (no cross-mod dependency).
  Copy the few helpers needed.
- On Nexus: one mod page, **main file** = Arrow Grab Assist, **optional file** = Arrow Quiver.
  Or two pages; decide at release.
- Build **typed** against the generated interop DLLs (like ArrowGrabAssist v1.1's barrel patch):
  `Il2CppHurricaneVR.Framework.Weapons.Bow.HVRBowBase`, `…HVRPhysicsBow`, `…HVRArrowLoader`,
  `…HVRArrow`, `Il2CppHurricaneVR.Framework.Core.Grabbers.HVRHandGrabber`, `…HVRSocket`,
  `…Core.Utils.HVRShoulderSocket`, `Il2CppHurricaneVR.Framework.Core.Player.HVRCameraRig`,
  `Il2Cpp.ANBStaticGameManager`, `Il2Cpp.ANBGameLogic`. Copy the csproj from
  `ArrowGrabAssist.csproj` and change the names. Typed is cleaner than reflection. Keep the
  `IsGripGrabActive` access as it is (it's protected in the game; check whether the interop
  property is public; if not, use reflection for that one member).

### 3.2 State machine per bow

```
Idle ──(bow in a socket)──────────────► remember HomeSocket = bow.Grabbable.Socket
Idle ──(bow in hand, BowHand != null)─► QuiverArmed(zone = HomeSocket.transform.position)
QuiverArmed ──(draw condition)────────► Carrying(arrow spawned in draw hand)
Carrying ──(arrow near string)────────► Swap → string grabbed → loader spawns nocked arrow
Carrying ──(grip released away from string)► drop: destroy the carried arrow (no ammo used)
any ──(scene change / bow destroyed)──► reset
```

### 3.3 Home socket tracking (the key idea)
- Poll each bow every frame (cheap): if `bow.Grabbable.IsSocketed` / `Socket != null`, store
  `HomeSocket = Socket` (keep the **last non-null** value). ❓ test which property is populated
  (`Socket` vs `LinkedSocket` vs `StartingSocket`). Log all three on first detection.
- When the bow is taken out, `Socket` goes null, but `HomeSocket` keeps pointing at the (now empty)
  shoulder socket. **The zone moves with the body** because the socket is parented to the rig.
- **Fallback (bow never holstered, e.g. bought or picked up from the world):** a point relative
  to the head, on the **drawing hand's** side: `cam.position + side*cam.right*0.15 - up*0.20 -
  flatForward*0.10`. (For Evhenii the drawing hand's shoulder = the bow's shoulder, so the rule
  stays consistent.) Make it a config toggle.
- Bonus: if `HomeSocket` is an `HVRShoulderSocket`, log `leftShoulder` / `rightShoulder` so the
  log shows which side got picked.

### 3.4 Draw (spawn an arrow into the hand)
Condition, checked per frame for the hand that is **not** `bow.BowHand`:
1. Grip **press edge** (own edge detection on `IsGripGrabActive`, like Arrow Grab Assist), with the
   same ~0.35 s buffer.
2. The hand isn't grabbing (`IsGrabbing == false`).
3. Distance from `hand.Palm` to the zone < `QuiverRadius` (start at **0.20 m**; shoulder zones need
   to be forgiving because you can't see them).
4. `bow.Arrow == null` (nothing nocked) and no carried quiver arrow already.
5. `ANBStaticGameManager.ANBmain.AmmoArrows > 0`.

Action:
```
arrow = loader.CreateArrow(false)            // pure spawn, no ammo change
arrow.transform.SetPositionAndRotation(hand.Palm.position, hand.Palm.rotation)
hand.TryGrab(arrow.Grabbable, true)          // force = skip CanGrab (it was never hovered)
remember CarriedArrow = arrow, CarryHand = hand
```
- ❓ test: whether the arrow sits well in the hand (it has hand-pose grab points; the default
  pose should apply). If orientation is odd, rotate so the tip points forward along the hand.
- Optional: a short haptic pulse on draw (`HVRHandGrabber` / controller haptics; find the API
  tomorrow if wanted).

### 3.5 Nock (swap to the proven string path)
While `Carrying`, each frame:
- If `CarryHand` is within `NockRadius` (**0.15 m**, same as the grab assist) of
  `bow.NockGrabbable.transform.position` **and grip is still held**:
  ```
  CarryHand.ForceRelease()
  UnityEngine.Object.Destroy(CarriedArrow.gameObject)
  bow.NockHand = CarryHand                    // same trick Arrow Grab Assist uses
  CarryHand.TryGrab(bow.NockGrabbable, false) // → HVRArrowLoader.OnStringGrabbed → nocked arrow
  ```
  If `TryGrab` returns false (hand still releasing), retry for up to ~5 frames.
- Result: exactly the state a normal string grab produces (`createdNok = true`, so ammo is
  subtracted on the shot). **Ammo accounting stays identical to vanilla**, with no special cases.
- **Alternative to test first (❓):** leave the carried arrow alone and see whether the game's own
  `NockSocket` nocks it when brought to the string. If it does, it's more "physical", **but**
  `createdNok` stays false, so shots would cost no ammo. Either set `bow.createdNok = true` in
  `OnArrowNocked` (Harmony postfix) or stay with the swap. **Default recommendation: the swap.** It
  reuses the path proven by the 55/55 test.

### 3.6 Drop / cancel
- Grip released while `Carrying` and not at the string → `ForceRelease` + `Destroy` the carried
  arrow. No ammo was used, so nothing to refund.
- Bow released, bow re-holstered, scene change → destroy any carried arrow and reset.

## 3.7 Added by Evhenii (Sep 24 2026, late): infinite arrows + stab with a held arrow

### Infinite arrows — ALREADY vanilla behaviour (Evhenii: "no arrow counter, arrows are infinite")
- **Correction:** there's nothing to build. In normal play the game shows no arrow counter and
  never runs out, even though the code keeps an `AmmoArrows` int, subtracts it per shot
  (`substractAmmo`, only if `createdNok`), and gates spawning on `AmmoArrows > 0`. So the game must
  refill it somewhere, or arrow ammo is effectively unlimited in the modes he plays. ❓ **Test
  step 5 already covers it:** log `AmmoArrows` before and after shots. If it never drops (or
  refills), the quiver needs **no** ammo logic at all. **The quiver must simply not break vanilla
  behaviour:** using the proven string path (§3.5) guarantees that, because it does exactly what a
  normal shot does.
- Keep the §3.4 `AmmoArrows > 0` gate anyway (it mirrors the game's own check and costs nothing),
  and **drop `InfiniteArrows` / `BlockLeaderboardWhenInfinite` from the config.** Only revisit
  the notes below if some mode or difficulty turns out to limit arrows.

*(Background kept for reference, only relevant if arrows ever turn out to be limited:)*
- **Don't use the game's own cheat.** `ANBGameLogic.CheatUnlimitedAmmo` (bool, 0x428) exists,
  read via `CheatUnlimitedAmmoCheck()`, but (a) it covers **all** ammo, not just arrows, (b) it's
  gated by `cheatsBlocked()`, and (c) it sits next to `CD_checkpoint_cheated` (0x426), which likely
  **flags the save/score as cheated**. Other cheat flags nearby: `CheatGod` 0x425,
  `CheatUnlimitedMag` 0x429, `ForceAllowCheats` 0x424.
- **Do this instead:** keep `ANBStaticGameManager.ANBmain.AmmoArrows` topped up:
  `if (AmmoArrows < max(1, AmmoArrowsMax)) AmmoArrows = max(1, AmmoArrowsMax)`. Run it every frame
  while a bow is held (a cheap int write), **or** Harmony-prefix `ANBGameLogic.substractAmmo(int
  removeVal, string ammo)` and skip it when `ammo` is the arrow ammo id (find the id string first:
  log the `ammo` argument once on a real shot). Prefer the prefix: it touches only arrow
  subtraction and never writes the ammo field directly.
- This also simplifies §3.4: with infinite arrows, the `AmmoArrows > 0` gate always passes.
- ❓ **Leaderboards:** the game has `SteamLeaderboard.SubmitScore`, and the `oneshot` mod blocks it.
  Decide whether infinite arrows should also block score submission (fair play) or leave it.
  Probably add `BlockLeaderboardWhenInfinite = true`.

### Stab with a held arrow ("spin it and stab")
- **Arrows already ARE knives:** every arrow is an `ANBKnife` with `isArrow = true` (0xd0), plus
  `Stabber` (`HVRStabber`, 0x70), `knifeDamage` 0x4c, `stabThresholdMomentum` 0xf8,
  `allowedAngle` 0xfc, `stabActive` 0x105, `isHeld` 0x98, `killArrowOnHumanHit` 0xe0,
  `canSlash` 0xf0 / `slashDamage` 0xf4. Methods: `grabKnife(hand, grabbable)`, `releaseKnife()`,
  `stabEnemy(StabArgs)`, `makeTempStabAll()`, `toggleSlash(bool)`, `switchCollisions(bool)`,
  `unstab()`.
- **So a carried quiver arrow may stab already.** ❓ **Test first**: draw from the quiver and thrust
  it into an NPC. Possible outcomes:
  1. It stabs → nothing to build.
  2. It doesn't → the arrow's stabber is probably only armed in flight (the bow calls
     `makeTempStabAll()` on shot). Arm it while held: call `makeTempStabAll()` on draw, or enable
     `Stabber` / set `stabActive`. Read `ANBKnife.grabKnife` and `makeTempStabAllExec` in the
     disassembly to see which flags a *real* knife sets on grab, and copy that.
- Check that `killArrowOnHumanHit` doesn't destroy the arrow mid-stab in a way that leaves the hand
  in a weird state (acceptable if it's just consumed).
- **"Spin it":** the grip is set by the arrow's HVR grab points/poses. A reverse (dagger) grip would
  mean flipping the arrow's rotation on draw (tip toward the pinky), maybe as a toggle
  (`QuiverDaggerGrip`). Treat it as a stretch goal; first make sure a normal held arrow stabs.
- **Changes §3.6 (drop):** with stabbing, a carried arrow shouldn't vanish the moment grip is
  released in melee. New rule: release away from the string → the arrow drops as a normal physics
  object and is destroyed after `DroppedArrowLifetime` seconds (e.g. 10). Arrows are infinite
  in vanilla, and an un-nocked quiver arrow never touches ammo anyway, so nothing is lost.

## 3.8 Grip positions on the quiver arrow + A-button switch (Evhenii, Sep 24 2026)

**Wanted:** out of the quiver, the hand holds the arrow **at the back** (nock end, tip forward),
the natural grip for putting it on the string. **A (right) / X (left)** spins it to a
**middle, tip-down "dagger" grip** for stabbing, and pressing again switches back. This mirrors
what the game already does for knives and katanas (normal → reverse → throwing grip).

### How the game does it for knives (✅ verified)
- It's stock HurricaneVR **`HVRGrabPointSwapper`** (on the knife prefab): `GrabPoints`
  (`HVRPosableGrabPoint[]`, 0x28), `RotateAxis` (`HVRAxis[]`, 0x30), `SwapTime` (0x38),
  `OtherHands` (0x40). Methods: `CheckInput(HVRController)` → `GetActivated(controller)` →
  `Swap()` → **`HVRHandGrabber.ChangeGrabPoint(HVRPosableGrabPoint grabPoint, float time,
  HVRAxis axis)`** (that's the actual "spin the hand to the next pose" call; the only caller is
  `Swap`).
- **`GetActivated` reads `controller.PrimaryButtonState.JustActivated`** (`HVRController` 0x34 is
  `PrimaryButtonState`, and `HVRButtonState` = `Active` +0 / `JustActivated` +1 / `JustDeactivated`
  +2 / `Value` +4). Primary = **A on the right Quest controller, X on the left.** The exceptions are
  controller type 2 (trackpad left/right) and type 3 (a global setting). Quest is the default path.
- Not this: `ANBHVRGrabbable.switchGrabPoints(bool)` (`GrabPointsOrig`/`GrabPointsAlternate`) is
  only called by **NPC** code (`ANBBasicNPC.StartAttack`, `reset`, …). It's irrelevant for the
  player.
- Explicit-point grab exists: **`HVRHandGrabber.Grab(HVRGrabbable, HVRGrabTrigger,
  HVRPosableGrabPoint)`** lets the draw pick the back grip deliberately instead of "closest
  point".

### Plan
1. **❓ Inspect the arrow prefab first (log on the first draw):** `arrow.Grabbable.GrabPoints` and
   its children (`HVRPosableGrabPoint` names, local positions, rotations), `arrow.Notch` local
   position, `ForwardGrabbable` (`HVRArrowPassthrough`, a second grabbable on the arrow), the
   renderer bounds (for shaft length), and whether an `HVRGrabPointSwapper` is already on the
   prefab. **This decides everything below.**
2. **Back grip (default on draw):** grab with the explicit-point overload, using the grab point
   nearest `arrow.Notch`. If the prefab's only point isn't at the back, clone it (below) and move
   the clone next to the notch.
3. **Dagger grip (created at runtime):** `Object.Instantiate` an existing `HVRPosableGrabPoint`
   GameObject as a child of the same parent. The clone keeps the hand-pose data. Then set
   `localPosition` = middle of the shaft and `localRotation` = original × 180° about the right
   axis, so the tip points down out of the pinky side. Tune the numbers in game.
4. **A/X switching:** preferred option is **the mod polls the carrying hand's controller**
   (`PrimaryButtonState.JustActivated` for that side) **while carrying a quiver arrow** and calls
   `hand.ChangeGrabPoint(next, SwapTime≈0.15f, axis)`, i.e. the exact call the knife swapper
   makes. Copy the `HVRAxis` choice from `HVRGrabPointSwapper.Swap` (read its disassembly). The
   alternative, adding an `HVRGrabPointSwapper` to the arrow via `AddComponent`, is riskier: its
   `Awake` runs before `GrabPoints` can be set. Only try it if polling misbehaves.
5. **❓ Check that A/X isn't already bound** to something else while an arrow is held (e.g. a game
   action on the primary button). If it is, make the button configurable.
6. **Stabbing with the dagger grip:** knife logic is object-relative (`stabDirection`,
   `allowedAngle`), so a tip-down stab should register. Verify alongside §3.7.
7. **Nocking from either grip:** irrelevant. §3.5 destroys the carried arrow and spawns a fresh
   nocked one, so the grip used to carry it doesn't matter.

Config additions: `DefaultGrip = Back`, `GripSwitchButton = Primary`, `DaggerGripOffset` (m
along the shaft), `DaggerGripRotation` (degrees).

## 4. Rejected approach (don't re-litigate): HVRSocket.AutoSpawnPrefab
Setting `HomeSocket.AutoSpawnPrefab = ArrowPrefab` while the bow is out would make the shoulder
refill itself with a visible arrow (stock HVR behaviour). Rejected because:
1. The socket would be **occupied by an arrow when you re-holster the bow** → you must clear it at
   the right moment, and socket filters (bow tag only) may reject or eject things.
2. Auto-spawned arrows bypass ammo entirely.
3. It modifies a shared game component's state. The virtual zone touches nothing.

## 5. Interactions and edge cases
- **Arrow Grab Assist installed too:** no conflict. Its buffer only acts when the hand is **empty**
  (`IsGrabbing == false`). While carrying a quiver arrow the hand is grabbing, so the assist stays
  out of it. When the quiver swaps to the string, `bow.Arrow` gets set, so the assist's spawn-retry
  sees an arrow and does nothing.
- **Other weapon on the other shoulder:** untouched. The zone is only the **bow's** home socket,
  which is empty while the bow is in hand.
- **Left-handed / swapped hands:** works by construction (draw hand = whichever hand isn't
  `BowHand`).
- **Two bows in the scene:** track state per bow. Only the bow with `BowHand != null` arms a zone.
- **Ammo 0:** no spawn. Optional: a single short "empty" haptic.
- **Holster in view:** grip near the empty holster also hovers the HVR shoulder socket (empty →
  nothing). ❓ test that this doesn't swallow the grab.

## 6. Config (`[ArrowQuiver]`)
```
Enabled            = true
QuiverRadius       = 0.20   m, around the bow's home holster
NockRadius         = 0.15   m, carried arrow → string swap distance
PressBufferSeconds = 0.35
UseHeadFallback    = true   zone beside the head when the bow was never holstered
ArmHeldArrowAsKnife = true  make a carried quiver arrow stab (only if it doesn't already)
DroppedArrowLifetime = 10   s before a dropped quiver arrow is cleaned up
DebugLog           = false
```

## 7. Build order for tomorrow (each step independently testable, log-first)
1. **Scaffold** `GunmanContracts/ArrowQuiver/` (copy the csproj and change names). `MelonInfo`
   "ArrowQuiver", `MelonGame("ANB_Seth","GunmanContracts")`.
2. **Observe only:** each frame, log per bow when `Socket` / `LinkedSocket` / `StartingSocket`
   change, the socket's type and `leftShoulder`/`rightShoulder`, and `BowHand` changes. **Test:**
   holster/unholster the bow on the right shoulder and confirm the log names the right socket. *(Answers §3.3 ❓.)*
3. **Zone probe:** log `distance(drawHand.Palm, HomeSocket)` on each grip press. **Test:** reach to
   the right shoulder and confirm it's < 0.20 m. Tune the radius from real numbers.
4. **Draw:** spawn + `TryGrab(force)`. **Test:** the arrow appears in the right hand, oriented sanely.
5. **Nock-swap:** **test** a full draw → nock → shoot cycle, and that `AmmoArrows` drops by
   exactly 1 per shot (log it).
6. **Cancel paths:** drop the carried arrow, re-holster the bow mid-carry, change scene.
6a. **Infinite arrows (vanilla):** confirm with the step-5 log that `AmmoArrows` doesn't run out
    (it either never drops or gets refilled). Nothing to build unless it does.
6b. **Stab test:** thrust a quiver arrow into an NPC. If there's no stab, arm it per §3.7 and retest.
6c. **Grips (§3.8):** log the arrow prefab's grab points on the first draw → back grip on draw →
    clone a dagger grip → A/X switching via `ChangeGrabPoint` → stab with the dagger grip.
7. **With Arrow Grab Assist installed together:** run 20+ mixed shots (string grabs + quiver draws).
8. Release: bare `ArrowQuiver.dll` + a Nexus description block (same format as
   `release/NEXUS_DESCRIPTION.txt`).

## 8. Useful tooling (already built)
- `GunmanContracts/il2cpp_tools/`: `il2.py` (metadata + method/field map, cache `il2map.pkl`),
  `disx.py <Class::Method>` (named disassembly). Dumps: `bow.txt`, `sockets.txt`,
  `anbbreakable.txt`, `anbknife.txt`, `grabswap.txt` (grab-point swapper, ANB grabbable,
  posable grab points).
  Example: `python disx.py HurricaneVR.Framework.Core.Grabbers.HVRSocket::CheckAutoSpawn`.
- ⚠ Run the tools from their own folder (not a folder containing a file named `dis.py`), and
  **never** walk parent chains with `parents()` in `il2.py`: it loops forever and ate ~60 GB of
  RAM once. Use a bounded loop.
- Live log watch: `MelonLoader\Latest.log`, filtered on `[ArrowQuiver]`.
