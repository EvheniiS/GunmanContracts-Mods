# Throw Assist 0.1.0

Throw assist with real physics, and thrown pistols that hurt. Required by the Daredevil package (Billy Clubs + Radar
Sense). Moved out of Billy Clubs 0.10.x.

- The game picks the target (its own assisted-throw option must be on); the mod stops the game's drag coroutine,
  which zeroes speed and spin and drags the item with `MovePosition`, and steers the item with real velocity instead,
  keeping its spin and collisions.
- **Pistols:** the game ships every pistol's assist off (`dontUse = true`); `PistolAssist` switches it on. A thrown pistol
  that hits an enemy does `meleeDamage x PistolThrowDamage` (3 = 60 to the body, head x5 kills) and staggers them
  (`PistolStagger`), once per enemy per throw. The game's own hit is 10-20, never a stagger (needs > 5 kg; a pistol is
  1.5), and nothing during the enemy's hit stun.
- **Other items** (knives, katanas, bottles, pans, crowbar): `SteerOtherItems` (off, untested; knives must land point
  first to stab).
- **Billy Clubs** fly their own club flight (ricochets, spin styles, floor shots): objects named `BillyClub-*` are left
  to that mod, so the two never steer the same throw.

## Settings (`[ThrowAssist]`)

`PistolAssist` (on), `SteerOtherItems` (off), `MinAssistSpeed` (6 m/s: slower throws get no assist; the 0.10.0 pistol
assist homed every lob and sped it up to 13 m/s), `MaxSpeed` (18: assisted items fly at your throw speed up to this),
`SearchDistance` (15 m), `MaxFlyDistance` (20 m), `AimHead` (off = chest), `PistolDamage` (on), `PistolThrowDamage` (3),
`PistolStagger` (on), `DebugLog`.

Log: `pistol '...' thrown at X m/s, assist: enemy '...' D m, steered at S m/s` (or `none (<reason>)`) and
`pistol '...' hit '...' <part> at X m/s: damage N, stagger, health A -> B`.

## Build

```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-0.1.0
```
