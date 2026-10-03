# Gunman Contracts game updates

Running log, newest first. One section per game update: the developer's change log (verbatim, from the Steam
announcement as pasted Oct 2 2026) and a first guess at what it means for our mods.

**The "Could affect" columns are hypotheses from reading the change log, not findings.** Nothing here was tested
against the new build. Confirm with [Tools/UPDATE_RECOVERY.md](Tools/UPDATE_RECOVERY.md) (snapshot, triage, smoke
checklist) before writing any of it down as fact. Record what a playtest actually showed in the section's
"Result" part.

---

## 0.3.1.1 (announced Oct 2 2026)

Previous build on this machine: **0.3.1.0** (Unity 6000.0.41f1, metadata v31, MelonLoader 0.7.3). The pre-update
baseline for the recovery workflow is in `feature/update-triage/` (`current-baseline`, `native-baseline`,
`ready-20261001`; git-ignored). Not yet launched on 0.3.1.1 when this entry was written.

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

### Snapshot taken after the update (Oct 2 2026 13:08 local)

`feature/update-triage/snapshot-20261002-130826-356112` was captured **after** Steam had already installed 0.3.1.1, so it
records the new files, not a before state. The 0.3.1.0 evidence that still exists: `current-baseline`, `native-baseline`
(with the old `GameAssembly.dll` and metadata) and `ready-20261001`. Its `Latest.log` is the old 0.3.1.0 log, not a new run.

Compared with `native-baseline` (SHA-256):

- **Changed:** `GameAssembly.dll` (84,700,160 -> 84,986,368 bytes), `global-metadata.dat` (21,471,540 -> 21,566,308),
  `globalgamemanagers`, `boot.config`. Metadata version is still 31.
- **Unchanged:** `GunmanContracts.exe`, `UnityPlayer.dll` (same Unity engine build).
- **MelonLoader's generated `Il2CppAssemblies` are identical to before** (Assembly-CSharp 3,478,016 bytes, HurricaneVR the
  same). The game binary changed but the interop was not regenerated, so the game has not been launched since the update.
  The first launch will show whether MelonLoader regenerates them. Save the generator messages from that launch
  (see UPDATE_RECOVERY.md).
- Mod DLL differences (Daredevil, GrabFix, SlowMotionHands, Killmes-Wicked-Buttery-Knives) are our own builds and a mod
  added since the baseline, not the update.

### What could affect our mods

Ordered by how likely it is to touch something we ship. "Why" points at the code or note that makes it relevant.

| Change | Could affect | Why | Check |
|---|---|---|---|
| **Object Culling (Outpost only)**: renderers/layers of static scenery switched by distance (see the code diff) | Radar Sense (renderer toggling); Weapon Framework / Daredevil only if a mod object sits under a culled root | **Revised after the code diff:** the game does not `SetActive` objects. It flips `Renderer.enabled` for "small objects" and changes `GameObject.layer` for background roots, with per-layer camera cull distances. Enemies and mod-spawned clubs are runtime instances, so they are unlikely to be in its lists (not proven). Radar Sense also switches enemy renderers, so check the two do not fight. | Outpost run: Radar Sense silhouettes of far enemies, a thrown club far away and recalled (F8), `[Cull]` lines in the game log. |
| **Retrieve-lost-weapons button on the terminals** | **Weapon Framework, Daredevil (clubs), Better Bow, VR Holster Customization** | The terminal is the screen WF patches (`printInfo`, `pickWeapon`, panel buttons). "Not held or holstered" is decided by the game, which does not know about our back holsters or mod entries. It could treat a mod-holstered weapon as lost and duplicate it, or fail for mod items. Overlaps Billy Clubs' own F8 recall. | Open the arsenal with and without mod entries: panel layout, WF's hidden ammo/modify/buy buttons, Retrieve still works, the new button with a club on the back holster and with a club thrown away. |
| **Savegame backup / broken-save detection** | **Weapon Framework** (save guard), VR Holster Customization (saved holster layout) | WF edits what the game writes (`saveSpotLarge` in `SaveData_WeaponSetups.json`, clamped to the last game index). A new validator could treat a mod index, a holster entry for a removed mod, or a changed file as "broken" and restore an older backup, which would roll back real progress. | `Tools`-side: `WeaponFramework/Check-WFSave.ps1` before and after. Retrieve a mod entry, holster, quit, relaunch. Look for backup files created in the save folder and any "restored" message. Test with the mods disabled too. |
| **Hand-stuck-on-geometry checks (VR)** | **Slow Motion Hands, Better Bow, Grab Fix, Weapon Framework, VR Holster Customization** | Changes how the physics hand follows the controller. Slow Motion Hands rescales the hand spring/force/damper. Better Bow 1.0 measures the quiver reach from `HVRHandGrabber.ControllerHandTarget` because the hand stops at a wall; our draw/holster reach checks assumed the old behaviour. The change may also make that workaround unnecessary. | Draw from the back holster with a wall behind you. Slow Motion Hands' hand-gap line (slow vs normal). `hand held back N cm` in the Better Bow log. |
| **Pistol stabilizing grab (VR)** | **Fire Selector, Grab Fix, Throw Assist (pistols)** | Fire Selector hooks the support hand's foregrip grab (`OnHandFrontalStabilizerGrabbed`). Grab Fix widens support-hand pickup and treats pistols specially for throw assist. | Two-hand a pistol: stabilise, hold A/X for the fire selector, release, throw a pistol. |
| **Shotgun haptics (VR)** | Fire Selector (haptics), Physical Dodge/Heavy Melee (haptic helpers) | Fire Selector pulses the support hand for mode changes. Shotguns now vibrate on firing, so the two cues may stack or mask each other. Cosmetic. | Fire a shotgun with and without a selector change. |
| **Volumetric blood re-added** | Radar Sense, FrameProbe, perf baselines | New particle/volume effect on hits. May cost frame time in fights and changes the transparent-effect layer Radar Sense draws over. Frame time numbers from 0.3.1.0 are not comparable on gunfight scenes. | Compare a Radar Sense session for artefacts. Re-take a FrameProbe baseline for any perf claim. |
| **Teleport fixes (VR)** | Enemy Awareness Fix, Physical Dodge (indirect) | Both use the player's tracked position. A teleport that no longer glitches through walls changes how the position jumps, not what we read. Unlikely to matter. | Only if awareness logs show the player position jumping. |
| **No Damage bonus counted correctly** | Physical Dodge (shots miss), Heavy Melee | Our mods change how often the player gets hit. A correct bonus may now be earned more often. | Watch the contract result screen after a clean Physical Dodge run. |
| **Vignette now working** | Slow Motion Hands, Radar Sense (visual) | A comfort vignette now draws, possibly during slow motion and dodges. | Look during a dodge. |
| **Steam-Stats sync** | None expected | Reads credits/spraykits/progress, no hooks of ours. Matters for save safety only through the backup system above. | None. |
| **News section in the main menu** | Mod Settings (flat Ctrl+M board), main-menu UI | New main-menu UI. Mod Settings opens from the phone/Ctrl+M, not the menu. | Open Ctrl+M on the main menu once. |
| **NonVR: 1911 hand position, shotgun empty reload, dual wield reload** | FLAT_MODE.md only | Flat guns wrap the HVR guns. No flat mod audit item depends on these. | Skip unless testing flat. |
| **"more smaller issues fixed"** | Unknown | Not itemised. | Run the full triage workflow. |

### What this update might fix that we were working around

Nothing is confirmed. These are the items that match problems recorded in our notes:

- **Hand stuck on geometry** matches the Better Bow quiver-reach workaround and Grab Fix's hand notes. If the game's own check now works, some of our guards become redundant (and could double-correct).
- **Pistol stabilizing grab stability** may relate to support-hand grab issues Grab Fix and Fire Selector handle.
- **Lost weapons** is the same problem Billy Clubs' F8 recall and WF's retrieval solve for modded weapons.
- **Spawn-validation exceptions** (`ANBNpcSpawner.spawnPointValidation` → `outOfSight`, present in the 0.3.1.0 log) are not mentioned. Check whether they still appear; they predate this update and were not tied to a mod.

### Code diff 0.3.1.0 -> 0.3.1.1 (Oct 2 2026, `il2cpp_tools/snapshot.py`)

Maps: `feature/update-triage/re-baseline-with-nested.json` (old, hash `02e61d48...`) vs `after-0.3.1.1.json`; result
`game-diff-0.3.1.1.json` (git-ignored). Scope ANB*/HVR*/GD_* types plus nested, 625 types. **It sees method signatures and
field offsets only: logic changes inside an unchanged signature are invisible.** The tool also lists ~140 "added/removed"
nested types (`...+ANBHitBodyEvent` under unrelated parents); those are the metadata's nested-type attribution shifting,
not real changes, and are ignored. There are 17 real type changes. Almost every `ANBGameLogic`, `ANBUIManager` and
`ANBGunwall` field offset moved (+8 to +40 bytes) because fields were added. **Named access through the regenerated interop
is unaffected; raw offsets are not** (none known in our mods).

**No method or field used by any of our mods was removed or renamed.** Removed: `ANBGameLogic.LoadMap(string)` (our
`LoadMap` hits are Daredevil's own texture helper), `tempToggleCollisions`, `TemporarilyDisableHandCollisions`,
`ANBAutoScroll.SnapTo(GameObject)`, `ANBHighscores.CheckSteamBest`, the one-argument `AddAchievementStat`.
Our Harmony targets in `ANBGunwall` (`pickWeapon`, `endAnimation`, `printInfo`), `ANBGunwallSpot` (`grabGunCall`,
`placeGunCall`) and `ANBSaveData.SaveWeapons` still exist. The tool lists 8 mods as candidates (BetterBow, Daredevil,
EnemyAwarenessLog, PhysicalDodge, SlowMotionHands, ThrowAssist, VRHolsterCustomization, WeaponFramework) only because they
patch `ANBGameLogic`, which changed everywhere. That is a flag to check, not a break; the first launch loaded all of them
with no patch error.

What each real change is (read from the disassembly where marked):

- **Savegame validation (read, `ANBSaveData.SaveFileValidation`).** At start each of the 7 `SaveData_*.json` files must read as
  a non-empty string for named keys (`SaveData_WeaponSetups`: `gunSetups` and `holsterSetup`; Core: `allMapsPlayed`; Tutorials:
  `allTutorialsSeen`; Highscores: `allHighscores`; Purchases: `PC_W`). It checks key presence, not values. An invalid file is
  moved to `BrokenSavefiles` and replaced from `Backup`. Weapon Framework only changes `saveSpotLarge`, which is not validated,
  so it should never trip this. **Checked on disk after the first launch:** `Backup/` exists next to `Data/` with copies of all
  7 files, no `BrokenSavefiles` folder, `saveSpotLarge = 5` (the framework's clamp held), `Check-WFSave.ps1` PASS. The backups
  look like a copy of the current file, so a file we corrupt could be copied into the backup too; validation does not protect
  against wrong values.
- **Retrieve lost weapons (partly read).** `ANBGunwall` gains `returnGunsToWall`, `returnGunsToWallLogic(bool check)`,
  `returnGunsToWallAvailable`, `returnGunButton`, `currentGunSpots` (array) and an `Update`. It walks the game's own gun spots
  and compares each against about six held/holstered references. Weapon Framework's mod entries are not in the game's gun-spot
  lists, so the button should ignore them. **Whether the game counts a gun in our back holster (the game's own shoulder
  sockets) as holstered is unknown.**
- **Hand unstuck (names only).** `TemporarilyDisableHandCollisions(Transform, float)` was replaced by
  `TemporarilyDisableHand(GameObject, HVRGrabbable)` with a new `handUnstuckingDuration` field; `HVRTeleporter` gains
  `TeleportOriginCheckPos`. No mod references these.
- **Grabbable auto-release (use not found).** `ANBHVRGrabbable` gains `autoReleaseDelay` / `autoReleaseTimer`. None of its own
  methods touch them, so what uses them is unknown. Daredevil clubs are copied from a game weapon: **watch for a held club or
  arrow dropping by itself.**
- **Object culling (read).** New types `GD_CullDistanceManager`, `GD_CullDistanceOverride`, `GD_CustomCullJob`,
  `GD_DistantCubemapManager`, `GD_RTLUpdateJob`, and `ANBGameLogic.GD_DistanceCulling`. `ApplySmallObjectChanges` sets
  `Renderer.enabled`; `ProcessCullingData` reads renderer bounds and changes `GameObject.layer`; it logs
  `[Cull] created native arrays ...`. It targets scenery, not NPCs.
- **Steam stats, news, restore.** `BackupProgressViaStats`, `RestoreValuesFromStats`, `SyncSteamBest`, `ANBSteamNews*`,
  `ANBUIManager.LoadNews/OpenNews`, restore-from-Steam prompts. UI and save only.
- **Scene loading and misc.** `ANBGameLogic.LoadMap(name, wait)` + `LoadMapExec` coroutine + `MapLoadRangeWithIntro`,
  `exitingRange`, `forceRangeIntro` (maps now load with a delay and a Range intro; matters for scene-change hooks such as
  Radar Sense's "radar off (scene change)", Daredevil's club restore and the awareness log's scene header);
  `saveHold`/`savePause*` (save throttling); `ANBHighscores` fields (no-damage bonus, resend); `ANBTargetRun.missedAllowed`.

### Test plan / checklist for the next session

Mod behaviour first (one Outpost contract is the real risk), then the quick items. Write what you saw next to each box; a missing
log line means unknown, not pass. Weapon Framework, Better Bow, Fire Selector, Throw Assist and Heavy Melee have `DebugLog = false`
now; turn them on for the session if you want detail (Radar Sense, Grab Fix, Enemy Awareness Fix/Log already log).

**Before launching**
- [ ] Game closed. `./Tools/Update-Triage.ps1 Snapshot` is already taken (`snapshot-20261002-130826-356112`). Optionally `-ArchiveNative` to keep the 0.3.1.1 binary.
- [ ] Bootstrap line says 0.3.1.1 and the interop is not regenerated again.

**A. Arsenal, Weapon Framework, saves (highest risk)**
- [ ] The Range terminal shows the new Retrieve-lost-weapons button; our entries (Crowbar, Billy Clubs) still show picture and no ammo/modify/buy buttons.
- [ ] Throw a game gun away, press the new button: it returns to the wall. Guns held or holstered are not treated as lost.
- [ ] Put a game gun in a **back (shoulder) holster** and press the button: counted as holstered, or duplicated?
- [ ] Throw a Billy Club away, use the game's button, then F8: no duplicate clubs, nothing breaks.
- [ ] Retrieve the mod entry, holster, quit, relaunch: `Check-WFSave.ps1` PASS (`saveSpotLarge` <= 5), `Backup/` updated, **no `BrokenSavefiles` folder**, no "savefile replaced" warning on the main menu.
- [ ] Optional: move `Daredevil.dll` and `WeaponFramework.dll` out, start: game and save still fine.

**B. The Outpost, 3-4 waves**
- [ ] `[Cull]` lines in the log, no new exceptions. The 0.3.1.0 log had a `spawnPointValidation` trampoline exception: gone, same or worse?
- [ ] Radar Sense (slow motion or Always): far enemies still glow with no flicker from the game's renderer culling (`Reveal = Moving` and `All`).
- [ ] Enemy Awareness Fix/Log: enemies search where they lost you, wave spawns still get rough guesses, `STUCK?` count not much worse than the 0.3.1.0 baseline.
- [ ] Throw a club far, walk away, recall with F8. Holster tubes and belt clubs survive a checkpoint restart and a contract end.
- [ ] Use a headset overlay for any performance claim (volumetric blood and culling both change frame time; FrameProbe has no CPU/GPU data).
- [ ] Contract end screen: no-damage bonus after a clean Physical Dodge run.

**C. Hands and grabs (new hand-unstuck system)**
- [ ] Grab Fix: pick up near a wall or table, a pistol with the palm, enemies only with the palm on them; catch lines still appear.
- [ ] Back-holster draw with a wall close behind you; Better Bow quiver draw near a wall (does `hand held back N cm` still appear?).
- [ ] Hold a club, an arrow and a gun for 10+ s: nothing releases by itself (new `autoRelease*` fields).
- [ ] Slow Motion Hands: trigger slow motion and swing; the end-of-session summary compares hand gap slow vs normal.
- [ ] Teleport (if used) in the start area and next to walls; Daredevil `restored N holstered club(s)` still follows teleports and scene loads.

**D. Weapons**
- [ ] Two-hand a pistol (stabilizer), hold A/X for Fire Selector (auto/burst/single), release, throw the pistol (Throw Assist).
- [ ] Shotgun: new haptics on firing; Fire Selector pulses still readable; fire selector on a shotgun.
- [ ] Heavy Melee / Knee Shot Stun: one leg shot (kneel 4 s longer), one hit on a downed enemy.
- [ ] Throw Assist: knife throw (blade first), thrown pistol damage.

**E. Menus and misc**
- [ ] Main menu News section opens; Mod Settings (Ctrl+M or phone tile) opens and applies a change live.
- [ ] Scene changes (MainMenu, Range, contract): the new load delay and Range intro do not break Radar Sense "radar off (scene change)", the club restore or the awareness log's scene header.

**After**
- [ ] Run the triage on the new `Latest.log` (UPDATE_RECOVERY.md) and fill in "Result" below, one line per item.

### Result

**First launch, Oct 2 2026 13:14-13:19 (MelonLoader log; user also reported Billy Clubs, Weapon Framework, Grab Fix and
Better Bow working by hand; smaller mods not yet tried):**

- Bootstrap reports **Game 0.3.1.1**, Unity 6000.0.41f1, MelonLoader 0.7.3. Metadata read as v31.1.
- **MelonLoader regenerated the interop on its own** ("Assembly Generation Needed", Cpp2IL then Il2CppInterop, no errors).
  `Assembly-CSharp` 3,478,016 -> 3,478,528 bytes; `Il2CppHurricaneVR.Framework` 8,406,016 -> 8,460,288 (+54 KB, so the
  framework gained code). No clean-output workaround needed.
- IL unstrip: 11,007 successful / 1,448 failed bodies, 1,108 methods and 13 fields not restored. **No 0.3.1.0 generation log
  exists to compare against**, so whether these numbers changed is unknown.
- **21 mods loaded, 0 `[ERROR]`, 3 warnings, all three already in the 0.3.1.0 log:** `Class::Init signatures have been
  exhausted`, Frame Probe's missing `ProfilerRecorder` constructor, Daredevil "no club template yet" (expected on the main menu;
  the template was built from the crowbar on entering The Range). The `spawnPointValidation` trampoline exception from the
  0.3.1.0 log did not occur, but no contract was started, so that proves nothing.
- Scenes visited: MainMenu, The_Range_001, MainMenu. **No contract, no Outpost.** So Object Culling, enemy awareness, the
  new Retrieve-lost-weapons button, savegame backup and shotgun/pistol-stabilizer changes are still **untested**.
- Seen working in the log: holster setup (VR Holster Customization), `restored 2 holstered club(s)` after returning to the
  menu, Weapon Framework `arsenal (load): 2 mod entries`, crowbar on the wall, Daredevil clubs grabbed/swung/holstered,
  Radar Sense on in The Range (3 enemies, `mod 0.003 ms/frame`), Mod Settings menu built, Grab Fix detectors on every scene,
  Slow Motion Hands attached. Frame rate held 90 fps in The Range (1% lows 50-78 fps; the hitches are scene loads).
- Not exercised: Slow Motion Hands' slow-motion path, Fire Selector, Throw Assist, Heavy Melee, Knee Shot Stun, Physical
  Dodge (these need enemies or a contract).
