# Throw Assist 0.2.1 debug candidate

Throw assist with real physics, and thrown pistols that hurt. Required by the Daredevil package (Billy Clubs + Radar
Sense). Moved out of Billy Clubs 0.10.x.

- The game picks the target (its own assisted-throw option must be on); the mod stops the game's drag coroutine,
  which zeroes speed and spin and drags the item with `MovePosition`, and steers the item with real velocity instead,
  keeping its spin and collisions.
- **Pistols:** the game ships every pistol's assist off (`dontUse = true`); `PistolAssist` switches it on. A thrown pistol
  that hits an enemy does `meleeDamage x PistolThrowDamage` (3 = 60 to the body, head x5 kills) and staggers them
  (`PistolStagger`), once per enemy per throw. The game's own hit is 10-20, never a stagger (needs > 5 kg; a pistol is
  1.5), and nothing during the enemy's hit stun.
- **Other items:** `SteerOtherItems` is on for new installs. It applies to items with the game's
  `ANBAssistedThrowingObject` component (knives and eligible props); items without that component cannot be assisted.
  Existing `MelonPreferences.cfg` values remain in effect, so set `SteerOtherItems = true` if upgrading from 0.2.0.
  Knives fly blade first at no less than their prefab's own assist speed (17 m/s on the tested combat knife).
  `KnifeSpeedMultiplier = 1` matches that speed; raise it in small steps only after checking impact logs.
- **Billy Clubs** fly their own club flight (ricochets, spin styles, floor shots): objects named `BillyClub-*` are left
  to that mod, so the two never steer the same throw.

## Settings (`[ThrowAssist]`)

`PistolAssist` (on), `SteerOtherItems` (on for new installs), `MinAssistSpeed` (6 m/s: slower throws get no assist; the 0.10.0 pistol
assist homed every lob and sped it up to 13 m/s), `MaxSpeed` (18: assisted items fly at your throw speed up to this),
`KnifeSpeedMultiplier` (1: use the knife's own game speed as a floor),
`SearchDistance` (15 m), `MaxFlyDistance` (20 m), `AimHead` (off = chest), `PistolDamage` (on), `PistolThrowDamage` (3),
`PistolStagger` (on), `DebugLog` (on for new installs; existing saved values remain in effect).

For the debug session, check `[ThrowAssist]` in `UserData/MelonPreferences.cfg` and set `DebugLog = true` and
`SteerOtherItems = true` if the file already contains older values. Logs go to `MelonLoader/Latest.log`. Each release
reports raw speed, target/skip reason, game threshold, item speed, spin and chosen aim. The flight summary reports
why steering ended, last speed, and for knives the tip angle, observed body collision speed/part, and stab result.
Pistol hits report damage and health. A knife that hits a face but does not stab can then be distinguished from a
slow collision, bad blade alignment, or a missed hit zone. `DebugLog` may be turned off after testing.

Speed recap: pistols and props use the hand's release speed clamped to `MinAssistSpeed`–`MaxSpeed`; knives use at
least `item assist speed × KnifeSpeedMultiplier`. The game's own assist instead moves a knife at its prefab's fixed
speed while suppressing velocity and spin. The mod preserves velocity steering and aligns the stab line each step.
The earlier 0.2.0 test logged 14/14 assisted knife stabs, but failures that never entered the assist were initially
unlogged. This build records those releases too; face bounces still need an in-game test.

## Build

```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-0.2.1 --no-restore
```
