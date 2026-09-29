# Weapon Framework test strategy

Every framework release runs **Gate A** (build + install) and **Gate B** (in-game core). Before a Nexus upload, also
**Gate C** (save + removal). New features add their own section under Gate D.

Helper after every session (game closed): `pwsh -File WeaponFramework\Check-WFSave.ps1` prints `saveSpotLarge` with
PASS/FAIL, the framework's log lines, and any wall errors. It also works **while the game runs**: Retrieve writes the
save file at once and MelonLoader writes `Latest.log` live, so several save checks fit in one session. Keep `[WeaponFramework] DebugLog = true` while testing.

Evidence the helper catches (Sep 28 22:24 session, 0.1.1): log `save: wall index 6 is a mod entry - saved as 5`, but
`SaveData_WeaponSetups.json` written the same second holds `"saveSpotLarge":6` → FAIL. That was the 0.1.1 bug.

## Gate A: build and install (no game)

| # | Check | Pass |
|---|---|---|
| A1 | `dotnet build WeaponFramework\WeaponFramework.csproj -c Release -o feature\WeaponFramework-<ver>` | 0 warnings, 0 errors |
| A2 | `dotnet build WeaponFramework\Example\ExampleBaton.csproj -c Release` | builds (the public API still compiles for others) |
| A3 | Game closed, back up `Mods\WeaponFramework.dll` + `SaveData_WeaponSetups.json` to `feature\WeaponFramework-backup-before-<ver>-*`, copy the new DLL | SHA256 of the installed DLL = the built one |
| A4 | Start the game | log `Weapon Framework v<ver>`, `[Weapon_Framework] loaded`, `registered 'BillyClubs'` |

## Gate B: core in The Range (5 minutes)

| # | Do | Pass |
|---|---|---|
| B1 | Enter The Range | `arsenal (load): 1 mod entry added after the game's 6 (BillyClubs)` once, no duplicates |
| B2 | Page the Big Guns panel with `+` to the end | `ARSENAL 007 / 007`, `BILLY CLUBS`, the picture, no ammo / modify / buy buttons |
| B3 | Retrieve | `retrieved` then `settled` lines; clubs on the board, flat |
| B4 | Take both clubs (each hand, and force grab) | both grab |
| B5 | Retrieve a rifle, then Billy Clubs again | no duplicate clubs; the ones you carry are not re-spawned |
| B6 | Go to a contract and back to The Range | B1 again, exactly once |
| B7 | **Action log** (0.1.3+): retrieve a game weapon, take it off the stand, put it back, holster/draw a pistol and a knife, take a mod item off the stand | exactly one line per action (`retrieve …`, `stand: took/put back …`, `holster <side>: '…' in/out`, `stand: took '<Id>' item …`); after a scene load one `after load: stands filled (…), loadout …` line instead of ~30 stand/holster lines |

## Gate C: save and removal (before every Nexus upload)

**Save**

| # | Do | Pass |
|---|---|---|
| C1 | Retrieve Billy Clubs **last**, quit, run the helper | `saveSpotLarge` 0-5 (**this is the 0.1.2 fix test**) |
| C2 | Retrieve a game weapon last (e.g. the bow), quit | `saveSpotLarge` = that weapon's index (the guard leaves game entries alone) |
| C3 | Retrieve Billy Clubs, then on the **pistol** wall open Modify and change an attachment (gun editing also calls `SaveWeapons`, while the Big Guns wall still points at the clubs) | still 0-5 |
| C4 | Restart after C1 | the wall opens on a game weapon, no errors |

**Removal** (rename a DLL to `.dll.disabled`; MelonLoader skips it)

| # | Do | Pass |
|---|---|---|
| C5 | After C1: disable `Daredevil.dll`, start, open the panel, page through it | 006 / 006, no `ArgumentOutOfRange` / wall errors, framework logs `loaded` and nothing registered |
| C6 | Enable Daredevil, disable `WeaponFramework.dll` | Daredevil still loads and warns `required mods not installed: Weapon Framework`; no errors; clubs already on the belt are restored; no terminal entry |
| C7 | Enable both | back to Gate B state; belt clubs from before C5 still there |

## Gate D: robustness and features

| # | Do | Pass |
|---|---|---|
| D1 | **Two mod weapons:** also install `ExampleBaton.dll` | 007 and 008 both work; retrieving one hides the other (`OnHidden`); C1 still passes with either selected |
| D2 | **Callback crash:** a test build of the example that throws in `OnShown` | error logged, panel keeps working, other entries fine |
| D3 | **Flat mode** (no headset) | panel shows the entry; note exactly what happens on "take" (roadmap section 4 decides from this) |
| D4 | **Pistol wall untouched** | `saveSpotSmall` unchanged by anything above |

Upcoming features add rows here when built:
- **Back holsters (0.2.0):** with a back side EMPTY of game guns: release the crowbar / a club slowly at the shoulder →
  `holster back-left: 'Crowbar' in (mod)`; it rides on the back; grab it back → `out (mod)`; the game can't put a gun
  on that side while it's full; change `TiltDeg` etc. on the Mod Settings board → it moves live; restart / enter a
  contract → `back holsters restored: …`.
- **Test entry (crowbar):** spawns on the terminal slot, grabs, is not duplicated on re-retrieve; with Billy Clubs it
  gives 007 + 008 (D1 without the example); with `TestEntries = false` it vanishes and C5-style removal passes.
- **Sockets (wall return, holsters):** release near the wall / a slot snaps it in, ghost shows, state survives scene
  load and restart, back holsters accept framework items, Mod Settings moves slots live.
- **JSON packs:** a bad field is logged by name and only that pack is skipped; reload on entering The Range works.

## Results so far

- **0.1.2 (Sep 29 2026): Gate C C1-C7 all PASS.** Action log B7: partial in 0.1.4 (see GUNMAN_CONTRACTS.md); 0.1.5 fixes
  scene-change bursts, knife names and knife/gun side collisions, untested.
- **0.2.0 (Sep 29 2026):** crowbar entry retrieves and grabs (no icon; hung upright it stuck out of the slot). Crowbar
  (back-right) and a club (back-left) went IN. **FAIL:** neither could be drawn again, and the game still put the bow
  and the AB15 on those sides. 0.2.1: draw assist (grip within `DrawCm`), the block moved to `GrabGrabbable`, crowbar
  lies horizontally. Retest: draw both, try a gun on a full side (expect `holds '…' - the game's holster refused '…'`).

## Record results

One line per run in GUNMAN_CONTRACTS.md: version, gates run, any FAIL with its log line. Turn `DebugLog` off when the
framework is done being worked on (debug-log rule).
