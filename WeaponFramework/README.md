# Weapon Framework 0.1.1

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
- **Save safety:** Retrieve saves the wall index (`saveSpotLarge` → `ANBSaveData.SavePurchases`). A mod index there
  would point past the list once the mod is removed, so the save always gets the last game index instead.
- The panel's ammo, modify and buy buttons are hidden for mod entries (`printInfo` postfix).

Log lines: `arsenal (load): N mod entries added after the game's 6 (...)`, `'Id' retrieved - mount at ...`,
`save: wall index 6 is a mod entry - saved as N`.

## Status and limits (0.1.1)

- **Tested in game (Sep 28 2026) with Daredevil 0.3.3:** panel entry, retrieve, grabbing, the pair fitted flat on
  the board after `OnSettled`. Not yet confirmed: a game restart after retrieving the mod entry (the save guard is
  logged working).
- Big Guns wall only (the pistol wall builds its slots differently). The Range only (the only scene with the arsenal).

```powershell
dotnet build WeaponFramework/WeaponFramework.csproj -c Release -o feature/WeaponFramework-0.1.1
dotnet build WeaponFramework/Example/ExampleBaton.csproj -c Release   # the example (not shipped)
```
