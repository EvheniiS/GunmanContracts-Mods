# Daredevil 0.3.3

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
