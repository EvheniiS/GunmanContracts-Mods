---
name: debug-logs
description: Turn Gunman Contracts mod debug logs on or off, all at once or for one mod, by editing the game's MelonPreferences.cfg. Use whenever the user says disable/enable/turn off/turn on the debug logs, "logging", DebugLog, SwingLog, PerfLog, "quiet the logs", "log only X", or asks which logs are currently on.
---

# Debug logs on/off

Files:
- Config: `E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\UserData\MelonPreferences.cfg`
- Game folder: `E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\` (logs: `MelonLoader\Latest.log`, `UserData\`)

**Close the game first.** MelonLoader rewrites the cfg on exit and undoes the edit. The script warns if `GunmanContracts.exe` is running.

Run from the repo root (`Tools\Set-DebugLogs.ps1`):

```
pwsh -NoProfile -File Tools\Set-DebugLogs.ps1 Status                  # every log switch and its value
pwsh -NoProfile -File Tools\Set-DebugLogs.ps1 Off                     # all logs off
pwsh -NoProfile -File Tools\Set-DebugLogs.ps1 On                      # all mod logs on
pwsh -NoProfile -File Tools\Set-DebugLogs.ps1 On  -Mod BillyClubs     # one mod (partial name, comma list ok)
pwsh -NoProfile -File Tools\Set-DebugLogs.ps1 Off -Mod RadarSense,ThrowAssist
```

What counts as a log switch: keys `DebugLog`, `SwingLog`, `PerfLog`, `LogCutTiming`, `LogPerformanceStats`, `LogAppliedSettings`, `LogSounds`, `LogStateChanges`, `LogAlerts`, `FollowThroughVerboseLog`, plus `Enabled` of the record-only probe mods (`EnemyAwarenessLog`, `GrabLog`, `FrameProbe`, `SoundProbe`).
- `Off` turns all of them off, probes included.
- `On` skips the probe mods (off by default, heavy) unless `-Probes` or the probe is named with `-Mod`.
- Only these keys are touched; gameplay settings never change. Section names are the cfg's `[Section]` headers (e.g. `BetterBow`, `BillyClubs`, `RadarSense`, `VRHolsters`, `GrabFix`, `Decapitation`).

After a change, run `Status` and report the count. If a new mod adds a log key under a different name, add it to `$logKeys` in the script.
