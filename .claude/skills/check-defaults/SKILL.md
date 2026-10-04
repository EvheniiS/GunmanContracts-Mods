---
name: check-defaults
description: Compare the defaults the Gunman Contracts mod source ships (CreateEntry in each mod's .cs) with the user's live settings in the game's MelonPreferences.cfg, and flag debug/log switches that ship on. Use for the monthly defaults audit, before any release ("are the defaults what I play with?", "did I forget to update a default?", "check defaults", "defaults drift", "is debug off for release"), and as the defaults gate inside nexus-release.
---

# Check shipped defaults against the user's config

Forgetting to copy the user's preferred values into the source before a release keeps happening, so this is a script, not a memory exercise. `Tools/Check-Defaults.ps1` is read-only and makes no changes.

```
pwsh -NoProfile -File Tools\Check-Defaults.ps1                    # every mod
pwsh -NoProfile -File Tools\Check-Defaults.ps1 -Mod BetterBow     # one or more (partial folder names)
pwsh -NoProfile -File Tools\Check-Defaults.ps1 -Quiet             # one line per mod
```

Source of truth for "mine" is `E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\UserData\MelonPreferences.cfg` (`-Cfg` to override). The cfg only has a key after the game has run with that mod version, so a brand-new setting shows as `NOCFG` (not compared): tell the user to launch the game once, then re-run. The game must have been closed since the user's last settings change, since MelonLoader writes the cfg on exit.

## Reading the output

- **DIFF** `Section.Key source=… yours=…`: the shipped default differs from the user's value. Debug switches are excluded. It is a question, not an error: ask per value (or per group) whether the user's value is the preferred default (then change the `CreateEntry` default in the source, and the page-text Settings row if the mod has one, and remember that a changed default is shown to players by Mod Settings) or just an experiment (then leave it and say so). Never edit a default without the user's answer.
- **DEBUG**: a debug/log switch (`DebugLog`, `SwingLog`, `PerfLog`, `Log*`, ...) whose source default is `true`. Debug always ships `false`: fix it in the source (a default-only change; it does not need a play test, see `mod-status`), whatever the user's cfg says.
- **PROBE**: record-only probe mods (EnemyAwarenessLog, GrabLog, FrameProbe, SoundProbe, AttachmentProbe) ship on by design. Informational; check only that a probe is not being released as a player mod by accident.
- **NOCFG** / **?**: keys not in the cfg yet, or a category the script could not map. Not compared.
- `Saved*` keys are runtime state the mods write; skipped.
- Exit code 2 = a debug switch ships on, 1 = DIFFs only, 0 = clean.

If a new log key has a name outside the list in the script (`$logKeys`, same list as `Tools\Set-DebugLogs.ps1`), add it to both. Names starting `Debug` or `Log<Capital>` are caught automatically.

## Monthly audit

Run it for all mods, group the DIFFs by mod, and give the user a short table (key, shipped, yours) plus your read on which look like deliberate preference and which look like experiments (a test flag like `TestEntries = true` is an experiment). Apply only what they confirm, then re-run to show it clean. The Nexus page Settings tables follow the code, so after a default changes run `Tools\Pack-Release.ps1 -Mod <Mod> -LintOnly` to see the page rows that went stale.

## In a release

`nexus-release` runs this for the mod being released (gate step). For that mod every DEBUG must be fixed before packaging, and every DIFF must be answered (change the default, or consciously keep it) before the zip is built, since the DLL carries the defaults.
