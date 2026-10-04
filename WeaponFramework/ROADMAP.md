# Weapon Framework roadmap (Sep 29 2026)

What happens when mods are removed, how to make adding a weapon easier (JSON packs), how to move holstering into the
framework, and what flat mode needs. Flat-mode findings for all mods: [../ROADMAP.md](../ROADMAP.md) §4 and [../GAME_KNOWLEDGE.md](../GAME_KNOWLEDGE.md) §8.

Status legend: **checked** = read in the game code or files; **inferred** = follows from the code but not seen in game;
**untested** = needs a run.

## 1. Removing mods: what happens

The framework never writes a mod weapon's id into the game's save, and the items belong to the weapon mod, so removing
things is mostly harmless. One real bug was found and fixed while checking this (0.1.2, below).

| What is removed | Result | Status |
|---|---|---|
| The weapon mod (framework stays) | Nothing registers, no extra slot is built, the wall shows the game's 6 entries. The mod's items (on the belt, in hand, on the wall) simply don't exist next launch. Its cfg section stays in `MelonPreferences.cfg`, unused. | checked in code |
| The framework (weapon mod stays), **optional** dependency | The weapon mod runs without the panel entry. Daredevil lists the framework as required (Sep 29: the terminal is the only player way to get clubs; F8 is a dev key) but still loads, warns, and keeps belt clubs. | checked in code |
| The framework, **required** dependency (`MelonAdditionalDependencies`) | MelonLoader refuses to load the weapon mod and logs which dependency is missing. The weapon is gone until the framework is back. | MelonLoader behaviour |
| One of several weapon mods | Entries after the game's 6 shift up by one. Harmless, because the wall index is never saved (0.1.2). | inferred |
| Anything, while its item is holstered | Daredevil keeps its belt state in its own cfg entry `[BillyClubs] SavedHolsters`, not in the game save. The game's holsters refuse a club, so `SaveLoadout` never sees one. | checked |

### ★ Bug found: 0.1.1 saved the mod's wall index anyway (fixed in 0.1.2, built, untested)

- `ANBGunwall.pickWeapon` sets `savedSpot = currentSpot`, copies it to `ANBDataCollection.saveSpotLarge` (0xd0), then
  calls **`SavePurchases`** and **`SaveWeapons`**. Only **`SaveWeapons`** writes the key `"saveSpotLarge"`
  (to `SaveData_WeaponSetups.json`). `SavePurchases` writes `PC_W` / `PC_C` (purchases) and never touches it.
- 0.1.1 swapped the index around `SavePurchases`, so the swap did nothing. **His live save proves it:**
  `%USERPROFILE%\AppData\LocalLow\ANB_Seth\GunmanContracts\Data\SaveData_WeaponSetups.json` has `"saveSpotLarge":6`,
  which is the Billy Clubs entry (the game's own are 0-5). The `save: wall index 6 is a mod entry - saved as N` log
  line was printed, but it described a save that never included the index.
- **Removal effect with such a save (inferred):** at start `ANBGunwall.WaitForStart` calls `autoStart` (which clamps
  `currentSpot` into range), **then** sets `currentSpot = savedSpot` with no clamp. With the weapon mod gone, that is
  index 6 in a 6-entry list, so the panel code (`printInfo`, `pickWeapon`, both read `Items[currentSpot]`) most likely
  throws until the player pages with `+`/`-`. Not a save-file corruption, but a broken-looking wall.
- **0.1.2 guards `SaveWeapons`** (callers: `pickWeapon`, gun editing via `ANBWeaponAttachments.saveNow/toggleEditor`,
  `revertAllWeaponData`, `LoadWeapons`). **C1 passed Sep 29.**
- **Test:** retrieve Billy Clubs, quit, the JSON must show 0-5. Then rename `Daredevil.dll` to `.disabled`, start,
  and check the wall panel opens on a game weapon with no errors in the log.
- **No upgrade path work:** 0.1.1 had no custom weapon released with it, so hardly anyone has a mod index saved. The goal
  is a solid foundation, not migration code.

### Game holsters and mod items (corrected Sep 29 2026)

First version of this said "never use the game's holsters". Too strict. The game restores a holster in
`ANBGameLogic.LoadContractHolstersSingle` by looking the saved id up in its weapon prefab lists (`GetComponent<ANBWeaponType>`
per prefab) and instantiating the match. So:
- **Guns and knives with a real `ANBWeaponType`** (a pistol re-skin, a knife-based custom blade) can use the game's holsters if the
  framework adds their prefab to those lists. Saved and restored natively; with the mod removed the id matches nothing
  and the holster most likely comes back empty (inferred from the loop, test it).
- **Items with no gun/knife component** (clubs, crowbar, pan) can't: the sockets call `holsterGun(side, ANBHVRGunBase)`.
  They need framework slots, or a patch that lets the game's back sockets hold them while the framework saves them.

### Removal test for every weapon mod (add to the checklist)

1. Retrieve the mod weapon, holster it, enter a contract, quit.
2. Disable the weapon mod. Start: no errors, wall on a game weapon, loadout intact.
3. Enable it again: the belt state comes back (from its own state, since nothing was lost).
4. Same with the framework disabled instead.

## 2. Easier development: weapon packs (JSON)

Today a new weapon means a C# mod: a csproj, the optional-dependency dance, a template copy, the two game traps
(optimiser, `Awake` on `Instantiate`), a grab patch, an embedded icon. `Example/ExampleBaton.cs` is ~120 lines for a
plain crowbar copy. Two changes remove most of that.

### 2a. Move the shared plumbing into the framework (do this first, benefits code mods too)

| New API | Replaces | Moved from |
|---|---|---|
| `Templates.Copy("Crowbar")` → an inactive, awake-safe copy with foreign components stripped | each mod's own template code | Billy Clubs template code |
| `Items.Spawn(template, parent)` → `Instantiate`, all `HVRGrabbable` enabled, removed from `GD_HVROptimiser`, re-checked once a second | each mod's `Optimiser.cs` | `Daredevil/BillyClubs/Optimiser.cs` |
| `Items.OnGrab(go, handler)` → the `HVRGrabberBase.GrabGrabbable` prefix, unparent + physics back | each mod's `GrabPatch` | Example `GrabPatch`, Billy Clubs |
| `Wall.Hang(mount, items, layout)` → Upright / Diagonal / Cross layouts, fitted to the board in `OnSettled` | Daredevil `Arsenal.cs` layout + board raycast | `Daredevil/BillyClubs/Arsenal.cs` |

With these, `ExampleBaton.cs` drops to about 20 lines (register + `Spawn` + `Hang`).

### 2b. Data-only weapon packs

A folder the framework scans at start, no code:

```
UserData/WeaponFramework/Weapons/Machete/
    weapon.json
    machete.obj        (optional: without it the base model is used, re-coloured)
    machete.png        (texture)
    icon.png           (panel picture, optional: generated if missing, see below)
```

```jsonc
{
  "id": "Machete",                       // letters and digits, unique
  "displayName": "Machete",
  "base": "Crowbar",                     // which game item to copy: grip, colliders, damage script, throw assist
  "model":  { "mesh": "machete.obj", "texture": "machete.png", "scale": 1.0,
              "rotation": [0, 90, 0], "offset": [0, 0, 0.05] },   // relative to the base item's grip
  "icon": "icon.png",
  "mass": 2.5,
  "damage": { "kind": "Blunt", "multiplier": 1.0 },               // Blunt = ANBBluntWeapon path (base Crowbar)
  "throw":  { "assist": true },
  "wall":   { "count": 1, "layout": "Upright", "offset": [0, 0.1, 0] },
  "holster": { "slot": "BeltLeftBack" }                           // named slot, see section 3; omit = no holster
}
```

- **Bases:** Crowbar is proven (Billy Clubs). Knife exists (`ANBKnife`, has its own game holsters, but see the socket
  rule above). the old ideas list also named a katana: scan The Range with `il2cpp_tools/gunwall_scan.py` /
  `prefab_tree.py` for the full list before promising more.
- **Model convention** (the only thing a modeller must get right): origin at the grip centre, blade/head along +Z,
  metres, Y up. The collider stays the base item's, scaled to the mesh bounds along Z.
- **OBJ loader:** a small parser (v, vt, vn, f, triangulated) into a `Mesh`; texture via `ImageConversion.LoadImage`,
  as the icon is loaded today. No AssetBundles, so no Unity Editor needed.
- **Icon:** if `icon.png` is missing, render one from the OBJ with the existing Blender pipeline
  (`BlenderRefs/render_arsenal_icon.py` + `make_arsenal_icon.py`). Better: one Blender script
  `make_weapon_pack.py` that exports the OBJ, renders the icon and writes a starter `weapon.json` in one go.
- **Validation:** one log line per pack, `pack 'Machete': ok (base Crowbar, 2.1k tris)` or the exact field that failed
  (`pack 'Machete': model.rotation needs 3 numbers`). A bad pack is skipped, never breaks the others.
- **Schema:** ship `weapon.schema.json` so VS Code autocompletes and underlines mistakes (`"$schema"` line in the
  example pack).
- **Fast iteration:** re-read every pack each time The Range loads, and on a Mod Settings button ("Reload weapon
  packs") so offsets can be tuned without restarting the game. `[WeaponFramework] ShowMount = true` draws the mount
  axes and the board plane in the world while tuning.
- **Code mods can use it too:** `Arsenal.RegisterPack(path)` for a C# mod that wants JSON for the numbers and code
  only for the special behaviour.

## 3. Holstering: move it into the framework

**Why own holsters and not the game's:** the game's sockets reject non-guns (`DemoHolster` / `HVRShoulderSocket`
`OnGrabbed` throw or call `holsterGun` without a gun/knife component, `HVRTagSocketFilter` tags), and patching them to
accept mod items would put mod ids into the game's loadout save (section 1 rule). So each mod needs its own slots, and
today every mod would rebuild what Billy Clubs has:

- find the belt (`HolsterRight`'s parent `Waist/Holsters`), make anchors;
- snap on release within `HolsterSnapDistance` (0.4 m) below `HolsterMaxSpeed` (2.5 m/s, faster = a throw);
- keep holstered items kinematic and parented, draw on grab (`GrabGrabbable` prefix);
- the ghost tube (`HolsterVisual.cs`, Rim Dissolve like the game's holster highlight);
- remember which slots are full across scenes (`SavedHolsters` cfg), refill 2 s after load (after the game's loadout);
- recall key; exclusion from `GD_HVROptimiser`; Grab Fix already skips kinematic "docked" items.

Proposed API:

```csharp
Holsters.RegisterKind(new ItemKind {
    Id = "BillyClub",
    Spawn = () => MakeClub(),            // used to refill slots after a scene load / into contracts
    IsMine = go => go.name.StartsWith("BillyClub-"),
});
Holsters.UseSlot("BeltLeftBack", "BillyClub");      // named positions, no coordinates
Holsters.UseSlot("BeltRightBack", "BillyClub");
```

- **Named slots** (`BeltLeftBack`, `BeltRightBack`, `BeltLeftFront`, `Back`, `ChestLeft`, …) with framework-owned
  positions, each tunable in `[WeaponFramework]`. Two mods asking for the same slot: the second gets the next free
  one and a log line. This is the actual simplification: authors pick a name, players tune one place.
- **Persistence in the framework's own file** (`UserData/WeaponFramework/holsters.json`, kind id per slot), never the
  game save. A kind whose mod is missing is kept but skipped, so re-installing the mod brings its items back.
- **Put back on the wall:** releasing an item near its wall mount hangs it there (open idea, see ../ROADMAP.md).
- **Migration:** Daredevil moves to it (removes the belt code in `BillyClubs.cs`, `HolsterVisual.cs`, `Optimiser.cs`),
  reading `[BillyClubs] SavedHolsters` once so nobody loses their clubs.
- JSON packs get holsters through the `"holster"` field for free.

## 4. Flat mode

Details in [../ROADMAP.md](../ROADMAP.md) §4 and [../GAME_KNOWLEDGE.md](../GAME_KNOWLEDGE.md) §8. For the framework:

- The arsenal panel and `pickWeapon` are shared by both modes, so a mod entry will show and slide out in flat.
  **Taking it is the problem:** flat pickup is `ANBFpsInteraction.grabGun(ANBFpsInteractionObject)` on the slot's
  `ANBFpsInteractionObject`, which expects a gun in the (switched-off) socket. What it does with a mod slot is untested.
- **Step 1 (cheap, do with 0.1.2 or 0.2.0):** in flat mode, hide mod entries, or show them with a "VR only" name,
  so nothing half-works. Flat detection as Mod Settings does it (`!XRSettings.isDeviceActive`, or the Low Poly
  Shooter Pack `Character` up with no VR hands).
- **Step 2 (idea, unverified): flat melee weapons through the flat knife.** Flat melee is the Low Poly Shooter Pack
  "Knife Attack" animation (`Character.PlayMelee`, `SetActiveKnife`). Swapping the knife mesh in the flat arms for the
  mod's model would give every melee pack a flat version without a new weapon system. Damage would come through the
  knife's slash path (`ANBKnifeSlasher` → `TakeKnifeSlashDamage`), so a blunt weapon needs a re-route.
- Flat guns (a new LPSP `Weapon` with animator and offsets) are out of scope.

## Order of work (revised Sep 29 2026, after his feedback)

**Test templates available in The Range (level2 scan):** `Prop-Bluntweapon-Crowbar` (blunt + throw assist), katanas
and knives. **Pans are NOT in The Range:** `Prop-LongGrabBase_Pan*` exist only in level4 and level5 (contracts; level4
also has the guitar), so a pan entry would need that level visited first in the session: not a good test item.
**Katanas and knives are not candidates either** (his correction): the game already offers them on its own melee wall,
which has a few fixed spots. That is why the game skips knives when it builds the gun wall, and why custom weapons go
on the terminal (arsenal panel) regardless of type, melee or not.

1. **0.1.2 save fix: DONE** (Gate C C1-C7 all passed Sep 29 2026).
2. **Crowbar = the framework's own test weapon — BUILT 0.2.0 (`TestCrowbar.cs`, `Items.cs`), untested.** Built into the framework (off by default,
   `[WeaponFramework] TestEntries`), using the shared plumbing (2a): a copy of the Range crowbar on the framework mount,
   the path Billy Clubs proved. Its code becomes the reference for 2a, and the second registrant for Gate D1 (two mod
   entries) without installing the example. Optional variant: a second crowbar entry with another scale/colour, to test
   three entries and paging.
   - Kept for later, not a test entry: the slot's own socket (`DemoHolster`) accepts knives, so a **custom blade built on
     a knife base** might use the game's full slot (wall ghost, put-back) instead of the framework mount. Untested.
2b. **Foundation logging — 0.1.3-0.1.5 (`ActionLog.cs`); load fold + retrieve/stand/holster lines confirmed in play; 0.1.5 burst/knife fixes installed, untested** (with DebugLog, one line per event): every wall Retrieve incl. game entries, the game's own
   stand take/put-back (`ANBGunwallSpot.grabGunCall` / `placeGunCall`) and holster in/out (`ANBGameLogic.holsterGun` /
   `unholsterGun`, `holsterKnife`), plus mod items via the socket system later. Gives the socket work a before/after
   baseline. Optional: remember a mod entry as the last retrieve in the framework's own file and reopen the wall on it
   (the save itself keeps a game index).
3. **One socket system for wall return + holsters** — **first part BUILT 0.2.0: back holsters (`Holsters.cs`) on the
   game's shoulder sockets, shared one-weapon-per-side, live pose settings, restore; clubs + crowbar use it. Still to do:
   wall return, ghost highlight, belt slots (Daredevil migration), moving the game's own holsters.** (section 3): release near the wall mount or a holster slot to snap
   in, Rim Dissolve ghost for both, named slots incl. **back holsters**, positions (the game's holsters too) on the Mod
   Settings board. Daredevil migrates (fixes clubs too close to the pistols).
4. **Pistol (Small Guns) wall** + gun re-skins registered into the game's prefab lists: needs reverse engineering of
   the `Pistolset_` / `_Pistolspot_` build first.
5. JSON packs (2b) + Blender pack script.
6. Flat mode (section 4): hide mod entries first, flat melee via the knife later.
