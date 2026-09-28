# Enemy Awareness Fix: Nexus upload sheet

**Status: ready for 1.0 (currently 0.1.6, enjoyed in actual play). New mod page.**

**Enemy Awareness Log ships on the same page as an optional file**, not a separate mod — it's a
diagnostic companion for this mod, not something people install standalone.

## Files in this folder

| File | What |
|---|---|
| `EnemyAwarenessFix-0.1.6.zip` | the main file: contains `Mods/EnemyAwarenessFix.dll` |
| `EnemyAwarenessFix.dll` | the shipping build, 23,552 bytes, sha256 `86f8c9e7c179dd055596724360c3e4c5b5536b5bba28c872fe8fb9b40b741c74` |
| `EnemyAwarenessLog-0.5.2.zip` | the **optional file**: contains `Mods/EnemyAwarenessLog.dll` |
| `EnemyAwarenessLog.dll` | the optional-file build, 47,616 bytes, sha256 `ed1b657b696b932d2731d9975e25d281e226efba35353526d27d18df326ee5ec` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode), covers both files |

Rebuild:
```powershell
dotnet build EnemyAwarenessFix/EnemyAwarenessFix.csproj -c Release -o feature/EnemyAwarenessFix-build
dotnet build EnemyAwarenessLog/EnemyAwarenessLog.csproj -c Release -o feature/EnemyAwarenessLog-build
```
then copy each DLL here and zip as `Mods/<Name>.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Enemy Awareness Fix` |
| Author | `Evgeeso` |
| Version | `0.1.6` |
| Category | Gameplay / AI |
| Language | English |
| Tags | VR, Gameplay, AI, Difficulty |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Enemy Awareness Fix` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |
| Optional file name | `Enemy Awareness Log` |
| Optional file description | `Diagnostic companion, changes nothing by itself. Extract alongside Enemy Awareness Fix.` |
| Images | a screenshot/clip of enemies searching a last-known spot instead of walking straight through a wall at you |

**Summary (330 chars):**
`Enemies that lose sight of you search where they last saw you (or a rough guess), instead of walking to your exact position through walls. Wave spawns don't always pick the nearest point. Floor-aware: no hearing you through a floor, no "arrived" a storey away. Enemy Awareness Log ships as an optional diagnostic file.`

## Before uploading

- [x] Enjoyed in actual play at 0.1.6 — tracking, guessing, sharing, searching and floor-awareness
      all hold up.
- [ ] Decide it feels solid enough to publish as 1.0 (or ship 0.1.6 as-is and bump later).
- [ ] Tag: `git tag enemyawarenessfix-v0.1.6` (or `-v1.0.0` if bumped) and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`.
