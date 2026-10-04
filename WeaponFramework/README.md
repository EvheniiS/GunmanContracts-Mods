# Weapon Framework 0.3.0

Tested: 0.3.0 (2026-10-04)

Lets other mods put their own weapons on the game's **arsenal panel** in The Range (the Big Guns wall with the
rifles, shotguns and the bow). The player pages to the entry with `+` / `-`, presses **Retrieve**, and the wall slot
comes out with the mod's items on it, ready to take. No keyboard, no UnityExplorer.

First user: **Daredevil** 0.3.3 (`Daredevil/BillyClubs/Arsenal.cs`). Without this DLL it still works, with the
spawn key only.

Install: `Mods/WeaponFramework.dll`. Config `[WeaponFramework]`: `DebugLog` (default off).

**Adding your own weapon: [MODDING_GUIDE.md](MODDING_GUIDE.md)** (step by step, with the two game traps) and the
compiling example [Example/ExampleBaton.cs](Example/ExampleBaton.cs). Release files: [`release/WeaponFramework/`](../release/WeaponFramework/).

## For mod authors

Reference `WeaponFramework.dll` (not copied into your output) and register once:

```csharp
[assembly: MelonOptionalDependencies("WeaponFramework")]   // if your mod should also run without it

WeaponFramework.Arsenal.Register(new WeaponFramework.ArsenalWeapon
{
    Id = "BillyClubs",                  // unique, letters and digits
    DisplayName = "Billy Clubs",        // the panel prints it upper case
    Description = "...",
    Icon = texture,                     // white shaded render, transparent, ~2.15:1 (the bow's is 1364x635)
    OnShown = mount => { /* hang your items on `mount`, kinematic, parented; fill only what is missing */ },
    OnSettled = mount => { /* the slot finished sliding in: measure / align against the wall here */ },
    OnHidden = () => { /* another entry was retrieved: the slot goes back into the wall, with your items */ },
});
```

Keep the call in its own `[MethodImpl(NoInlining)]` method and check that the `WeaponFramework` assembly is loaded
first, so nothing touches its types when it isn't installed (see `Daredevil/BillyClubs/Arsenal.cs`).

**Two game traps for spawned items** (found with Billy Clubs):
- **The game's `GD_HVROptimiser` switches grabbables off.** It collects every grabbable once, including inactive ones
  (so your hidden template too), disables them all, then re-enables only those in range and in front of the player.
  A copy of a disabled template starts ungrabbable. After `Instantiate`, set every `HVRGrabbable.enabled = true` and
  remove them from `GD_HVROptimiser.instance._grabbables` (see `Daredevil/BillyClubs/Optimiser.cs`).
- `Description` is drawn over the name on the panel; the game's own weapons leave it empty.

Your items are your own: the framework never spawns, holsters or saves them. A grab is a normal HVR grab
(`HVRGrabberBase.GrabGrabbable`), so unparent and give physics back there, as Billy Clubs does for its belt.

## How it works

The game builds the arsenal in `ANBGameLogic.LoadAssetLoop`. For every prefab in `ANBDataCollection.allOthers`
that isn't a knife (that's the bow) it instantiates `othersSlotPrefab` (`WeaponSlot_Big`) under
`ANBGameLogic.WeaponFolder`, fills the `ANBGunwallSpot` inside it and appends the slot to `gunWallLarge.Items` and
the weapon name to `gunWallLarge.ItemsID`. The panel reads everything it shows from the `ANBWeaponType` on the spot's
`WeaponPrefab`; Retrieve (`ANBGunwall.pickWeapon`) moves `Items[currentSpot]` to the wall's `mainSpot`.

After the game logs `Processed Completed Gunwall` (`ANBGameLogic.LogLoadTime`), this mod adds one slot per
registered weapon the same way, with these differences:

- `WeaponPrefab` is an inactive object that only carries an `ANBWeaponType` (name, description, icon, price 0).
- The slot's gun socket (`gunstorage`, a `DemoHolster`, which throws on anything that isn't a gun or knife) is
  switched off before the slot wakes; `initSlot` is never called for it. The mod's items go on a mount at the
  socket's gun position.
- Not added to `allGunSpots` / `allGunSpotObjects` (the loadout code walks those).
- Always owned (`checkPurchaseDataWeapon` prefix), without the game's free auto-purchase, which would write the id
  into the save.
- **Save safety:** Retrieve saves the wall index (`saveSpotLarge` → `ANBSaveData.SaveWeapons`, file
  `SaveData_WeaponSetups.json`). A mod index there would point past the list once the mod is removed, so the save
  always gets the last game index instead. **0.1.1 guarded `SavePurchases`, which never writes that key, so 0.1.1 did
  save mod indices** (seen Sep 29 2026: `"saveSpotLarge":6`); 0.1.2 guards `SaveWeapons`. An old save heals itself
  the first time 0.1.2 saves (any Retrieve or gun edit).
- The panel's ammo, modify and buy buttons are hidden for mod entries (`printInfo` postfix).

Log lines (`DebugLog = true`): `arsenal (load): N mod entries added after the game's 6 (...)`, `'Id' settled - mount at ...`,
`save: wall index 6 is a mod entry - saved as N`, and since 0.1.3 one line per player action (`ActionLog.cs`):
`retrieve big #7/7 'BillyClubs' (mod)`, `retrieve pistol #3/9 '…'`, `stand: took 'CompoundBow'` / `put back`,
`stand: took 'BillyClubs' item 'BillyClub-1' (RightHand)`, `holster right: 'M9' in` / `out`. While a scene loads the game
fills every stand (~30 put/take calls) and restores the loadout; since 0.1.4 that is one line:
`after load: stands filled (18 put, 11 taken), loadout backRight CompoundBow, right ShadowGuard2` (window = 6 s after the
scene start, +2 s after every `LogLoadTime` step). Knives are named like the stand names them (`CombatKnife_v3`).
Leaving a scene empties every stand at once; since 0.1.5 four or more stand/holster events within 0.2 s of each other
become one `N stand/holster events at once (…) - scene change` line. Knife holsters show as `knife-left/right`.
Repeats within 0.5 s are dropped.

## 0.2.x: shared item code, back holsters, test crowbar (Sep 29 2026, tested through 0.2.3)

This section records the earlier 0.2.x implementation. In 0.3.0, holstering, docking, item shape, and holster logs moved into [VR Holster Customization](../VRHolsterCustomization/README.md). Install `VRHolsterCustomization.dll` with `WeaponFramework.dll`; the framework now requires it. `Items.MakeTemplate` and `Items.Spawn` remain, while the other `Items` helpers forward to the holster mod. The test crowbar registers with `VRHolsterCustomization.Holsters`.

- **`Items`** (`Items.cs`, moved from Billy Clubs): `MakeTemplate` (copy under an inactive holder, nothing wakes),
  `Spawn` / `Manage` (grabbables on, out of `GD_HVROptimiser`, re-checked once a second, skipped by the game's
  `ANBGeneratePhysics.resetMe`), `Hang` (pinned to a wall mount until a hand takes it), `Measure` (long axis from the
  longest box collider, grip end = the end nearer `GrabPoint_Base`).
- **`Holsters`** (`Holsters.cs`): back holsters for mod items **on the game's own back holsters** (`HVRShoulderSocket`
  left/right). **One weapon per side**: ours is free only while the game's socket is empty, and the game's socket can't
  take a gun while ours is full (`CanHover` postfix). Mods register a `HolsterKind { Id, IsMine, Spawn }` and call
  `Holsters.TryHolster(go)` on release. Pose settings `[WeaponFrameworkHolsters]` (ints, live on the Mod Settings
  board): `OutCm UpCm BackCm DropCm TiltDeg LeanDeg SpinDeg SnapCm`, mirrored left/right; `SavedBackHolsters` restores
  after the game's own loadout on every load. The first run logs the socket positions for calibration.
- **Test crowbar** (`TestCrowbar.cs`, `[WeaponFramework] TestEntries = true`): a copy of The Range's crowbar on the
  panel, built only from `Items` + `Holsters`; one crowbar at a time.
- Daredevil 0.3.5 registers its clubs as kind `BillyClub` (belt holsters stay Daredevil's own for now).

## Status and limits (0.2.4)

- **0.2.1-0.2.3 tested (Sep 29 2026):** back holsters work with either hand (draw assist: grip within `DrawCm` of the
  item), the game refuses a gun on a full side, items carry into contracts, crowbar picture shows, 3 kg crowbar fine.
- **0.2.4 (installed, untested):** buzz when a held mod item reaches a free back holster, pose moved to where the hand
  actually reached (`OutCm -13`, `DropCm 30`, `BackCm 7`), put-back up to 5 m/s, waiting sides kept (below).
- **Known limit: a contract started straight from the main menu has no crowbar/clubs on the back.** Their template is
  copied from The Range's crowbar, which exists only in that scene. The saved side now waits (`back holsters restored:
  back-right 'Crowbar' waits …`) and comes back in the first scene after a visit to The Range; before 0.2.4 it was
  dropped for good. Daredevil's belt clubs have the same limit. A real fix needs the template without The Range
  (see ROADMAP).

- **Tested in game (Sep 28 2026) with Daredevil 0.3.3:** panel entry, retrieve, grabbing, the pair fitted flat on
  the board after `OnSettled`. 0.1.2 (save guard on the right method) is built, not tested: retrieve the mod entry,
  quit, check `SaveData_WeaponSetups.json` holds a game index (0-5).
- Removal, dev flow, holsters, flat mode: [ROADMAP.md](ROADMAP.md). Test gates: [TESTING.md](TESTING.md) + `Check-WFSave.ps1`.
- Big Guns wall only (the pistol wall builds its slots differently). The Range only (the only scene with the arsenal).

```powershell
dotnet build WeaponFramework/WeaponFramework.csproj -c Release -o feature/WeaponFramework-0.2.0
dotnet build WeaponFramework/Example/ExampleBaton.csproj -c Release   # the example (not shipped)
```
