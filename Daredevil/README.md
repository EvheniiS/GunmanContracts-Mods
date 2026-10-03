# Daredevil 1.1.2

## 1.1.2 (built Oct 3 2026, untested)

- **Optional club return (off by default).** Same idea as the game's knives, which fly back to their holster after
  a while (and as VR Holster Customization's `[VRHolsters_KnifeReturn]` times them). Two settings in `[BillyClubs]`,
  both on the Mod Settings board:
  - `ReturnClubs` (`false`): a club you threw or dropped goes back to its belt holster by itself. Off = clubs stay
    where they land, like in the show; F8 and the arsenal terminal still bring them back.
  - `ReturnSeconds` (`10`): how long it lies where it landed first. Counted from the moment it comes to rest
    (under 0.15 m/s for 0.2 s), so a long throw never returns in mid-air. A club that is still rolling 10 s after the
    release counts as at rest.
  - The timer stops while a hand holds the club, it is holstered (belt, wall, back) or in flight, and starts again
    from zero at the next rest. The return is the F8 recall (`SendToSlot`), logged as
    `club returned to the left holster (N s after it came to rest)`. With both belt slots taken it waits and asks again.
  - Code: `BillyClubs/ClubReturn.cs`. Off costs one bool check per club per frame.
  - A club last drawn from a back holster returns there (the side it came from, else the other, else the belt), like a
    katana. Needs **VR Holster Customization 0.3.7** (older: the belt). Drawn from or put in the belt: the belt.
- Test: turn `ReturnClubs` on, throw a club at a wall and watch it go back after `ReturnSeconds`; pick it up before
  the time is up (nothing must happen); throw it down a stairwell or off a ledge; set `ReturnSeconds` to 0 and 30;
  draw a club from your back, throw it and let it lie (back holster again), then draw from the belt and do the same
  (belt again); leave `ReturnClubs` off and confirm clubs stay put.

## 1.1.1 (built Oct 3 2026, untested)

- **Holstered and wall-mounted clubs no longer collide with anything.** Their colliders turn into triggers while
  holstered (the same thing the game's holsters do to guns: `HVRSocket.DisableCollision` -> `SetAllToTrigger`) and
  turn solid again on the draw. The bow hand and enemies no longer bump into the clubs on your hips. Drawing still
  works: the hand's grab bag sees trigger colliders. Needs **Grab Fix 1.1.2** (older Grab Fix skipped trigger
  colliders in its holstered-item reach check, so a holstered club could not be drawn).
- Test: draw and re-holster both clubs; draw with the bow in the other hand; walk into an enemy with full holsters;
  throw a drawn club at a wall (it must bounce, not pass through).

## 1.1.0 release notes (release candidate on `dev`, tested Oct 2 2026; not on Nexus yet)

**Radar Sense**
- **Fixed: silhouettes disappearing on close enemies, or only a floating vest showing.** The game reuses enemies and
  gives each respawn a brand-new body. Radar Sense kept outlining the old, destroyed one. It now notices the new body
  and outlines it. A full wave session after the fix logged no missing silhouettes (the run before: 563).
- **Fixed: enemies behind a doorframe, counter or half wall losing their outline** when only their head showed over
  it. New `SeenFraction` (0.7): how much of an enemy you must see before it counts as "seen" and drops the outline.
- **New: standing enemies show too, dimmed.** `ShowStill` (on) and `StillBrightness` (0.35): an enemy you haven't
  seen that stands still now shows as a dim silhouette instead of nothing. Moving enemies, and every enemy while
  you focus, stay at full brightness.
- **New: `Brightness` (0.6)** darkens the silhouette colour so it stays below the game's bloom (no red aura merging
  neighbouring enemies). **`Detail`** (Core / Body / Clothes / Full, default Body) picks how much of the body is
  outlined: fewer meshes, cleaner look, less work. All three apply live from the Mod Settings board.
- `IncludeLods` (off): outlines lower-detail body meshes too. Leave it off: it draws every detail level on top of
  each other (about 4x the meshes) and fixes nothing.
- Cost measured in a full wave session: 0.02-0.05 ms per frame for the mod's own work.

**Billy Clubs**
- **Snappier club in the hand.** The physics hand now turns toward your controller 1.6x as hard
  (`HandTorqueScale`), and a held club may spin up to 80 rad/s (`ClubMaxSpin`, the game's limit is 30), so fast
  wrist flicks no longer leave the club lagging behind.
- `ClubInertiaScale` and `HandStrengthScale` (both 1 = game default) for further tuning; `Mass` now applies live,
  even to a club in your hand.
- A thrown club to the body stuns for 1 s (`ChestStunSeconds`, was 3).
- Diagnostics (off by default): `SwingLog` logs how far the club tip lags your hand per swing.

**Upgrading from 1.0.0:** the new settings arrive with the values above. `ChestStunSeconds` already existed, so a
saved 3 stays 3: set it to 1 on the board (or delete its line from `MelonPreferences.cfg`) for the new default.

## 1.0.0

Version 1.0.0 saves the club belt loadout in The Range. Drawing or losing clubs during a
contract changes the live holsters but should not change the loadout restored on a death/retry.
When the game resets its player loadout without reloading the scene, Daredevil recalls loose clubs
to the saved leg holsters. The 2026-09-30 contract test confirmed that clubs returned on two
same-scene checkpoint resets.

**Club door kick:** while holding a Billy Club, press **A** on the right controller or
**X** on the left while aiming the controller at the door's marked kick point. This calls the game's VR door-kick
routine, so its VR door-kick setting and range apply. `[BillyClubs] ClubDoorKick` enables it (default `true`). A
successful kick is logged; with `[BillyClubs] DebugLog = true`, presses that find no marked door are logged too.
The 2026-09-30 playtest confirmed successful kicks. Some steel doors returned `no marked door in range`
when the game did not expose a kick point; aim at the marked point when it appears.

The tested direct club-assist turn limit is 25° by default (`[BillyClubs] ThrowAssistMaxTurnAngle`).
Throw Assist 0.2.2 uses a matching 25° limit for head aim. Existing saved preferences keep their
values until changed.

The Daredevil package, one DLL: **billy clubs** (from the crowbar: belt holsters, F8 recall, spin styles, ricochets,
knockouts, arsenal panel entry) + **radar sense** (enemy silhouettes through walls in slow motion or always, louder
3D enemy footsteps, dropped clubs glow).

**Required mods** (separate DLLs; Daredevil still runs without them and logs which are missing):
| Mod | What it adds |
|---|---|
| [Gloves](../Gloves/README.md) | Dark red gloves (default; any colour on the Mod Settings board) |
| [Throw Assist](../ThrowAssist/README.md) | Thrown pistols that home and hurt |
| [Mod Settings](../ModSettings/README.md) | The in-VR settings board |

**Optional:** [Weapon Framework](../WeaponFramework/README.md) puts the clubs on the arsenal panel in The Range.
**Optional:** [VR Holster Customization](../VRHolsterCustomization/README.md) lets clubs use the back slots and tints the club holster tubes. Weapon Framework 0.3.0 requires the holster mod.

> **⚠ Shared code with VR Holster Customization: change one, check the other.** Daredevil calls its public API
> (`Holsters.RegisterKind`, `TryHolster`, `Holds`, and since 1.1.2 `ReturnHome` and `ForgetHome`; wrappers in
> `BillyClubs/Arsenal.cs`) and was compiled against it. Daredevil 1.1.2 (club return to the back holster) needs
> **VR Holster Customization 0.3.7 or newer**; with an older one the club returns to the belt. When you change
> that API, or the holster's draw / return / ghost-collider code (Daredevil's belt clubs do the same ghosting,
> `SetGhost`), rebuild and retest both, and ship them together.

**Optional but highly recommended:** [Heavy Melee](../HeavyMelee/),
[Physical Dodge](../PhysicalDodge/), and [Enemy Awareness Fix](../EnemyAwarenessFix/).
These companion mods are recommended for the Daredevil experience, but are not required to run it.

Install: `Mods/Daredevil.dll` + the required DLLs above. **Remove standalone `BillyClubs.dll` and `RadarSense.dll`**
if present (legacy), or everything loads twice. Settings: `[BillyClubs]`, `[RadarSense]`, `[Gloves]`. Log lines:
`[Daredevil]` = clubs, `[Radar Sense]` = radar sense.

0.3.3: the pair is fitted when the wall's slide-in ends (Weapon Framework 0.1.1 `OnSettled`), 2 cm off the board.
0.3.2: the arsenal pair lies flat on the board (laid out in the slot's own frame, fitted once the slot stops);
`WallLayout` Diagonal (default, 45°) / Upright / Cross. 0.3.1: clubs spawned mid-scene (arsenal wall, F8) can be grabbed again: the game's grabbable optimiser had them switched off
(`BillyClubs/Optimiser.cs`); the arsenal pair hangs as an X on the board (`WallStyle`, `WallPosition`). 0.3.0: the gloves moved out into the Gloves mod (`[BillyClubs] GloveColor` → `[Gloves] Color`), and the package
checks for its required mods. 0.2.0: arsenal panel entry.

## How it's built

**Daredevil is the only project (Sep 28 2026).** The standalone Billy Clubs and Radar Sense mods are legacy: no more
separate builds or versions. All development happens here:

- `Daredevil.cs`: the package name/version (`MelonInfo`) and the hooks that run Radar Sense from the one MelonMod.
- `BillyClubs/`: clubs, holsters, recall, throws, damage, arsenal entry (`Arsenal.cs`). Its README has
  the model/asset details.
- `RadarSense/`: see-through enemies, loud steps, club glow. Its README has the design history.
- `Tests/`: the OBJ loader test.

Config sections stay `[BillyClubs]` and `[RadarSense]` (existing settings keep working); log names `[Daredevil]` and
`[Radar Sense]`. One version number: any change bumps Daredevil.

```powershell
dotnet build Daredevil/Daredevil.csproj -c Release -o feature/Daredevil-<version>
```
