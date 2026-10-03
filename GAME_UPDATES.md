# Gunman Contracts game updates

Running log, newest first: one section per game update with the developer's change log (verbatim, from the Steam announcement),
what the code diff showed, and what the first launch showed. The workflow for handling an update is
[Tools/UPDATE_RECOVERY.md](Tools/UPDATE_RECOVERY.md) (snapshot, triage, smoke checklist, selective repairs). What the game's code
does is in [GAME_KNOWLEDGE.md](GAME_KNOWLEDGE.md). Things still to play-test after an update are listed in
[ROADMAP.md](ROADMAP.md) §1. Write only what was seen: a missing log line means unknown, not pass.

---

## 0.3.1.1 (announced Oct 2 2026)

Previous build on this machine: **0.3.1.0** (Unity 6000.0.41f1, metadata v31, MelonLoader 0.7.3). Pre-update baseline for the
recovery workflow: `feature/update-triage/` (`current-baseline`, `native-baseline`, `ready-20261001`; git-ignored).

### Developer change log (verbatim)

**NEW FEATURES**

- (ALL) Volumetric Blood effects re-added to the game (not quite sure when during development they got lost, but now they are back)
- (ALL) Savegame-Backup-system implemented. Broken Savegames will now be detected and automatically replaced with the latest backup
- (ALL) Earned credits, spraykits and game progress will now be synced to Steam-Stats, which can be used to restore lost progress from the game options
- (ALL) The weapon terminals now have a button to retrieve lost weapons to the gunwall (when not held or holstered)

**BUGFIXES**

- (ALL) No Damage bonus now counting correctly
- (VR) Improved checks to prevent hand getting stuck on world geometry
- (VR) Pistol stabilizing grab should now be more stable
- (VR) Teleport is now working in the start area of the game
- (VR) Teleport no longer glitches through walls (I hope)
- (VR) Vignette feature now working correctly
- (VR) Shotguns now have haptics (vibration when shooting)
- (NonVR) 1911 hand position fixed
- (NonVR) Shotgun reloading glitch fixed when empty
- (NonVR) Dual wielding reloading glitch fixed
- (ALL) more smaller issues fixed

**OPTIMISATION / POLISH**

- (ALL) Introduced "Object Culling" for performance testing. This is currently only used on "The Outpost" map and can reduce CPU usage by disabling objects in the distance.

**UI/UX**

- (ALL) News section in the main menu added. This will let players browse the steam community announcements ... like this one!

### Files that changed (SHA-256 against `native-baseline`)

- **Changed:** `GameAssembly.dll` (84,700,160 → 84,986,368 bytes), `global-metadata.dat` (21,471,540 → 21,566,308), `globalgamemanagers`,
  `boot.config`. Metadata version is still 31. **Unchanged:** `GunmanContracts.exe`, `UnityPlayer.dll`.
- The `snapshot-20261002-130826-356112` snapshot was taken **after** Steam had installed 0.3.1.1, so it records the new files, not a
  before state; the 0.3.1.0 evidence is `current-baseline`, `native-baseline` and `ready-20261001`.

### Code diff 0.3.1.0 → 0.3.1.1 (`il2cpp_tools/snapshot.py`, Oct 2 2026)

Scope ANB*/HVR*/GD_* types plus nested, 625 types, compared as `re-baseline-with-nested.json` vs `after-0.3.1.1.json` (result
`game-diff-0.3.1.1.json`, git-ignored). **It sees method signatures and field offsets only: logic changes inside an unchanged signature
are invisible.** ~140 "added/removed" nested types are metadata nested-type attribution shifting, not real changes; there are 17 real
type changes. Almost every `ANBGameLogic`, `ANBUIManager` and `ANBGunwall` field offset moved (+8 to +40 bytes). **Named access through
the regenerated interop is unaffected; raw offsets are not** (none known in our mods).

**No method or field used by any of our mods was removed or renamed.** Removed: `ANBGameLogic.LoadMap(string)` (our `LoadMap` hits are
Daredevil's own texture helper), `tempToggleCollisions`, `TemporarilyDisableHandCollisions`, `ANBAutoScroll.SnapTo(GameObject)`,
`ANBHighscores.CheckSteamBest`, the one-argument `AddAchievementStat`. Our Harmony targets in `ANBGunwall` (`pickWeapon`,
`endAnimation`, `printInfo`), `ANBGunwallSpot` (`grabGunCall`, `placeGunCall`) and `ANBSaveData.SaveWeapons` still exist. The tool lists
8 mods as candidates (BetterBow, Daredevil, EnemyAwarenessLog, PhysicalDodge, SlowMotionHands, ThrowAssist, VRHolsterCustomization,
WeaponFramework) only because they patch `ANBGameLogic`, which changed everywhere: a flag to check, not a break.

What each real change is (read from the disassembly where marked):

- **Savegame validation (read, `ANBSaveData.SaveFileValidation`).** At start each of the 7 `SaveData_*.json` files must read as a
  non-empty string for named keys (`SaveData_WeaponSetups`: `gunSetups` and `holsterSetup`; Core: `allMapsPlayed`; Tutorials:
  `allTutorialsSeen`; Highscores: `allHighscores`; Purchases: `PC_W`). Key presence only, not values. An invalid file is moved to
  `BrokenSavefiles` and replaced from `Backup`. Weapon Framework only changes `saveSpotLarge`, which is not validated. Checked on disk
  after the first launch: `Backup/` next to `Data/` with copies of all 7 files, no `BrokenSavefiles`, `saveSpotLarge = 5`,
  `Check-WFSave.ps1` PASS. The backups look like a copy of the current file.
- **Retrieve lost weapons (partly read).** `ANBGunwall` gains `returnGunsToWall`, `returnGunsToWallLogic(bool check)`,
  `returnGunsToWallAvailable`, `returnGunButton`, `currentGunSpots` and an `Update`. It walks the game's own gun spots and compares each
  against about six held/holstered references. Weapon Framework's mod entries are not in the game's gun-spot lists, so the button should
  ignore them. **Whether the game counts a gun in our back holster (the game's own shoulder sockets) as holstered is unknown.**
- **Hand unstuck (names only):** `TemporarilyDisableHandCollisions(Transform, float)` → `TemporarilyDisableHand(GameObject, HVRGrabbable)`
  with `handUnstuckingDuration`; `HVRTeleporter` gains `TeleportOriginCheckPos`. No mod references these. Might make some of our
  quiver-reach / hand guards redundant (and double-correct).
- **Grabbable auto-release (use not found):** `ANBHVRGrabbable` gains `autoReleaseDelay` / `autoReleaseTimer`. Watch for a held club or
  arrow dropping by itself.
- **Object culling (read):** `GD_CullDistanceManager`, `GD_CullDistanceOverride`, `GD_CustomCullJob`, `GD_DistantCubemapManager`,
  `GD_RTLUpdateJob`, `ANBGameLogic.GD_DistanceCulling`. `ApplySmallObjectChanges` sets `Renderer.enabled`; `ProcessCullingData` changes
  `GameObject.layer`; logs `[Cull] created native arrays ...`. Scenery, not NPCs; Radar Sense also switches enemy renderers.
- **Steam stats, news, restore:** `BackupProgressViaStats`, `RestoreValuesFromStats`, `SyncSteamBest`, `ANBSteamNews*`. UI and save only.
- **Scene loading:** `ANBGameLogic.LoadMap(name, wait)` + `LoadMapExec` + `MapLoadRangeWithIntro`, `exitingRange`, `forceRangeIntro`
  (maps load with a delay and a Range intro; matters for scene-change hooks such as Radar Sense's "radar off (scene change)", Daredevil's
  club restore, the awareness log's scene header); `saveHold`/`savePause*`; `ANBHighscores` no-damage fields; `ANBTargetRun.missedAllowed`.
- Pre-existing, not tied to a mod: `ANBNpcSpawner.spawnPointValidation` → `outOfSight` → `ANBEncounterSystem.BlockedSight` →
  `Transform.get_position` native trampoline exceptions in the 0.3.1.0 log (Oct 1 01:23), and Frame Probe's unavailable draw-counter
  constructor.

### Result (first launch, Oct 2 2026 13:14-13:19)

- Bootstrap reports **Game 0.3.1.1**, Unity 6000.0.41f1, MelonLoader 0.7.3, metadata v31.1. **MelonLoader regenerated the interop on its
  own** (Cpp2IL then Il2CppInterop, no errors): `Assembly-CSharp` 3,478,016 → 3,478,528 bytes; `Il2CppHurricaneVR.Framework` 8,406,016 →
  8,460,288. No clean-output workaround needed. IL unstrip: 11,007 successful / 1,448 failed bodies, 1,108 methods and 13 fields not
  restored (no 0.3.1.0 generation log to compare).
- **21 mods loaded, 0 `[ERROR]`, 3 warnings, all already in the 0.3.1.0 log:** `Class::Init signatures have been exhausted`, Frame Probe's
  missing `ProfilerRecorder` constructor, Daredevil "no club template yet" (expected on the main menu). The `spawnPointValidation`
  exception did not occur, but no contract was started, so that proves nothing.
- Scenes visited: MainMenu, The_Range_001, MainMenu. **No contract, no Outpost**, so object culling, enemy awareness, the Retrieve button,
  savegame backup and the shotgun / pistol-stabilizer changes are untested.
- Seen working in the log: holster setup, `restored 2 holstered club(s)` after returning to the menu, Weapon Framework
  `arsenal (load): 2 mod entries`, crowbar on the wall, Daredevil clubs grabbed/swung/holstered, Radar Sense on in The Range
  (`mod 0.003 ms/frame`), Mod Settings menu built, Grab Fix detectors on every scene, Slow Motion Hands attached. 90 fps in The Range
  (1% lows 50-78 fps; the hitches are scene loads). The user also reported Billy Clubs, Weapon Framework, Grab Fix and Better Bow
  working by hand.
- Not exercised: Slow Motion Hands' slow-motion path, Fire Selector, Throw Assist, Heavy Melee, Knee Shot Stun, Physical Dodge.
