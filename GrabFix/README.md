# Grab Fix 1.1.0

More forgiving VR hand pickups, with normal grip controls and native grab eligibility checks.

- Finger and palm distance targeting together; optional finger-only mode.
- Wider nearby pickup area (12 cm minimum by default) and distance detectors (1.2x width).
- A 0.30-second buffer lets an early grip press complete a pickup while grip remains held.
- Recently released loose items can be caught within 18 cm of the palm. A fresh grip press opens a 0.60-second catch window; items remain eligible for 15 seconds after release.
- Holstered, socketed and wall-mounted items retain native detection limits and are excluded from assisted catch retries.
- Disabling the mod restores original detector geometry. Held-item poses are unchanged.

Requires MelonLoader; built for game 0.3.1.0 and MelonLoader 0.7.3. No gameplay mod dependencies.
Settings are in `[GrabFix]` in `UserData/MelonPreferences.cfg`; all defaults are listed in the [Nexus description](../release/GrabFix/NEXUS_DESCRIPTION.txt).

## Release

Version 1.1.0 is a playtested release candidate. See the [upload sheet](../release/GrabFix/NEXUS_UPLOAD.md) for the package, checksum and validation scope.

```powershell
dotnet build GrabFix/GrabFix.csproj -c Release -o feature/GrabFix-1.1.0
```
