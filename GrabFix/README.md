# Grab Fix 1.1.2

Tested: 1.1.2 (2026-10-03)

More forgiving VR hand pickups, with normal grip controls and native grab eligibility checks.

- Finger and palm distance targeting together; optional finger-only mode.
- Wider nearby pickup area (12 cm minimum by default) and distance detectors (1.2x width).
- A 0.30-second buffer lets an early grip press complete a pickup while grip remains held.
- Recently released loose items can be caught within 18 cm of the palm. A fresh grip press opens a 0.60-second catch window; items remain eligible for 15 seconds after release.
- Holstered, socketed and wall-mounted items retain native detection limits and are excluded from assisted catch retries.
- Enemies and downed enemies can only be grabbed with the palm right on them (`EnemyGrabRadius`, default 10 cm); no distance or buffered grabs on enemies, so a fist swung near a face or a lying body no longer grabs it.
- Disabling the mod restores original detector geometry. Held-item poses are unchanged.

Requires MelonLoader; built for game 0.3.1.0 and MelonLoader 0.7.3. No gameplay mod dependencies.
Settings are in `[GrabFix]` in `UserData/MelonPreferences.cfg`; all defaults are listed in the [Nexus description](../release/GrabFix/NEXUS_DESCRIPTION.txt).

## 1.1.2 (built Oct 3 2026, untested; release docs prepared)

- The holstered-item reach check now counts an item's solid colliders even while they are switched to triggers
  (the game and Daredevil 1.1.1 do that to holstered items). Before, such an item had no measurable distance and
  could not be drawn. The native grab bag never skipped them.

## Release

Version 1.1.2 is built and untested (1.1.1 plus the holstered-item reach fix that Daredevil 1.1.1 needs; 1.1.1's enemy grab radius was tested Oct 2 2026). Release docs are prepared. Package, checksum and live version: `Tools/Pack-Release.ps1 -Mod GrabFix`, `Tools/Check-Nexus.ps1 -Mod GrabFix`.

```powershell
dotnet build GrabFix/GrabFix.csproj -c Release -o feature/GrabFix-1.1.0
```
