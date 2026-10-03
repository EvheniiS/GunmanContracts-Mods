# Gunman Contracts – Stand Alone

Research and development notes for the mods in this repo. `<game>` = the game's install folder
(`...\steamapps\common\Gunman Contracts - Stand Alone`); each mod deploys as `<game>\Mods\<Mod>.dll`.
**Better Bow on Nexus:** https://www.nexusmods.com/gunmancontractsstandalone/mods/22 · code in `BetterBow/`,
release DLL + Nexus BBCode in `release/BetterBow/` (see Source control).
**Fire Selector on Nexus:** https://www.nexusmods.com/gunmancontractsstandalone/mods/25 ·
**Knee Shot Stun on Nexus:** https://www.nexusmods.com/gunmancontractsstandalone/mods/26
Engine: **Unity 6000.0.41f1, IL2CPP** (metadata v31), built on the **HurricaneVR** framework.
Developer string: `ANB_Seth`. Game logic lives in `HurricaneVR.Framework.dll` (classes prefixed `ANB*`).

**Sound (how the game plays it, clip arrays, ways to add custom sounds): [SOUND.md](SOUND.md).**

## Game-update recovery audit (Oct 1 2026)

The Sep 27 recovery entry below is superseded by [Tools/UPDATE_RECOVERY.md](Tools/UPDATE_RECOVERY.md).
Current source has 17 projects, dynamic targets and shared dependencies; compilation and clean logs cannot prove
that hooks execute or that asset/scene/save assumptions still hold. The new local `Tools/Update-Triage.ps1` workflow
backs up preferences, enables installed repository diagnostics with reviewed category mappings, collects logs and
source/core/reference/DLL hashes, and restores only its own preference changes. It does not build or repair mods.
Research maps now reject unsupported metadata/layouts, use content-keyed caches, validate registration discovery,
and preserve overload addresses. `il2cpp_tools/snapshot.py` captures/diffs structural maps for selective research.

Observed existing evidence: `MelonLoader/Latest.log`, bootstrap Oct 1 01:19:59 local, game 0.3.1.0,
Unity 6000.0.41f1, MelonLoader 0.7.3. It already contains repeated native trampoline exceptions through
`ANBNpcSpawner.spawnPointValidation` → `outOfSight` → `ANBEncounterSystem.BlockedSight` → `Transform.get_position`
around 01:23:36, plus Frame Probe's unavailable draw-counter constructor. These predate any hypothetical update;
neither a responsible mod nor a new game regression was established. Gameplay source/DLLs were not changed by this
audit. The current game's metadata/registration map was successfully captured; future-build compatibility remains
untested. See the workflow's verification section for tool tests and limitations.

Verification completed: 12 focused tool tests passed; dump/disassembly/slot/caller commands ran against the installed
game. The research baseline includes 625 scoped/nested types and 5,711 method entries, with a self-diff returning no
structural changes. The current native binary and metadata are archived under git-ignored `feature/update-triage/`.
`Prepare` was applied while the game was closed, with its backup and plan in `feature/update-triage/ready-20261001`:
exactly 14 diagnostic keys changed, other preference bytes preserved, and a Restore dry run passed. Radar Sense's
gameplay toggle and Frame Probe remain disabled. Grab Log is not installed, so no DLL was added. No new game session,
gameplay verification, mod build or DLL deployment was performed by this audit.

## Frame Probe log review (Oct 1 2026)

FrameProbe 0.1.0 was built and installed Sep 30, then used in game. The current installed game reported version 0.3.1.0 in the Oct 1 00:39 MelonLoader bootstrap. The full 120 Hz FrameProbe session started Oct 1 00:25 and ended 00:38 (local time), but its matching MelonLoader text log is no longer available, so its exact mod set and game version cannot be independently verified from that session.

- In the 120 Hz CSV, full 30-second Range windows had median effective 119.8 frames/s and median window p95 frame delta 9.442 ms. The Warehouse contract had median effective 118.6 frames/s and median window p95 9.656 ms across 15 windows. Two later 30-second Warehouse windows fell to 106.9 and 106.4 frames/s, with p95 14.482 and 14.418 ms (00:34:25 and 00:37:25 local). The scene-entry window at 00:31:25 was slower. The CSV does not mark loading screens, menus, checkpoints, or active play, so none of these slow windows can be classified as gameplay drops from this file alone. These are Unity frame counts and `Time.unscaledDeltaTime`, not headset reprojection statistics.
- Every CPU/GPU timing field was blank and `gpu_missing` equaled the sample count. A matching earlier 90 Hz run (Sep 30 18:58, game 0.3.1.0, 19 loaded mods including GPUInstancer 1.3.7) logged `ProfilerRecorder` failing to start the draw-call counter because its native constructor was unavailable. The same CSV fields were blank in that run. Jev filtered its warning lines and assigned the FrameProbe warning 0.77 possible performance relevance; this is a relevance judgment, not a diagnosis of the game slowdown.
- These runs do not establish a per-mod performance hit or CPU-vs-GPU bottleneck. A probe-only run with matching settings and VR runtime CPU/GPU/reprojection readings is still needed.

## Daredevil 1.0.0 first-release package (Sep 30 2026)

The 0.4.0 source was renamed to 1.0.0 for the first public release. A Release build using the
local game installation completed with zero warnings/errors (latest MelonLoader log: game 0.3.1.0).
`release/Daredevil/Daredevil.dll` and `Daredevil-1.0.0.zip` were prepared; the ZIP contains only
`Mods/Daredevil.dll`, byte-identical to the release DLL. The 0.4.0 ZIP and the pre-fix 1.0.0
package are retained for rollback. Nothing has been uploaded or tagged yet.

**Leg holster death/retry fix, game 0.3.1.0:** the user reported that clubs from the leg holsters
were missing after death in a contract and clarified that the level remains loaded: loose clubs
are still lying where they fell. Source audit found `[BillyClubs] SavedHolsters` was rewritten on
every belt draw. Daredevil now persists this loadout only in The Range and rereads saved belt
occupancy when a new player rig loads. Game metadata and earlier binary research identify
`ANBGameLogic.resetPlayerLoadout` as the game's saved-loadout reload path from `resetScene`.
Daredevil also hooks that method to recall loose clubs after a same-scene contract restart.
The revised 1.0.0 build completed with zero warnings/errors, is installed, and is packaged.
The test prepared clubs in the leg holsters in The Range before entering the contract.
The Sep 30 game log (`MelonLoader/Logs/26-9-30_17-58-16.log`) confirms same-scene checkpoint
restores at 18:06:00 (2 loose clubs recalled) and 18:07:39 (1 recalled, 1 spawned); the user
confirmed the leg holster fix worked. The installed 1.0.0 test DLL includes a separate club door
kick change from another agent. It opened marked doors at 17:59:57, 17:59:59 and 18:01:30.
After the second checkpoint reset, A/X attempts from held clubs returned `no marked door in range`
while both `[BillyClubs] ClubDoorKick` and the game's `useVRDoorKick` were on. The log does not
distinguish aim/range from door marker state; door-kick behavior after a checkpoint needs follow-up.

**Back holster precaution:** source audit found `[VRHolsters_BackSlots] SavedBackHolsters` was
also rewritten on every back-slot draw. VR Holster Customization 0.2.2 now persists these mod-item
back slots only in The Range. Its test build was built and installed; the user will test back
holsters separately. Its same-scene checkpoint behavior is still unverified. The 0.2.1 release
package remains unchanged.

**Later Sep 30 follow-up:** the user confirmed the leg-holster checkpoint fix, but a Weapon Framework
crowbar saved in the right back holster did not return on a same-scene checkpoint reset. The saved
preference was `R=Crowbar`; Weapon Framework had registered the kind and tracked the live item.
VR Holster Customization previously ran back-slot `Restore()` only after a scene load. The 0.2.2
test build now hooks `ANBGameLogic.resetPlayerLoadout` and recalls the tracked or drawn item after
the game's own loadout reset, spawning only when no live item is available. Build succeeded with
zero warnings/errors and was installed while the game was closed; VR verification is pending.
The 0.2.1 public release package is unchanged.

The same session's Throw Assist log showed pistol releases at 3.8 and 5.3 m/s rejected by its
6 m/s mod gate, while an assisted 6.3 m/s pistol flew at only 6 m/s. The user requested a general
4 m/s trigger. A private 0.2.2 test build now uses a 4 m/s activation setting for pistols and
other props (still subject to the game's gate) and a 13 m/s pistol-only assisted flight floor.
Knives retain their already-lower game threshold of about 3.5 m/s. Build succeeded with zero
warnings/errors and was installed while the game was closed. The local `MinAssistSpeed` preference
was changed from 6 to 4 m/s; the previous preference file and both previous DLLs are backed up
under the game's `ModBackups/Codex-backslot-pistol-20260930-185549/`. Gameplay feel remains unverified.

## ★★★ Daredevil is the ONE package; standalone Billy Clubs / Radar Sense are LEGACY (Sep 28 2026)

His decision: stop maintaining two separate mods. **All club, glove, radar-sense and club-glow work happens in
`Daredevil/`** (`Daredevil/BillyClubs/`, `Daredevil/RadarSense/`, one csproj, one version number, `Mods/Daredevil.dll`).
The standalone `BillyClubs.csproj` / `RadarSense.csproj`, their `MelonInfo` and every `DAREDEVIL` switch are gone from
`dev`; their last standalone code lives in git history (Billy Clubs 0.12.0, Radar Sense 0.3.1). Config sections stay
`[BillyClubs]` / `[RadarSense]` so settings carry over. Older notes below that say "Billy Clubs x.y" describe what is
now the Daredevil package. `Mods/BillyClubs.dll.disabled` and `RadarSense.dll.disabled` can be deleted any time.
- **Same day, his follow-up: the gloves are their own mod, `Gloves/`** (`[Gloves] Color`, default `#8A0F0F`, live on
  the Mod Settings board's colour palette via `OnEntryValueChanged`). **Daredevil 0.3.0 requires Gloves, Throw Assist
  and Mod Settings** (soft: `Daredevil.cs` logs `required mods not installed: …`; it still runs), Weapon Framework
  optional. Installed Sep 28 (game closed): Daredevil 0.3.0 `B6FCD83F…` (backup `feature/Daredevil-backup-before-0.3.0-*`),
  Gloves 0.1.0 `86F27629…`. Old `[BillyClubs] GloveColor` is no longer read.

## ★★ Debug-log rule (Sep 27 2026)

**Debug logs are OFF by default in the live cfg, except for mods still being worked on.** Currently on:
**Heavy Melee** (not on Nexus, may be fine-tuned), **Enemy Awareness Fix**, **Enemy Awareness Log** (a logger by
design), **Billy Clubs**, **Radar Sense** (both also as the **Daredevil** package, whose log shows Billy Clubs as `[Daredevil]`) and **Throw Assist** (in development). Off: Better Bow, Knee Shot Stun, Fire Selector, Physical Dodge (plus
the dead `[ArrowGrabAssist]` / `[ArrowQuiver]` sections).
→ **Before handing any mod (new or old) over for testing, check `<game>\UserData\MelonPreferences.cfg` and make sure
that mod's `DebugLog = true`.** Turn it back off when the mod is done. Edit the cfg only while the game is closed
(MelonLoader writes it on exit).

## Modding stack

- **MelonLoader 0.7.x is required.** 0.6.2 (the Nexus `site/mods/702` bundle)
  **fails on first run**: its assembly generator asks for Unity
  runtime libs `6000.0.41.zip`, which 404s (Sep 24 2026), so no interop assemblies are
  generated and no mod loads. The game-specific Nexus mods also say to use 0.7.x.
- **MelonLoader 0.7.3 works** (official installer, Sep 24 2026). First launch generates the
  interop assemblies in ~40 s. The generator's "Failed to restore N methods/fields" and
  Il2CppInterop's "Class::Init signatures exhausted" lines are harmless.
- **⚠ The black screen / flat-mode start on Quest (Virtual Desktop) was NOT the mods.** The game
  showed loading → black with working audio and menu hover, then started flat. Disabling the mod
  and MelonLoader didn't clearly fix it, but a **PC restart + reconnecting the Quest** fixed it
  with everything enabled. **Before blaming mods for a VD black screen, restart the PC and VD.**
- **UnityExplorer REMOVED (Sep 28 2026)**: it kept getting in the way, and clubs now spawn from the arsenal panel via
  Weapon Framework. Moved (not deleted) to `<game>\_removed\UnityExplorer\` (its Mods DLL + `sinai-dev-UnityExplorer`
  folder, `UserLibs\UniverseLib.ML.IL2CPP.Interop.dll`, and its cfg section). Nothing else used UniverseLib; none of our
  mods reference either, and no release zip contains them. To bring it back for debugging, move the files back.
- Mods reference `Il2CppHurricaneVR.Framework` (e.g. `oneshot.dll` patches
  `ANBBasicNPC.TakeDamage` and `SteamLeaderboard.SubmitScore`).

## ★ Bow: "have to grip twice to get the next arrow" — root cause (Sep 24 2026)

How the arrow is taken: there is **no quiver**. `HVRArrowLoader` subscribes to the bow's
**string/nock grabbable**. Grabbing the string runs `OnStringGrabbed` → `CreateArrow(true)` →
the arrow is spawned straight into the nock. It spawns only if `bow.Arrow == null` **and**
you have arrow ammo.

Found by disassembling `GameAssembly.dll`:

1. **★ `HVRHandGrabber.CanHover`: while grip is HELD, the hand refuses any NEW hover target.**
   It can only keep the one it already had.
2. **★ `HVRHandGrabber.CheckGrab` grabs only on the grip-PRESS frame** (`IsGripGrabActivated`).
3. → Press grip a moment before the string becomes the hand's hover target (routine when
   shooting fast: the hand is still travelling, and the string is snapping back to rest)
   and **the press is lost**. Hover then stays locked until you release and press again.
   That is the double grip. Nothing is on a timer and the zone isn't really "too small".
   It's an unbuffered, edge-triggered input.
4. Secondary: `HVRPhysicsBow.ShootArrow` clears the old arrow only after the **next
   FixedUpdate** (`ExecuteAfterFixedUpdate`). A string grab inside that window finds
   `Arrow != null` and spawns nothing.

**Ruled out:** `HVRHandGrabber.releaseWait` looked promising but isn't it. `OnReleased` sets
it to 0.1 s and `Update` counts it down, but the only reader is **`HVRForceGrabber.CanGrab`**
(distance/force grab). It never gates the normal grab at the string.

Other numbers seen: `HVRANBTriggerGrabbableBag` (the game's own hand bag):
`grabbableFixTimer` 0.01 s, `ghostHandsTime` 0.5 s, `releaseFixDistance` 1.0.

## Fix: `ArrowGrabAssist` MelonLoader mod (was `BowGrabAssist`) — grab fix WORKING (tested Sep 24 2026, Quest 3 via VD)

**First session: 55 of 55 fast string grabs landed on the first press. 0 refused, 0 errors, 0
arrow-spawn retries.** Every grab completed 10–16 ms after the press (avg 12.1 ms, i.e. one
frame), with the hand 1.6–11 cm from the nock (avg 5.7 cm).
- **★ The hand was usually within a few cm of the string and the game STILL didn't grab.** That
  confirms the diagnosis: the grab zone was never too small. The press was dropped by the
  edge-triggered grab plus the hold-locks-hover rule.
- **Every grab went through the assist, none through the game's own path.** Either the game
  practically never grabs on the press frame during fast shooting, or the assist gets there one
  frame before it would. The outcome is the same either way.
- The spawn-retry path (fix 2) never fired. Keep it as a safety net; it costs nothing when idle.
- Scans: `0 bow, 2 hands` in menus; `1 bow, 2 hands, 1 arrow loader` once the bow is out.
- **Tester verdict: "exactly right, the perfect solution"; accuracy felt better too. No tuning
  wanted, and the 0.15 m radius / 0.35 s buffer defaults are final.**
- **v1.1.0 = renamed to ArrowGrabAssist + explosive-barrel fix (below).** Release files in
  `release/`: the bare `ArrowGrabAssist.dll` plus `NEXUS_DESCRIPTION.txt`
  (BBCode; the readme lives on the Nexus page). Tagged
  `MelonGame("ANB_Seth", "GunmanContracts")` so it only loads in this game. Tested on game
  version 0.3.1.0.
- **Build now references the game's generated interop DLLs** (`<game>\MelonLoader\Il2CppAssemblies`),
  because the barrel fix needs typed Harmony patches. The grab-assist part is still
  reflection-based. A game update means MelonLoader regenerates them. Rebuild if a patch target
  changed.

Source: `ArrowGrabAssist/` (since merged into Better Bow as `BetterBow/StringGrab.cs`).
Deployed at `<game>\Mods\ArrowGrabAssist.dll`.

- **Press buffer:** grip held, hand empty, press within `PressBufferSeconds` (0.35),
  hand within `GrabRadius` (0.15 m) of the string of a bow held in the *other* hand →
  sets `bow.NockHand` and calls the game's own `TryGrab(nock, false)`. That keeps
  `CanGrab` checks, grab events and the arrow-loader spawn intact.
- **Spawn retry:** string held < 0.5 s with no arrow → re-run `HVRArrowLoader.OnStringGrabbed`
  once. The game's own path still checks ammo.
- Config: `UserData\MelonPreferences.cfg` → `[ArrowGrabAssist]`. Turn on `DebugLog` to see each
  assisted grab (ms after press, cm from string) in the ML console.

## ★ Explosive barrels need two arrows — root cause + fix (Sep 24 2026)

Breakables (barrels, crates, glass) are `ANBBreakable`: `health` / `healthMin` / `healthStart`,
`bulletDamageMultiplier` (0x48), `explosionDamageMultiplier` (0x4c), `explosionOnBreak` (0x204),
`indistructable`, `onlyByPlayer`, `invincibleTime`.
- **Arrows are knives.** An arrow is an `ANBKnife` with `isArrow = true` (the bow calls
  `ANBKnife.makeTempStabAll` on shoot). Arrow damage runs `ANBKnife.stabEnemy` →
  `ANBBreakable.hit(origin, knifeDamage, isBullet=false, 0, isExplosion=false, 0)`.
- **`hit` scales damage only for bullets (`× bulletDamageMultiplier`) and explosions
  (`× explosionDamageMultiplier`). Knife/arrow damage passes through raw.** It sets
  `gettingHit` and starts `hitAfterTime`, which does `health -= damage` and calls `shatterMe`
  (the explosion, if `explosionOnBreak`) only when `health <= 0`.
- → An arrow's `knifeDamage` is under half a barrel's health, so it takes two. This is a
  balance oversight (arrows never got a breakable multiplier), not a crash bug.
- Other callers of `hit`/`shatterMe`: `ANBGameLogic.BulletImpact` (bullets, via `hitDelayed`),
  `ANBBreakable.OnCollisionEnter` (impacts), `ANBKnifeSlasher.OnTriggerEnter` (slashes).
- **Fix (`ExplosiveArrowsDetonateBarrels`, default on):** Harmony prefix/postfix on
  `ANBKnife.stabEnemy` sets a flag when `isArrow`. A prefix on `ANBBreakable.hit` then raises
  `bulletDamage` to `health + 1` for **explosive, destructible** breakables only. Knives,
  bullets, explosions and non-explosive breakables are untouched. **Tested and working Sep 24 2026**
  (one arrow detonates; numbers in the Quiver section). With `DebugLog` on, each hit logs
  `arrow hit explosive barrel: damage X -> Y (health Z)`. That will also reveal the real
  `knifeDamage` and barrel health numbers.

## Quiver (колчан) — feasibility (Sep 24 2026): YES, buildable entirely in the mod

**➜ Full implementation plan: [BetterBow/QUIVER_PLAN.md](BetterBow/QUIVER_PLAN.md).**
**v0.1.0 WORKS (tested Sep 24 2026)**: draw, nock, shoot and stab all fine. v0.2.0 fixes the
reported lag (draw latency + a periodic scan hitch) and is deployed but untested. Source
`ArrowQuiver/` (since merged into Better Bow as `BetterBow/Quiver.cs`), `<game>\Mods\ArrowQuiver.dll`.
Results and what to read in the log: QUIVER_PLAN.md §0.
- **★ Arrows are infinite in practice:** `AmmoArrows` stayed 150/150 over ~13 real shots even with
  `createdNok=True`.
- **★ A hand-held arrow already stabs** (it's an `ANBKnife`, damage 100). It's destroyed on a human
  hit (`killArrowOnHumanHit`).
- **★ Lesson for both mods: never search the scene periodically.** `FindObjectsOfType` every 2 s in
  two mods = a single-frame hitch every 2 s. Register via Harmony postfix on the component's
  `Start` instead. ArrowGrabAssist **1.1.1** carries the same fix (deployed, untested; `release/`
  still holds the tested 1.1.0).
- **★ A holster's pivot is not where the hand reaches for it**: 32–42 cm off in practice. Centre
  grab zones on the observed grab position, not the socket transform.

**Explosive-barrel fix CONFIRMED in game (Sep 24 2026):** `arrow hit explosive barrel: damage 100 ->
251 (health 250)`, and the barrel detonated on one arrow. So `knifeDamage` = 100 and barrel health
= 250 (the game needed 3 arrows, not 2).
Spec: a **separate** `ArrowQuiver` mod (Arrow Grab Assist stays as it is). **The
quiver = the socket the bow was holstered in** (e.g. bow on the right shoulder: left hand
takes the bow, right hand draws from the right shoulder; left shoulder stays free for a
shotgun or rifle). Ammo global: `ANBStaticGameManager.ANBmain.AmmoArrows` (0x1070).

The game has **no quiver code**: the only arrow source is `HVRArrowLoader` on the string. The
mod can build one from the game's own parts, without new assets:
1. **Draw:** free hand presses grip in a zone over the shoulder, positioned relative to the HMD
   camera. The mod calls `HVRArrowLoader.CreateArrow(false)` (spawns an un-nocked arrow) and
   `hand.TryGrab(arrow.Grabbable, true)` to put it in the hand.
2. **Nock:** when the hand holding that arrow comes within ~15 cm of the string, destroy the held
   arrow and run the existing string-grab path (`NockHand` + `TryGrab(nock)` →
   `OnStringGrabbed` → `CreateArrow(true)`). That reuses the **proven** nock/ammo path. The held
   arrow is only visual.
- Ammo: `CreateArrow(true)` sets `createdNok`, and `HVRPhysicsBow.ShootArrow` then calls
  `ANBGameLogic.substractAmmo` only if `createdNok`. So step 2 keeps ammo accounting
  identical. `ANBGameLogic.calcAmmo(string)` / `substractAmmo(int,string)` exist if needed.
- The string grab keeps working alongside it, so the quiver is optional.
- Open design choices: shoulder side (right/left/auto from bow hand), zone size, haptic tick.

## ★ "Shoot here to burst open door" only works with guns — fixed for the bow (Sep 24 2026)

- The mark is an `ANBDoorKicker` (fields `doorScript` → `ANBPhysicsDoor`, `canvas` = the "shoot
  here" UI, `col`, `linkedKicker`). The door opens via **`ANBGameLogic.TryKickDoor(pos, dir)`**:
  gated by `useVRDoorKick` (0x7a1; `useFPSDoorKick` 0x7a0 in flat mode), it does
  `Physics.Raycast(pos, dir, doorKickDistance (0x7a4), doorKickMask (0x7ac))`, and if the collider has an
  `ANBDoorKicker` it calls `doorScript.playerKickDoor()` (plus the linked kicker's door).
- **Only guns call it:** `ANBHVRGunBase.OnShoot` (VR), `Character.OnTryFire` and
  `ANBFpsInteraction.Interact` (flat mode). The raycast runs **at the moment of the shot, from the
  muzzle**, not on bullet impact. The bow has no call, so arrows never breach.
- **Fix: ArrowGrabAssist 1.2.0 `ArrowsBreachDoors`** (branch `feature/bow-door-breach`, untested). A
  prefix on `HVRPhysicsBow.ShootArrow(direction)` calls `TryKickDoor(nockedArrow.position,
  direction)`. Same moment, range, mask and gate as a pistol.
- The phone "Settings" button stops responding after a while in a session: a **known game
  bug** (per the game's docs), not the mods.

## ★ Better Bow 1.0.0: all the bow mods in one (Sep 24 2026), release candidate, under test

All the bow mods were combined into one, **"Better Bow"** (`BetterBow.dll`, settings
section `[BetterBow]`). Source: [BetterBow/](BetterBow/),
branch `release/better-bow-1.0`. `main` moves and gets tagged `betterbow-v1.0.0` only after the in-game
test. Nexus text: `release/NEXUS_DESCRIPTION.txt`.
- `BetterBow.cs`: entry point, settings, and the shared loader/hand registry (Harmony postfixes on
  `Start`). `StringGrab.cs`: the first-press string grab + spawn retry, **ported from reflection
  to typed**. `ArrowPower.cs`: barrels + doors. `Quiver.cs`: quiver + dagger grip.
- **New in 1.0 (untested): the quiver also measures from `HVRHandGrabber.ControllerHandTarget`**
  (`PhysicsHandTarget ?? TrackedController` = the pose the physics hand chases). Reported: reaching
  back to draw sometimes gets stuck on a wall, because the physics hand stops at the
  wall while the controller keeps going. Reach point = `target.TransformPoint(hand.InverseTransformPoint(palm))`,
  and the closer of palm/reach counts. Debug line: `hand held back N cm`.
- Replaced mods: remove `ArrowGrabAssist.dll` / `ArrowQuiver.dll` from `Mods`, or both would run.
  Their old cfg sections `[ArrowGrabAssist]` / `[ArrowQuiver]` are dead and can be deleted.
- Tested before the merge: quiver draw/nock/stab, dagger grip (knuckle-aligned, hand stays put),
  door breach with an arrow, one-arrow barrels, first-press string grab.

## ★ Throw aim assist for thrown quiver arrows: Better Bow 1.1.0 → 1.1.1 (Sep 24 2026)

**1.1.0 test: didn't work.** The log showed every throw detected (`carried arrow thrown: flying`) and
then nothing. **Cause: the `ANBAssistedThrowingObject` ctor sets `isHoming = true`**, so the mod's "already
homing?" guard returned silently on every throw. **★ Lesson: when a game object has a state flag,
read its constructor default before using the flag as a guard. And every early return in a debug path
logs.** Fixed in 1.1.1 (deployed, untested). The real knife tuning was logged: `Knife-Combat-type1`
speed **17 m/s**, stop **0.7 m**, skips arms (not legs) for **2 s**. Game config has `assistedThrow~true`.

Branch `feature/arrow-throw-assist` (commit `0dc0091`), built to `feature/BetterBow.dll` and copied to
`<game>\Mods`. `release/BetterBow.dll` is still the tested 1.0.0. Source:
[BetterBow/ThrowAssist.cs](BetterBow/ThrowAssist.cs).

**How the game's knife throw assist works (from `GameAssembly.dll`):**
- **`ANBKnife.releaseKnife`** (no direct callers, so it's wired through the grabbable's release event).
  The assist runs only if `allowAssistedThrow` (0x118), **game setting `ANBGameLogic.assistedThrow`**
  (0x70b), a live `ThrowScript` (0x110, an `ANBAssistedThrowingObject`) and `|rb.velocity| >=
  assistedThrowAtVelocity` (0x70c). Then it runs three steps: `ToggleBodyPartCollisionsExec(0)` (only if
  `ignoreBodyPartsOnThrow`), then `makeTempStabAll()` (**the same stab-anything mode the bow uses on a
  shot**), then `ThrowScript.StartAssistedThrow()`.
- **`StartAssistedThrow`** checks `dontUse`, the setting and the velocity again. `homingTarget =
  GetClosestAndCenteredTarget(true)` (enemies), and if that finds nothing, `(false)` (other targets).
  **Targeting is gaze-based:** it uses `ANBStaticGameManager.MainCam` position and forward plus
  `assistedThrowViewRadius` (0x718) / `assistedThrowViewAngle` (0x71c), not the throw direction.
- **The homing coroutine** zeroes velocity, then on every FixedUpdate does `MovePosition` toward the target at
  `speed`. It stops at `stopDistance` (then sets velocity = direction × speed so the object flies in), or past
  `assistedThrowMaxFlyDistance` (0x720) / `maxFlyDistanceOverride`. Rotation: if `changeRotation` and
  `torque == 0` → `LookRotation(dir) * Euler(0, aimCorrectionAngle°, 0)` (default 90 is for a sideways
  blade). With a torque it spins instead.
- `ANBAssistedThrowingObject` ctor defaults: speed 10, stopDistance 0.5, aimCorrectionAngle 90, torque 10.
  `Awake` takes the Rigidbody from its own GameObject and **destroys itself if there isn't one**.
- `ANBKnife.Awake` does **not** fill `ThrowScript` (it's a prefab reference). → **Why arrows never got
  the assist: the arrow prefab has no throw script.**

**The mod:** when a carried quiver arrow leaves the hand with grip released at or above
`assistedThrowAtVelocity`, it adds an `ANBAssistedThrowingObject` to the arrow's rigidbody object. Tuning
(speed, stop distance, overrides, body-part flags) is copied from the first real throwing knife whose
`Start` runs; otherwise the ctor defaults are used. It sets `torque 0`, `changeRotation`, and
`aimCorrectionAngle = -atan2(tip.x, tip.z)` so the tip faces the target, then runs the same three steps. A
thrown arrow is never removed as "back in the quiver". Settings: `ThrowAssist` (on) and
`ThrowAssistSpeed` (0 = the knives' speed).
- **Needs the game's own assisted-throw option on.** With it off, the debug log says so.
- Scope: only **quiver arrows carried by hand**. An arrow pulled off the string is not covered.
- Debug lines to read in the test: `throw assist tuning from knife '…'` (the real knife numbers),
  `arrow thrown at X m/s -> homing on '…'` or `no target in view`, `below the … m/s throw threshold`,
  `thrown quiver arrow hit (damage …)`.
- **Open:** is the tip really along local +Z (then the angle logs as ~0)? If the arrow flies sideways or
  backwards, the tip heuristic (`Quiver.TipDirection`, the shaft-mesh centre) is the suspect.

## ★ Arrows fall short: the game's shot speed is QUADRATIC in the pull → Better Bow 1.2.0 (Sep 25 2026)

Reported: it's hard to pull far enough, and arrows don't fly far enough. **The data explains it.** Compound bow
prefab (`sharedassets2.assets` pathID 75289, `HVRPhysicsBow`): **`StringLimit` 0.40 m** (full draw),
**`ShootThreshold` 0.20 m** (a shorter release doesn't shoot), **`Speed` 50 m/s**, `StringLimitStyle` 0, and
**`SpeedCurve` keys (0,0 slope 0) → (1,1 slope 2), which is exactly `t²`.** So `speed = 50 × tension²`, and
range goes with speed² (≈ tension⁴): **90% of the pull gives 81% speed and about 66% range; 80% gives 64% speed
and about 41% range.** A few centimetres short of full draw costs a third of the range.

- Code path (disassembled): `CheckArrowRelease` → `OnArrowShot` → `ArrowShootCall(arrow, Tension, false)`,
  which writes `Tension` (0xac) and `_shootSpeed` (0x10c) = `SpeedCurve.Evaluate(tension) × Speed`, then calls
  `ShootArrow(forward)` → base sets `arrow.velocity = dir × _shootSpeed`. `Tension = _nockDistance / StringLimit`
  in `FixedUpdateBow`. `HVRPhysicsBow.ShootArrow` doesn't touch `_shootSpeed`.
- **Fix: a prefix on `HVRPhysicsBow.ShootArrow` recomputes `_shootSpeed` from the same `Tension`, rescaled so
  `FullDrawAt` counts as full, times `ArrowSpeedMultiplier`.** Settings: **`FullDrawAt` 0.9** (full speed at
  36 cm instead of 40), **`ArrowSpeedMultiplier` 1.0** (clamped 0.5–2). DebugLog prints `pull X% -> Y%, speed
  a -> b m/s` for every shot. The physical string, the 20 cm shoot threshold and the haptics are unchanged.
- ⚠ Untested risk: multiplier > 1 means > 50 m/s, ~1 m per 50 Hz physics step. If arrows start passing through
  thin geometry or enemies, the arrow rigidbody's collision detection is the suspect. Prefer `FullDrawAt` first.
- On **`dev`** (commit `bcebf09`), version 1.2.0. Built, copied to `feature/BetterBow.dll`
  and `<game>\Mods`. **Not tested in game yet.** Asset reader: scratch script, same raw-layout approach as
  `bowpaint.py` (MonoScript `HVRPhysicsBow` = `globalgamemanagers.assets` pathID 3626).

## ★ Weapon paint: the bow already has a BLACK paint in the game (Sep 24 2026)

Goal: a fully black bow. **No mod needed. The game ships it. CONFIRMED: painted black in-game,
it looks right, so the "darker than 0.047" mod idea below is shelved.** Paint is done at the
**Gun Edit Table** (`ANBGunEditTable`: place the weapon on its `gunSlot` socket). Each weapon's
`ANBWeaponAttachments` has 2 paint parts × 5 colours (`designColorName/Col/Objects/ID{part}_{n}`), and
`designColor{part}max` sets how many are offered. A colour swaps **pre-made mesh variants**
(`designColorObjects` = child GameObjects), not a material tint.
- **Compound bow table** (`sharedassets2.assets`, MonoBehaviour pathID 89005, read with UnityPy):
  part 1, max 3: **grey** (`free`, the default seen in-game, object `Bow - basecolor`) · **black**
  (`col1_2`, RGB 0.047 = #0C0C0C, object `Bow - black`) · **brown** (`col1_3`, object `Bow - yellow`).
  Part 2 max = 0, so it is not offered on the bow. Cams, sight, rest and stabiliser are black already.
- **Unlock cost: 1 spray can per colour** (`colorUnlocked` → `substractSpraycan(1.0)`, requires
  `ANBGameLogic.currentSprayCans > 0`). Spray cans come from **spray kit collectibles** found in
  contracts (`ANBSprayKit`, `CollectSpraykit`).
- If "black" is still too grey (it's 0.047, lit PBR, not true black), a mod could set the
  `Bow - black` renderers' material colour/smoothness. Untested idea, not built.
- Tool: [il2cpp_tools/bowpaint.py](il2cpp_tools/bowpaint.py). Finds
  the `Bow - *` GameObjects, then the MonoBehaviour that references them by PPtr, and parses the colour
  block. The same method works for any weapon. **IL2CPP asset files have no type trees**, so the block is
  located by the `designColor1/max1/2/max2` floats and parsed using the field order from `il2.py`.

## ★ Bow model exported for remodelling (Oct 1 2026)

`python il2cpp_tools/export_bow.py` → `BlenderRefs/game/bow/` (git-ignored game assets, never publish).
Source: `sharedassets2.assets`, `CompoundBow` → `Bow - basecolor` → `HuntingBow` (**SkinnedMeshRenderer**).
- **One mesh, 38,975 verts / 44,301 faces, one material (`HuntingBowShader`, URP Lit), 28 bones.** Riser, limbs, cams, cables,
  sight, rest and stabiliser are all in that one mesh; the three paint variants (`Bow - basecolor/black/yellow`) are the **same
  mesh and the same textures** with a different `_BaseColor` tint (grey 0.368, black 0.085, brown 0.66/0.49/0.25).
- 1:1 mesh size 0.10 × 0.955 × 0.536 m; the prefab root `CompoundBow` is scaled **0.92** in the game. Limbs run along Unity Y.
- Textures: `HuntingBow_D` 2048² (base), `_N` 4096² (DXT5nm, converted to plain RGB), `_M` 2048² (R metallic, A smoothness), `_AO` 512².
- Lower LODs `bow_lod1..3.obj` (31k / 23k / 17k verts) are reference only. `bones.json` has bind-pose joint positions.
- **A replacement model must keep the rig to animate.** The string/limb flex is skinned to the 28 joints (`botSpine*`, `topSpine*`,
  `bowClamp_JNT`, `arrowLOCATOR_JNT`, …), so a static remodel only works as a swap of the whole mesh with new weights (or a mod-side
  static prop). Not attempted.

## ★ Better Bow 1.1.0 RELEASED (Sep 24 2026)

Tester: **"everything works flawlessly"** on the 1.1.5 test build (throw assist hits reliably, the phone
Settings menu works, hands stay put). Released as **1.1.0** (1.1.1–1.1.5 were test builds only). The only
change after the tested build: routine pause/phone/hand-distance logs now appear only with DebugLog (repairs
still always log), plus updated setting texts.
- `feature/arrow-throw-assist` merged `--no-ff` into **`main`** (`d0b2fab`), tag **`betterbow-v1.1.0`**.
  **Not pushed to `origin`.**
- `release/BetterBow.dll` (60,416 bytes, sha256 `85f5cd8a…`) = deployed `Mods\BetterBow.dll`.
  **`release/BetterBow-1.1.0.zip`** (git-ignored) = `Mods/BetterBow.dll`, same layout as 1.0.0.
- `release/NEXUS_DESCRIPTION.txt` has the 1.1.0 features, settings and a changelog (it tells 1.0.0 users that
  the Settings/hands bug was the mod's dagger grip, and that DaggerTip Down now really points down).

## ★★ Thrown arrows stick in enemies without damage — real cause: the stab ray is too short (1.1.2 log)

**1.1.2 test: pause/Settings fix CONFIRMED** (exited via the phone menu; the log shows the dagger clone
taking the right wrist-HUD slot on every build and the mod handing it back). Throws: **11 of 19 did
100 damage, 8 stuck.** **Armor was NOT it** (every target logged `(no vest)`). The failures all read
`stab #1..#8 into spine_03 / spine_01 (layer EnemyCollisions): no enemy hit zone on the stab ray`,
8 attempts within about 4 ms, then stuck. One later re-stab did land (`stab #9 … stomach - stab damage 100`,
0.7 s later), which matches "it stuck, wiggled, then he reacted".
- **Mechanism (`ANBKnife.stabEnemy`):** if the touched collider has the enemy-body component, it casts
  `Physics.Raycast(StabOrient.position, StabLineWorld.normalized, knifeRayLength (0x48), StabMask (0x38))`.
  Hit → `StabEnemyFinal` (damage). Miss → `HVRStabber.ForceUnstab`, and the stabber re-stabs on the next
  contact (the 8 retries, and the wiggle).
- The physics body (`EnemyCollisions` layer) is bigger than the hit zones (`ANBEnemyDetect`), so the ray
  must reach inward. **A bow shot is far faster and already deep in the body when contact registers**,
  and the arrow's `knifeRayLength` is tuned for that. A homing throw always arrives at the knife's
  17 m/s. **How hard you throw doesn't matter after homing takes over** (the reported speed guess was close:
  it is an arrival-speed/depth effect, but a fixed one).
- **1.1.3:** `ThrowStabReach` (0.5 m) lengthens `knifeRayLength` only during `stabEnemy` of arrows the
  assist threw, and restores it straight after. The throw log now prints the arrow's own stab ray, and the
  knife tuning line prints the real knife's, for comparison.

- **1.1.3 test: barely better** (still about half the throws stuck). The log shows the arrow's own stab ray is
  **0.15 m** and the real knife's is **1 m**, so length alone is not it. **Hits and misses name the same
  colliders (`spine_01` / `spine_03`).** New theory: **a raycast never detects a collider it starts inside**.
  The game's ray starts at `StabOrient` (the tip), and when contact registers the tip is often already inside
  the hit zone, so the zone is invisible to the ray at any length.
- **1.1.4 (deployed, untested):** during `stabEnemy` of an assist-thrown arrow, `knife.StabOrient` is swapped
  for a helper transform `ThrowStabBack` (0.3 m) behind the tip, and `knifeRayLength` = back + reach; both
  are restored right after. With DebugLog, each thrown-arrow stab line ends with
  `[ray from the tip: … ; from 30 cm back: …]`. **Read those first:** "tip: nothing / back: spine_03 at
  0.2 m" confirms the inside-the-collider theory. "Back: nothing" as well means the stab direction or the
  mask is the problem.

- **1.1.4 test: "much more consistent, always hear the impact", but sometimes 2–3 arrows before one
  counts.** Log: the inside-the-zone theory was **confirmed** (many `ray from the tip: nothing; from
  30 cm back: spine_01 at 0.2 m` → damage). **New failure, caused by the 1.1.4 fix:** in ~7 of 20 throws the
  ray from behind hit the **arrow's own tip collider** `tip (1)` (2 cm behind the tip, on `StabMask`). That
  went to `StabEnemyFinal` with a collider that has no `ANBEnemyDetect`: impact sound, no damage. In most
  of those the plain tip ray would have found `spine_03` 5 cm ahead.
- **1.1.5 (deployed, untested):** per stab, `RaycastAll` for the first real hit zone (has `ANBEnemyDetect`,
  and is not under this arrow's `ANBKnife`), first ahead of the tip and then from `ThrowStabBack` behind.
  The helper `StabOrient` goes **1 cm in front of that zone**, ray 0.05 m. If there's no zone, the game's
  default runs (it unsticks and retries, rather than a fake hit). The debug suffix now reads
  `[zone X N cm ahead of the tip]` / `[tip already inside; zone X entered N cm behind the tip]` / `[no enemy
  hit zone …]`, and a non-zone `StabEnemyFinal` is labelled `NOT a hit zone … no damage`.
- **★ Lesson: a probe ray moved backwards along an object will hit the object's own colliders. Filter by
  owner, not just by layer.**

### Earlier: the armor theory (1.1.2) — ruled out as the main cause, kept as a guard

Reported: assisted-throw arrows stick in enemies but do no damage until you shoot them. After a knee shot (the
kneel animation), a thrown arrow does damage. Log: the homing target is always **`knifeSpotChest`**, and
`stabEnemy` fires (up to 8× per throw).
- **`ANBGameLogic.StabEnemyFinal`**: `collider.TryGetComponent<ANBEnemyDetect>` (the hit zone). If
  **`isArmored` (0x2b)** → `ANBBasicNPC.TakeArmorDamage(..., damage = 1.0 hard-coded, ...)`, else
  `TakeStabDamage(knifeDamage)`. So a chest-aimed arrow into a vest does ~nothing. Zone flags: `head`,
  `chest`, `stomach`, `groin`, arms, legs, `isArmorVisor`. NPC: `hasVest` (0xdd), `aimAtHead` (0x5f0),
  `aimAtChest` (0x5f8), `ChestCollider`.
- **1.1.2:** `ThrowAssistAvoidVest` retargets to `aimAtHead` when `hasVest`. DebugLog shows every thrown-arrow
  stab as `into <collider>: <zone> - ARMORED …` / `- stab damage 100` / `no enemy hit zone on the stab ray`.
  **Unconfirmed until that log is read:** if the stabs show unarmored zones and still no damage, the cause is
  elsewhere (a stab that never reaches `StabEnemyFinal` = the stab raycast misses).

## ★ Dagger tip "Down" pointed up in 1.0.0: an inverted mapping, fixed in 1.1.1 (Sep 24 2026)

The knuckle vector is index→little finger, and tip along it = out past the little finger = **down** in a
fist. The earlier "tip points up" report was made with the `[BetterBow]` key
`DaggerOrientation = "KnucklesReverse"` (little→index). Commit `4cd0057` assumed it was `Knuckles` and
made **Down = the reverse**, i.e. exactly what had been reported as up. 1.1.1: Down = index→little, Up =
reversed. **★ Lesson: before acting on "it points the wrong way", read the user's cfg for which mode
produced it.** The stale `DaggerOrientation` / `DaggerFlipAxis` keys in the cfg are dead and can be deleted.

## ★★★ ROOT CAUSE FOUND (1.1.1 log): the dagger grip broke the pause menu → dead Settings + drifting hands

**It was the mod, not the game.** The 1.1.1 log at the Settings press:
`game PAUSE (useHands True…)` → `NullReferenceException at Component.get_gameObject ← ANBGameLogic.pauseGame`.
- **`ANBWristHud.Awake` writes itself into `ANBmain.ANBwristHudLeft` (0x1108) / `ANBwristHudRight` (0x1110)
  with no check** (`isLeft` 0x29 / `isRight` 0x28). The arrow's grip points carry a preview hand with a wrist
  health display. **The dagger grip's `Instantiate` of a grip point woke that HUD and it took the right-wrist
  slot.** When the arrow was destroyed, the slot held a dead object (14 dagger grips that session).
- **`pauseGame`'s pause branch calls `.gameObject` on both HUDs without the `op_Implicit` check the unpause
  branch has.** It throws after step 1 (hands reparented onto their `Target` = controller pose) and before
  timeScale / `Paused` / the menu. So: no menu, `Paused` stays false (nothing ever unpauses the hands, they
  stay on the controllers and get dragged by locomotion), and `showMenuExec` dies inside `pauseGame`, leaving
  `switchingToMenu` set → **Settings dead for the rest of the session**. The same bug appeared first in the
  0.3.0 era, when the dagger grip was introduced. **Very likely also the "known game bug" Settings report.**
- **The first theory was wrong:** the unpause parent (`playerHealth.parent` = `PlayerObject/TechDemoXRRigOpenXR`)
  is the same as the pre-pause parent. A normal pause/unpause is fine.
- **Fix in Better Bow 1.1.2 (deployed, untested):** the dagger clone gives the HUD slots back and destroys its
  own `ANBWristHud`. Before any pause, dead slots are repaired (partner's `otherWristHud`, or one search for
  a non-arrow HUD). A hand found parented to its `Target` outside a pause is moved back.
- **★ Lesson for any mod that clones game objects: cloning runs `Awake` on everything inside, and game
  singletons register themselves in `Awake`. Diff the game's global slots around every `Instantiate`.**

### Earlier notes (hands drift / phone), before the root cause

Reported: after taking the phone and trying Settings, moving with the left stick makes the hands move
weirdly and away from the player (the earlier "hands run ahead" report). No Unity `Player.log` exists
for this game (`LocalLow\ANB_Seth\GunmanContracts` has only saves/config), so **nothing logged it**.

From `GameAssembly.dll`:
- **Phone Settings = `ANBSmartphone.showMainMenu`**: returns if `switchingToMenu` (0xec), else sets it
  and starts `showMenuExec`. That releases the phone (vtable call on its grabbable), waits **0.05 s
  (scaled)**, calls `ANBGameLogic.pauseGame(true, true)` and only then clears `switchingToMenu`. **If the
  coroutine dies in between, the flag stays set and every later press is ignored.** That's a plausible
  mechanism for the "Settings stops responding" bug, unconfirmed.
- **`pauseGame(useHands, showMenu)` is a toggle on `Paused` (0x13e8).** In VR, on pause:
  `UIManager.LeftHand/RightHand` (`HVRJointHand`, the physics hands) get `transform.parent = hand.Target`.
  Then timeScale 0, `freezeWorld`, NPC `gamePause`, InputSystem update mode. **On unpause (only if
  `useHands`) they get `parent = playerHealth.transform.parent` = the player rig.** A physics hand
  parented under the rig is dragged by locomotion on top of its joint pull → it runs ahead. That's the
  prime suspect: the unpause parent may not be where the hands started.
- Callers of `pauseGame`: `showMenuExec`, `ANBWristHud.Update`, `ANBHVRControllerInputs.checkVRButtonTaps`,
  `ANBUIManager.ExitAndSave`, `ANBGameLogic.Start`, the flat-mode menu handlers. All seen pass `(true, true)`.
- **Better Bow 1.1.1 `PauseHands.cs`** (runs without a bow too) logs every PAUSE/UNPAUSE with both hands'
  parent paths and each phone Settings press (flagged if the game ignores it). It warns when a hand stays
  >30 cm from its controller for 1 s. Two fixes, each with its own setting:
  `HandsKeepParentAfterPause` puts the hands back under their pre-pause parent after an unpause, and
  `PhoneSettingsRescue` runs `pauseGame(true, true)` and clears the flag if the switch hasn't finished
  1 s after a press.
- **What the next log decides:** whether the UNPAUSE line shows a different parent from the PAUSE line
  (the theory holds and the restore fixes it), and whether "phone menu switch never finished" appears.

## Knee Shot Stun: leg-shot enemies stay down longer (Sep 24 2026): 1.0.0 TESTED: "works flawlessly"

Separate mod (not bow-related): source [KneeShotStun/](KneeShotStun/KneeShotStun.cs),
on `main` (merged Sep 25 2026), deployed as `<game>\Mods\KneeShotStun.dll`. Config `[KneeShotStun]`:
`ExtraKneelSeconds` (3.0), `Enabled`, `DebugLog` (turned on for the first test).

**How the game does the knee shot:**
- A leg hit sets `ANBBasicNPC.gettingHitLegs` (0x9bf), crossfades the animator's **"Hit" layer (index 5)**
  to `hit_legs_1` / `hit_legs_1_m`, and starts `GetHit`. `GetHit` copies **`stunAtHitTimeLegs` (0x564,
  default 3.0)** into `overallHitTime` (0x568). `checkAbilities` counts that down, and `isGettingHit`
  (0x99c, the AI stun) ends at 0. `GetHit` also starts `endLegHit`, which waits **`endLegHitAfter` (0x56c,
  default 3.0)** and then clears `gettingHitLegs` + `gettingHitLegsFull`. `gettingHitLegsFull` picks the
  kneeling death animation.
- **★ The visual get-up does NOT follow any timer.** `hit_legs_1` is one 2.8 s humanoid clip, and its
  transition back to `No hit` is exit time 0.911 with no conditions. So raising `stunAtHitTimeLegs` alone
  would only leave the enemy standing there stunned.
- Hip height (`RootT.y`) across the clip: fall 0–0.85 s, **kneel 0.85–1.5 s (hips steady at 0.46–0.51 m)**,
  rise 1.5–2.4 s.

**The mod:** a Harmony prefix on `GetHit` extends `endLegHitAfter` and registers the NPC. Each frame it
drives the Hit layer's time with `Animator.Play(state, layer, t)` over 0.85→1.5 s, stretched by
`ExtraKneelSeconds`. The fall and the rise play at normal speed. While it holds, it keeps `overallHitTime`
≥ the remaining hold + 1.5 s and `isGettingHit` true, so a follow-up hit can't shorten the stun (GetHit
resets `overallHitTime`). The controller has no StateMachineBehaviours, so re-entering the state with
`Play` every frame is safe.
- **To check in the first log:** `knee shot, holding the kneel …` shows the real prefab values of
  `stunAtHitTimeLegs` / `endLegHitAfter` and the animator's `updateMode`. Then look in game for whether
  enemies shoot from the knee and whether the hold looks natural or like a freeze.

**Asset tools added** (UnityPy 1.25): `anim_dump.py` (states, transitions and conditions of
`ANB_BasicNPC_Animator`), `clip_info.py` (clip lengths and events), `clip_root.py` (samples a humanoid
clip's hip height; **humanoid value index 8 = RootT.y**, since 0–6 are the motion T/Q), `scanoff.py`
(which NPC methods touch a field offset).

## ★ Fire-mode selector (single / burst / auto): feasibility (Sep 25 2026), YES, and cheap

**The framework already implements all three modes. The game just never lets you switch.**
`HVRGunBase.FireType` (**0x54**, enum `GunFireType`: **0 Single · 1 ThreeRoundBurst · 2 Automatic**) is
read by exactly three methods, all framework code that `ANBHVRGunBase` does not override:
- `TriggerPulled`: Single → fires once now (if `Time.time - TimeOfLastShot (0x150) > Cooldown (0x40)`).
  Otherwise it sets `IsFiring` (0x168) and `RoundsFired` (0x16c) = 0, and won't restart a burst mid-burst.
- `UpdateShooting` (ANB override = the same logic plus a null check): while `IsFiring`, shoots every `Cooldown`.
  `CanFire` false (empty) → `IsFiring = false`.
- `Shoot`: `RoundsFired++`; **Burst stops at 3**. `TriggerReleased`: **only Automatic stops on release**, so a
  burst finishes even if you let go.
- **Game code never reads 0x54** (scanned all `ANBHVRGunBase` methods). Enemies fire through
  `EnemyTriggerPulled`, a separate path. → A mod only has to write `FireType` on the held gun; the
  burst cadence = the gun's own `Cooldown` (its auto rate).

**Input map while holding a gun (`ANBHVRControllerInputs.checkVRButtonTaps`, `HVRANBAmmoReleaseAction`),
all on "just pressed":**
- **Gun hand A/X = magazine release** (`HVRANBAmmoReleaseAction.CheckInput`, plus `Gun` vtable 0x2a8).
  **The same input as the knife grip swap** (`HVRGrabPointSwapper.GetActivated` = `PrimaryButtonState`
  just-activated) and Better Bow's dagger grip. **So "the knife-grip button" on the gun hand is taken.**
- **Right B = slow motion** (`buttonSlowMotion`, side == "Right"). **Left Y: nothing** with a gun in the left hand.
- **Support hand on the foregrip:** `OnHandFrontalStabilizerGrabbed` writes the gun into **that hand's
  slot too**, so `checkVRButtonTaps` gets `Gun == otherGun == the rifle`. **With the laser/light attachment**
  (`WeaponAttachments.AT2` 0x69, stabilised 0x290): A/X = `ToggleFlashlight`, B/Y = `ToggleLaser`.
  **⚠ Without it, support-hand A/X = `Gun.ReleaseAmmo` (vtable 0x2a8) = a SECOND magazine release.** (The
  first reading said "does nothing". That's wrong, and only true for a FREE support hand, whose slot is null.)
- `HVRInputAction` (base of the ammo-release action) sets `Grabbable = GetComponent` on its own object and
  polls only `Grabbable.HandGrabbers` → the gun-hand mag release never sees the foregrip hand.
- Stick clicks = sprint / crouch (`HVRPlayerInputs`). Per-hand guns: `ANBGameLogic.rightHandGun` (0x10a0),
  `leftHandGun` (0x10a8). `checkVRButtonTaps` side strings are `"Left"` / `"Right"`.

## Fire Selector (Sep 25 2026): 1.0.0 TESTED, works

Chosen input: **support-hand A/X**. Source [FireSelector/](FireSelector/FireSelector.cs),
on `main` (merged Sep 25 2026), `<game>\Mods\FireSelector.dll`, config `[FireSelector]`
(`DebugLog = true` pre-seeded for the first test).
- **Hold A/X ≥ `HoldSeconds` (0.35) on the support hand** (free, or on this gun's foregrip) → Auto → Burst →
  Single → Auto. Haptics on that hand: 1 pulse single, 3 pulses burst, long buzz auto. Only guns that start
  **Automatic** (not bow/shotgun). Gun hand A/X (instant mag drop) is untouched.
- **The game acts on the press, so a Harmony prefix on `checkVRButtonTaps` holds back a support-hand A that
  could become a selector press. A short tap replays the call on release** (mag drop / flashlight, exactly as
  the game would have). Cost: the support-hand mag drop fires on release (~0.1 s later).
- `SetMode` also clears `IsFiring` / `RoundsFired`: **switching mid-fire would otherwise run away**, because
  `UpdateShooting` fires while `IsFiring`, and only Automatic clears it on release (burst only at exactly 3).
- Remembers the mode per `ANBwpt.WeaponID` in `SavedModes` and re-applies it whenever a held gun's `FireType`
  differs (guns get reset/pooled).
- **1.1.0 (Sep 27 2026, on `dev`, deployed, untested): `AllowBurst` (default true).** A Nexus user asked for it
  (extra mode is hard to cycle mid-fight). false → Auto ↔ Single only; a remembered burst is applied as Automatic.
  **Release files ready** in `release/FireSelector/` (zip, updated BBCode description, `NEXUS_UPLOAD.md` = update
  steps, changelog, reply to the requester, checklist). Commit `0a9bf9b`. **`main` fast-forwarded to `dev` on
  request**, so `main` now also holds the unreleased Physical Dodge + Enemy Awareness work. Not pushed or
  tagged. Tag `fireselector-v1.1.0` after the in-game test.
- **Flat-screen version: assessed Sep 27 2026. Decided not to build it** (kept for reference). Flat mode is a separate gun system (Infima **Low Poly
  Shooter Pack**: `InfimaGames.LowPolyShooterPack.Character` + `Weapon`), not HurricaneVR. `Weapon.automatic`
  (bool, 0x44) is the only mode, so no burst exists. Fire: `OnTryFire` press sets `holdingButtonFire` (0x152) and
  resets `shotsFired` (0x158). `Character.Update` fires while held on automatic guns. `Character.Fire` does
  `shotsFired++`. Plan: single = `automatic=false`; burst = prefix on `Character.Fire` that blocks at
  `shotsFired >= 3`. **Bindings (from the game's input asset):** keyboard B/N/J/K/L/U/I/Z are free (V, X, C, F, R
  are taken). **Every gamepad button is used**, so the gamepad needs hold-reload. Reload runs on phase Performed
  (= press) in `OnTryPlayReload`, which also ends gun editing, so hold-reload needs a hold-back like the VR one.
  The `CallbackContext` can't be replayed, so a tap would have to redo the game's reload gates by hand.
- **First log to read:** `gun '…' (id): automatic, cooldown X s (N rounds/min) - selector available` = which
  guns qualify. Then `A/X pressed on …`, `fire mode -> …`, `short tap - game action replayed`. If no gun ever
  says "selector available", no rifle ships as Automatic, or the grip-hand check (`Grabbable`/`myGrabbable`
  pointer) fails.

## ★★ Heavy Melee (Sep 25 2026): 1.0.0 RELEASE CANDIDATE, committed on `main`, NOT released / not tagged

**RC handoff:** 1.0.0 = 0.4.0 + `DebugLog` default off. `release/HeavyMelee/`: `HeavyMelee.dll` (21,504 bytes,
sha256 `991eab2b…`, = deployed), `HeavyMelee-1.0.0.zip` (`Mods/HeavyMelee.dll`, forward slash — PS 5.1
`Compress-Archive` writes `Mods\…`, so zip with Python), `NEXUS_DESCRIPTION.txt`, `NEXUS_UPLOAD.md` (status +
checklist). **Release undecided** (the game's enemy AI makes melee balance tricky). Only untested piece:
the 8 m/s open-palm strike (0.4.0; last log was 0.3.0). The test cfg keeps `DebugLog = true`; dead keys
`WeaponMinSpeed` / `MinHitSpeed` can be deleted.

**0.1.0 test log ("seems to be working", but enemies often fall and die from one hit):**
- **Real numbers:** `meleeDamage` 10, enemy health 100, **`meleeMagnitude` 0** (so the game's only speed
  gate is the hard-coded rb speed > 1 m/s), `StumbleOnRBMass` 5 kg, **hand rigidbody 20 kg** (so a bare fist
  always stumbles), pistol/bow 1.5 kg, **`BehaviourPuppet.getUpDelay` 0** (prefab; ctor default is 5).
- **"Dies from one hit, but not always" = the body part.** Weapon 30 × head 5 = 150 → every weapon head hit
  was a one-shot kill; fist 20 × 5 = 100 exactly. Torso 60 / 40. Limbs never killed.
- **★ Bug: one punch counted two or three times.** The game-path punch starts the stun, and the same
  swing's next contact (~10 ms later, another body part) then took the mod's "stunned" path; the cooldown
  was only set by the mod's own hits. E.g. 100 → 60 → 20 → dead within 0.3 s from one swing.
- **Bug: with `meleeMagnitude` 0 the speed gate was 0**, so touches at 0.5–1.3 m/s counted (deliberate swings
  logged 3–8 m/s).
- **Bug: "stays down" was a no-op.** Resetting `unpinnedTimer` means nothing when `getUpDelay` is 0; the puppet
  (`BehaviourPuppet.OnFixedUpdate`: timer += dt, compare to `getUpDelay` 0x18c) gets up as soon as it stops moving.

**0.2.0:** `MaxDamagePerHit` 50 (after the game's ×5/×2, computed with `CheckBodypart(go,"head"/"torso")`,
same order as the game) → nothing dies from one hit, head/torso twice. The game-path punch now starts the
per-enemy cooldown. `MinHitSpeed` 2 m/s (replaces `WeaponMinSpeed`; the old cfg key is dead). Keep-down raises
`getUpDelay` by `KeepDownSeconds` (2 s) while held, resets the timer per hit, and restores the game's value
once the puppet leaves Unpinned / dies / on scene change. Resulting hits to kill from 100: weapon head 2,
torso 2, limb 4 (limbs only finish a downed enemy); fist head 2, torso 3, limb 5.

**0.2.0 test ("way better, seems to work"):** double-hit fix, 50 cap, hold-down all confirmed in the
log ("held down for 2 s" … "getting up"). Remaining complaints: too sensitive to touches (hard to grab a hand
or the mouth, they stumble at once), and enemies "take damage from surrounding objects when getting up".
- **Touch sensitivity:** `MinHitSpeed` only gated the mod's own path; standing-enemy fist contacts still went
  through the game's path, whose only gate is rb speed > 1 m/s — and the **hand rb is 20 kg > the 5 kg stumble
  rule**, so every touch stumbled. The log had `punch (game path) on RightHand/LeftHand` at full health = the grabs.
  Also, **a hand gripping the enemy keeps making fresh contacts**, each a "punch".
- **Objects:** in `collisionEnter`, any rigidbody over 1 m/s that isn't a gun/ammo/knife/hand/blunt goes to the
  damage path as a thrown object (`TakeMeleeDamage`, stumble > 5 kg). A downed/rising enemy kicks a chair, it
  moves, it counts as a hit on them. Vanilla rule, amplified by the longer knockdowns. (`TakeFallDamage` is
  NOT it: it is an instant kill that only fires when `isFalling` (0x9b0) or on a head slam while
  `isHeadGrabbed` (0x9ad), no helmet on tanks.)

**0.3.0 (deployed, untested):** `MinPunchSpeed` / `MinWeaponSpeed` 3 m/s (the old `MinHitSpeed` key is dead;
renamed so the saved 2.0 doesn't stick), applied to the game's fist path too, and a slow held gun no longer
falls through to the game (which counts a 1.5 kg gun at 1 m/s). **`OpenPalmNoHit`: fist = `hand.Controller.Grip`
≥ `FistGrip` (0.5)**; an open hand does nothing (`HVRController` has `Grip`/`Trigger` floats and finger curls).
**A hand holding anything that isn't a weapon never punches** (`Source.Holding`). `IgnoreKickedObjects`: while
`isOffBalance`, a loose object moving no faster than the body part it touched (+1 m/s) is ignored; thrown
objects still hurt. Debug lines: `open-palm touch … ignored`, `ignored '<obj>' … pushed by its own <part>`,
game-path punches now log their speed.

**0.3.0 test log:** no warnings. ~110 `ignored 'FragmentN'` = **debris shards of broken `ANBBreakable`s**
(0.3–1.2 kg) plus one bullet casing, pushed by the downed enemy's own feet/legs. That was the "damage from
surrounding objects". 61 open-palm touches ignored, all with grip 0.00 at 3–8 m/s, arriving in bursts (one
swing = many contacts; the ignore path has no cooldown, so the debug line repeats). Counted punches 3.4–8.8 m/s.
- **Shoulder throw is not collision-driven:** `ANBNpcPuppetmasterSettings.checkGrab` (per frame from
  `checkAbilities`) starts `ShoulderThrow` once the grabbed hand is pulled `throwAtDistance` (0xa0) from
  `throwCenterStart`; also `ANBTakedownController.handthrowExecNew`. The mod can't block it or change its
  direction, and returns early while `isThrowing`. The Holding rule stops the gripping hand from firing
  stumbles mid-grab. **Still open:** hitting a grabbed enemy with the other hand stumbles them (the game's own
  `isGrabbed` rule, kept); make it optional if it spoils throws.

**0.4.0:** Request: a fast open hand (a slap, a "ninja" chop to the back) to count. `OpenPalmStrikeSpeed`
8 m/s: an open hand at or above it hits exactly like a punch (same damage, stumble, downed rules); below it,
still ignored. The 0.3.0 log's fastest open-hand contacts were 7.9 m/s, so ordinary pushes and grabs stay safe.
The ignored-touch debug line now logs once per 0.5 s per enemy.

### 0.1.0 design notes

Request: hitting an enemy with a held pistol/rifle does nothing (no stumble, no damage),
knuckles are too weak, and an enemy on the ground "doesn't feel" punches (drop them ~3 times to kill).
Wants guns to hit like heavy metal (like the game's blunt props: crowbar, pans, guitar), stronger fists, and hits that keep a
downed enemy from getting up. Source [HeavyMelee/](HeavyMelee/HeavyMelee.cs),
`<game>\Mods\HeavyMelee.dll`, config `[HeavyMelee]` (`DebugLog = true` for the first test). Not committed yet.

**How the game does melee — all three complaints are game rules, not bugs:**
- Each body part's `ANBBodyMeleeCollision.OnCollisionEnter` just forwards to
  **`ANBBodyMeleeCollisionManager.collisionEnter(bmc, collision)`**, which classifies the other collider
  with `GetComponentInParent<…>`: `ANBBluntWeapon` → `blunt.hit` (speed tiers, **no stun gate**);
  **`ANBHVRGunBase`** → counts only past `meleeMagnitude` (0x28) speed, then only if the gun's **rb mass
  > 1.0** (else `noDamage`), and stumbles only if mass > `ANBPM.StumbleOnRBMass` (0x17c) — no forced
  stumble for a swing; **`HVRHandGrabber`** → **ignored unless `ANBGameLogic.LeftHandWeapon` (0x10b8) /
  `RightHandWeapon` (0x10c0) == `"none"`**, so the knuckles of a gun hand never count; `ANBKnife` /
  `ANBHVRAmmo` → ignored.
- **★ If `isGettingHit` (0x99c) → `TakeMeleeDamage(..., noDamage = true)`.** The first punch starts the hit
  stun, and it covers lying on the ground → every later punch does 0. That is the "drop them 3 times".
- `TakeMeleeDamage(bodyPart, collision, stumble, fromEnemy, noDamage, damageOverride)`: damage =
  **`ANBGameLogic.meleeDamage` (0xf94)**, **×5 head, ×2 torso**; override > 0 replaces it. **Only head/torso
  hits can kill; anything else floors health at 1.0.** Not off-balance → `GD_PuppetMeleeHit.ApplyHit` push
  (∝ relative velocity × `meleeImpactForceMultiplier`). `stumble` is passed to `GetHit` as `isRunning`,
  which runs `ANBPM.EnemyStumble`. Blunt damage (`TakeBluntWeaponDamage`) has the same 1-HP floor unless
  `canKill` + head.
- **Down = PuppetMaster `BehaviourPuppet.state != Puppet`**; `ANBNpcPuppetmasterSettings.checkBalance` is
  the only writer of `isOffBalance` (0x9b2) and just mirrors the state. Get-up = BehaviourPuppet's
  `unpinnedTimer` (0x27c) passing `getUpDelay` (0x18c). `StandUp` sets `isGettingBackUp` (0x9b7).
- Physics matrix (globalgamemanagers): **`Grabbable` and `Hand` both collide with `EnemyCollisions`**, so a
  held gun's contact does reach `collisionEnter`; the rejection is purely the rules above.

**The mod:** prefix on `collisionEnter` takes over (returns false) for (a) a held gun/bow (main grip or
foregrip in a hand, not `EnemyGun`) or a hand holding one, and (b) a bare fist on an enemy that is
stunned or down. It calls the game's own `TakeMeleeDamage` with `damageOverride = meleeDamage × mult`
(weapons ×3, fists ×2), `stumble` = always for weapons on a standing enemy, the game's mass rule for fists,
never on the ground. Speed gate = the enemy's own `meleeMagnitude`; per-enemy cooldown 0.3 s (one swing
touches several parts). Bare-fist punches on a standing enemy stay on the game path with the damage scaled
(prefix on `TakeMeleeDamage`, scoped by a flag set only in that path). On a downed enemy: `unpinnedTimer = 0`
(stays down); during `GetUp` → `SetState(Unpinned)` (knocked back down); a limb hit that would take health
below 0 → `KillNPC(null)` + `AddMeleekill` (knockout). Blunt props (`ANBBluntWeapon`: crowbar, pans, guitar; **the game has no nunchucks**) keep their damage and get only the keep-down
part (postfix on `TakeBluntWeaponDamage`).

**First log to read:** `game melee values - meleeDamage X, punch speed Y, stumble above Z kg, health H,
get-up delay D` (the real numbers, to tune the multipliers), then per hit `… hit <part> at N m/s (min M,
mass K kg), standing/stunned/on the ground, damage …: health A -> B … stays down / knocked back down /
KNOCKED OUT`, and `punch (game path), damage X -> Y`. **If a gun swing logs nothing at all**, the gun's
collision never reaches `collisionEnter` (layer / `IgnoreCollision` while held) — then the fallback is an
overlap check from the gun side.

## ★ Nexus release prep: Knee Shot Stun 1.0.0 + Fire Selector 1.0.0 (Sep 25 2026), TESTED, tagged, released
Knee Shot Stun: https://www.nexusmods.com/gunmancontractsstandalone/mods/26 ·
Fire Selector: https://www.nexusmods.com/gunmancontractsstandalone/mods/25

Both bumped 0.1.0 → **1.0.0** (first public version, like Better Bow), commit `f5ee82c`, pushed. The 1.0.0
DLLs are deployed to `<game>\Mods` (sha256 matches `release/`), so the test build = the shipping build.
Per mod in `release/<Mod>/`: the DLL, `<Mod>-1.0.0.zip` (`Mods/<Mod>.dll`, git-ignored), `NEXUS_DESCRIPTION.txt`
(BBCode, Better Bow's structure), and **`NEXUS_UPLOAD.md` = every Nexus form field** (summary under 350
chars, tags, requirement, file entry), an image plan and a pre-upload checklist.
- **Tested Sep 25 2026 → tagged `kneeshotstun-v1.0.0` / `fireselector-v1.0.0` (pushed).** Left: record clips,
  make images, upload, then README status → released + Nexus link. The Nexus texts get a later pass.
- **No images yet:** they need a gameplay clip or screenshot first. Better Bow's were made with image_gen from
  a gameplay frame, and its prompts are in `release/BetterBow/*-prompt.txt`.
- Descriptions deliberately claim nothing the test hasn't confirmed: e.g. no "enemies can't shoot from the
  knee" (still an open check), and no list of which rifles get the selector.

## ★★ Enemy awareness: "they always know where you are" is real, from a degenerate check (Sep 25 2026)

Reported: once a fight starts, wave enemies seem to know the player's exact position. **The code confirms it.**
All in `ANBBasicNPC` (ctor defaults in brackets):
- **Senses are real:** view cone `viewRadius` 25 / `viewRadiusEngaged` 50 m, `viewAngle` 160° / engaged 240°,
  per-difficulty variants; `FovSight` + `BlockedSight` raycast set `targetInSight` (0xa4c). Reaction times
  `playerSeenReactionTime*` 1.2/1.0/0.8 s. Hearing via `ANBAlertManagement.MakeNoise` / `AlertAllNPCs`.
- **Attack navigation goes to `lastKnownPosition` (0xa50)**: `Navigation` copies it into the destination
  and calls `SetDestination`. Only `markLastKnownTargetPosition` writes it.
- **★ The leak, in `AL_targetCheck` (0x181c39070):** while the target is out of sight and `lostTargetTime`
  (0xb9c) > 0, it runs
  `if (lostTargetTime > lostTargetTime - loseTargetPositionUpdatesAfter || !canLoseTarget) markLastKnownTargetPosition(attackTarget.position)`.
  **Same field on both sides**, so the test is just "`loseTargetPositionUpdatesAfter` > 0", and the default is
  **17**. → **Every tick of the "lost you" window, the last known position is set to the player's LIVE
  position.** The field name implies "stop updating after N s"; the compiled compare can't do that. Almost
  certainly a dev bug (it would need the timer's start value). With `canLoseTarget` false it never ends.
- **Timer:** set to `lostTargetTimeSpawn` (15) on the first unseen check after `StartAttack` (`initialSet`
  0xbad), else `Random(lostTargetTimeMin 20, lostTargetTimeMax 30 + 1)` when sight is lost. It drains by
  `deltaTime × ActionPulseRate` (ctor 5; real duration unconfirmed) and **does not drain while `isFlanking`**.
  At 0 → `LooseTarget`: `canLoseTarget` false → `StartAttack` again; true → `StartHunt` / idle.
- **Wave/alert entry points hand over the player Transform itself**, not a position:
  `ANBNpcSpawner.setSpawniesAttackPlayer` → `attackPlayer` → `StartAttack(Playertarget 0xb88)`; `StartAttack(null)`
  also falls back to `Playertarget`. Spawner has `startAttackingPlayer`, `alwaysAttackPlayerOnAlert`,
  `canLoseTarget`, `lostTargetTimeSpawn` overrides. So a spawned wave attacker that never saw you still
  tracks you live for its whole spawn window.
- **Hunt is centred on you too:** `HuntingLogic`/`Navigation` pick `FindRandomNavMeshPosition(huntTarget, huntRadius 15)`
  with `huntTarget` = the player Transform → "searching" = random spots within 15 m of your current position.
- `ANBEncounterSystem.SetCollectivePlayerposition` shares `lastKnownPosition` only when that NPC has sight
  (`useCollectivePlayerposition`, `lastKnownPlayerPositionKeepTime`); readers not traced yet.
- **Unverified:** the prefab/spawner values actually used (IL2CPP assets have no type trees). A logging mod
  reading `loseTargetPositionUpdatesAfter`, `canLoseTarget`, `lostTargetTime` per NPC would settle it.
- **Fix idea (not built):** postfix on `AL_targetCheck`: when out of sight, allow live updates only for a
  short grace (~1–2 s, "saw which way you went"), then restore the frozen position; optionally centre the hunt
  on the frozen position instead of the player Transform.
- **★ "Same door" (player/community complaint: camp somewhere and enemies keep coming through one door):**
  `ANBNpcSpawner.getSpawnPoint` with **`SpawnPointOrder` = Closest** (enum Closest / Random / Ordered) re-sorts
  `spawnPoints` with `ANBEncounterSystem.SortByDistance(points, ANBGameLogic.PlayerHitTarget)` and takes the
  first that passes `spawnPointValidation` (hidden from you `SpawnpointCheckHidden` via `outOfSight`, min
  distance `minSpawnDistanceToPlayer`, radius, other-NPC distance, cooldowns). → Standing still = the same
  nearest hidden point every time. Plus attack pathing goes to your (live) position, so they all take the same
  shortest navmesh route. Which order the challenge spawners use is unverified (the logger prints it).
- `calcWaveSetup` / `AllDeadTrigger` / `UpdateHuntTarget` have **no direct callers** (xref); waves advance inside
  coroutines. `onAllDeadTriggers` is called from `SpawnieDeath`. Don't hook the former to detect waves.

### Enemy Awareness Log 0.1.0 (Sep 25 2026), diagnostic mod, deployed, untested

Source [EnemyAwarenessLog/](EnemyAwarenessLog/EnemyAwarenessLog.cs),
`<game>\Mods\EnemyAwarenessLog.dll`, config `[EnemyAwarenessLog]`. Changes nothing in the game. Output: ML console
**and `UserData\EnemyAwarenessLog\session-<time>.log`** (one file per launch — read that one).
- Per enemy type (once): real `canLoseTarget`, `lostTargetTime*`, `loseTargetPositionUpdatesAfter`,
  `ActionPulseRate`, view radius/angle, `startAttackingPlayer` etc., plus the code's prediction ("FOLLOWS you").
- Per spawner (once): `SpawnBehaviour`, **`SpawnPointOrder`**, point list with positions, validation checks,
  spawner overrides. Per spawn: `spawn E7 at '<point>', 24 m NE, BEHIND you` (via `spawnNPC` pre/post diff of
  each point's `mySpawnie`).
- `E7 FIRST SAW YOU from (x,y,z), <dist> <compass>, <front/left/right/behind>`.
- **Unseen chase lines (the leak test):** reference = where it last saw you (or where you were, if it never
  did); verdict **LIVE-TRACKED** if its `lastKnownPosition` stayed within 1.5 m of you in ≥60% of checks after
  you moved ≥3 m from the reference; also counts `markLastKnownTargetPosition` writes while `targetInSight` is
  false, and where its NavMeshAgent destination was vs you / vs the reference. **To test it: break line of sight
  and move ≥3 m.** Standing still gives "can't tell".
- `LOST-TARGET timer ran out after X s from value Y` → the real drain rate. Hunt points, alerts/noises (rate-limited).
- Summaries per wave (polls `waveSurvived`) and on all-dead / scene change: spawn-point histogram, first-sight
  spots clustered within 3 m, chase verdict counts, whether you stayed put.
- **⚠ Spawner distances print SQUARED:** `initSpawner` squares `minSpawnDistanceToPlayer` (0x1b0),
  `minSpawnDistanceToOtherNPCs` (0x1b8), `maxSpawnRadius` (0x1c0) in place, and `spawnPointValidation` compares
  them to the 3D `sqrMagnitude` to `PlayerHitTarget`. Logged "36 m" = **6 m**, "2500 m" = 50 m. Fix the print in 0.2.

### ★★ First log (Sep 25 2026, `Contract_02_Restaurant_2` wave challenge, 3 waves + a bit): BOTH SUSPICIONS CONFIRMED

- **Wave spawner `NPC_Spawner`:** `Wave`, **order Closest**, 24 points, point cooldown 1.8 s (7 s once seen),
  hidden check on, min distance to you **6 m**, radius 50 m. **Overrides: `startAttackingPlayer` True,
  `canLoseTarget` FALSE.** Enemy prefab: lostTargetTime 4/8/15, `loseTargetPositionUpdatesAfter` 8,
  `ActionPulseRate` 4, view 30/50 m 200/280°. Encounter: collective position on (keep 3 s), flanking max 3.
- **Live tracking, unambiguous:** of all unseen chases where the player moved ≥3 m, **every single one was LIVE-TRACKED;
  "searched where it lost you" = 0.** Most never saw the player at all (they spawn already attacking). Worst cases:
  E37 37.6 s unseen, the player moved 19 m, last-known within 1.5 m of the player in 138/143 checks; E36 30 s / 24 m. **NavMesh
  destination averaged 0.1 m from the player's real position** vs 7–21 m from where the player had been.
- **No lost-target timer ever ran out and no hunting ever happened** (0 `LOST-TARGET`, 0 hunt points): with
  `canLoseTarget` false the timer never drains, and the `!canLoseTarget` half of the check forces the live
  update regardless. **So in waves the degenerate compare is not even needed — the override alone does it. A
  fix must cover the `!canLoseTarget` branch too.**
- **Same door:** Closest + 1.8 s cooldown → one point feeds a burst: `SpawnPoint_38` at t 284.9 / 286.7 / 292.4,
  `SpawnPoint_61` ×3 in 10 s, `_41` ×3. First sightings cluster (4 of 8 at one spot in wave 3). The player moved a lot
  (up to 150 m), so the "door" moves with the player — it's always the nearest hidden point, and each enemy then walks
  the shortest path to the player's exact position, i.e. single file through the same doorway.
- Side notes: several enemies saw the player **0.4 s after spawning 6–7 m away** (the hidden check passes, but it's
  close); every spawn fires `alertWorld(attack)` → `AlertAllNPCs(targetHitlayer, attack)`.

### More live-position leaks found while designing the fix (Sep 25 2026)

- **Flanking:** `findFlankPosition` → `FindRandomNavMeshPosition(attackTarget = player Transform, …)` +
  `IsPathFromPosToTargetWithinLength(…, attackTarget.position)`. Reported: they always seem to know when the player flanks them.
- **Hunting (the game's search mode):** `HuntingLogic` copies `huntTarget.position` (live) or the collective
  position into `huntTargetCurrentPosition` (0xa5c); `Navigation` picks `FindRandomNavMeshPosition(huntTarget,
  huntRadius)` = random points around your live position.
- **`StartHunt` can't be used for waves:** first check `if (!canLoseTarget) → StartAttack`. With the encounter
  system's collective position ≈ zero (nobody has seen you) it also bounces to `StartAttack`.
- **Turning:** `rotateToTarget(Transform rotationTarget, Vector3 pos, float rotSpeed, float offset)`, called from
  `UpdateExec`, `StartAttack`, `checkPlayerTooClose`, `quickRotateTarget`/`quickRotatePath`.
- `FindRandomNavMeshPosition(Transform target, float searchRadius, float clearDistance, bool dontMoveCloser)`
  callers: `AL_meleeCheck`, `Navigation` (hunt), `PatrolPointNext`, `findEvadePosition`, `findFlankPosition`,
  `pickEscapePoint`. `markLastKnownTargetPosition` is called only from `AL_targetCheck` (seen branch + unseen branch).
- `spawnPointValidation(sp)` writes no fields → safe to call from a mod. `getSpawnPoint` has one caller (`spawnNPC`).
- `awarenessTooCloseRadius` (0x334) has no ctor default (prefab value, unknown).

## ★ Enemy Awareness Fix 0.1.0 (Sep 25 2026): deployed, untested

Problem: stealth is nonexistent. The bow can't be used stealthily, and enemies that lose the player can't
be flanked. Chosen design: freeze-after-grace + search, rough guess for never-seen wave enemies, randomised spawn
points (+ optional raised min distance), and path spreading "if it isn't hard" (it falls out of per-enemy
guesses + scattered shared sightings). Also noted: **story mode likely has the same always-knows scripting,
and enemies there must still find you eventually** → the anti-stall rules below.

Source [EnemyAwarenessFix/](EnemyAwarenessFix/EnemyAwarenessFix.cs),
`<game>\Mods\EnemyAwarenessFix.dll`, config `[EnemyAwarenessFix]` (`DebugLog = true` pre-seeded). Design:
- **Per-enemy belief**, updated every 0.1 s from `encounterSystem.allEnemies` (only `isEnemy`, chasing = attack
  with `attackTarget == Playertarget` or hunt with `huntTarget == Playertarget`). Sees you (`targetInSight`) or
  you're within `SenseDistance` 3 m → live, as vanilla. Lost sight → keeps following for
  `TrackAfterLosingSight` 1.5 s, then **frozen**.
- **Never saw you** → rough guess `GuessMin` 3 – `GuessRadius` 8 m from you (NavMesh.SamplePosition).
- **Shared sightings:** an enemy that sees you tells enemies within `ShareRadius` 25 m of it (0 = all, −1 = off),
  scattered `ShareScatter` 2.5 m, accepted at most every `ShareInterval` 3 s.
- **Search:** reaching the belief (≤ 2 m flat) → random points around it (`SearchRadius` 6 m, grows to ×2), dwell
  1–2.5 s each, 15 s stuck timeout. After `SearchSeconds` 20 s: wave enemies (`canLoseTarget` off) get a new
  rough guess (anti-stall); story enemies may give up the game's way (LooseTarget → hunt → idle). If that give-up
  bounces straight back into attack, they get a new guess too (no loop).
- **Hooks:** `markLastKnownTargetPosition` prefix (skip the write and set `lastKnownPosition` = belief);
  `LooseTarget` prefix (blocked until the mod's search is done); `FindRandomNavMeshPosition` and `rotateToTarget`
  prefixes re-call the method with a per-enemy "ghost" Transform at the belief when the target is the player
  (no Harmony `ref` on object params needed; the recursive call passes because ghost ≠ player).
- **Spawns:** `getSpawnPoint` postfix: Closest-order spawners pick randomly among `RandomSpawnAmongNearest` 3
  nearest valid points (validated with the game's own `spawnPointValidation`). Prefix raises
  `minSpawnDistanceToPlayer` to `MinSpawnDistance` 10 m (stored squared) on spawners that check it.
- **Test with Enemy Awareness Log also installed:** its chase verdicts should flip from LIVE-TRACKED to "searched
  where it lost you", and the spawn histogram should spread out.
### 0.1.0 test (Sep 25 2026, `Contract_03_Warehouse_23`, ENEMY-COUNT mode, 5 min, walked 1.5 km)

- **Enemy-count mode ≠ waves:** one `NPC_Spawner`, behaviour **Once, order Random**, 51 points, all enemies
  spawned at start idle, `canLoseTarget` True, `attackOnSight` True, min spawn distance already 10 m. The spawn
  fix correctly did nothing (it only touches Closest order).
- **Awareness fix works:** logger verdicts **searched-where-lost-you 32, live-tracked 5, partly 4** (vanilla waves:
  0 / 27). The 5 are short (1.5–6 s) chases explained by the 1.5 s grace and shared sightings.
- **⚠ CORRECTION (later same day): the E109 evidence below is INVALID** — "first saw you from its spawn point" is
  what EVERY enemy logs, because the logger read the static root transform (see the wave-run section). The
  unreachable-guess mechanism is still plausible and the 0.1.1 guards stay, but it was never proven; the 0.1.2 body
  position fix is the likelier cure for "never-seen enemies stay in place" (sense/arrival were measured from spawn).
- **Bug (reported: enemies that never saw the player sometimes stay in place):** e.g. E109 alerted at t=318.8 and
  "first saw you" 78 s later **from its own spawn point coordinates**. Cause: `NavMesh.SamplePosition` put the rough
  guess on navmesh the enemy can't reach (multi-level warehouse: shelves, upper floor at y=6) → agent never
  moves → "arrived" never fires → search never starts → and the mod blocks `LooseTarget` until the search is done
  → stands forever. The fix log had **zero** "reached … searching" lines.
- Sharing is busy: ~110 "told where you were 0.0–0.1 s ago" events in 5 min on a 51-enemy map. Watch whether
  that still feels like "they always know"; knobs `ShareRadius` / `ShareInterval`.
- Logger 0.1.0 threw "Collection was modified" (Respawn inside the Tracks loop) during ~40 samples.

**0.1.1 (deployed, untested):** guess / shared / search points must have a **complete NavMesh path** from the
enemy (`NavMesh.CalculatePath`, agent's `areaMask`); guesses retry closer to you, then fall back to your area.
"Arrived" = within `agent.stoppingDistance + 1` (min 2 m) **or stood still 3 s** ("stopped short of"), and the
search starts after **30 s at the latest** ("couldn't reach"), so `LooseTarget` is never held forever. Debug line
now says which, with distances. **Logger 0.1.1:** loop fixed; each chase line adds `it walked N m, stood still N s`
and flags `(STUCK?)` when it stood > 80% of the chase.
- Open: blind fire (`fireWeaponExec` reads `lastKnownPosition`) now aims at the belief — probably fine.

### ★★ Warehouse WAVE run (Sep 25 2026) exposed 2 bugs in both mods

- **Warehouse wave spawner:** `Wave`, **order Random** (not Closest), 33 points, min 6 m, radius 32 m,
  `dynamic` True, `startAttackingPlayer` True, `canLoseTarget` False. So wave spawners differ per map.
- **★ `ANBBasicNPC.transform` is a STATIC ROOT that stays at the spawn point.** Every "FIRST SAW YOU from" in
  every log (Restaurant included) was the spawn point's exact coordinates, every chase "walked 0 m", and the fix
  logged "stopped short of its guess (36.6 m off)". **The moving body is `agentTransform` (0x868, the
  NavMeshAgent's object); the game measures player distance from `visionBase` (0x750, the eyes)** in
  `checkPlayerDistance` / `AL_targetCheck`. Both mods now use `Body(n)` = agentTransform ?? visionBase ?? transform.
  In the fix this had broken `SenseDistance` and arrival (→ the "stood still 3 s" rule fired at once); navigation
  itself was unaffected (the game's own). **Earlier logger first-sight / "walking" / distance lines are invalid.**
- **★ Dynamic spawners never call `getSpawnPoint`:** `<spawnCheck>d__226::MoveNext` inlines the same logic
  (`SortByDistance` when order == Closest, walk from `spawnPointCurrent`, `spawnPointValidation`) and calls
  `spawnNPC(overrideSP, spawnSize)`; `spawnNPC` only calls `getSpawnPoint` when `overrideSP` is null. So spawn
  randomisation + min distance **never applied** in 0.1.0/0.1.1.
- **Fix 0.1.2 / Logger 0.2.2 (deployed, untested):** fix hooks `spawnNPC` (prefix re-issues the call with the random
  pick, `[ThreadStatic]` re-entry guard, `ref bool __result`) and `spawnPointValidation` (min distance, both paths).
  Logger spawn lines add `3D N m, min allowed N m`, de-duplicate the re-issued spawn (same NPC + same frame), and
  use body positions everywhere.

### Restaurant enemy-count run with Fix 0.1.2 / Logger 0.2.2 (Sep 25 2026)

- Restaurant in enemy-count mode: `NPC_Spawner` **Once, Random**, 54 points, min 8 m (raised to 10 after the
  first validations: **min-distance fix CONFIRMED**), all spawned at start idle.
- **Body-position fix confirmed:** first sightings now at real spots, chases show enemies walking 40–106 m.
  Conclusive chases: **searched 12, live-tracked 1**.
- **New problem: enemy E53 kicked door `DoorModular (3)_1` every ~2 s for 50 s (t=170–220)** — ~2,000 `openDoor`
  calls, a `QuickAlertToPlayer` each time — stuck at a door. E57 stood still 87 of 99 s (mostly vanilla hunt).
  Fix log shows e.g. `E20 stopped short of the shared sighting (30.6 m off)` at the same times — probably the same
  enemy, but the two mods numbered enemies independently, so unprovable. **Unknown whether vanilla or caused by the
  fix sending it to a point behind a door it can't get through.**
- 88 "told where you were" shares in ~2 min.

- **Enemies stuck kicking doors also happens without the mods (vanilla).** Don't chase it as a fix bug
  unless a STUCK? report shows the enemy's destination is a mod belief point behind that door.

### Restaurant WAVE run, 5 waves (Fix 0.1.2 / Logger 0.2.2, Sep 25 2026) — "overall it's getting better"

- This Restaurant wave spawner: **Random** order, 34 points, min 6 m (→10), radius 35 m, dynamic — NOT the Closest /
  24-point spawner of the first Restaurant wave run. The map has more than one wave setup; **the Closest-order
  spawn randomisation is still untested in play.**
- Chases: searched 20, live-tracked 2 (vanilla first run: 0 / 27). Fix log: "reached" 21 vs "stopped short" 2.
- Spawns: 8 distinct points per wave, none repeated; nearest spawn 10.7 m (min distance works).
- Stuck: 1 of 65 chases (2.8 s, then died). Door repeats ≤ ~190 opens per enemy (vs 2,000 before).
- Bow + silenced pistol alerted nobody; enemy door kicks newly alerted 1 enemy in 5 waves.
- **Sharing: 86 shares in ~5 min. `ShareRadius` stays 25 for now; revisit if sharing feels too strong.**
- **Committed + pushed Sep 25 2026: `3bd659b` "Enemy Awareness Fix 0.1.3 and Enemy Awareness Log 0.3.0"** on `main`
  (no tags; not released). Deployed DLLs = those versions.

**Logger 0.3.0 + Fix 0.1.3 (deployed after the game closed):**
both print `#<Unity instance id % 100000>` in every enemy tag, so lines match across mods. Logger adds
`STUCK? E53 #1234 hasn't moved for N s (attack, in cover) | agent on, stopped, hasPath, path PathPartial,
remaining, stopping distance, destination N m from it / N m from you | its last-known position N m from you | has
opened door 'X' Nx in the last N s` (after 8 s still, repeats every 20 s); door and door-kick-alert lines are
rate-limited to one per door+opener per 10 s; open chases are flushed at scene end / quit.

## ★★★ STATUS Sep 26 2026 (end of day): Physical Dodge + Enemy Awareness — release candidates, missions still untested

Tester: a successful session with the dodge, and enemy awareness "works pretty great already". Ready for a public
release once it has also been tested in the missions. All on `dev`, not released.

| Mod | Version | State |
|---|---|---|
| **Physical Dodge** | 0.4.2 (`d1cca53`) | Deployed after the last session (hash-checked); 0.4.1 is what was last played. |
| **Enemy Awareness Fix** | 0.1.6 (`bb7b37e`) | Deployed, ran in the final session. "Works pretty great." **Sep 27 2026: Evhenii played without it (DLL parked in the game root) and vanilla enemy behaviour is "BAD", so the fix clearly matters.** |
| **Enemy Awareness Log** | 0.5.1 (`d1cca53`) | Deployed; 0.5.0 was last played. Dev tool, not for release. |

**Before a public release:** test both in **story missions / contracts** (only the Outpost and Restaurant challenge maps
have been played with them). Especially: do story enemies still find you eventually (the anti-stall rules), and do the
bomb waves now bring enemies to the bomb (Fix 0.1.5 ShareMaxAge, only seen working in wave 1 so far).

**How Physical Dodge works now (0.2.0 → 0.4.2):** no trigger move and no dodge window. Every enemy bullet aimed at you is
re-aimed at where your head was 0.2 s earlier (`AimLagSeconds`), counting only your **real body movement** (stick
movement off by default). The game's spread still applies. A shot that would have hit and now misses is
a DODGE: it's pushed out to clear your body by 10 cm, buzzes the controllers, and starts the game's own slow motion for 1 s
(strength 2 = the last-enemy one, 1.5 s cooldown). **In practice a dodge needs ~0.2 m+ of head movement at the moment
they fire (~1 m/s).** Standing up out of a crouch can still get you hit: your body rises into where your head was.

**Final session (Outpost wave 1, died in the wave; logs `MelonLoader\Logs\26-9-26_4-18-6.log` + `session-20260926-041812.log`):**
- Every change behaved: one `==== wave 1 ====` header, free misses not logged, **4 DODGED and none of them hurt** (the
  10 cm margin works), **all 4 got slow motion** (1.5 s cooldown). MelonLoader log **21 KB** (was 333 KB), awareness
  file 115 lines / 14 KB.
- Fix: every shared sighting was ≤ 1 s old; new spawns got guesses 4–5 m from the player. ShareMaxAge works.
- **Bug found → 0.4.2:** hip hits at 0.45 / 0.37 m of movement while the player rushed #15922 (4.3 → 2.3 m). The rush
  conversion took its direction from the across-the-line part = mostly head bob, so ~half the rushes pushed the shot
  DOWN into the body. Now always horizontal.
- Log 0.5.1: no `STUCK?` for enemies > 50 m away (after the player's death, 8 lines about two enemies 150 m away).
- The tester still "didn't get the feeling" consistently: some dodges land, some don't. Next test with 0.4.2; if it's still
  unclear, consider raising `AimLagSeconds` 0.2 → 0.25 (a dodge then needs less movement).

**0.4.2 test (last run of the day, Outpost waves 1-3 + a restart): no issues, no fix needed.** 11 dodges, none hurt,
all 11 with slow motion; no more hip hits from rushing. 16 hits, every one at <= 0.16 m of movement. The final
four hits at 1.0-1.4 m logged `moved 0.00` = head held still while fighting at point blank (stick movement doesn't
count by design; Better Bow also warned the left hand was stuck 65 cm from its controller then). Fix: 62 shared
sightings, 0 older than 5 s; the `STUCK?` reports were enemies with the NavMesh agent off = knocked down (Heavy Melee
"getting up"). Awareness file 280 lines / 28 KB for ~5 waves.

**With/without-Fix comparison (Sep 27 2026):** baseline numbers of the Fix sessions are in [EnemyAwarenessLog/BASELINE_WITH_FIX.md](EnemyAwarenessLog/BASELINE_WITH_FIX.md), made by `awareness_stats.py` (same folder), which measures any session the same way. The no-Fix run to compare against: `session-20260927-171428.log` (Log 0.5.1, Outpost, 5.7 min). **Result:** vanilla live-tracked 26 : 0 conclusive chases, the Fix 2 : 18, so the Fix does its job; the no-Fix run's lower damage taken comes from more hit-grace windows, not from awareness (details in that file).

**Open items (parked):**
- **Door loops** (one enemy kicking the same door 20–80x in 10 s): game behaviour, worse during Fix searches. Every kick
  alerts every enemy to you. Fix idea: rate-limit `openDoor` per enemy+door.
  - **⚠ Log 0.5.0–0.5.1 can't see door loops (found Sep 27 2026).** 0.4.x logged every `openDoor`; 0.5.x logs only the
    kick alert (`QuickAlertToPlayer`), which the game fires only now and then. Loops showed only as a `has opened door
    … Nx` note on a STUCK? line: Fix 0.1.6 still had one (E9, 218 opens in 9 s, `041812`). So "no loops since 0.1.6"
    was the logger, not the Fix. No-Fix runs (`171428`, `174137`, ~17 min): no door note, but the metric was blind.
    3 of the 6 door notes on record came while the player was dead / 150 m away, so some loops may be the post-death state.
  - **Log 0.5.2 (built, not yet deployed):** `DOOR LOOP: E# opened '<door>' 10x in N s` once per loop, and the wave
    summary adds `door loops N (longest M opens)`; `awareness_stats.py` reads it. Next: one no-Fix and one Fix
    session, bow only, similar length.
- **Enemy gunfire exposes YOU** (`FireBullet`: the `Expose()` branch runs for every enemy shot). Probable game bug; a
  fix would skip `Expose` when `FromEnemy != 0`.
- **"Invisible shooter"**: seen once (0.2.0 test), not reproduced since; the per-shot `gun N` (gun-to-body distance)
  and `in view / OUT of view` fields in the dodge log are there to catch it.
- **Hardcore "no free misses"**: see the section below.

## ★★ Hardcore mod idea: what makes enemy shots harmless on Hard (checked in game code Sep 27 2026)

47–80% of enemy shots on Hard are harmless (the bullet uses `HitLayerMaskEnemyCooldowned` and flies through you).
`ANBHVRGunBase.FireBulletNew` makes a bullet harmless when `InCooldown || nonLethalFire || !weaponFiredOnce`.
**On Hard only two rules actually produce harmless shots:**

1. **Warm-up second:** every enemy's first ~1 s of fire (`setWeaponFiredOnce` waits 1.0 s before setting
   `weaponFiredOnce`). It re-arms after you and that enemy haven't seen each other for `resetFirstShotTime`
   (Easy/Normal/Hard 5 / 8 / 15 s). Enemies fire about every 0.8 s, so this is 1–2 shots per enemy per engagement.
2. **Hit grace:** each hit, stab or grab you land runs `ANBEncounterSystem.startEnemyCooldown`, and then **every**
   enemy's shots are harmless for `EnemyCooldownTime` 2 s. Each enemy can re-trigger it every 4 s. The game has its own on/off field, `ANBEncounterSystem.useEnemyCooldown`.

- **The "warning shot" (`checkMissShot` → `nonLethalFire`) adds nothing on Hard.** Difficulty numbers: 1 Easy, 2 Normal,
  3 Hard. On Hard it fires only when the enemy is out of your view, and it can only return true while
  `!weaponFiredOnce` or `InCooldown`, which are already harmless. Easy/Normal add more warning shots; ignore them.
- **So the mod = two switches:** force `weaponFiredOnce` true (or skip the 1 s wait), and skip `startEnemyCooldown`.
  Physical Dodge already reads the same three flags, so it's the natural host, or a separate "Hardcore" mod.
- **Expected size:** about 1 harmless shot per spawned enemy on Outpost Hard (43 of 41 spawns in both the no-Fix and
  the Fix runs). Removing both rules roughly doubles to quintuples the real shots at you (1.9–6.1/min now), so
  Physical Dodge is what makes it playable.
- **First step before building:** have the logger split harmless shots by cause (warm-up / grace / warning); today
  it only counts the total. Details and addresses: `IDEAS.md` → "Hardcore: no free misses".

**Repo rules:**
- **One checkout**, normally on `dev`; `main` only for releases. No worktrees.
- **Lean logs:** one short line per real event; non-events counted in summaries; context (wave, scene) as a header line
  once; the awareness logger writes its detail only to its own file.

## ★ Physical Dodge (Sep 26 2026): 0.1.0 tested (didn't feel like it worked); 0.2.0 on `dev` (`acbd3b2`), deployed, untested

Request: "crouch or move triggers a very small dodge window that lets you close in on enemies and dodge
their bullets." Source [PhysicalDodge/](PhysicalDodge/PhysicalDodge.cs),
`<game>\Mods\PhysicalDodge.dll`, config `[PhysicalDodge]` (`DebugLog = true` pre-set for the first test).

**How enemy bullets reach you (from `GameAssembly.dll`):**
- `ANBHVRGunBase.EnemyTriggerPulled(source, dir, spread)` only **stores** `tmpEnemyBulletSource` (0x3d8) /
  `tmpEnemyBulletdirection` (0x3e0) / `tmpEnemyBulletspread` (0x3ec), then calls the gun's trigger.
- **Every bullet goes through `FireBulletNew`**, whose enemy branch (`EnemyGun` 0x3f0) re-reads them:
  `dir = ApplyRandomAngle(tmpDir, enemyBulletSpreadAddition (0x414) + tmpSpread)`, or
  `ApplyRandomAngle(tmpDir, ShotRadius (0x21c))` per pellet when `isShotgun` (0x20c). Then `FireBullet` from
  `tmpEnemyBulletSource`. → one prefix sees every bullet of a burst separately.
- `ANBGameLogic.ApplyRandomAngle(dir, max)` = `AngleAxis(Random.Range(-max, max)°, onUnitSphere) * dir`.
- The harmless flag passed to `FireBullet` = `UsedByNPC.InCooldown || nonLethalFire || !weaponFiredOnce`
  (confirms IDEAS.md).
- **★ The ONLY bullet damage path: `BulletImpact` → `HurtPlayer`, and only when the hit collider's transform ==
  `PlayerHitTarget` (0x13b0), `PlayerHitTargetLegs` (0x13b8) or `PlayerHitTargetHips` (0x13c0).**
  `HurtPlayer`'s other callers are punches, fall pain, pain triggers and a force coroutine. So a line that
  clears those three colliders really misses.

**0.1.0 (dodge windows) test, Contract_01_Outpost + range, Sep 26 2026. Result: "didn't feel that it worked".**
- **The mechanism worked:** 7 shots were re-aimed inside dodge windows, and **0 hits landed during a dodge**.
- **The coverage was the problem:** 28 shots came at the player and only 7 fell in a window; **9 of the 21 outside one hit**, several
  "in the cooldown (0.00 s left)". The enemy fired about every 0.8 s, and a window covered 0.3 s followed by a 0.5 s lockout.
- **Steady movement never counted.** The trigger was a *change* in velocity, so strafing after the first burst gave nothing.
- **Ordinary movement used up the windows:** 16–18 dodges per 30 s on the range with no enemies at all.
- **No feedback, and the game's free misses mask it:** 4 of the 7 re-aimed shots were harmless anyway.
- **★ Hit colliders (logged):** `PlayerHitTarget` = **a 0.30 m SphereCollider exactly at the head** (centre 0.00 m below the
  camera, so it follows a duck), hips = 0.39×0.40 CapsuleCollider 0.45 m below the head, legs = 0.39 m sphere 1.35 m below.
  The "chest" target is really the head.

**0.2.0 = "enemies react late" (chosen over "fix the windows"):** no windows, no cooldown. Every enemy bullet whose
line passes within 1 m of you is aimed at the game's own aim point moved back by your head movement over the last
`AimLagSeconds` (0.2 s). The game's spread then applies normally, so a miss is honest: move too little and you still get
hit. `RushDodges` (on): movement straight at or away from the shooter throws the aim off by the same distance, sideways,
so closing in works too (off = only sideways/vertical movement, as in real life). `StickMovementDodges` (on).
- A `FireBullet` prefix reads the final direction (spread included) and the harmless flag, and applies the same spread
  rotation to the game's original aim, so each shot is classified exactly: **DODGED - would have hit your head/hips/legs**,
  hits, harmless anyway, or missed anyway. `Haptics` (on): a short buzz on both controllers
  (`HVRInputManager.Instance.Left/RightController.Vibrate`) on a real dodge.
- **Log to read:** one line per shot at you (`shot from N m: DODGED … ; you moved X m in 0.2 s, aim thrown Y m off you`),
  `you were hurt: … (last shot: you had moved X m)`, and a 30 s summary.
- **Tuning:** stick walking alone may make you nearly unhittable at 0.2 s. If so, lower `AimLagSeconds`, or turn
  `StickMovementDodges` off. Too hard → raise `AimLagSeconds`.
- Still parked in IDEAS.md: turning off the game's free misses, which would make dodging matter more.

### ★ Physical Dodge 0.2.0 test (Sep 26 2026, Outpost wave, body movement only): "felt great… more forgiving, more epic"

Logs `MelonLoader\Logs\26-9-26_3-25-40.log` + `EnemyAwarenessLog\session-20260926-032546.log`.
- **26 enemy shots at the player: 5 DODGED, 5 hit, 14 harmless anyway (the game's free misses), 2 missed anyway.** Matches the
  logger's "enemy gunshots 26 (harmless 14)" exactly. So half of the shots that would have hurt were dodged.
- The clear dodges came from 0.20–0.39 m of movement. Several came from only 0.05–0.15 m, i.e. partly luck with the spread
  (the same spread on the game's own line would have hit, but only just).
- **The death:** both killing hits (t 373.5 and 374.3, 2.6 and 3.2 m, head) landed while the player was crouched and still (moved
  0.02 / 0.11 m). `HurtPlayer` subtracts, and `checkHealth` → `PlayerDeadCall` runs inside it, so death is on the last hit; the
  scene change 23 s later is the death sequence (the stance lines at 381–382 with "moving 1.00" are the death camera).
- **Only E14 was alive**, so it was the shooter. **"He disappeared / was invisible" has a trace:** the logger put E14's
  tracked position (its NavMesh agent) **0.6–0.7 m from the player, not moving for 8 s** (`STUCK?` at 375.2, hunt, arrived), while
  the shots came from a muzzle **2.6–4.9 m** away. The tracked position and the gun didn't match. Unexplained; a logger line
  per enemy shot (shooter, muzzle vs agent position, renderer visible) would settle it.
- **The pistol "call" chain fits the timeline:** E14 spent 65 s searching Fix guesses 6–7 m from the player (two "gave up → new
  guess"), kicking doors (22 and 40 kicks in 10 s at (4.8, 0.1, 5.9) and (10.6, 0.1, −4.7)). The player fired the pistol at 349–351
  and 358.6–359.0 s; **E14 first saw the player at 361.9 s, from 10.7 m, inside the 3 s gunfire-exposure window** of the last
  shot. No hearing involved (no alert reached anyone).

### ★ 0.3.0 test (two Outpost runs, 7 waves) → Physical Dodge 0.4.0 + Enemy Awareness Fix 0.1.5 (`6350c58`, deployed, untested)

Reported: slow motion is "pretty cool", but sometimes triggers after the player was already shot and "doesn't trigger
consistently"; unclear what movement counts. **And the bomb ("rook") waves: enemies don't come to the bomb unless the player
makes noise.** Log `MelonLoader\Logs\26-9-26_3-44-41.log`.
- **Dodges: 16.** Every dodge had 0.16–0.60 m of head movement in the 0.2 s before the shot; every hit had ≤ 0.15 m.
  So the rule in practice: **be moving ~1 m/s (head) at the moment they fire.** The end-of-wave-1 duel vs #86364:
  **6 of 7 shots dodged in 4 s**, but only 2 slow motions (the 3 s cooldown ate the rest) — why it felt inconsistent.
- **"Triggered when already shot" was real: 2 "DODGED" bullets still hit** (hurt 30 ms / 130 ms later, no other shot in
  between). The re-aimed line cleared the head sphere by millimetres; with the head still moving, the bullet clipped
  it. (The 130 ms one flew during the slow motion it had just started.)
- **Rook waves (Fix bug):** the Fix keeps ONE global last sighting with no age limit. **At the start of every wave the
  first 2–3 spawns were "told where you were 14–64 s ago"** (16 of 115 shares) = where the player had been at the end of the last
  wave. In the bomb waves the player had gone down to the basement; they went upstairs, searched 20 s, gave up, and only then got
  a rough guess near the player. `ANBGameLogic.DeathByRook` is called from `ANBChallengeTriggerbox.Update/timeOver` (the bomb
  timer kills you).
- **Fix 0.1.5:** `ShareMaxAge` 5 s → a stale sighting isn't shared, so new spawns get the normal rough guess 3–8 m from
  the player (= at the bomb).
- **Physical Dodge 0.4.0:** all classification in the FireBulletNew prefix. It draws the game's spread itself
  (`ApplyRandomAngle`), applies the same scatter to the game's aim and the lagged aim, zeroes the game's spread for that
  bullet. Shots the game already made harmless (`UsedByNPC.InCooldown || nonLethalFire || !weaponFiredOnce`, the exact
  FireBulletNew expression) are left alone. **A dodge is pushed out to clear all three colliders by `DodgeMargin` 0.1 m
  (9 rays), so it can no longer clip.** `SlowMotionCooldown` 1.5 s (test cfg edited too). Wave = the last spawner's
  `waveSurvived` + 1 (`spawnNPC` postfix). **0.4.1 (`1fd62f5`, lean logs):** one
  `==== wave N ====` line per wave (no per-line tag), a per-wave summary, and one short line only per DODGED / HIT shot:
  `DODGED (head) -> slow-mo, 4.5 m, moved 0.31 | #99136 attack, sees, in view, body 5.2, gun 0.6`. Free misses and
  spread misses are only counted in the summary.

### Enemy Awareness Log 0.5.0 / Fix 0.1.6 (`bb7b37e`): lean logs

- **The logger used to copy every line into MelonLoader's log too** (241 of 333 KB). Now: detail only in
  `UserData\EnemyAwarenessLog\session-*.log`; the MelonLoader log gets just scene + wave headers and warnings, which
  is enough to line up Physical Dodge / Fix / Better Bow lines with waves. **Read the session file for awareness.**
- Counted in the wave summary instead of logged: alerts nobody reacted to, arrows, stance changes (the aim-follows-
  head question is settled), enemy callouts, hit-grace windows, door opens, unclear unseen chases (< 3 m moved,
  not stuck). A door kick is ONE rate-limited line; a loop shows as its `(+N similar in the last 10 s)`.
- Replayed on the 3-44-41 session: 1218 lines / 191 KB -> about 500 lines / 67 KB.

### Physical Dodge 0.3.0 (Sep 26 2026): slow motion on a dodge + shooter details; `d5598cc`, deployed, untested

Haptics barely come through rubber controller grips, so a real dodge now also starts a brief slow motion.
- **The game's own timed slow motion:** `ANBGameLogic.scriptedSlowmotionMin/VeryMed/Max(time)` = step 1/2/3 into
  `scriptedSlowmotionExecute(time, step, nrt)`. The coroutine sets `scriptedSlowmotionActive` (0xd51), saves the current
  step `Slowmotion` (0xcfc), calls `toggleSlowMotion(step, scripted=true)` (time scale, pitch, post effect), waits
  `WaitForSecondsRealtime(time)` (the `NRT` variants use scaled time), then restores the saved step (or 0 unless
  `useSavedSlowmotionAfterScripted`). The wrappers do nothing while one is running. The last-enemy slow motion
  (`scriptedSlowmotionAllDead`) is step 2, ×2.5 time when `isPromoRecording`.
- Settings: `SlowMotion` (on), `SlowMotionSeconds` 1.0, `SlowMotionStrength` 2, `SlowMotionCooldown` 3 s. Skipped
  while the player's own slow motion (right B) is on, since the restore would end it.
- Each shot line now ends with `| shooter #id (attack/hunt, sees you?, in your view?), body N m from you, gun N m from
  its body`. The #id is the same as in Enemy Awareness Log. That's what should explain the "invisible shooter".

## ★ Fix 0.1.4 / Log 0.4.0 run (Sep 26 2026, Outpost waves, same session as the dodge 0.1.0 test): "pretty amazing"

Tester: enemies really search now, and sometimes the player has to search for them; they react to pistol
shots and come to the player. Logs:
`UserData\EnemyAwarenessLog\session-20260926-023017.log` + `MelonLoader\Logs\26-9-26_2-30-11.log`.
- **Searching works:** unseen chases searched-where-lost 12, live-tracked **0**, unclear 13 (two waves). Fix: 34
  shared sightings, 13 "reached … searching", 3 give-ups bounced back by the game → new rough guess.
- **Floor handling (0.1.4) checks out:** every arrival was on the search point's floor. The 6 "(it is a floor above
  you)" arrivals were at shared sightings where the player HAD been (the player had gone down to the basement, y ≈ −2.9); they
  searched 20 s and got a new guess 3–7 m from the player. No floor stalls. The 2 `STUCK?` reports (E6, E16) were on the player's
  floor with **agent OFF** right after the player's hits → most likely down/stunned (Heavy Melee / Knee Shot Stun), not awareness.
- **Crouch:** `PlayerHitTarget` height = head height in every stance line → **enemies' aim point follows a crouch**
  (answers the IDEAS.md question; the Physical Dodge collider log agrees). The Outpost HAS the visibility system:
  height 0.6–1.6 m (weight 0.35), movement ≤3 m/s (0.35), light, visibility 0.15–1, gunfire exposure 3 s.
  Crouched 0.28 vs standing 0.56 when still and unlit; in fights it sat at ~1.0 because of gunfire exposure.
- **Free misses:** 30 enemy shots, **17 marked harmless by the game**; "YOU HIT AN ENEMY → 2 s harmless" fired 12+ times.
- **"Still alive" list never printed:** no one was alive at either summary, so the feature is still unproven.
- **Door loops still happen, and are NOT only vanilla-in-far-searches:** E21 kicked `DoorModular` at (−2.8, 0.1, −6.9)
  **51 times in 10 s** during its Fix search (400–421 s). In the 00:38 run the worst loops (E221 ×82, E212 ×58,
  E233 ×37 in 10 s) happened while the enemy was **within ~3 m of the player** (belief ≈ the live position). Common factor:
  a door between the enemy and a close destination. Every kick also runs `QuickAlertToPlayer`.
- **★★ Why pistol shots seem to "call" enemies — it's not hearing.** The logger saw **every** player gunshot alert
  make nobody react (wave enemies are already in attack, so there is nothing to "newly alert"). What does change:
  **`ANBGameLogic.FireBullet`: `if (isSilenced && FromEnemy == 0) MakeNoise("silenced shot") else { GunFireAlert(); playerDetector.Expose(); }`**
  (only caller of `Expose`). An unsilenced shot sets your visibility to full for `exposeTime` 3 s → enemies with a
  line of sight notice you faster and from further → Fix 0.1.4 shares that sighting with everyone within 25 m →
  they converge on you. **Probable chain, not yet proven** (a logger line "after your gunshot, N enemies saw you
  within 3 s" would settle it). The silenced pistol skips `Expose` entirely (7 m noise only).
  - **★ Game bug candidate: the same `else` runs for EVERY enemy shot** (FromEnemy ≠ 0) → enemies firing at you
    expose YOU for 3 s. Matches the log: gunfire exposure 0.87–0.99 at t≈253 with no player gunshot yet.

## ★ Sound awareness: what makes noise (Sep 25 2026) — the bow release itself is SILENT

Expectation: with a bow, enemies shouldn't be alerted by sound. From `GameAssembly.dll` (xref):
- **Gunshots:** `ANBHVRGunBase.FireBulletNew` → `ANBGameLogic.FireBullet(source, …, float FromEnemy, float NotUsed,
  bool isSilenced, …)` → `MakeNoise("silenced shot")` + **`ANBAlertManagement.GunFireAlert`** → `AlertAllNPCs`.
  `FireBullet`'s only other callers are `ANBBreakable.ShootBullets` (exploding barrels) and a physics coroutine.
  **`HVRPhysicsBow.ShootArrow` never reaches it → a bow shot raises no alert at all.**
- **Indirect noises an arrow can still cause:** `ANBGameLogic.StabWorld(hit, knifeDamage, useDecal, isArrow)` (arrow
  into the world) → `SpawnDecal` → can call `MakeNoise("impact")` (gated by an arg/flags, not decoded);
  `ANBSoundPhysicsBase/ANBSoundPlayer.PlayClipPhysicsHard/Medium/Soft` → `MakeNoise("noise")` (collision sounds,
  `objectDropRadius`); `ANBBreakable.shatterMe` → `MakeNoise` + `AlertAllNPCs` directly.
- **Door kick = loud:** `<kickDoor>d__79` → **`QuickAlertToPlayer`** → `AlertAllNPCs(…)` on everyone. Better Bow's
  arrow door breach goes through `playerKickDoor` → same.
- NPC voice lines (`PlaySound*`), `confirmThreatValidation`, `ScreamAlertAfterTime` also `MakeNoise`.
- **Hearing is synchronous:** `AlertAllNPCs` itself calls `StartAttack` / `StartHunt` on each enemy (plus recursion).
  **⚠ For the fix:** a heard noise whose `target` is the player Transform makes enemies attack/hunt the PLAYER,
  not the noise origin — an arrow thudding into a far wall could point them at you. Check the logger's `HEARD`
  lines (target vs origin) before changing anything.

**Enemy Awareness Log 0.2.0 (deployed, untested):** logs `YOU FIRED '<gun>' (silenced)`, `YOU SHOT AN ARROW`,
`DOOR KICKED/BREACHED`, and for every `AlertAllNPCs` a `HEARD <cause> [target, origin, attack, radius, hear only]
-> E12 attacks 14 m from you; E15 hunts 22 m (was already chasing)` line. The cause comes from whichever hook
raised it (`FireBullet`, `StabWorld` isArrow, `QuickAlertToPlayer`, `shatterMe`, `MakeNoiseExec` type + "after
your bow shot / arrow hitting the world" if within 1.5 s; origin 'NPC' with no cause = enemy callout). Alerts
nobody reacted to are rate-limited `sound, nobody reacted`. Summary adds weapon counts and "enemies newly alerted,
by cause".

### Sound log (Sep 25 2026, Warehouse enemy-count, 193 s: 12 bow shots, 64 silenced pistol shots)

- **Bow is silent, confirmed:** 12 arrows, 7 hit the world → **no "impact" noise from any arrow**. Only side effects:
  2 `scream` noises (12 m, hit enemies) — nobody reacted.
- **Silenced pistol:** `silenced shot` noise radius **7 m**, hear-only; 21 alerts, **0 enemies reacted**.
- **The ONLY sound that woke enemies: door kicks — 9 enemies from 15 alerts, each sending ~3 enemies to HUNT
  (21–37 m away).** `ANBPhysicsDoor.kickDoor(pushSide, kick)` (run by `openDoor(origin, fromenemy, kick)` /
  `closeDoor` / `toggleDoor`) plays `SFXOpenKick` and calls `QuickAlertToPlayer` = `AlertAllNPCs(null, null, …)`
  → `StartHunt(player)`, **regardless of who kicked**.
  - 2 of 15 were on the exact frame of an arrow shot = **Better Bow `ArrowsBreachDoors`** (TryKickDoor on shoot).
  - **~10 had no player shot within 1 s → almost certainly ENEMIES kicking doors open, and the game then tells
    every enemy where YOU are.** Unconfirmed until logger 0.2.1 (hooks `openDoor`, prints `door '<x>' opened by
    enemy E12, KICKED` and names the opener in the alert). Built in `feature/`, not deployed (DLL locked by the
    running game).
  - Door kicks alerting enemies is acceptable as game design (Sep 25 2026).
  - Fix candidates (parked): skip `QuickAlertToPlayer` when an enemy kicked the door (turn it into an investigate of the door
    instead), and make the Awareness Fix treat a door-kick hunt like a noise at the door, not a guess near you.
  `ref Vector3 __result` in the `FindRandomNavMeshPosition` prefix is the one Il2Cpp-Harmony pattern not yet
  proven in this repo.

## Source control: one repo for all the mods (Sep 25 2026)

**This repo = "Gunman Contracts Mods", one git repo for every mod of this game.** One place for
everything, not a repo per mod, and nothing more complicated than that.
- **Layout:** one folder per mod (`BetterBow/`, `KneeShotStun/`, `FireSelector/`), each its own `.csproj`
  with a git-ignored `GameDir.local.props`. `release/<Mod>/` = last released DLL + Nexus text (media and
  zips git-ignored). `il2cpp_tools/` shared. Root `README.md` = the mod index with a status column;
  `BetterBow/README.md` = Better Bow's own page, `BetterBow/QUIVER_PLAN.md` = its dev log.
- **★★ Branching (Sep 25 2026): TWO branches only, `main` and `dev`. NEVER create `feature/*`
  branches.** **`main` = exactly what is released on Nexus.** **`dev` = unreleased work** (Heavy Melee /
  stronger punches, Better Bow 1.2.0 draw power, …). Releases are still marked by per-mod tags.
  - **★ ONE CHECKOUT (Sep 26 2026), normally on `dev`; switch to `main` only to release.** No worktrees
    (a `GunmanContracts-dev/` worktree existed briefly and was removed). **Consequence: only one person or agent
    should work in the checkout at a time**, since a branch switch swaps the files under anyone else. Before switching, commit or
    check `git status` is clean.
  - Merge day (Sep 26 2026): `main` merged into `dev` (`ec5858b`); the then-uncommitted Enemy
    Awareness Fix 0.1.4 / Log 0.4.0 + `IDEAS.md` committed on `dev` (`44de37d`). `main` still = `3bd659b`,
    which carries the unreleased Enemy Awareness 0.1.3/0.3.0 (left as is, like the Sep 25 starting point).
  - Release = test on `dev`, then `git switch main`, `git merge dev` or cherry-pick the mod's commits, tag,
    push, `git switch dev`.
  - Starting point (Sep 25 2026, left as is): `main` = `dev` = `bcebf09`, i.e. both also
    carry the unreleased Heavy Melee 1.0.0 RC and Better Bow 1.2.0. The main-only-released rule applies from
    here on; nothing was moved back.
- Test builds go to the git-ignored `feature/` folder (`dotnet build -c Release -o ../feature`), then get
  copied to `<game>\Mods`.
- **Mod author name** is set in each mod's `MelonInfo`. Check `git config user.email` before committing.
- **GitHub: https://github.com/EvheniiS/GunmanContracts-Mods** (renamed from `GC_BetterBow_mod` Sep 25 2026;
  GitHub redirects the old URL). `main` and all tags pushed
  Sep 25 2026 (`8e50336`).

## ★ Holster loadout only saves at the gun wall or the range elevator — game design, not the mods (Sep 24 2026)

Reported: weapons put in shoulder holsters come back reset after a restart. Did the mods break
saving? **They don't.** From the binary (`xref.py`):
- Holstering only updates in-memory state: `HVRShoulderSocket.OnGrabbed` → `ANBGameLogic.holsterGun`,
  and `OnReleased` → `unholsterGun`. Nothing is written.
- **`ANBGameLogic.SaveContractHolsters` → `ANBSaveData.SaveLoadout` has exactly four callers:**
  `ANBGunwallSpot.grabGunCall` / `placeGunCall` (**taking a gun from or putting one on the gun
  wall**), `<RangeElevatorExitExec>` (**leaving the range through the elevator**), and
  `revertAllWeaponData` (a reset).
- Loaded by `ANBSaveData.LoadLoadout` from the scene loader and from `resetPlayerLoadout`
  (`resetScene`).
- → Holster a weapon and quit **without** touching the gun wall or taking the elevator, and it
  reverts to the last saved loadout. Matches the logs: every session the bow started in
  `'gunstorage'` (a `DemoHolster`), never on `RightShoulder`.
- Neither mod calls any save or load code. The quiver's only holster-adjacent action (removing an
  arrow that ends up in a socket) never fired.
- **To keep a loadout: set the holsters, then use the gun wall or leave via the elevator.**

Sep 29 2026 update: VR Holster Customization 0.1.1 now calls the game's `SaveContractHolsters` one second after a gun or knife is placed in a holster. This is canceled on scene load and is not triggered by unholstering, so the earlier manual save step is no longer required when that mod is installed.

## Tooling — IL2CPP analysis with no Cpp2IL/Il2CppDumper

[il2cpp_tools/](il2cpp_tools/): `xref.py <Class::Method>` finds direct callers (E8/E9; scans the **`il2cpp` PE section, where game code lives**, plus `.text`; 0 hits ≠ never called, since vtable/delegate/coroutine calls are invisible). `il2.py` parses v31
`global-metadata.dat` (type-def stride **88** bytes, method 36, field 12). It finds
CodeRegistration's module table by searching for `u64 imageCount` followed by a pointer
whose first entry names a `.dll`, and MetadataRegistration by searching for
`fieldOffsetsCount == typeDefinitionsSizesCount == typeCount`. Output: method→address,
field offsets and types. `disx.py` = capstone disassembly with call targets named.
**⚠ Identical-code folding: shared stubs get random names** (`FHierarchyIcons::.cctor` =
the delegate ctor). Trust only names on non-trivial bodies.
Quick field-usage search: grep the disassembly for `+ 0x<offset>]`.
- **`slot.py <Class::Method>`** (Sep 25 2026): decodes every metadata-usage slot a method loads →
  **string literals, TypeInfo, and generic instances like `GetComponentInParent<ANBHVRGunBase>`**.
  Encoding (v31): `ty = v >> 29`, `idx = (v & 0x1FFFFFFE) >> 1`, low bit 1; 5 = string, 1/2 = type,
  3 = method, 6 = MethodSpec (resolved through MetadataRegistration's `methodSpecs` / `genericInsts`, found
  just before `fieldOffsetsCount`). **Run it first on any method: the literals and component types usually
  explain the branches before you read a single instruction.** `dumpt.py <Type>` = fields with offsets +
  methods with signatures and addresses.
- **Sep 28 2026:** `slot.py`'s command line never resolved generic methods (its `__main__` ran before the MethodSpec
  decoder was defined): fixed, it now prints `GetComponentInChildren<ANBGunwallSpot>` etc. **`disa.py <Class::Method>`**
  = whole-method disassembly (to the next method start) with calls named and every metadata slot decoded inline.
  **Asset side (UnityPy):** `monoscripts.pkl` (MonoScript pathID → class, from `globalgamemanagers.assets`; rebuild if
  the game updates) classifies MonoBehaviours in IL2CPP scenes (no typetrees); **`prefab_tree.py <file> <pathID>`**
  prints a hierarchy with component classes; **`gunwall_scan.py`** dumps the arsenal walls and spots per level.
  Raw MonoBehaviour layout: GO pptr 12, enabled 4, script pptr 12, name string; **every bool is 4-byte aligned**.

### ★★ What a game update breaks, and the recovery order (Sep 27 2026)

**Historical plan — superseded by the [Oct 1 audit and workflow](Tools/UPDATE_RECOVERY.md).**
The mod count, universal compile-error guarantee, unconditional regeneration assumption and repair-time estimates
below must not be used as current instructions.

**The mods bind to the game by NAME only.** All 7 mods hook through ~40 `[HarmonyPatch(typeof(X), nameof(X.M))]`
targets on the generated interop assemblies. There are no hard-coded offsets, RVAs or byte patterns in any `.cs`
(`IntPtr` is used only as an object-identity key). The only string binding is KneeShotStun's
`Animator.StringToHash("hit_legs_1"/"hit_legs_1_m")`. **So addresses and field offsets moving on a rebuild costs
nothing.** MelonLoader regenerates the interop assemblies when `GameAssembly.dll` changes, and the interop layer
re-resolves names at runtime.

| Breaks | How it shows | Cost |
|---|---|---|
| Method/field/class **renamed or removed**, signature changed | **Compile error** on rebuild, naming the exact member | Minutes |
| Method **reworked** (same name, different logic) | Compiles, mod misbehaves; the mod's own log goes quiet or odd | The real work: re-read with `slot.py` → `disx.py` |
| Small method now **inlined** by IL2CPP (or never called) | Patch installs, **never fires**, no error | `xref.py` for callers; move the patch up a level |
| Animator state renamed | KneeShotStun kneel silently stops | `anim_dump.py` |
| **Unity version bump** | MelonLoader fails to generate or load | Wait for or update MelonLoader (0.7.x) |
| **Obfuscation turned on** (Beebyte etc.) | Everything, all names scrambled | Days. Only real disaster, and unlikely for this dev |

**Recovery order:** launch once (MelonLoader regenerates) → `dotnet build` each mod → fix compile errors → play
one mission per mod reading its log for missing events → only then the Python tools, on whatever misbehaved.
- **Tool fragility is the metadata FORMAT, not the build.** `il2.py`/`slot.py` hard-code v31 strides (typedef 88,
  method 36, field 12, header order). A same-Unity patch keeps v31. A Unity upgrade can bump it → check the
  version u32 at offset 4 of `global-metadata.dat` first.
- **Fixed Sep 27 2026:** `il2map.pkl` used to load unconditionally, so after an update the tools would have
  returned **the old build's addresses**. It is now keyed on `GameAssembly.dll` + metadata size/mtime and rebuilds
  itself (~1 s).

## Billy Clubs model: Blender starter project for GPT (Sep 27 2026)

Evhenii wants GPT (with Blender MCP) to build the real club model. **`BlenderRefs/`** is that project: brief
`BILLY_CLUBS_MODELING.md` (GPT's draft, corrected: +X axis not +Z, OBJ + 4 separate PNGs not FBX, 30-36 mm grip,
one baked material), `AGENTS.md` pointer, `setup_scene.py` → `BillyClubs_start.blend`, and `export_club.py` (checks
length/centre/diameters/triangles/UVs/material/textures, then writes `out/billy_club.obj`; tested on a dummy).
- Game references come from `il2cpp_tools/export_club_refs.py` into `BlenderRefs/game/` (git-ignored, game assets): crowbar,
  shaft collider (596 × 22 × 26 mm), grip point (−157 mm from the centre), the player's right glove
  `righthand_fpsgloveorig_HD` (resources.assets, bind pose), and today's placeholder club.
- **Club space convention** (the future OBJ loader must match): origin = centre of the crowbar's shaft collider; Blender
  +X = towards the metal tip (away from `GrabPoint_Base`), +Z = up; Unity → Blender is `(x, y, z) → (x, z, y)` with faces
  reversed. The OBJ files are Y-up, so Blender's default OBJ import/export axes are used on both sides.
- Not built yet: the mod's runtime OBJ + PNG loader (mesh from OBJ, URP Lit material, metallic + (1 − roughness) packed
  into the metallic/smoothness map).

## ★ Grab Fix 1.1.0 RELEASE CANDIDATE, CONFIRMED (Sep 28 2026): docked-item fix holds up in play

Grab Log (`GrabLog/`, observation only) writes `UserData\GrabLog\session-*.jsonl`; the `grip_cycle` lines
(`GotObject`) and the final `summary` counts give the hit rate per session.
- **Distance grab = a fan of 5 trigger capsules per hand** (`Main/Up/Down/Left/Right`, radius 0.17, height 4.14, along
  hand +Z = the finger direction) in `HVRForceGrabber.GrabBags`. Local pickup = one 0.08 m trigger sphere.
- **Palm forward is ~90° off the finger axis** (left +X / right −X), which is why the game feels finger-aimed.

| Session | Mod | Grip presses | Grabs |
|---|---|---|---|
| 16:00 | vanilla | 69 | 42 (61%) |
| 16:23 | vanilla (club distance grabs mostly) | 17 | 16 (94%) — not comparable |
| 16:56 | Grab Fix 0.1.0, `AimMode=Palm` (fan moved to the palm) | 126 | 48 (38%) — misses in 3–5-retry bursts |

- **Palm-only aim is worse, measured, not just unfamiliar.** The grip buffer and recent-release catch did work
  (7 buffered distance grabs, one 17.9 cm club catch at timeScale 0.22).
- **Grab Fix 0.2.0 (`GrabFix/`):** `GrabDirection = Both` (default) keeps the native finger fan and adds a palm-aimed
  copy of each capsule, appended after the native bags in `GrabBags`. The goal is for a finger target to win, but only if
  the game checks the bags in list order (not verified). `Finger` = native aim only. Palm-only removed. The local sphere is
  re-centred between the native centre and the palm face and grown to cover both (min `NearGrabRadius` 0.12). The copies
  are removed from `GrabBags` and destroyed when the mod is switched off. ~~Grip is a deliberate button, so a wider zone
  can't cause accidental grabs.~~ **WRONG — see below (Sep 28 2026 evening).** A wider zone can't cause an accidental
  grip *press*, but it absolutely widens what a deliberate press next to your belt will catch.

**★★ 1.1.0 RELEASE CANDIDATE — promoted off today's live session, no further Grab Log testing planned.**
Session `26-9-28_19-17-43.log` (19:17–19:33, real play, not a synthetic test): **29 buffered-catch attempts
logged (`DebugLog`), 29 succeeded, 0 `result=False`, 0 `[ERROR]`/exceptions all session.** 19 were the
distance/force-grab path (17 R / 2 L), 10 the local-sphere path (7 R / 3 L). Recovery latency (time from the
grip press the native check missed to the buffered retry) mostly under 100 ms, a few up to 235–277 ms — these
are exactly the grabs that would have read as a whiff without the fix. Felt great in play; no further tuning
idea on the table. **Version bumped 0.2.0 → 1.1.0, `DebugLog` default flipped off** (was `true`, meant for the
"first test build" — RC no longer needs the per-grab log line by default). Deployed to `Mods\GrabFix.dll`.
**Grab Log (`GrabLog/`) retired for now** — its job (measuring session hit rate to compare aim modes) is done;
disabled in-game as `Mods\GrabLog.dll.disabled` rather than deleted, since its 134 MB/session jsonl writes are
pure overhead once Grab Fix isn't being iterated on. Re-enable (rename back to `.dll`) only if a future Grab
Fix change needs the same before/after measurement.

**⚠ RC call held back same evening: he reported the clubs (and holstered weapons generally) are too easy to
grab by accident** — reaching for a floor item with a full belt regularly grabs a holster instead. Root cause
is exactly the geometry the RC evidence praised: the widened near-sphere (`NearGrabRadius` ≥ 0.12 vs the
game's native 0.08) is the *actual* physical trigger collider, mutated in place, not a copy - so it directly
extends what the **game's own native hover/grab** can reach, with no GrabFix retry logic involved at all. A
belt holster sits close enough to the hand's downward reach path that the widened sphere routinely overlaps
it. Separately, `RecentReleaseAssist` (grab-fix's own assist, 15 s / up to 0.18–0.3 m from the palm) doesn't
care that an item was re-holstered after release - `OnRelease` fires on every grip release, including a
gentle one next to a free holster slot that the game (or Daredevil) then docks - so a club sitting in its
holster stayed "recently released and grabbable" for up to 15 s after every holstering.

**Fix: distinguish "docked" from "loose" by `Rigidbody.isKinematic`, not by item type.** Holsters, HVR sockets,
and wall mounts (WeaponFramework arsenal) all park an item by setting `rb.isKinematic = true` while docked
(confirmed in `Daredevil/BillyClubs/BillyClubs.cs` `Holster()`: `rb.isKinematic = true` on dock, `false` on
draw) - this is the standard HVR docking convention, not something Daredevil invented, so the fix is generic
and applies to the game's own gun/knife holsters too, not just clubs. A **lying** or **flying** item keeps
`isKinematic = false` (physics is actually driving it) and is untouched by this change.
- Added `GrabFixMod.Docked(g) => g.Rigidbody.isKinematic` and excluded it from `Eligible()` — this alone stops
  `RecentReleaseAssist` and both buffered retries from ever completing a grab on a docked item.
- Added `Geometry.NativeContains(g)`, which re-tests a candidate against each local sphere's *stored native*
  centre/radius (before `NearGrabRadius` widening), and a `HVRHandGrabber.CanHover` postfix
  ([Patches.cs](Patches.cs)) that clamps a native-true result back to `false` for a docked item that only
  passed because of the widened collider. This is the one that actually stops the accidental native grab —
  `Eligible()` alone doesn't, since the widened sphere is real detector geometry, not a GrabFix-side retry.
  A docked item is still grabbable exactly as before once the hand is genuinely inside the *native* 0.08 m
  radius — "close to the holster itself," per his ask.
- ~~Scoped to the local/near path only, not `HVRForceGrabber.CanHover`... force-grab needs a deliberate
  flick/`GrabStyle` match to actually grab, so accidental capture is far less plausible there.~~ **WRONG,
  disproven same evening — see round 2 below.**

**Round 2 (still Sep 28 2026): reported "still can grab my left club from the right palm on the right
hip" — confirms the force-grab path, not the local sphere.** The local sphere (0.08–0.25 m) physically
cannot reach 0.46 m across the belt (`HolsterLeft`/`HolsterRight` are ±0.23 m either side of centre); only
the distance/force-grab capsules are long enough to span the body. Two ways `HVRForceGrabber`'s own
detectors can catch a docked item across the belt, neither touched by round 1:
- `DistanceGrabWidth` (default 1.2×) widens the **native** capsule radius in place, same mechanism as the
  local sphere.
- In `Both` mode (his saved setting) the **palm-aimed copy** is a wholly separate capsule, repositioned at
  the palm and re-oriented by `turn = FromTo(hand.forward, palm.forward)` — at hip height the wrist often
  isn't square to the arm, so `turn` can swing the copy's whole capsule length across the body far more than
  a plain width multiplier ever could. This is the more likely culprit for "left club from the right palm."
- **Fix:** added `Geometry.NativeContainsFar(g)` — rebuilds the **native** (pre-`DistanceGrabWidth`,
  finger-aimed, never the palm copy) capsule as a world-space line segment from each far detector's stored
  original radius/centre/transform (untouched by widening) and tests the candidate against that segment's
  closest point, same closest-point-then-`SurfaceDistance` approach as the sphere check. Added a
  `HVRForceGrabber.CanHover` postfix ([Patches.cs](Patches.cs)) mirroring the hand one: a docked item stays
  hoverable only if it also passes this native-segment test — which the palm copy, being a different object
  entirely, cannot rescue it into.
- **CONFIRMED in play the same evening — "now it seems to be fixed."** No further docked-item accidental
  grabs reported (neither the local-sphere case from round 1 nor the cross-belt force-grab case from round
  2), loose/flying-item assist and close-range holster draws unaffected. **1.1.0 promoted to release
  candidate**, `DebugLog` reset to `false` (the default) in `UserData\MelonPreferences.cfg` now that the fix
  is verified. Nothing else on the table for this mod right now.

## ★★ Flat-mode audit + Weapon Framework roadmap (Sep 29 2026); framework 0.1.2 save fix built, untested

Full write-ups: [FLAT_MODE.md](FLAT_MODE.md) (every mod vs flat mode) and
[WeaponFramework/ROADMAP.md](WeaponFramework/ROADMAP.md) (removal, JSON weapon packs, holsters in the framework, flat).
- **★★ Weapon Framework 0.1.1's save guard was on the wrong method.** `pickWeapon` calls `SavePurchases` AND
  `SaveWeapons`; only **`SaveWeapons`** writes `"saveSpotLarge"` (`SaveData_WeaponSetups.json`). His save held
  `"saveSpotLarge":6` = the Billy Clubs entry. `WaitForStart` sets `currentSpot = savedSpot` after `autoStart`'s clamp,
  so with the mod removed the panel would index past the list (inferred). **0.1.2 guards `SaveWeapons`** (built to
  `feature/WeaponFramework-0.1.2`). **Installed Sep 29 `26CFD2E2…`** (backup + the `6` save in
  `feature/WeaponFramework-backup-before-0.1.2-*`). **C1 PASS (Sep 29 14:25):** clubs retrieved last → file
  `"saveSpotLarge":1`, no wall errors. First session still started from the old `6` save with the mod present: no errors.
  Remaining release gate: C2-C7 in [TESTING.md](WeaponFramework/TESTING.md). **Decision Sep 29: Weapon Framework is now
  REQUIRED by Daredevil** (soft list in `Daredevil.cs`: the terminal is the only player way to get clubs, F8 is a dev
  key); built, not installed. Most debug logs turned off by him; `[WeaponFramework] DebugLog` stays on.
  **Session 1 (14:41-14:43) PASS: C2** (bow last → `5`), **C3** (clubs, then a pistol attachment change: `SaveWeapons` from gun
  editing logged `saved as 5`, file `5` at 14:43:21), **C4** (wall opened on index 1 = the game index the previous
  session saved in place of the clubs; working as designed). Logging gap found: taking a club off the wall/belt only
  logs with `[BillyClubs] DebugLog`; the game's own wall/holster actions aren't logged by anything.
  **→ Weapon Framework 0.1.3 (`ActionLog.cs`): one line per retrieve (both walls, game + mod), stand take/put-back
  (`ANBGunwallSpot.grabGunCall/placeGunCall`), holster in/out (`holsterGun/unholsterGun/holsterKnife/unholsterKnife`),
  mod item off its mount; loadout restore in the first 6 s after a load folded into one line; 0.5 s repeat filter.
  Logging only, save guard unchanged. Installed Sep 29 `9C932CB4…` (backup `feature/WeaponFramework-backup-before-0.1.3-*`),
  untested (TESTING.md B7).
  **Session 2 (Daredevil off, 14:53-14:55) C5 PASS:** no errors, nothing registered, big wall `#6/6`, save `5`. Action log
  worked (retrieve/stand/holster lines match his actions) but **the wall build at load logged ~30 `stand: put back/took`
  lines** (`initSlot` → `placeGunCall`/`grabGunCall` for every stand, ~40 s into The Range, after the 6 s fold window),
  and knives were named by object in holster lines (`Knife-Combat-type3`) vs `CombatKnife_v3` on the stand.
  **→ 0.1.4:** fold window extended +2 s on every `LogLoadTime` step, stand calls counted into one `after load:` line;
  knives named via `ANBKnife.WPT.WeaponName`. **Installed Sep 29 `D20B4DA7…`.**
  **Session 3 (framework off, 15:03) C6 PASS:** Daredevil 0.3.4 logged `Weapon Framework not installed - no arsenal entry`,
  `restored 2 holstered club(s)`, holstered again, no errors. Side note: at the first scene it warns `no club template yet -
  visit The Range once` (clubs are copied from the Range crowbar), so a game start straight into a contract has no clubs.
  **Session 4 (both on, 15:07-15:11) C7 PASS**, no errors, save `2`. **Gate C complete: 0.1.2 save fix done.** Action log
  (0.1.4): the load fold works (`after load: stands filled (40 put, 12 taken), loadout backLeft AB15, backRight
  CompoundBow, left ShadowGuard2, right Knife-Katana`), `retrieve big #7/7 'BillyClubs' (mod)`, `stand: took 'BillyClubs'
  item 'BillyClub-wall-right' (Physics RightHand)`. **The double pistol lines are real:** he bought the 1911 and took two
  (`took '1911'` → `holster left in` → `took '1911'` → `holster right in`), so the ShadowGuard case was two pistols too.
  Still wrong in 0.1.4: (1) **leaving The Range emptied every stand at once** → 17 `stand: took` lines + 2 holster outs in
  0.05 s; (2) knife holster lines still showed the object name (`Knife-Combat-type3`: `WPT` is empty on holstered knives);
  (3) **knife and hip-gun holsters share side names** (`left`/`right`), so the loadout line mixed them and one knife
  `out` printed `'?'`. **→ 0.1.5** (built, install when the game closes): stand/holster events < 0.2 s apart = a burst,
  ≥ 4 become one `N stand/holster events at once (…) - scene change` line; knife sides keyed `knife-left/right`; knife
  name via `WPT` → `ANBWeaponType` on the knife/parent → `knifeID` → object name. Installed Sep 29 `5FA4458A…`.
  **His feedback:** retrieving the clubs while both are on the belt puts nothing on the wall (by design: the wall only
  fills what you don't carry), and **there is no way to put a club back on the wall** → confirms the socket-system item
  (ROADMAP step 3). Daredevil's own log is still long (`draw check …` lines of ~250 chars, `club ignores 240 player
  colliders` ×2 per spawn): trim it when Daredevil is next touched. No upgrade-path work (no custom weapon was released with 0.1.1); foundation first.
  Test gates: [WeaponFramework/TESTING.md](WeaponFramework/TESTING.md); helper `WeaponFramework/Check-WFSave.ps1`
  (0.1.1 log said `saved as 5` while the file got `6` in the same second).
- **★ Flat guns are the game's HVR guns underneath:** `ANBFPSCore.addGun` wraps them, `ANBFpsWeapons.fire` →
  `ANBHVRGunBase.FPSshoot` → the same bullet/damage code. So **Knee Shot Stun and Enemy Awareness Fix most likely
  already work in flat**, and **Radar Sense** too (flat slow-motion key → `ANBGameLogic.buttonSlowMotion`, same state).
  Physical Dodge's aim lag runs in flat, its dodge detection doesn't (head moves ~0 in the tracking space).
- Flat melee = LPSP "Knife Attack" (`Character.PlayMelee`), flat bow = `BowFPSShootStart/End`, flat pickup =
  `ANBFpsInteraction`.

## ★ Weapon Framework 0.2.0 + Daredevil 0.3.5 (Sep 29 2026): test crowbar + back holsters for mod items; installed, untested

His ask: work on the crowbar test entry and back-holster settings, clubs on the back too. Design + API:
[WeaponFramework/README.md](WeaponFramework/README.md) "0.2.0".
- **Back holsters = the game's own back sockets** (`HVRShoulderSocket.leftShoulder/rightShoulder`, saved by the game as
  `holsterGunBackLeft/Right`), shared: each side holds a game gun OR one mod item. The mod item never enters the game's
  socket (so the game's loadout save never sees a mod id); ours is free only while the game's socket is empty, and
  `CanHover` is blocked for the game socket while ours is full. Item pinned under the socket's parent (the body), grip
  up, far end down; pose from `[WeaponFrameworkHolsters]` ints (live, mirrored). Saved in `SavedBackHolsters`, restored
  after the game's loadout (`ActionLog.Loading` over + 3 s).
- **Unknown until the first run: the sockets' parent frame.** The pose assumes x = right, y = up, z = forward; the
  first run logs `back holsters on '…': local L … R …; parent '…' up …, head fwd in parent …` to calibrate.
- **His loadout has AB15 (back-left) and CompoundBow (back-right)**, so both sides start taken: free one first.
- Test crowbar: `[WeaponFramework] TestEntries = true` (set in his cfg). Daredevil 0.3.5: clubs registered as kind
  `BillyClub`; a club the belt doesn't take on release goes to the back if near; clubs on the back are never recalled
  to the wall/belt; it also carries the framework-required warning and the pending throw-flight fixes (impact grace
  0.05 s, follow-through ends on a hit, `ThrowAimHead`) from another session, untested.
- Installed Sep 29 15:33: framework `658B0953…`, Daredevil `CEDC7EF2…` (backup + cfg in
  `feature/backup-before-WF-0.2.0-DD-0.3.5-*`).

**First test (15:45 log) and 0.2.1 (installed, `BE99B811…`; logs every failed draw / put-back near a back holster with the reason, and the crowbar's wall direction):**
- **Calibration: the back sockets live under `…/Camera/HeadRelativeInventory/LeftShoulder|RightShoulder`**, local
  L (-0.36, 0, -0.09) R (0.33, 0, -0.09), head forward in that frame = (0, 0, 1). So x = right, z = forward holds.
- Crowbar entry retrieved and grabbed; hung upright it stuck out of the slot → 0.2.1 lays it across the slot like the
  rifles (`Items.Widest` finds the hook's bend and lays it flat along the wall's up).
- Crowbar went into back-right, a club into back-left. **Neither could be drawn again.** Cause: an item in our holster
  is kinematic = "docked", and **Grab Fix only lets a hand hover docked items within its small native sphere** (on
  purpose: stops widened grabs pulling weapons off the belt). Behind your back that's too precise; the game's guns
  don't need it because the hand grabs them from the shoulder socket's big volume. Daredevil's belt `draw check` lines
  show the same pattern (hand near, club not in the grab bag). **Fix: framework draw assist**: grip press (window
  0.3 s) with the palm within `DrawCm` (15) of the shaft, hand empty and not hovering anything else →
  `HVRHandGrabber.TryGrab(item, force: true)`.
- **The game still put the bow and AB15 on the full sides.** `HVRShoulderSocket.CanHover` is a real override, but it's
  not the only way in. **All grabs, socket grabs included, end in the virtual `HVRGrabberBase.GrabGrabbable`, which
  only the base class declares** (`TryGrab(g, force)` = `CanGrab` unless forced, then `GrabGrabbable` via vtable 0x3c8).
  0.2.1 refuses there when the grabber is a back socket on a full side (log `back-right: holds '…' - the game's holster
  refused '…'`); the gun drops as if there were no holster.

**0.2.1 test (16:33 log): back holsters WORK** for the crowbar and both clubs, drawn with either hand, and the crowbar
carried into a contract (`back holsters restored: back-right Crowbar`). Crowbar lies across the wall slot (hook along the
wall, bend up). Numbers:
- ~25 draws, almost all by the assist at **13-15 cm** (= the edge: the grip is pressed while the hand is still moving
  in); **7 misses at 17-28 cm** "too far". One put-back refused at 2.6 m/s (limit 2.5). No gun was tried on a full side.
- Crowbar "floaty, no weight": the Range crowbar is **8 kg**; HVR's hand joint can't keep up, so it trails the hand.
  Billy Clubs use 3 kg and feel right.
- **0.2.2 (installed, `78727482…`):** `DrawCm` 20 (his cfg updated too), draw window 0.5 s, put-back speed limit 3.5 m/s,
  crowbar 3 kg, and draw/miss lines now say where the palm was relative to the grip at its closest approach
  (`8 out, 12 above, 5 behind the grip`), so the holster pose can be moved to where the hand actually goes.
- **Parent frame note:** `HeadRelativeInventory` up was (-0.05, 0.95, -0.32) in one session and (-0.05, 0.98, 0.21) in the
  next, so the back-holster frame follows head pitch (partly). The game's own back guns ride in the same frame.
- **0.2.3 (installed, `697C7442…`): crowbar panel picture**, his own image (grey render + outline, transparent),
  scaled to the game's sprite size 1364x635 → `BlenderRefs/out/crowbar_icon.png`, embedded in the framework DLL.

**0.2.3 test (17:18 log): all worked** — picture shows, the game refused the Howler on a full side 3× (`holds 'Crowbar' -
the game's holster refused …`), 3 kg crowbar fine (test weapon: doesn't always hit enemies; not worth more work, ships
inside the framework as the example). ~50 draws, palm consistently **~22 cm nearer the spine, ~18 cm below, ~5 cm
behind the grip end** → the 0.2.0 pose put the grip ~15 cm ABOVE the socket point (sockets are at head height, 35 cm out).
Put-backs refused at 3.5-4.0 m/s. Two findings:
- **Empty back holsters in a contract started from the main menu:** restore ran in `MainMenu`, the crowbar template
  didn't exist yet (copied from The Range) → `not made`, and **the entry was then deleted from `SavedBackHolsters`**.
  Same root as Daredevil's `no club template yet`. 0.2.4 keeps a waiting side until it can be made (after a Range visit).
- **No haptic when putting an item back** (the draw has one: HVR's own grab buzz). The game's cue for guns is
  `HVRSocket.GunHoverHaptics` → `ANBHVRGunBase.HolsterHaptics`; ours: `Controller.Vibrate(0.35, 0.06, 150)` once when a
  held mod item enters `SnapCm` of a free side (Better Bow's quiver uses the same pulse).

**0.2.4 (installed, `60CF4D28…`, his cfg updated):** hover buzz, pose `OutCm -13 / DropCm 30 / BackCm 7` (≈¾ of the
measured offset, since draws fire while the hand is still moving in), put-back speed 5 m/s, waiting sides kept.
- **Next: split all holster code into a new mod, VR Holster Customization** (base layer; Weapon Framework and
  Daredevil require it). Exact move list, dependency rule and phases: [VRHolsterCustomization/PLAN.md](VRHolsterCustomization/PLAN.md).
- Open: the crowbar's claw is pointy but it only does blunt damage (a stab needs the game's knife logic, i.e. a real
  crowbar weapon, not a test entry).

## Mod Settings 0.3.0 (Sep 28 2026): flat-screen menu (Nexus request); release files ready (`release/ModSettings/`, commit 2b29f28 on dev), not uploaded, in-game test pending

Users asked for Mod Settings in the flat version. **Ctrl+M in flat mode opens an IMGUI window** (`FlatMenu.cs`) with the
same pages/buttons, used with the mouse. Page logic moved out of `Panel` into `Pages.cs`, shared by both menus.
- **IMGUI survived stripping** (`GUI.Button/Label/DrawTexture(Rect,…)`, `GUIStyle(GUIStyle)` copy ctor, `fontSize`,
  `alignment`, `fontStyle`), but **`GUIStyle.set_wordWrap` / `set_richText` / `get_hover` are stripped** → the copied
  skin styles are used as-is and the flat description has no rich-text tags.
- **Flat controller = Low Poly Shooter Pack `Character`**: `cursorLocked` (0x154) gates `OnLook`, `OnTryFire`,
  `OnTryAiming`; `UpdateCursorState` sets `Cursor.visible/lockState` from it; `ManualLockCursor` refuses while
  `editingGun` (0x18d); `menuShown` = 0x18c. The menu clears `cursorLocked` while open (re-cleared each frame) and calls
  `ManualLockCursor` on close unless `menuShown`. No controller (main menu) → saves/restores `Cursor` directly.
- Flat detection: `!XRSettings.isDeviceActive` (in `UnityEngine.VRModule`, not XRModule), or controller active + no hands.
- Installed `9AACC571…` (backup `feature/ModSettings-backup-before-0.3.0-*`). **Test (flat):** Ctrl+M in The Range →
  menu centred, cursor visible, mouse look + fire blocked, WASD still walks; change a value → log line; close →
  look works again. Also in the main menu. **VR regression check:** phone tile + Ctrl+M still open the board.

## Mod Settings 0.2.2 (Sep 28 2026): tile in the centre slot; installs when the game closes

0.2.1 tested: tile in App_6 works, but it sits right above Game Options, and a stray press on Settings pauses the game,
after which the board doesn't respond until you leave the pause menu. He wants it centred.
- **The tile now lives in `App_5` (centre),** the slot holding the mission Data Breach holders (`BreachHolder`,
  `BreachHolder_1`). Their **holder `activeSelf`** is the mission signal: they stay off outside missions (his screenshot
  shows an empty centre with the phone in hand), while the inner buttons follow the phone's `toggleInput`. Checked 4×/s;
  while a holder is on, the tile moves to `App_4` (the first empty slot, left, away from Settings) and comes back
  after. Logged with DebugLog: `phone tile moved to …`.
- Unverified: whether a mission ever turns those holders on (the Data Breach prompt seen so far is `Popups/BreachPopup`,
  which is `dataBreachAppButton`). The fallback costs nothing if they never do.

## ★ Arsenal test 3 (14:23 log) → Daredevil 0.3.3 + Weapon Framework 0.1.1: fit when the slide-in ENDS — TESTED, "Perfect"

0.3.2: Diagonal layout right, but the pair stood ~17 cm off the board. Log: laid out 25.878, "fitted" 26.171 = **0.3 s
after Retrieve**: the slot pauses before sliding, so "still for 0.3 s" fired before the animation even started, and the
ray hit the wall face around the board window ('Plane', 4.5 x 2.55 m) while the slot was still down in the wall.
**Fix:** Weapon Framework 0.1.1 adds **`OnSettled`**, fired from an `ANBGunwall.endAnimation` postfix (the game's own
"switch done"); Daredevil 0.3.3 fits then (5 s timer as fallback). A ray hit counts only 0-15 cm behind the gun position,
else the board is assumed 2 cm behind it (`BoardBehind`); the pair is lifted 5 cm (`PairLift`) to the board's middle.
The fit line lists every ray hit in cm (`[ray hits, cm behind: ...]`) for calibration. Installed Daredevil
`FC55DD09…`, framework `7C8D58DA…` (backup `feature/backup-before-Daredevil-0.3.3-*`).

## Arsenal test 2 (13:21 log) → Daredevil 0.3.2: pair laid out in the slot's frame, fitted to the board once still

**0.3.1 result:** clubs grabbable (optimiser fix works: `took 2 club grabbable(s) out of the game's grabbable optimiser`,
many wall grabs), but the X "sticks out of the board". Two causes in the log:
- **"Along the wall" came from the line to the player's head**: he stood at an angle → `along (-0.74, 0, 0.68)` vs the
  true wall axis `(-1, 0, 0.04)` of test 1 → the pair lay in a plane 43° off the board.
- **Placed and raycast the moment Retrieve is pressed, while the slot is still sliding in** (mount y 0.87 vs 1.46 at rest):
  the board hit ('Plane', "+23 cm") was measured against a moving slot.
**0.3.2:** layout from the gun position's own axes (rigid with the wall: up = along the wall, forward = up, right = wall
normal; sign-checked against the player); clubs spawn with it and slide in; once the slot has been still 0.3 s, a ray
along the normal finds the board and centres the pair on its collider bounds if board-sized (log:
`arsenal: pair fitted - board '<name>' N cm behind the gun position, W x H m, centred on it`). **His layout choice:**
`WallLayout = Diagonal` (default: two parallel clubs, one above the other, `WallAngle` 45°, grip lower left),
`Upright`, `Cross`; `WallSpacing` 0.1 m, `WallShift` right,up,out. Old `WallStyle`/`WallPosition`/`WallGap` removed from
the cfg (backup `feature/MelonPreferences-before-0.3.2.cfg`). Installed `FC575A32…`.
**Optional, parked:** the game's hologram "ghost" of a taken weapon on the board (the pistol shows one) — for the clubs
it'd be a Rim Dissolve copy like the belt holster tubes.

## ★★ First arsenal test (Sep 28 2026, 13:07 log) → Daredevil 0.3.1: the game's grabbable OPTIMISER was the "can't grab" bug all along

**Worked:** panel `ARSENAL 007 / 007`, `BILLY CLUBS`, the X picture; Retrieve put a club on the slot (only one: the right
belt holster already held one, and the wall fills up to two). **Wrong:** description drawn over the name; club hung
upright at the gun position = too low (tip into the terminal) and ~8 cm in front of the board; **it couldn't be grabbed**.
- **★★★ `GD_HVROptimiser` (`Il2CppGD_Game`) switches grabbables off.** On first use it collects every
  `ANBHVRGrabbable` / `HVRGrabbable` / `HVRGrabbableBag` with **`FindObjectsByType(FindObjectsInactive.Include)`** and
  **disables each one immediately** (`AddToGrabbablesList<T>` @0x180a37bc0); its `Update` then re-enables, round-robin,
  only those within `_grabbableRange` **and in front of `_player`** (a forward-dot test). So:
  - the club **template** (inactive holder, ~20 m away) is disabled forever → **every club copied from it mid-scene
    starts with `HVRGrabbable.enabled = false`** and isn't in the list, so nothing turns it on. This is the old parked
    "F8-spawned clubs can't be drawn for a minute" AND the wall club.
  - clubs that existed when the list was built are in it → **switched off whenever they're behind you** = the belt club
    that sometimes wouldn't draw (`draw check … enabled False`, then True 20 s later in this log).
  - Found by xref of `Behaviour.set_enabled` (411 callers) filtered to methods that load a grabbable type.
- **Fix (`Daredevil/BillyClubs/Optimiser.cs`):** every new club gets its grabbables enabled and removed from
  `GD_HVROptimiser.instance._grabbables`; once a second the same for all clubs and the template (the list is filled
  inside `Update`, not at a hookable call). Log: `took N club grabbable(s) out of the game's grabbable optimiser`.
- **Placement:** `WallStyle = Cross` (default, an X like the panel picture) / `Upright`; a ray towards the wall finds the
  board (log: `board '<collider>' ±N cm from the gun position`), `WallPosition` = along,up,out (default `0,0.12,0`).
  The gun position's frame is turned (log: `mount up (1,0,0) fwd (0,1,0)`), so the layout is built from the line to the
  player, not from the mount's axes. Draw checks now cover wall clubs too. Arsenal description left empty.
- Installed 0.3.1 (`08722BA2…`, backup `feature/Daredevil-backup-before-0.3.1-*`); obsolete `GloveColor` / `WallOffset`
  removed from the cfg (backup `feature/MelonPreferences-before-0.3.1.cfg`).
- **Test:** Retrieve → the X on the board above the terminal → both clubs grab; belt club draws while looking ahead.

## ★ Weapon Framework 0.1.0 + Billy Clubs 0.12.0 / Daredevil 0.2.0 (Sep 28 2026): clubs on the arsenal panel; deployed, untested

His ask: get rid of F8/UnityExplorer spawning; pick Billy Clubs on the arsenal panel (the one with the crossbow),
Retrieve, take them off the wall; as a framework any weapon mod can use. Mechanism + API: [WeaponFramework/README.md](WeaponFramework/README.md).
- **★ The arsenal lists are EMPTY in the scene** (`level2` has two `ANBGunwall`: `WeaponSlot - Big Guns` / `Small Guns`).
  **`ANBGameLogic.LoadAssetLoop` fills them at load** from `ANBDataCollection`: `allRifles` / `allShotguns` / `allOthers`
  (bow + knives; knives skipped) each get `Instantiate(slotPrefab)` (all three share `WeaponSlot_Big`, sharedassets2
  #15423) under `ANBGameLogic.WeaponFolder`, spot `SlotID = "Othersspot_"+WeaponName`, `WeaponPrefab`, pose from
  `ANBWeaponType.gunWallPosition1/Rotation1`, then `gunWallLarge.Items.Add(slot)` + `ItemsID.Add(WeaponName)`.
  3 rifles + 2 shotguns + bow = the "ARSENAL 006 / 006" in his screenshot. Ends with `LogLoadTime("Processed Completed Gunwall")`.
- `WeaponSlot_Big` = `SmallGunStorage 1` (`ANBGunwallSpot` + `ANBFpsInteractionObject`) → `Demo`, **`gunstorage`
  (`DemoHolster` + `HVRTagSocketFilter`) → `gunPos`**, `FPStrigger`. DemoHolster = the socket that throws on non-guns
  (Billy Clubs 0.1 finding), so mod slots switch it off before waking and hang items on a mount at `gunPos`.
- **Panel:** `printInfo` = `Items[currentSpot].GetComponentInChildren<ANBGunwallSpot>().WeaponPrefab.GetComponent<ANBWeaponType>()`
  → `WeaponDisplayName`, `WeaponDescription`, **`WeaponIcon` = the big picture** (bow: `gunicon_other_CompoundBow`, 1364×635,
  white shaded render, dark outline, transparent), price, `ammoType` icons. Owned check `checkPurchaseDataWeapon`: in
  `purchasedContentWeapons` by `PurchaseID`, **or price 0 → auto `makePurchase`** (writes the id into the save).
- **Retrieve** = `pickWeapon`: `Items[i]` → `mainSpot` + SetActive, previous → `secondarySpot`, anim `switch`;
  `endAnimation` switches the previous slot OFF. **It saves the index**: `savedSpot` → `dc.saveSpotLarge/Small` →
  `ANBSaveData.SavePurchases`. A mod index there would break the wall once the mod is removed → the framework swaps in
  the last game index around every `SavePurchases`.
- Billy Clubs `Arsenal.cs`: optional dependency (`MelonOptionalDependencies` + assembly check + NoInlining register),
  two wall slots (`Slot.Wall`) under the mount, tip down, spread across the line to the player's head (the wall's axes
  aren't known yet). Shows the clubs you don't carry: loose ones recalled, new ones up to two. F8 pulls wall clubs too.
  Icon: `BlenderRefs/render_arsenal_icon.py` (headless Blender 5.2, Workbench clay + texture passes) →
  `make_arsenal_icon.py` → `out/billy_clubs_icon.png`, embedded.
- **Installed** Sep 28 12:09 (game closed): `Mods/Daredevil.dll` 0.2.0 (SHA256 `28DE192E…`, rebuilt after the package consolidation, backup
  `feature/Daredevil-backup-before-0.2.0-20260928-120942/`) + `Mods/WeaponFramework.dll` (`08B2FE6A…`).
- **Test:** The Range → log `arsenal (load): 1 mod entry added after the game's 6 (BillyClubs)` → Big Guns panel `+` to
  **007 / 007 "BILLY CLUBS"** (picture, no ammo/modify buttons) → Retrieve → `'BillyClubs' retrieved - mount at …` +
  `arsenal: 2 club(s) on the wall` → clubs hang on the slot → grab, belt → retrieve the bow, then clubs again (`0 new`)
  → restart: the wall must start on a game weapon (log `save: wall index 6 is a mod entry - saved as N`).
  **Most likely to need tuning: where the pair hangs** (`WallOffset` / `WallGap`; the log prints the mount axes).

## ★ Mod Settings 0.2.1 (Sep 28 2026): tile in the real empty slot; board rides on the rig

**0.2.0 tested: the tile press works** (opened the board, no pause, no side effect), but the tile sat half over Music
Player/Camera and showed the Score Keeper icon. **Cause: placement by geometry.** (1) **The home screen's local x is
MIRRORED** (Music Player, screen-left, is at +0.079), so "bottom-right" = Score Keeper. (2) `allButtons` also holds the
mission popup buttons (y +0.116, above the grid), so "top row" = the popups and the "middle row" = the real top row.
- **Real layout (dump):** `MainSceeen/Apps` = `GridLayoutGroup` with slots `App_1..App_9` in reading order; each has a
  dark `Button_Back`; filled ones hold `Button_*` (`ANBInterfaceButton`, `Button`, `EventTrigger`, `BoxCollider`,
  label `app name` with `ANBLanguageText`). **`App_4` and `App_6` are empty; `App_5` holds the hidden mission
  `BreachHolder`/`BreachHolder_1` Data Breach buttons.** `App_9/Button_Special` = Game Options, `App_8/Button_Special_1`
  = Set Floor Height, `App_7/Button_Highscore` = Score Keeper.
- **0.2.1:** structural placement: the clone of the last filled slot's button (Game Options) goes into the last empty
  slot above the bottom row (App_6 = middle row, right), same local pose as in its own slot. `Button.onClick` and the
  `EventTrigger` list are cleared too. **Board parented to the head's parent (the rig)** so stick locomotion carries
  it (0.2.0 closed itself "walked away" 4 s after opening); unparented + DontDestroyOnLoad again on close.
- **Lesson: place UI into a game's own layout by its structure (slots, sibling order), never by measured positions.**
- Built; installs automatically when the game closes (it was running).

## Mod Settings 0.2.0 (Sep 28 2026): opens from a phone tile

**0.1.0 tested:** the board works in VR (font `LiberationSans SDF`, URP Unlit, fingertips `LndexNub`/`RndexNub` found,
presses toggled KneeShotStun.Enabled and closed on X; saved). **The pocket gesture failed: grips landed 19-27 cm from
the pocket, and he found it confusing.** He asked for the phone's empty middle-row slots instead.
- **Phone facts:** `ANBSmartphone.mainApp` = home screen. Tiles are `ANBInterfaceButton` (fingertip `OnTriggerEnter` ->
  `buttonPush` -> click + `onButtonPush`, nested type `ANBInterfaceButton.ANBInterfaceButtonPushEvent`).
  **`toggleInput(held)` = `isHeld` (0x23c) + `SetActive(held)` on every GameObject in `allButtons` (0x20,
  List<GameObject>).** Mission apps sit in the middle row: `dataBreachAppButton` (0x148), `codeAppButton` (0x1c8).
  `ANBInterfaceButton.Awake` only checks its own components (no global slot), so copying a tile is safe.
- **0.2.0:** copy of the bottom-right tile (Game Options) made under an inactive holder, `ANBLanguageText` removed,
  push events replaced by empty ones (so it never pauses), placed in the free middle-row column (right-most first,
  skipping the mission apps' columns), added to `allButtons`; a `buttonPush` postfix toggles the board, which opens
  higher (6 cm below eye level) so it clears the phone. Pocket gesture removed. `UserData/ModSettings_phone.txt` =
  one-off home-screen layout dump (DebugLog).
- **To check:** `phone tile added: middle row, column N of 3` in the log; the tile sits on a dark slot (not hidden
  under it) and reads "Mod Settings"; pressing it opens the board and does NOT open the pause menu.

## Mod Settings 0.1.0 (Sep 28 2026): in-VR settings board for every mod

His request: change mod settings in the headset, for all mods. Source [ModSettings/](ModSettings/README.md),
`<game>\Mods\ModSettings.dll`, config `[ModSettings]`.
- **★ MelonLoader 0.7 supports live config (checked in the installed `MelonLoader.dll`):** a `FileSystemWatcher` on
  `MelonPreferences.cfg` (`OnFileWatcherTriggered`) reloads the file when it changes, and entries fire
  `OnEntryValueChanged`. So an external edit, or `entry.BoxedValue = x` + `MelonPreferences.Save()`, reaches every
  mod while the game runs.
- **Generic:** it lists every `MelonPreferences.Categories` entry, typed from `GetReflectedType()`: bool toggle,
  int/float `-big -small +small +big` (steps from the DEFAULT value's decade, so they don't jump), enum (≤ 24 values)
  and string choices cycle, `#RRGGBB` cycles a palette, other strings read-only. **String options are parsed from the
  description** (`Word (`, `Word =`, `(X, Y or None)`, `A or B`, `A / B`) and only trusted if the current value is
  among them. Checked against the real cfg: ThrowStyle, ThrowReaction, DaggerTip/Orientation/FlipAxis, HolsterGhost
  right; keys/paths/holster vectors read-only.
- **Open:** empty right hand + grip at the right front pocket (belt `Waist/Holsters` space `0.18,-0.18,0.08`, radius
  9 cm, 0.15 s wait so a grab of a club/force grab doesn't count), or **Ctrl+M**. Input choices ruled out: stick clicks
  = sprint/crouch, right B = slow motion, A/X = mag release.
- **UI:** quads (URP Unlit) + TextMeshPro 3D, no colliders. Presses = index fingertip bone (`LndexNub`/`RndexNub`, palm
  fallback) tested in panel space; a press needs the tip to come in from the front.
- **Live audit of our mods:** all live except Knee Shot Stun `ExtraKneelSeconds` (read in `OnInitializeMelon`) and
  Billy Clubs build-time values (Mass, ThrowSearchDistance, Length, Radius, BodyColor, holster positions, GloveColor)
  = next level load. Not changed yet (Billy Clubs was being edited by another session; Knee Shot Stun is released).
- **First-test checklist:** log `menu built: font …, shader …` on first open (missing font/shader = the one hard
  failure); `fingertips: left 'LndexNub' … right 'RndexNub'` (if it says the palm, pressing is with the palm);
  `right grip N cm from the pocket` lines to tune `PocketPosition`; text size/legibility (TMP 3D fontSize × 0.1 m
  per em, assumed from the TMP source).

## Billy Clubs 0.8.3 (Sep 27 2026): dark red gloves (`GloveColor`); deployed, untested

His request: Daredevil dark red gloves. New `Gloves.cs`.
- **★ Every glove uses ONE URP Lit material, `fps_vr_glove`** (VR hands `Left/RightHand_Gloves_HD_LOD0-5` in
  resources.assets, hand-poser previews, cutscene `Hands`). Atlas `gloved_default_color` (4096², near-grey leather,
  sRGB ~50-140) × `_BaseColor` **#414141**; normal + AO maps; metallic 0.128, smoothness 0.388.
- **Sleeves are a separate material, `fps_vr_glove_arms`** (only the cutscene `Full` arms). The glove atlas has a
  sleeve-looking area, but the VR hands only sample it with a hidden wrist cap (checked by overlaying the mesh UVs).
  So changing the material colour recolours gloves only; no texture work needed.
- The mod sets `_BaseColor`/`_Color` on every loaded `fps_vr_glove` material (`Resources.FindObjectsOfTypeAll`) on
  each scene load and once 5 s later (runtime copies). Shared asset, so hands spawned later are covered.
  Log: `gloves: N material(s) tinted #8A0F0F (was #414141), M found`.
- Default `#8A0F0F`: it multiplies the texture, so the glove albedo lands around sRGB (50, 3, 3). The grey gloves
  were ~22. If too dark/bright, change the hex in `[BillyClubs]` (restart). Empty/`off` = vanilla.
Installed SHA256 `9226E483…`; backup `feature/BillyClubs-backup-before-0.8.3-20260927-232431/`.

## ★ Billy Clubs 0.9.2 (Sep 28 2026): middle grip switched off (safely); line-grab slide found; TESTED, good

His ask: the club needs one grip (no mid-shaft hold), and eventually a grip switch or a Blade & Sorcery grip slide.
- **★ Safe way to lose a grab point, from `HVRGrabbable.GrabPointValid` (@0x181ce8d10):** it rejects a point whose
  `gameObject.activeInHierarchy` is false, whose component `enabled` is false, or whose `LeftHand` (0x34) /
  `RightHand` (0x35) flag doesn't match. So **deactivate, never destroy** (0.6.0's DestroyImmediate = NRE on every
  grab). `MiddleGrip.cs`: `GrabPoint_Additional` SetActive(false) + component disabled on the template and each new club;
  the base point inherits its `IsForceGrabbable`/hand flags first (so distance grabs can't break). `MiddleGrip`
  (default false) turns it back on, live, for clubs not in a hand. Template build logs both points' flags
  (`grab points: base …; middle …`).
- **0.9.2 TESTED, he likes it:** a club picked up from the ground now always sits at the grip end. **The second hand
  can grab anywhere:** log `grab points: base force yes, hands LR, one hand only yes; middle … one hand only yes` →
  once the first hand holds `GrabPoint_Base` it is taken, no valid posed point is left, and the grabbable's
  **`PhysicsPoserFallback` (0xf4)** closes the fingers wherever the hand touches. (Before, the 2nd hand snapped to the
  middle point.) Side effect worth keeping: hand-over-hand = a manual grip reposition.
- **★★ The grip SLIDE is native to HVR here: line grabs.** `HVRPosableGrabPoint` has `IsLineGrab` (0x78),
  `LineStart`/`LineEnd` (0x80/0x88 Transforms), `LineCanReposition` (0x9c, slide), `LineCanRotate` (0x9e),
  `LineFreeRotation` (0x9f), `LooseDamper`/`LooseAngularDamper`; `HVRHandGrabber.UpdateLineGrab` (@0x181d59960)
  switches tight/loose (`_tightlyHeld` 0x360, `SetupLooseLineGrab`), driven by `HVRSettings.LineGrabTriggerLoose` (0xb6)
  + the hand's grab/trigger state. **Next experiment:** make `GrabPoint_Base` a line grab along the shaft
  (two child transforms on the club axis), behind a default-off setting; first find out which input loosens it here.
- **Grip switch** fallback, if the line grab misbehaves: toggle the per-club `GripSlide` offset between two presets on a
  free button (A/X is free on a club hand: no gun).

## Billy Clubs 0.9.1 (Sep 28 2026): holster tubes actually show; BUILT, install when the game is closed

0.9.0 test: no tubes. The log shows they were built and copied the game's highlight from
`Holsters/HolsterKnifeRight`: material `holster_holograph (Instance)`, shader **`Shader Graphs/Rim Dissolve`**, colours
normal (0.35, 0.23, 0, **0**), hover (0.64, 0.42, 0, **0**), invisible (0, 0, 0, 0), fade 0.25 s.
**★ That shader is additive: alpha is always 0 and "invisible" = black.** `HVRANBSocketHoverFade`'s lerp coroutine
(`<lerpFunction>d__11::MoveNext` @0x181d63d40) just does `renderer.material.color = …`. 0.9.0 disabled the renderer
whenever alpha < 0.005, so the tubes were never drawn. 0.9.1 judges visibility by max(r, g, b, a) and sets only
`material.color`, like the game.

## Billy Clubs 0.9.0 (Sep 28 2026): visible holster tubes with the game's holster highlight; installed, untested

His request, with screenshots of the game's knife holster ghost (orange see-through silhouette): give the club slots a
minimal vertical tube, highlighted like the game's holsters; later maybe a holster framework.
- **How the game highlights holsters:** `HVRANBSocketHoverFade` (and `HVRANBSocketHoverKnifeFade`, with its own trigger
  colliders) on each belt holster lerps `rend`'s material colour between `colorInvisible`, `colorNormal` and
  `colorHover` over `duration` (fields 0x28-0x78; `useHandHover`, `alwaysVisible`, `gunIn`). The ghost is a mesh of the
  item's shape with that material.
- **`HolsterVisual.cs`:** in `FindBelt`, a Unity cylinder (collider removed, no shadows) per slot: 0.42 m long,
  26 mm radius, 8 cm below the anchor (covers the tip half of the hanging club). The material and the three colours are
  copied at runtime from the player's own `HVRANBSocketHoverFade` (logged: material, shader, colours); fallback = a
  transparent URP Unlit orange. States: invisible when the slot is full or no club is in a hand; normal while a hand
  holds a club; hover when that club is within `HolsterSnapDistance` of the free slot. `HolsterGhost` = Hover (default)
  / Always / Off.
- First test showed nothing because the game was still running 0.8.3 (the DLL couldn't be swapped while loaded).
  Installed Sep 28 01:23 after the game closed (SHA256 `DA9245F8…`; backup `feature/BillyClubs-backup-before-0.9.0-20260928-012319/`).
- Also fixed in 0.9.0: the 0.8.3 glove tint threw `NotSupportedException: Method unstripping failed` at
  `ColorUtility.ToHtmlStringRGB` (stripped from this IL2CPP build) right after tinting. The log line now formats the
  colour itself. **Rule: `ColorUtility.ToHtmlString*` is unusable here; `TryParseHtmlString` works.**

## ★ Billy Clubs 0.8.1 (Sep 27 2026): kneel really plays (chest throws + all club leg hits), +30% speed, faster tip turn; TESTED: "pretty amazing, worked flawlessly for the most part"

**Decided (0.8.2):** kneel only for leg hits; chest throws get a longer stun. New `ThrowReaction = Stun` (default, his
cfg too): after the blunt call (whose GetHit just set the game's short stun), `overallHitTime` is raised to
`ChestStunSeconds` (3 s) and `isGettingHit` is set. It is the same AI-stun timer Knee Shot Stun holds; `checkAbilities`
counts it down. Log: `- STUNNED 3 s (game X s)`. Kneel / Knockdown / None are still selectable. Deployed, untested.
**Tested Sep 28 2026: Stun looks unnatural** (they just stand there, gun still pointed at him). **Kneel looks better
for chest hits.** Back to `ThrowReaction = Kneel` (cfg + source default; the default takes effect at the next build).
Knockdown (falling down) is the next thing to try. A proper stun animation is in the long-term backlog (IDEAS.md).


0.8.0 feedback: feel "much better", damage "feels right" (falling and getting up suits a blunt weapon). Kneel didn't show.
- **★ Why the kneel failed:** log had 11 `KNEELS`, so the leg swap worked, but **the blunt path never plays the
  knee-shot animation.** `TakeBluntWeaponDamage` only calls `getHitAnimation` (result unused) and
  `GetHit(isRunning = 1)` = stumble. The `hit_legs_1` crossfade on the animator "Hit" layer exists only in the
  gunshot path. **Fix:** a `GetHit` prefix clears `isRunning` for the kneeling NPC (static set around the call). The
  postfix sets `gettingHitLegs` and `CrossFadeInFixedTime("hit_legs_1" / "_m" for right legs, 0.1 s, Hit layer)`.
  Knee Shot Stun has already registered the NPC in its own GetHit prefix and holds the clip.
- **New `KneelOnLegHits` (true):** his request mid-turn. Every club hit to a leg (thrown or swung) kneels instead of
  stumbling. He may later keep the kneel for legs only and set `ThrowReaction` to Knockdown/None for chests.
- **Speed +30%:** steered `MinSteerSpeed` 13 / `ThrowSpeed` 18; new `HandThrowBoost` 1.3 multiplies unsteered hand
  throws (follow-through compares the boosted hand speed).
- **TipFirst turns faster:** log showed the tip still 40-78° off at impact, because flights last only 0.08-0.15 s and
  0.12 s was the time constant. Now `TipTurnTime` 0.04 (his cfg too), cap 60 rad/s (`maxAngularVelocity` raised to
  match). Natural untouched.
- Noted: many "floor shots" at 24-55° hit the enemy directly (close, low targets). `FloorShotAngle` 15° may be too low.

## ★ Billy Clubs 0.8.0 (Sep 27 2026): chest-throw reaction (kneel), real enemy-hit detection, faster steering; deployed, untested

0.7.3 log `26-9-27_22-54-39`:
- **Bug: one throw knocked out 3 enemies although `RicochetFromEnemy` was off.** The damage hook said "KNOCKED OUT", but
  the impact check logged "hit a wall/object at 1.2 m/s" and ricocheted. It found the enemy by an OverlapSphere of 0.8 m
  *after* the velocity change, and the knocked-out ragdoll had already left it. **Fix:** the blunt-damage prefix records
  the hit enemy on the flight (`Flight.EnemyHit`); the next physics step handles it as an enemy impact before anything
  else. **Lesson: classify impacts from the damage/collision callback, never from proximity afterwards.**
- Speed: steered 6-11 m/s looked slow-motion (ricochets mostly the 6.0 floor). Now `MinSteerSpeed` 10 / `ThrowSpeed`
  14 (his cfg too); ricochets keep the incoming speed (no 0.8 factor).
- Follow-through data: most releases were "at the peak"; early ones only +0.5-0.9 m/s at 27-29 ms. So he doesn't
  release much too early. The feeling is more likely HVR's release-direction averaging; keep watching.

**Chest-throw reaction (`ThrowReaction`: Kneel default / Knockdown / None)**, with `ThrowDamageMultiplier` 3 → 2 (fast
body throw 68: the enemy reacts, the next hit knocks out):
- **Kneel:** `TakeBluntWeaponDamage`'s prefix swaps `bodyPart` (by ref) to the NPC's `RightLeg`/`LeftLeg` collider.
  The game then runs its own leg path: `gettingHitLegs`, `hit_legs_1` kneel, `GetHit(isRunning = 1)`, and Knee Shot
  Stun holds the kneel +3 s. The leg ×0.65 is undone by dividing the club multiplier, so damage stays a body hit's.
  Skipped for head hits, real leg hits, and enemies already down or kneeling. Knockout rules treat it as a body hit.
- **Knockdown:** `ANBPM.puppet.SetState(Unpinned)` (ragdoll fall; Heavy Melee then holds them down).
- **Blunt call site facts:** `GetHit(hitsound = 1, hitTime, isShotgun = 0, isRunning = r14b)`. r14b = 1 on head
  (canKill) and leg hits, so those stumble; plain torso blunt hits never stumble (just a flinch).
Installed SHA256 `D9F5BD18…`; backup `feature/BillyClubs-backup-before-0.8.0-20260927-230047/`.

## Billy Clubs 0.7.3 (Sep 27 2026): throw follow-through (early release still gets the full swing); deployed, untested

He opens his hand mid-swing like a real throw, and it feels as if the game wants a full swing with the hand held out
before letting go. **Mechanism:** HVR takes the release velocity from the hand's last few frames when the grip opens
(`HVRHandGrabber.ThrowLookback` / `ThrowLookbackStart`, `ComputeThrowVelocity`, per-grabbable `ReleasedVelocityFactor`).
An early release gets the slower, earlier part of the swing, and its direction. In reality the object's weight tells you
when; in VR nothing does. Not his mistake.
**`FollowThrough.cs`:** the releasing hand (`HVRHandGrabber` transform, noted in `BeforeGrab`) is sampled every physics
step (finite difference, two-step smoothing against tracking jitter). For `FollowThroughTime` (0.12 s) after the release,
until the first impact: if the hand is > 0.5 m/s faster than the club, the club takes the hand's velocity (unsteered) or
its speed within 6-11 m/s (steered). One line per throw: hand speed at release, club speed, and either "still speeding
up to X m/s N ms later - early release" or "released at the peak". That gives data on how early he lets go.

## Billy Clubs 0.7.2 (Sep 27 2026): steered speed follows the throw (6-11 m/s), not 20; deployed

0.7.1 test: everything works except speed ("absurdly fast"). Log `26-9-27_22-43-17`: hand releases 2.8-12 m/s, but
every assisted throw and ricochet was set to **20 m/s** (`ThrowSpeed`, copied from the crowbar's assist speed), so a
9 m/s throw doubled its speed on release. That speed bought no damage: the blunt fast tier starts at 3.5 m/s.
Now the mod steers at the throw's own speed, clamped `MinSteerSpeed` 6 to `ThrowSpeed` 11 (his cfg 20 → 11), and
ricochets at 80% of the incoming speed, clamped the same way. The ricochet log line shows the speed. Unassisted hand
throws are untouched. Slower flight also leaves more time to see TipFirst turn and Natural spin.

## Billy Clubs 0.7.1 (Sep 27 2026): TipFirst back (smooth turn), no enemy-to-enemy ricochet by default; deployed

0.7.0 feedback: the own assist works; knives unaffected. SpinEnd "too aggressive"; likes Natural; wants the tip-first
throw back, ideally turning from upright to horizontal and then stopping.
- **TipFirst** (default, his cfg): angular velocity = shortest-turn axis × angle / `TipTurnTime` (0.12 s, capped 25 rad/s),
  so it swings smoothly onto the flight line and settles with no roll, instead of snapping the rotation. It works now
  because the game's assist no longer zeroes the rotation every step. Ctrl+T cycles TipFirst → Natural → SpinEnd.
- **`RicochetFromEnemy` = false**: an enemy hit stops the club; walls and the floor still send it on to an enemy.

## ★ Billy Clubs 0.7.0 (Sep 27 2026): the mod replaces the game's throw assist; SpinEnd only; deployed, untested

**★ How the game's throw assist works** (`ANBAssistedThrowingObject`, reusable for any thrown-object mod):
- `StartAssistedThrow` (on release): returns if `dontUse`, if `ANBGameLogic.assistedThrow` (0x70b) is off, or if the
  release speed < `assistedThrowAtVelocity` (0x70c). Target = `GetClosestAndCenteredTarget(true)` (enemies), else
  `(false)` (other targets); view limits `assistedThrowViewRadius` 0x718 / `assistedThrowViewAngle` 0x71c, overridden
  per object by `targetSearchDistanceOverride`. Found → `StartCoroutine(StartAssistedThrowExec)`.
- **The coroutine (`<StartAssistedThrowExec>d__15::MoveNext` @0x181bf7db0): `isHoming = true`, `linearVelocity = 0`,
  `angularVelocity = 0`, then every fixed step `rb.MovePosition(pos + dir × speed × fixedDeltaTime)`** until within
  `stopDistance` or past `maxFlyDistance`; `changeRotation` → `MoveRotation(LookRotation(dir) × aimCorrectionAngle)`,
  else `Transform.Rotate(spinAxis, torque × dt)` (spinAxis 0 on the crowbar = nothing). End: `isHoming = false`,
  velocity = dir × speed. **It is a knife homing: it kills the thrown spin, and MovePosition on a dynamic body fights
  the physics.** That is the "keeps flying", "janky", "the assist takes over the item" he saw.
- Log proof: spin throws without a target kept 4.8 turns/s; with the assist 0.3-2.2. TipFirst held 0-1° off the
  flight line on unassisted throws.
- **Every spin topped out at exactly 4.8 turns/s = 30 rad/s = the rigidbody's `maxAngularVelocity`**, not the setting.

Changes: `ThrowAssist.cs` hooks `StartAssistedThrow`: the prefix saves velocity + spin, the postfix lets the game choose
the target, then stops its coroutine, restores both, and hands the target to the mod's own velocity steering (the same
code as ricochets; aim = chest via `AimPoint`, or the transform for non-enemy targets; steered for maxFly/speed + 0.3 s).
It logs the game's assist settings once and, for each throw, the target or why there was none (off / below speed / no
target). **Throw styles cut to `SpinEnd` (default, 5 turns/s, his cfg too) and `Natural`**. TipFirst and SpinFlat
are removed at his request, and old cfg values are mapped. SpinEnd no longer snaps the club upright; it applies the
smallest turn into the spin plane (re-done after each ricochet). `maxAngularVelocity` is raised during flight and
restored after. The flight no longer ends on `IsHandGrabbed` (a new grab ends it in `BeforeGrab`). Installed SHA256
`9D804B88…`; backup `feature/BillyClubs-backup-before-0.7.0-20260927-222436/`.

**Open, parked by him:** F8-spawned clubs mid-scene can't be drawn (`draw check`: HVRGrabbable `enabled` False for a
full minute), while clubs restored after a scene load draw fine. Menu → back fixes it. Don't force `enabled` every frame
(0.6.0 fought the game ~30×/20 s); next try: enable once ~1 s after an F8 spawn, or find who disables it.
**Idea:** the assist analysis could become a general "assist fixes" mod (knives, thrown props).

## ★ Billy Clubs 0.6.1 (Sep 27 2026): holster/grip changes of 0.6.0 REVERTED; damage kept; deployed

0.6.0 broke drawing: clubs fell out of the hand. Log: `NullReferenceException` in
`HVRGrabbable.GrabPointValid` ← `GetGrabPoint` ← `HVRHandGrabber.OnBeforeGrabbed` on every grab.
- **★ Lesson: never DestroyImmediate an HVR grab point.** HVR keeps it in more than `GrabPoints` (GrabPointsMeta /
  posable-grab-point data built from the serialized scene), so cleaning the `GrabPoints` list is not enough; the stale
  entry kills every grab. To lose the middle grip later: move `GrabPoint_Additional` onto `GrabPoint_Base` or disable it
  some other way, and test a grab in The Range before shipping.
- Also reverted: forcing `HVRGrabbable.enabled` back on while holstered (the game turned it off ~30 times in 20 s, a
  fight with an unknown owner). Holster code is back to 0.5.3 behaviour, which he says worked great.
- Kept: damage multipliers, body knockout, ricochet hit-pause bypass, hit log, spin 8 turns/s, flight tip-angle log.

## ★ Billy Clubs 0.6.0 (Sep 27 2026): club damage + knockouts, ricochet damage, draw fix, grip only; deployed, untested

From `26-9-27_21-42-38.log` (0.5.3) + game code:
- **★ Game blunt rules (`TakeBluntWeaponDamage` @0x181c4b240):** tier by speed (≥ fastSpeed 3.5 → 34, ≥ 2.1 → 10,
  else 5); head ×10 only with `canKill`; **legs** ×0.65 (the branch sets `gettingHitLegs` 0x9bf, so limb = legs only);
  `isUnhittable` 0x9bd = no damage. **Only a head hit can take the last health; any other hit floors it at 1** → body
  hits never knock out. **`ANBBluntWeapon.hit` clears `canHit` and `hitPause` waits 0.6 s** (constant @0x183a61e90),
  optionally ignoring collisions with that enemy meanwhile → **the 2nd enemy of a ricochet chain took no damage**, and the
  re-enabled collision after 0.6 s is what shoved a club lodged in an enemy.
- **★ Draw failures = HVRGrabbable `enabled` false while holstered** (every failed `draw check`; the one success came right
  after it read true). canBeGrabbed/line-of-sight/colliders fine; layer 2 vs 20 did not matter (layer 2 was in the bag).
  Who disables it is unknown. Fix: `KeepOnBelt` re-enables it every frame, logs the first time, count on draw.
- Holster misses at 0.6-0.96 m with the club 0.5 m above / 0.7 m in front of the belt = simply dropping it, not
  attempts; the miss log now only fires within 1.5× snap distance.
- Floor shots worked (floor → enemy → enemy chains). Spin settle cap working (peaks 22-34 rad/s capped).
- Throw styles "all feel the same": 3 turns/s over a 0.1-0.4 s flight is under one turn. Default + his cfg → 8.

Changes (`Damage.cs`): `SwingDamageMultiplier` 1.5 (fast body 51), `ThrowDamageMultiplier` 3 (fast body 102 = one-throw
knockout) by scaling the weapon's tier fields around the call; `BodyKnockout`: a floored hit that should have killed →
`KillNPC(null)` + `AddMeleekill` (legs only if already down); `SameEnemyCooldown` 0.3 s — a different enemy skips the
game's 0.6 s pause. **Every club hit logs** part/bone/speed/tier/thrown-or-swing/damage/health before→after/KO. Removed
`GrabPoint_Additional` (the middle grab point) from the template, then verified/cleaned `HVRGrabbable.GrabPoints` after
spawn (logged). Flight diagnostics: tip angle to the flight line at release and at the end (measured before the mod turns
it, so a large end angle = something overrides the rotation), and spin in turns/s. Installed SHA256 `B661C79F…`; backup
`feature/BillyClubs-backup-before-0.6.0-20260927-215928/`.
- **Idea parked:** a shared combat/hit logger for all mods (HeavyMelee already logs its own hits with DebugLog).

## ★ Billy Clubs 0.5.3 (Sep 27 2026): floor ricochets (Daredevil bounce shot); deployed, untested

- **Floor hits now count as ricochet surfaces.** A shallow floor bounce turns the velocity < 50°, which the old
  impact test (turn > 50° or speed < 60%) missed. New test: falling > 1 m/s, then ≥ 70% of that fall gone in one step,
  with walkable ground (normal.y ≥ 0.7, not an enemy) ≤ ~25 cm below the club. Logged as `hit the floor`; the
  ricochet looks and flies from 8 cm above the floor.
- **Floor shot:** a release ≥ `FloorShotAngle` (default 15°) below horizontal skips the game's homing (which would pull
  it up to the target before it lands), gravity on, re-stopped every physics step until the first bounce in case the
  game starts homing a frame late (logged). Ricochet 1 = floor → enemy, ricochet 2 = next enemy. `0` = off.
- Gravity is forced back on at every flight end (the homing may leave it off).

## ★ Billy Clubs 0.5.2 (Sep 27 2026): post-hit spin cap, swept flight, holster diagnostics; deployed, untested

Blender re-export: `billy_club.blend` had no unsaved changes and the re-exported OBJ is byte-identical
(SHA256 `00fd28bb…cb94`) to the one already embedded since 0.5.1, so the model in game was already current.

Test log `26-9-27_21-6-28.log` (v0.5.0) and `21-18-43.log`:
- **Every enemy hit logged 20.0 m/s** (the homing speed, after a ricochet). The flight then simply ended: the mod stopped
  forcing zero spin and the club was left inside the enemy's colliders at 20 m/s → depenetration + animation = "spinning
  like crazy". The game's homing coroutine was also only stopped on a ricochet, not when a flight ended.
- TipFirst **snapped** the rotation every step. After a wall hit that swings a 0.6 m club up to 180° in one step while it
  touches the wall = the wall-throw collision trouble.
- Re-holstering **never succeeded**: 9 slow releases missed at 0.26-0.45 m (snap 0.25). Some logged "misses" were throws.
- 21:18 session: F8 spawned 2 clubs into the holsters, then **no grab of any kind reached them for 100 s** (no
  `GrabGrabbable` call at all). Cause not in the log; restored clubs in the earlier session drew fine.

Changes: every flight end goes through `EndFlight` (stops game homing, restores collision mode); an enemy hit bounces the
club back at ~2.5 m/s and caps spin 12 rad/s / speed 6 m/s / depenetration 2 m/s for 1 s, logging the peak; flight uses
ContinuousSpeculative; TipFirst turns at 1080°/s instead of snapping. `HolsterSnapDistance` default and his cfg 0.25 → 0.4.
Diagnostics: holster misses log only slow releases, with the club centre + tip in belt space vs the slot (data for a
holster-placement mode); new `DrawWatch.cs` logs one `draw check:` line when a hand stays ≤0.2 m from a holstered club
for 0.5 s without drawing it (HVR hover target, club in the hand's grab bag, CanBeGrabbed, line-of-sight, colliders/layers,
kinematic). Build 0 warnings, asset tests pass, installed SHA256 `82055CCD…3232`; backup in
`feature/BillyClubs-backup-before-0.5.2-20260927-213715/`.

## ★ Billy Clubs 0.5.1 (Sep 27 2026): darker, coarser grip after successful first VR test; deployed

User feedback on 0.5.0: "Overall, this is a huge success." Silver reflections look good; grip is too bright/smooth-looking and its tiny knurl disappears in dim light. Log confirms `custom club visual ready` with 6,165 vertices, 5,876 triangles and URP Lit. Blender MCP revised the existing procedural grip: 32 rather than 80 circumference cycles (2.5x larger), bump distance 0.35 mm at strength 0.8, darker burgundy and roughness 0.40-0.50 with subtle darker groove pigmentation. Base color/normal/roughness rebaked at 2048px. Geometry, metallic map, silver material and gameplay logic unchanged.

Bright/dim comparison renders in `BlenderRefs/previews/13_v1_top_v2_bottom.png` and `14_v1_top_v2_bottom_dim.png`: previous grip above, new below. Detail is much clearer in both; final VR motion/aliasing check pending. Previous model/textures are in `BlenderRefs/checkpoints/grip-v1-0.5.0/`.

Release build: zero warnings/errors; all embedded-asset/parser tests passed. Installed with the game closed; DLL size 3,987,968 bytes, SHA256 `E3930E8207F7D7009CB6860C3F56B6A2E08BE7A862913745AA99FEE838360CF0`, verified equal to `feature/BillyClubs-0.5.1/BillyClubs.dll`. Prior installed 0.5.0 backed up to `feature/BillyClubs-backup-before-0.5.1-20260927-212231/BillyClubs.dll`.

## ★ Billy Clubs 0.5.0 (Sep 27 2026): authored Blender model integrated and deployed; subsequently tested successfully

The model built in the Blender MCP session is now embedded in `BillyClubs.dll`: one OBJ and four 2048px PNGs, no external asset installation. `ClubObj.cs` parses corner tuples, mirrors OBJ Z and reverses winding; `CustomVisual.cs` preserves split normals, generates Unity tangents, creates a shared URP Lit material, packs metallic R + (1 - roughness) A, and puts the visual on the existing shaft center/axis. The mesh is 600 mm long, 34 mm at the hand, 34.8 mm maximum, 5,876 triangles and 6,165 runtime vertices after UV/normal splits.

`UseCustomModel` defaults true; false restores the previous cylinder visual after restart. Asset-load failures log a warning and fall back automatically. `Radius` and `BodyColor` now apply only to the primitive fallback; `Length` scales the authored visual along its axis. The crowbar grip, colliders, holsters, recall, tuning and throw code are unchanged. Assets load once and are shared between clubs; mipmaps/trilinear filtering and anisotropy 8 are enabled.

Validation: Release build zero warnings/errors; dependency-free tests pass against the actual embedded resources (exact file bytes, dimensions, winding/normals of all triangles, tangent UVs, hand cross-section, locale/negative indices/seam splitting/error cases). This does not verify shader appearance or gameplay in Unity.

Installed while the game was closed to `<game>/Mods/BillyClubs.dll` (2,999,808 bytes). Build and installed SHA256 match: `4E5B92F0174F367DD0D4A0B74AAEAE1A4310A3C640C3F8A958FCAB97ADB8A779`. Prior DLL backed up to `feature/BillyClubs-backup-20260927-210507/BillyClubs.dll` (SHA256 `E81D2EA0E74CFD19D32582338CA157475507875023F4E3197F4ECA3A8BE7ACE7`). Tested build: `feature/BillyClubs-0.5.0/BillyClubs.dll`.

Next in-game check: launch, visit The Range, press F8. Confirm the `custom club visual ready` log, silver tip direction, grip texture/shading at hand distance, draw/holster/recall and throw behavior. No game launch or VR test was performed in this integration session. See `BillyClubs/README.md` for build/test commands and material details.

## ★ Billy Clubs 0.4.0 (Sep 27 2026): throw styles, ricochet, grip tuning, F8 recall; deployed, untested

**0.3.0 test: "far better".** Clubs sit in the hand, holster/draw works, the prop reset is skipped (logged three times),
the homing throw "worked for the most part". The belt reads 0.37-0.82 m below the head depending on posture; a
holstered club centre sat 0.72 m below the head, tip down. Near misses were 0.27-0.47 m from a slot (snap 0.25).
Complaints: the grip "doesn't feel natural"; a throw keeps the hand's orientation (flies upright) instead of metal tip
first and flat; want a spinning throw; ricochet unclear (there was none yet, only physics bounces); **after a second F8
the new holstered clubs couldn't be grabbed**. The log shows lots of `ForceGrabber` grabs (the player pulls thrown clubs
back with force grab), and 0.3.0's F8 **destroyed** the loose clubs. Most likely the force grabber or a hand was still
pointing at a destroyed club. Not proven.
- **F8 = recall:** loose clubs (and ones held by the force grab, which gets `ForceRelease`d) go back into free slots;
  new ones are spawned only while fewer than two exist. **Clubs are never destroyed.**
- **Throw control** (`ThrowStyle`, `OnFixedUpdate`): a release at >= 2.5 m/s that doesn't holster starts a flight.
  `TipFirst` (default) sets the rotation every physics step so the metal tip leads, lying flat; `SpinFlat` spins it
  flat like a helicopter blade; `SpinEnd` spins it end over end (`SpinSpeed` turns/s); `Game` = untouched. The game's
  homing only steers the velocity (the crowbar's `changeRotation` is off), so orientation is free for the mod.
- **Ricochet** (`Ricochets` 2, `RicochetRange` 12 m): an impact = the velocity turning > 50 deg or losing > 40% speed in one
  step (ignored for the first 0.15 s, the homing's big correction). The enemy hit is the nearest one within 0.8 m; the
  next target = the nearest live enemy (`Physics.OverlapSphere` → `GetComponentInParent<ANBBasicNPC>`) with a clear line
  (club's own colliders skipped), aimed at the chest (`aimAtHead` − 0.35 m; `RicochetAimHead` for the head). The game's
  homing coroutine is stopped and the mod steers for up to 1.5 s. Works off walls too (any impact).
- **Grip tuning keys, all with Ctrl (Evhenii's keyboard has NO numpad):** Ctrl+J/L slide the club in the hand
  (±1 cm), Ctrl+K/I and Ctrl+[ / ] tilt it (**no arrow keys: the game moves the player height with them, even with Ctrl**) on two axes (±5 deg), Ctrl+R resets, Ctrl+T cycles the throw style. Saved as `GripSlide` / `GripTiltA` / `GripTiltB` / `ThrowStyle`. It moves the
  club's mesh and colliders around the fixed grab point, so holster and throw poses follow.
- Log lines: `club thrown at N m/s (style)`, `club hit enemy/wall … - ricochet i/n to '…' d m away` or `no enemy in sight`,
  `grip: hand N cm from the grip end, tilt …`, `clubs: N brought back to the holsters, M new`.

## Billy Clubs 0.3.0 (Sep 27 2026): holster fixes after the first test

**First test of 0.2.0 (The Range):** F8 put 2 clubs "in the holsters", but they appeared lying on the floor; after
grabbing them they sat oddly in the hands; drawing and re-holstering then worked (log: `club drawn`, `club holstered`);
**when enemies were spawned the holstered clubs vanished**, and F8 said "both club holsters are already full"
(the objects still existed, just elsewhere). Scene reloads restored them correctly (`restored 2 holstered club(s)`
in MainMenu and The Range). The player rig is `PlayerObject/TechDemoXRRigOpenXR/Waist/Holsters`.
- **Vanishing = the game's prop reset:** a wave start runs `ANBGameLogic.resetPhysicObjects` →
  `ANBGeneratePhysics.resetMe` → `resetMeTicker` → `resetMeInstant` (kinematic on, `getSpawnPositions`, teleport to the
  spawn spot). The clubs inherited it from the crowbar. **Fix:** a prefix on `resetMe` skips clubs.
- **On the floor after F8 = the crowbar's own init running a frame later:** `ANBGeneratePhysics.Init` (from `Start`)
  calls `makePhysicable`, sets the layer and `isKinematic`, so a club pinned to the belt on its spawn frame was knocked
  loose and fell. **Fix:** while holstered (and not held) the club is re-pinned every frame: active, parented to its
  slot, kinematic, exact pose. Each kind of pull is logged once (`club (left) was … by the game - put back`).
- **Off-centre in the hand:** the club was centred on the crowbar's mesh bounds, which the hooks pull sideways. The
  hand poses hold the shaft, so the club is now centred on the **longest box collider** (the shaft) and logs how far it
  moved (`shaft collider … club moved N cm …`) and how far the grip point is off the shaft line.
- **`HVRPlayerWaist`** drives the belt: it follows the camera (`CameraOffset`, `WaistSpeed`, snap turn) and has
  `holsterSittingPos` / `holsterNormalPos` / `HolstersMovable` (a sitting-mode holster position).
- New debug lines: belt height from the head and the game's hip holster in belt space (once per scene), the club
  centre height and "tip down" check on every holster, near misses (`let go N m from the free … holster`), and which
  hand grabbed or drew a club.

## Billy Clubs 0.2.0 (Sep 27 2026): own belt holsters

**0.2.0 = holsters that carry into contracts.** A contract starts from the saved loadout in a freshly loaded
scene, so nothing physical carries over and the game's holsters can't take a club anyway (below). The mod adds
**two club slots on the player belt** `VR - Player/TechDemoXRRigOpenXR/Waist/Holsters` (the parent of the game's
`HolsterLeft/Right` at x ±0.266 and `HolsterKnifeLeft/Right` at ±0.165, 0.048, 0.132). Defaults: ±0.23, −0.12,
−0.10 (behind the hip holsters), club tip down, configurable as `HolsterLeft` / `HolsterRight`.
- **Holster:** let go of a club within 0.25 m (`HolsterSnapDistance`, measured to the club's axis) of a free slot at
  under 2.5 m/s → kinematic, parented to the belt, collisions with every non-trigger player collider ignored, any
  running homing throw stopped. **Draw:** prefix on `HVRGrabberBase.GrabGrabbable` (every grab goes through it)
  unparents it and turns physics back on before HVR sets up the grab.
- **Carry-over:** which slots are full is saved (`SavedHolsters` = `L`/`R`, written to the cfg) and cleared only when
  a club is drawn. After each scene load, once the belt exists (searched once a second for 30 s) plus 2 s, the full
  slots are refilled from the template. Survives game restarts too, once The Range has built the template.
- **F8** now fills the empty slots (and removes loose clubs); a free-floating pair only if no belt is found.
- **Test:** F8 in The Range → both clubs on the belt → draw, swing, re-holster → take the elevator into a contract →
  log should say `restored 2 holstered club(s)`, then draw and fight. Also check the club doesn't shove you when
  walking and the hands can still grab it.
- Hooks checked in the disassembly: `HVRGrabbable.InternalOnBeforeGrabbed` is a 20-byte virtual-dispatch stub
  (too small to detour safely), called only from `HVRGrabberBase.GrabGrabbable(grabber, grabbable, raiseEvents)`.

### 0.1.0

Concept 1 from [IDEAS.md](IDEAS.md): Daredevil clubs = copies of **`Prop-Bluntweapon-Crowbar`** (The Range only),
the one item with both `ANBBluntWeapon` and `ANBAssistedThrowingObject`. Source [BillyClubs/](BillyClubs/BillyClubs.cs),
`<game>\Mods\BillyClubs.dll`, config `[BillyClubs]` (`DebugLog = true` for the first test).
- **Crowbar as shipped:** rb **8 kg**; blunt `canKill` true, speed tiers **1 / 2.1 / 3.5 m/s** → damage **5 / 10 / 34**,
  head ×10, limb ×0.65; throw speed 20, stop 1.5 m, search 6 m, max fly 4 m. Two grab points on `Base - Grab/GrabPoints`
  (`GrabPoint_Base` x −0.134, `GrabPoint_Additional` x 0.05). Meshes = random designs under `Object/Meshes/tools2`
  (`ANBGeneratePhysics.useRandomDesign`; only `Plane009` active in the scene), colliders = 4 boxes, main one ~0.6 m.
- **Mod:** on the first Range load, copy the crowbar under an **inactive** DontDestroyOnLoad holder (no Awake runs),
  hide the crowbar meshes, build a red body + silver tip/band/caps from Unity cylinders along the long axis, mass 3 kg,
  `useRandomDesign` off, throw assist `dontUse` off with search 15 m / fly 20 m. **F8** spawns a pair floating in front
  of you (gravity off until grabbed or 30 s); a new pair replaces the old. Colliders and grab points stay the crowbar's.
- **First log to read:** `template built from the crowbar: axis …, crowbar length …, material … shader …`, then the
  `throw assist:` and `blunt:` value lines, `spawned a pair`, `club …: grabbed`.
- **Open risks:** references from the crowbar copy to Range scene objects dangle after the scene change (watch for
  errors in contracts); the material comes from the crowbar's, so if it isn't URP Lit the colours may be off.

**★ Holsters don't take it (checked Sep 27 2026, needs our own code):** hip holsters (`HolsterLeft/Right`) and knife
holsters (`HolsterKnifeLeft/Right`) are `DemoHolster` sockets with an **`HVRTagSocketFilter`**, and the crowbar has no
`HVRTagSocketable`, so they refuse it. Adding tags isn't enough: **`DemoHolster.OnGrabbed` fetches the knife/gun
component and throws a null-reference if it's missing** (every null check jumps to the il2cpp NRE thrower), then calls
`holsterKnife` / `holsterGun`. Shoulder sockets (`HVRShoulderSocket.CanHover`: base socket check + held by a hand +
a velocity cutoff; only `LeftShoulder` has a `HVRGrabbableSocketFilter`) may accept it, but their `OnGrabbed` also
calls `ANBGameLogic.holsterGun`. → Club holstering = a patch that skips those calls for clubs, or our own holster.

## ★ The game hides enemies you can't see (Sep 28 2026, found for Radar Sense)

`ANBBasicNPC.checkVisibilityRelatedActions`: out of view (`!isInView`) for `switchObjectsAfter` s, further than
`outOfViewObjectsSaveDist` (vs `currentDistanceToPlayer`), no custom animation → deactivates `outOfViewObjects` (0x818),
`NpcMeshes` renderers `enabled = false`, animator culled (`dynamicAnimatorCulled`), `outOfViewObjectsOff` (0x828) = true.
Back in view, or `outOfViewTime` (0xba4) below the limit → all restored, animator `AlwaysAnimate`. **Any mod that
reads enemy renderers or bone poses sees hidden, frozen enemies unless it keeps `outOfViewTime` at 0.** Radar Sense does.

**★★ Pooled enemies get a NEW body on respawn (Oct 2 2026, Radar Sense "silhouette vanishes / floating vest").** The same
`ANBBasicNPC` object is reused across spawns (`#79882` = E15, E52, E121 in one run) and re-dressed: its skinned meshes
(`CC_Combined_LOD0-4`, clothes, hair) are destroyed and NEW ones with the same names are created. A mod that cached the
renderer list once points at a dead body: 0 (or only the vest) of N meshes "on" while the game still draws the enemy.
Between spawns / while idle out of view the enemy is drawn by a plain `CulledMesh` MeshRenderer plus its gun, with every
skinned mesh off. Radar Sense 0.3.x fix: rescan the enemy (≤ every 2 s, only while it should show and ≤ 2 meshes are on),
drop dead parts, outline the new ones. Confirmed by the `rescan #id: 23 new renderers ...` log lines; the 01:20 session
had 0 `NOT DRAWN` lines (was 563 the run before). **Any mod caching an enemy's renderers or bones must re-fetch them.**
GPU Instancer mod ruled out: it only flags materials instanced and logs `0 skinned meshes skipped`.

## Throw Assist 0.2.0 (Sep 28 2026): knives fly like the game's knife assist; deployed, untested

**Log from the 19:39 session:** `AimHead` "didn't work" because every throw tested was a **Billy Club**, which Throw
Assist skips. Daredevil aims clubs with its own `AimPoint`, switched by `[BillyClubs] RicochetAimHead`, and that
setting covers every club throw, not just ricochets. Kept as it is: ricochet is club-only, so the setting stays in the Billy Clubs section.
Knives with `SteerOtherItems`: 3 throws, steered at 12 / 7 / 8 m/s (your hand speed) with the spin kept. The game's
own coroutine flies knives at a fixed **17 m/s** (`ato.speed`), turned point first. So ours was up to 2.5× slower
and hit at a random blade angle.
**0.2.0:** a thrown item with an `ANBKnife` flies at `max(steer speed, ato.speed)`. Every step
`rb.rotation = FromToRotation(Stabber.StabLineWorld, flightDir) * rotation`, spin zeroed. DebugLog: one line per
knife, `stabbed <collider>` (from a `StabEnemyFinal` prefix) or `no stab`, plus the tip angle off the flight line
on the last step before the hit. **Verify first:** the stab line points base → tip (Better Bow used it that way).
If knives fly handle first, flip it.
**0.2.0 test (19:57 log): the knife fix WORKS.** 14/14 assisted knives stabbed (13 head, 1 spine_03), tip 0–22°
off the flight before the hit. The failed throws he felt left **no line at all**, because the silent paths were
not logged. Fixes (still 0.2.0, deployed): a `releaseKnife` hook logs every knife throw that never reached
`StartAssistedThrow`, and `After` logs knives with no target. Likely cause: `MinAssistSpeed` (6) put
`dontUse` on knives thrown at 3.5–6 m/s, which the game itself would assist. Knives now use the game's 3.5 threshold.

## Daredevil 0.3.4 (Sep 28 2026): club throws follow `ThrowAssist.AimHead`; deployed, untested
Direct (assisted) club throws read `ThrowAssist.AimHead` (via `MelonPreferences.GetEntry`). Ricochets keep
`[BillyClubs] RicochetAimHead`. Log now says `(at the head|chest)` instead of the game's target name `knifeSpotChest`.
**Overlap, for the record:** Daredevil uses none of Throw Assist's code for clubs. It hooks the same
`StartAssistedThrow`, uses only the game's target choice, and has its own flight plus 4 duplicate settings
(`ThrowSpeed`↔`MaxSpeed`, `MinSteerSpeed`, `ThrowSearchDistance`↔`SearchDistance`, `ThrowMaxFlyDistance`↔`MaxFlyDistance`).
Throw Assist is required for pistols (moved out in Billy Clubs 0.11.0) and now knives.
**Ricochet "got worse" (20:21 log) — not the head-aim change; an old bug.** Ricochets that fired hit 6/6. But 5
unassisted throws ended their flight 0.13–0.17 s after release with no hit line, at the same moment as the
follow-through line, so they never ricocheted. Cause: unassisted flights ignored impacts for 0.15 s (a leftover
from when the game's homing still ran), and during that window the follow-through kept resetting the club's
velocity to the hand's, pushing it into the wall it had just hit. When the window closed, the club was below
2 m/s and the flight ended silently. Fix (0.3.4): impact grace 0.05 s for every throw; follow-through stops once
the club drops below 60% of its speed; a flight that ends by slowing down now logs `flight ended: slowed to …`.

## Throw Assist 0.2.2 (Sep 30 2026): private knee-assist experiment; built, deployed, initially playtested

Game 0.3.1.0, `MelonLoader/Logs/26-9-30_2-30-31.log`: the player enabled `allowKneeHit` during play with
head/knee angle limits of 25 degrees. Three knife throws selected Knee at 5.2, 16.7 and 12.8 degrees; two
logged `KNEELS`. Contacts included LeftLeg, RightUpLeg and RightFoot. One throw logged `stabbed LeftLeg`
without a kneel trigger. Those prefix/collision logs do not establish damage or the precise reason for a miss.
Knee Shot Stun 1.0.0 was loaded with four extra seconds; its extension was not separately validated here.

The user liked the feature but reported occasional difficult knee selection and inaccurate leg trajectories.
Steering currently follows the leg collider's world-bounds center. Collider placement and moving poses are
possible explanations, not proven defects. Increasing the angle limit can admit more throws but cannot fix
the three already-selected trajectories. Keep the feature experimental and private, with `allowKneeHit = false`
by default and the user's personal saved opt-in retained. The Nexus description draft mentions the private
experiment explicitly; the 0.2.1 release package is unchanged. See `ThrowAssist/TESTING.md` for remaining checks.

## Challenge NPC Limit 0.1.0 (Sep 30 2026): takedown enemy selection cap

Research on the local Gunman Contracts 0.3.1.0 installation (`GameAssembly.dll` and IL2CPP metadata):

- `ANBContractTerminal.buttonEnemiesAdd` increments `ANBGameLogic.CD_totalSpawns` (float, offset `0x1498`) and clamps it to the selected `ANBContractData.maxTakedownEnemies` (float, offset `0xB4`). `buttonEnemiesSub` similarly clamps to `minTakedownEnemies` (`0xB8`). `updateContractInfo` clamps to both bounds again and copies the selected count to `currentTakedownEnemies` (`0xBC`).
- `ANBContractData`'s constructor defaults `maxTakedownEnemies` to 40. Given the reported 30 limit, a serialized value on the challenge data is the likely source; the value on that asset has not been extracted independently. `ANBNpcSpawner.maxEnemiesAtOnce` is 0 in previous local challenge logs; it is a separate simultaneous-enemy setting.
- `ChallengeNpcLimit.dll` raises `maxTakedownEnemies` on takedown-available challenge data to the configured value (60 by default). It patches data initialization and the terminal paths that clamp the count. Build: Release, zero warnings/errors. Installed for local testing; selecting and playing >30 enemies has not yet been verified in game.

## Sep 30 2026 release preparation (game 0.3.1.0)

The user confirmed the VR Holster Customization 0.2.2 back-slot checkpoint reset in game, including the crowbar. Daredevil 1.0.0 club door kicks worked repeatedly with A/X; some steel doors did not expose a usable marked kick point. The 1.0.0 ZIP now includes the tested door-kick code and the earlier confirmed club-leg checkpoint fix.

Throw Assist 0.2.2 now defaults to 3.5 m/s for both pistol and other-prop assist gates; knives continue to use the game's own 3.5 m/s gate. Its optional knee assist remains off by default. These three builds passed with zero warnings and errors, were packaged and installed after backup. The 3.5 m/s change itself has not yet been tested in VR.

## Oct 1 2026: club swing feel ("floaty / delayed / keeps going after I stop"): diagnostics added, no fix yet

Report: a fast swing lags the hand a lot, and a fast sideways swing keeps moving and twists back after the hand stops, like a heavy object.
Clubs are 3 kg already (crowbar 8). Daredevil's `Mass` setting now applies live (also to a club in the hand).

**HVR's chain, from the game's own types (`il2cpp_tools/dumpt.py`):** two soft links, tuned separately.
- **A, controller → physics hand:** `HVRJointHand` has a `ConfigurableJoint` (`Joint`) on its `RigidBody`, driven by `Strength` (`PDStrength`: Spring, Damper, MaxForce, TorqueSpring/Damper/MaxTorque). `HVRHandStrengthHandler` swaps that strength while something is held (`StrengthOverride`, `HandGrabOverride`, the grabbable's `OneHandStrength`/`TwoHandStrength`) and can also apply `HVRJointSettings` (`CurrentSettings`). The hand body is 20 kg (`StumbleOnRBMass` notes), so a swung club is a small part of the load; a weak MaxForce/Spring is what looks like heaviness.
- **B, hand → club:** `HVRHandGrabber.Joint`, a `ConfigurableJoint` built from the grabbable's `JointOverride` / `OneHandJointSettings` (`HVRJointSettings`: X/Y/Z drive, SlerpDrive or AngularX/YZ drives, `MassScale`/`ConnectedMassScale`, `CriticalDampPosition`, `DampConnectedBody`). Spring + low damper = the "keeps going, twists back" ringing; low MaxForce = lag.
- The settings are **ScriptableObjects shared with the other items**: change a clone, never the asset.
- Candidate levers, in the order to try: club `Mass` (live now) → hand strength while holding a club → B joint spring/damper/massScale → a "stiff grip" that steers the club's velocity to the controller pose each physics step (as Throw Assist steers thrown items), which removes both links but pushes nothing back on the hand.

**`[BillyClubs] SwingLog = true`** (set in his cfg; `SwingLogMinSpeed` 4) writes, from `BillyClubs/SwingLog.cs`:
- once per grip (identical physics later = one short line): club mass/inertia/COM/drag/max spin, hand body, hand strength + settings now, the grabbable's overrides, and the live drives of both joints, all as spring/damper/maxForce;
- one line per swing: peak tip speed the hand asks for vs the club reaches, **tip delay in ms split into hand (A) + grip (B)**, max lag in cm and where it peaks, twist in degrees, and after the stop: cm behind/ahead, overshoot cm and when, swing-back count, twist before/after;
- one line per grip: swing count, delay range, worst lag.
Method: offsets of tip/axis/hand relative to the controller are measured while the controller is still (0.25 s), then each frame the "ideal" tip (bolted to the controller) is compared with the real one; the delay is the shift that best matches the two paths over the swing. Sampled in `OnLateUpdate` from transforms (what the player sees), unscaled time; slow motion is tagged.
**Next:** play one session (a few slow and fast swings with each hand), then read the `hold` block and the `swing` lines: if delay is mostly "hand", tune A; if "grip" with overshoot, tune B; if the numbers look small but it feels bad, suspect the view (interpolation) rather than the physics.

### Oct 1 2026 SwingLog result (log `26-10-1_22-49-35.log`, Daredevil built + installed; 76 swing lines, 5 grips)

His verdict: melee is mostly great; the remaining issue is a delay between his movement and the glove. Numbers (90 Hz physics, 11.1 ms step):
- **Hand strength is the game's default: spring 9000, damper 900, maxF 9000, torque 500/50/75, on a 20 kg body**
  (`HVR_DefaultHandStrength`; no override while holding a club). That is critically damped at ωn ≈ 21 rad/s, so at an acceleration `a`
  the hand trails the controller by about `a / ωn²` (100 m/s² → 22 cm); the log shows hand lag max 15-25 cm at fast swings. **Doubling
  the spring should halve that.** The grab joint to the club is rigid (slerp/angular 100000/1000, projection PositionAndRotation).
- **Typical swing at 10-20 m/s: tip delay 24-44 ms = hand 16-24 + grip 12-24.** At 20 m/s that is 50-90 cm of tip lag, which
  matches the logged "lag max 60-90 cm". Light swings (4-7 m/s) are 8-20 ms and 5-20 cm. Overshoot after the stop is small
  (0-8 cm, 0-1 swing-backs), so the "keeps going like a heavy object" is not a ringing joint.
- **The mass is not the cause:** club 3 kg, inertia (0, 0.14, 0.14), centre of mass 6 cm from the middle; the drive is the same for an empty hand.
- Slow motion (x0.22, Radar Sense focus): the few samples show 88-96 ms (hand 40-44), roughly double. Physics steps run at 0.22 of real
  speed, so in slow motion the hand can only catch up at that rate. Needs more samples before blaming it.
- Not trustworthy: the first "swing" after a grab often reads 19-21 m/s with the club moving 1-3% (the club was still being drawn
  or the pose snapped), and a few left-hand lines with 100+ cm lag and 130-180° twist (club knocked out of the grip pose or the baseline taken in a
  different pose). Treat lines where the club speed is under 15% of the hand's as noise.
- Hand body interpolation is **None** (club: Interpolate). With 90 Hz physics and a display at another rate, the glove updates in steps
  while the club is smoothed; to be checked with the new probe, since it adds up to one step of delay and judder.

**Built next (installed, not yet played): `HandProbe.cs`.** Per hand, empty or holding, it follows the raw controller (`TrackedController`), the
physics target, the 20 kg body and the glove (`HandModel`) and logs, per hand movement over 2.5 m/s (relative to the head): delay in ms and max
gap in cm of each stage behind the raw controller, plus rotation delay. A one-off `chain` line names the transforms and the body's interpolation.
And **`[BillyClubs] HandStrengthScale`** (default 1 = unchanged, live, 0.5-4): scales the shared hand strength (spring and force by x, damper by √x, so it stays
critically damped). It changes both hands and everything they hold. Test order: (1) play at 1 to see the bare-hand delay and which stage owns it;
(2) set 2, then 3, and compare delay and feel. Grab Fix is acquisition only (detector spheres, buffered presses); it does not touch how a held item or the hand moves.

## ★ Grab Fix 1.1.1 (Oct 1 2026): enemies only grabbable with the palm right on them — TESTED, release candidate (Oct 2 2026)

**Report:** while throwing a fist (Heavy Melee) at a standing enemy, the hand grabbed a downed (ragdoll) enemy lying
nearby, or the face of the enemy being hit. Cause: the widened local pickup sphere (`NearGrabRadius` 0.12, grown to
span palm + finger side in Both mode) is the real trigger collider, and enemy limbs are ordinary `HVRGrabbable`s, so any
fist closing within ~12+ cm of a body part hovered and grabbed it.
- **Fix:** `GrabFixMod.EnemyPart(g)` (`GetComponentInParent<ANBBasicNPC>`) + `EnemyReachable`: in the `HVRHandGrabber.CanHover`
  postfix an enemy part stays hoverable only if its collider surface is within **`EnemyGrabRadius` (0.10 m, 0 = off; 0.04 was tested and rejected deliberate grabs, since the palm point cannot get closer than ~10 cm to a body surface) of the
  palm**. `HVRForceGrabber.CanHover` refuses enemy parts outright (no distance grabs on enemies); `Eligible()` refuses them
  (no buffered / recent-release grabs). Held loose items and docked-item rules are unchanged.
- **Test:** punch a standing enemy with a downed one at your feet / hold a fist at a face: no grab. Grab a face or arm on
  purpose with the palm on it: still works. If too strict raise `EnemyGrabRadius` to 0.06; if still grabbing, lower to 0.02.

### Oct 1 2026 (late): club "spins around my hand" after a fast sideways swing: hypothesis from the numbers, flip trace + knobs built and installed, NOT yet played

Log `26-10-1_23-36-10.log` (HandProbe build, settings untouched). Last swings (23:43:06-13): club twist 160 deg, held at 160 for 350+ ms (`twist 160 -> 160`), then back; controller turn 24-30 rad/s.
- **Bare hands:** body delay 16-20 ms, glove = body (the glove model follows the body with no extra delay), position lag max 11-26 cm, rotation delay 4-24 ms. Hand body path is `PlayerObject/TechDemoXRRigOpenXR/Physics RightHand`, interpolation None; `target = raw` (no separate PhysicsHandTarget).
- **With a club** the position delay is the same (12-24 ms) but the **rotation delay of the body is 28-48 ms (empty: 4-24)**, and it is 72-80 ms on a 40 rad/s turn.
- **Hypothesis (arithmetic, unproven):** the hand's rotation drive is torque spring 500 / damper 50 / **max 75 N*m**, against a club with inertia ~0.14 kg m2 about its centre, 3 kg, centre of mass ~0.2 m from the hand: inertia about the hand ~0.28. Max angular acceleration ~75/0.28 = 270 rad/s2, so stopping a 30-40 rad/s turn takes 1.7-3 rad (100-170 deg) of travel. That matches the 160 deg flip, and it explains why the empty hand (inertia 0.02) is fine. Also the club's rigidbody caps its spin at 30 rad/s while the wrist turns 30-40.
- **Built (Daredevil, installed):** `FLIP` log line (club > 100 deg off the hand's line: decides "HAND body turned" vs "club slipped in the GRIP", with spin rates, hand gap, whether the grab joint vanished or the hand was "returning to controller", a ~22-row trace, and nearby collisions via a postfix on `ANBSoundPhysicsItem.OnCollisionEnter`). Settings (live, defaults = game values): `HandTorqueScale` (1; try 3), `ClubInertiaScale` (1; try 0.4, mass unchanged, via `ResetInertiaTensor`), `ClubMaxSpin` (30; try 80), `HandStrengthScale` (position, 1). Torque/position scaling edits the shared `HVR_DefaultHandStrength` asset in memory, so it affects both hands and everything held.
- **Next:** read the FLIP lines (hand-turn vs grip-slip) and compare at HandTorqueScale 3 / ClubInertiaScale 0.4.

**Flip trace result (log `26-10-1_23-56-15.log`, game still running, settings untouched): hypothesis CONFIRMED for the real spins.** R-hand flips at 23:58:56 and 23:58:59: club 159 deg off the hand's line, **hand body 178-179 deg off the controller, grip slip only 5-18 deg** -> the hand turned, not the grip. Club and hand spin are identical (peaks 31-34 rad/s, equal to the club's 30 rad/s cap) while the controller reached 25-41 rad/s. After the controller stops (1-3 rad/s) the hand keeps turning at 14 -> 8 rad/s over ~200 ms (about -100 rad/s2: a constant, saturated torque), goes through the target and ends 178 deg away, then returns (a ring-down 46 -> 75 -> 14 -> 178 -> 0 deg). Collisions did not appear in the trace. The ten `FLIP L` lines (23:57:44-58:05) are a logger artifact: the left hand held the right club as a second hand, so its controller says nothing about the club; fixed in the source (tracking only while exactly one hand holds the club, 2.5 s repeat guard). That build was not deployed (game running, DLL locked).

## Death Details (was Close Eyes, Oct 2 2026): dead enemies close their eyes; 0.7.1 RELEASE CANDIDATE (tested Oct 2 2026)
- **Enemy faces are Synty Sidekick meshes** (`CC_Combined_LOD0`-`LOD4`, found in `resources.assets`): 147 blend shapes =
  the full ARKit set (`eyeBlinkLeft/Right`, `jawOpen`, `eyeLookDown*`...) + Sidekick `shp_*`/`mod_*`/`body_*` shape keys.
  The game never drives the face shapes, so corpses stare. The same shapes could do other face work later (jaw slack on
  death, pain squint on hits).
- **★ The game rewrites face blend shapes EVERY frame** (`ANBBasicNPC.faceAnimator` + `ANBBlendShapeSync`, which copies
  `mainMesh` = `CC_Combined_LOD0` onto the other face/beard meshes in `UpdateExec`). Measured: 232/232 frames overwritten.
  So any face-shape mod must write every frame (LateUpdate + an `UpdateExec` postfix); 0.1.0's one-time set did nothing.
- 0.2.0: `KillNPC` postfix + `isDead` poll; writes `eyeBlinkLeft/Right` (indices 19/82) every frame on the sync meshes
  and every child mesh that has them (face LOD0-2, beards); restores on pool revive. LOD3/4 faces have no shapes.
- **0.3.0 optimisation:** `UpdateExec` has one caller, `checkVisibilityRelatedActions` (per enemy), and copies all
  147 weights main -> others each call. When closing starts the mod stops the corpse's `faceAnimator` and skips its
  `UpdateExec` (prefix), writes the eyelids, then only reads one weight per corpse per frame (rewrites if changed).
  Corpses end up cheaper than vanilla. Tested with 0.4.0 ("works great").
- **★ "Lying in pain" = the TWITCHER state, and `isDead` is already TRUE in it** (checked in game code). `TakeDamage`
  sets `isTwitcher` on a lethal hit when `combatModeActive`, not a headshot, `torsoHit <= 2`, `chestHit <= 1` (so
  most body-shot kills in combat). `startTwitcher` (after `ANBPM.twitcherWaitTime`) needs `isTwitcher && isDead`:
  twitcher anim + `PlayFaceAnim("face_twitcher")` + `PlaySoundTwitcher`. `ANBBasicNPC.UpdateExec` ends it when
  `killedTimer > twitcherSurvivalTime` (Random `twitcherTimeTillDeathMin..Max`) or `outOfViewTime >
  killTwitcherAfterOutOfSightTime`; `TakeDamage`/`ExplosionDamage` end it on another hit. `killTwitcher`:
  `twitcherKill` anim, body animator off, `isTwitcher`/`isKillingTwitcher` cleared, `PlayFaceAnim("face_death")`.
  `ANBPM.forcedTwitcher` 2 = force, 1 = never. Flags: `isTwitcher` 0x9b3, `isTwitcherStarted` 0x9b4,
  `isKillingTwitcher` 0x9b5. Death Details 0.4.0 waits while `isTwitcher` is set (`WaitForTwitch`).
- **★ Twitchers writhe FOREVER in play (Oct 2 2026), and leg shots are ignored BY DESIGN.** `TakeDamage` on a
  twitcher (killedTimer >= 1.75): limb hit -> only `AddForceAtPosition` + `PlaySoundHurt`; non-limb -> `killTwitcher`.
  The bleed-out check runs only in `ANBBasicNPC.UpdateExec`, which `ANBUpdateCentral.UpdateCalls` calls only for
  `encounterSystem.allNpcs` entries with `NPCSpawned` (0xb06) && `NPCFullySetup` (0xb03); why it never fires is open
  (Death Details 0.5.0 logs the game's numbers on its first 3 forced bleed-outs per level). 0.5.0 enforces it:
  `forcePuppetMasterActive(1, false)` + `ANBPM.StartCoroutine(killTwitcher(true))` past `twitcherSurvivalTime`
  (or `MaxPainSeconds`); any `TakeDamage` that leaves it writhing -> `killTwitcher(false)`. **Both opt-in
  (`BleedOut`, `AnyHitEndsPain`, default off, untested)**: Evhenii prefers no extra load over the timeout; the RC is the
  tested 0.4.0 behaviour.
- **0.6.0 `JawOpen` (tested; 0.1 looks best = default):** the open mouth on corpses is the game's `face_death` face (jawOpen
  shape), not the ragdoll. Evhenii wants it slightly parted: when the face freezes, `jawOpen` eases from where
  `face_death` had it to `JawOpen` (face LOD0-2 + beards), once. -1 = leave the game's jaw.
- **★ Melee never reaches a dead body, but its collision event does.** `ANBBodyMeleeCollision.OnCollisionEnter`
  tail-jumps to `ANBBodyMeleeCollisionManager.collisionEnter` unconditionally; the game's handler checks `isDead`
  inside (and Heavy Melee / Daredevil skip dead too). So a club or pistol whip to a writhing enemy's head did
  nothing. Death Details 0.7.0 (`HeadHitEndsPain`, tested): `collisionEnter` postfix, `bmc.isHead` + player
  hitter (`ANBBluntWeapon` / held gun or bow / `HVRHandGrabber`) at `HeadHitSpeed` -> `killTwitcher(false)`.
  Cost: one lookup per contact in a table of currently writhing bodies.
- **★ The game's melee hit SOUNDS (for any future custom-SFX work):** clip arrays on `ANBGameLogic`: `BluntHit`
  (0x1000), `BluntHitHead` (0x1008), `FleshKnifeHit` (0xff0), `FleshKnifeSlashHit` (0xff8), `WoodHit` (0x1010).
  `TakeBluntWeaponDamage` and `TakeMeleeDamage` (living targets only) play `Random` from `BluntHit` with
  `ANBGameLogic.PlayAudioClip(clip, part.transform.position, false, "default", false, -1, -1)` (pitch/volume -1 =
  the game's defaults). Death Details 0.7.1 plays `BluntHitHead` that way on the finishing head hit (tested: sounds
  right). Replacing or adding clips = swap entries in these arrays at runtime.
  Details: `DeathDetails/README.md`.


## VR Holster Customization 0.3.0: katana and knives on the back (Oct 3 2026, built + installed, untested)

Request: store the katana (and knives) in the back slots. Built, 0 warnings, installed with the game closed
(backup of the 0.2.2 DLL + `MelonPreferences.cfg` in `feature/backup-before-VRHC-0.3.0-20261003-0215`);
`[VRHolsters] DebugLog` switched on for the test. Settings and behaviour: `VRHolsterCustomization/README.md`.

**Game facts (read from the binary this session):**
- The katana is an `ANBKnife` like every knife (`Knife-Katana`, `Knife-Katana-double` and the combat/kitchen knives are
  prefabs in `sharedassets2`). Arrows (`isArrow`) and pens (`isPen`) are `ANBKnife` too: excluded.
- **Knife auto-return:** `ANBKnife.Update → checkAutoReturn`: while `ANBGameLogic.gameStarted`, a knife that is neither
  held (`isHeld` 0x98) nor socketed (`socketed` 0x12c) counts `autoReturnAfterCurrent` (0x128, set by `releaseKnife`)
  down, then `returnKnife()` sends it to `savedHolster` or its wall spot. A knife docked by a mod is neither, so the mod
  must keep the timer at 0.
- **Knives are moved, never instantiated, on load:** `ANBGameLogic.LoadContractHolsterKnife` loops
  `ANBdataCollection.allKnifeSpots` (`List<ANBGunwallSpot>`), matches `SlotID == knifeID`, takes `spot.mygun`, releases
  it from `spot.Hanger` (virtual call) and grabs it with the holster socket, then sets `knife.savedHolster`.
- **Katana colliders:** blade split into two boxes (0.38 + 0.34) plus a 0.40 handle box, so "longest box collider"
  (`ItemShape.Measure`, fine for crowbar/clubs) picks the handle. Blades use `ItemShape.MeasureBlade` (bounds of all
  solid boxes, ~0.76 m incl. the 0.95 prefab scale). Grip point is `GrabPointNormal`, not `GrabPoint_Base`.

**Open questions for the test:** do contracts have knife spots (debug line `knife spots: N (…)` ~10 s after load)?
Does a slow knife release near the shoulder (game throw gate ~3.5 m/s, our snap limit 5 m/s) dock instead of throwing?
**0.3.1 (Oct 3 2026, built + installed `2318B579`, untested):** first report: with the katana in the left back slot
and the bow in the right, the bow caught on the katana when drawn. Back-slot items were docked kinematic but still
solid. Now every back-slot item is non-solid while holstered (solid colliders → triggers, re-asserted every frame in
case a knife's `switchCollisions` flips them; concave mesh colliders skipped), restored on draw. Same approach as
Daredevil's belt clubs (`SetGhost`) and the game's own socketed guns (`HVRSocket.DisableCollision` →
`HVRGrabbable.SetAllToTrigger`). Debug line: `'<item>': N collider(s) non-solid while holstered`.

**0.3.2 (Oct 3 2026, built + installed `2EB8D749`, untested):** second report: a katana thrown after drawing it from the
back auto-returned to the belt knife holster. Cause, from `ANBKnife.returnKnife`: it only knows `savedHolster` (the belt
knife holster, if that holster's loadout string is `none`, i.e. empty: `ForceUnstab(false)`, `socketed = 1`, holster
`TryGrab`) or else `myWallSpot` (`inWall = 1`); if the holster is full it re-arms the timer from
`ANBGameLogic.autoReturnKnifeAfter`. Fix: a `returnKnife` prefix sends a blade last drawn from the back to its back side
(else the other free side, else the game's return), after `ForceUnstab(false)` + `abortHoming()`. "Home" is forgotten
when the knife is taken out of a socket (HVR `HVRGrabbable.IsSocketed`) or put in a belt knife holster
(`ANBGameLogic.holsterKnife` postfix). **`ANBKnife.socketed` / `inWall` are NOT reliable:** `grabKnife` / `releaseKnife`
only write `isHeld`, so those flags stay stale after a draw.

**0.3.3 (Oct 3 2026, built + installed `90BA105B`, untested):** third report: a katana restored onto the left
back slot pointed sideways through the chest. Log (`26-10-3_2-57-59.log`): `'Knife-Katana' shape: 0.50 m long, centre
(0,0,0), far end (0,0,1)` = the fallback defaults. Cause: 0.3.1's ghosting ran in `Put` before the first `ShapeOf`, so
`ItemShape` (solid boxes only) found nothing, and the default shape was cached for the session (draw-assist misses
"32 in" from the grip came from the same wrong shape). A manual put measured fine because `TryHolster` measures before
`Put`. Fix: `ItemShape.Measure`/`MeasureBlade` overloads take a `solid` predicate; the back slots count their own
ghosted colliders as solid, and log a warning if nothing measurable is found. Same session: the `HeadRelativeInventory`
frame read `up (-0.49,0.65,0.58)` at calibration (it follows head pitch), and The Range logged all 10 knife spots
including `Katana2`; the main menu has none (`knife spots: 0`, back-left `waits`).

## ★ Knife unlocks and the double katana (Oct 3 2026, game 0.3.1.1, from the game code + scene files)

**Knives unlock by kills, not credits.** `ANBKnife.registerKnifeKill`: each kill with a knife counts its
`killsNeededToUnlock` down; at 0 it calls `makePurchase(knifeID, 0)` and adds the Steam stat `Knifecollector`.
The Range knife wall (`ANBGunwallSpot.checkKeepExec`) sets the spot's `ANBWeaponType.PurchaseID = knifeID`, then
`checkPurchaseDataWeapon`: ID in `purchasedContentWeapons` → shown; else `WeaponPrice > 0` → hidden; price 0 → free
(auto-purchased). Free from the start: `CombatKnife_v2`, `CombatKnife_v3` (price 0). Everything else has price 3
and needs kills. Contract maps place knives in the scene; `ANBKnife.checkSpawnConversion` →
`ANBObjectSpawner.convertWeapon` swaps each for the master prefab with the same name (not a random pick).

**`Knife-Katana-double` (knifeID `Katana2`) is not double-bladed.** It is an exact copy of `Knife-Katana` (same
`wakizashi_bohi` mesh, same components), meant as the second sword for dual wielding. Its wall spot is
`KinfeWall / KinfeTable / KnifeSlot_10`, next to the katana's `KnifeSlot_9`.

**It is unobtainable in 0.3.1.1:** no contract map places a `Katana2`. Knives per map (scene build order: level3
Warehouse, level4 Restaurant, level5 Outpost): Warehouse none; Restaurant kitchen knives, pens, `CombatKnife_v3`;
**Outpost has the only katana (`Katana`, one copy)**. With no `Katana2` instance to kill with, `Katana2` can never
reach the purchase list, so `KnifeSlot_10` stays empty. Possibly unfinished or planned content.

**Built: Melee Unlocks 0.1.0 (Oct 3 2026, built + installed, untested).** Prefix on `ANBGunwallSpot.checkKeep`
(called once per spot from `ANBGameLogic` LoadAssetLoop on scene load) calls `makePurchase(knifeID, 0)` for the IDs in
`[MeleeUnlocks] Unlock` (default `Katana2`, or `All`). `makePurchase` skips IDs already owned, subtracts the price via
`substractCoins` (0 here) and calls `ANBSaveData.SavePurchases`: the same end state as the last kill, minus the Steam
`Knifecollector` stat. Must run before the wall check because a locked spot's knife is **destroyed**
(`Object.Destroy(mygun)`), not hidden; calling `checkKeep` again later would not bring it back. Save backed up first
to `%USERPROFILE%\AppData\LocalLow\ANB_Seth\GunmanContracts\Data.bak-before-meleeunlocks-20261003`.
Both katanas use the same mesh (`wakizashi_bohi`) and material (`mat_sword_wakizashi`): they look identical.
**Test:** load The Range, look for the second katana next to the first on the knife wall, log line `unlocked 'Katana2'`.

### Melee Unlocks 0.1.0 FAILED in game, 0.2.0 built (Oct 3 2026)

0.1.0 loaded but logged nothing and unlocked nothing. Cause: `ANBGunwallSpot.checkKeep` is only
`StartCoroutine("checkKeepExec")` **by name**, and a scan for that string literal found a second starter,
`<initSlot2>d__33::MoveNext`: the knife spots run the check from `initSlot` → `initSlot2`, never through `checkKeep`
(only `ANBGameLogic` LoadAssetLoop calls `checkKeep`). **Lesson: before hooking a method that starts a coroutine by
name, scan for every reference to that name string.** 0.2.0 hooks `ANBDataCollection.checkPurchaseDataWeapon`
instead (prefix; knives only, recognised by `ANBKnife` on the weapon), which every path asks. `initSlot` =
`Instantiate(WeaponPrefab)` + `initSlot2`, so it refills a spot whose knife was destroyed; used on setting change.
Also: Mod Settings only shows a string as a selector when the description lists the options in a shape it parses
(`Name (…)`, `A or B`); 0.1.0's did not. VR Holster Customization logged `knife spots: 10 (CombatKnife_v1-v4,
KitchenKnife_v1, v2, v4, KitchenPen_v1, Katana, Katana2)` in The Range, so `allKnifeSpots` includes locked spots.
**0.2.0 built + installed (hash verified), untested.**

### Melee Unlocks 0.2.0 ran but the purchase did not stick; 0.2.1 installed (Oct 3 2026)

0.2.0 log: `unlocked 'Katana2'` at Range load, then the summary still listed `Katana2` as locked. Cause, from the
full `makePurchase` disassembly: it starts with **`if (!ANBGameLogic.gameStarted) return;`** (field 0x1329), and
`gameStarted` is false while The Range loads and the wall runs its checks. So makePurchase is a no-op there (real kills
happen in play, gate open). The same gate silently skips the free-knife auto-purchase inside
`checkPurchaseDataWeapon` (it still returns true). **Lesson: log what a game call actually changed, not that it was
called.** 0.2.1: gate closed → add the id to `purchasedContentWeapons` in memory (wall check passes, knife stays),
then call `makePurchase` once `gameStarted` is true (checked each second), which saves. **0.2.1 TESTED Oct 3 2026: works.** Log: `unlocked 'Katana2' ... in memory` at
Range load (03:09:05), `saved 'Katana2' to the game's purchases` 4 s later (so `gameStarted` does turn true in The
Range), wall summary no longer lists it. The double katana hangs next to the katana and dual wielding works.

**0.3.4 (Oct 3 2026, built + installed `457E4996`, untested):** fourth report: two katanas (`Knife-Katana` left,
`Knife-Katana2` right) on the back in The Range did not come into the Warehouse contract. Log `26-10-3_3-8-24.log`:
`back holsters restored: … 'Knife-Katana2' waits …, 'Knife-Katana' waits …` and `knife spots: 0` in the contract.
**Contracts have no knife wall.** `LoadContractHolsterKnife` branches on `ANBGameLogic.IsRangeScene` (0x95): Range =
take `spot.mygun` from `allKnifeSpots`; otherwise loop `ANBDataCollection.allOthers` (0x58, `GameObject[]` prefabs),
match `GetComponent<ANBKnife>().knifeID`, `checkPurchaseDataWeapon`, `Object.Instantiate(prefab)`. The back-slot
restore now does the same (and skips `IsMainMenuScene`). Same log: 0.3.3's shape fix confirmed (`'Knife-Katana' shape:
0.80 m long, centre (-0.18,0.01,0), far end (-1,-0,-0)`), dual katanas drawn and re-holstered together repeatedly with
the draw assist at 1-5 cm.
