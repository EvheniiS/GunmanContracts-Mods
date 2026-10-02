# Grab Fix: Nexus upload sheet

Status: 1.1.1 packaged Oct 2 2026 from `dev`; the enemy grab radius was tested in game. Not uploaded or tagged. 1.1.0 was never uploaded, so 1.1.1 is the first public version.

| Field | Value |
|---|---|
| Mod name | `Grab Fix` |
| Version | `1.1.1` |
| Author | `Evgeeso` |
| Suggested category | Gameplay |
| Language | English |
| Summary (max 350 chars) | `More forgiving VR pickups: finger and palm targeting, wider pickup zones, a short buffer for early grip presses, and easier catches for recently released items. Holstered and mounted items retain their original grab zones. Normal grip controls and held-item poses are preserved.` |
| Description | Paste [NEXUS_DESCRIPTION.txt](NEXUS_DESCRIPTION.txt) as BBCode |
| Suggested tags | VR, Gameplay, Quality of Life |
| Requirements | MelonLoader 0.7.3 or newer; [download](https://github.com/LavaGang/MelonLoader/releases). No gameplay mod dependencies. |
| Optional | [Mod Settings](https://www.nexusmods.com/gunmancontractsstandalone/mods/28) for live settings |
| Permissions | Source is MIT; match permissions to the repository license |

## Main file

| Field | Value |
|---|---|
| Upload | `GrabFix-1.1.1.zip` |
| File name | `Grab Fix` |
| Version | `1.1.1` |
| Category | Main files |
| File description | `Extract into the game folder. Contains Mods/GrabFix.dll. Requires MelonLoader 0.7.3 or newer. For VR hand pickups.` |

`GrabFix-1.1.1.zip`: 13,950 bytes, sha256 `DFB6DF18DAD995191B66820D688E54B78C6F10DA8378BE4120784D86F90471C2`.
`GrabFix.dll`: 31,232 bytes, sha256 `470BC34806FB07B873DCE6C1C1D9574FEED7ACF72741645BCCAB7D25034BDC98`.
The ZIP contains only `Mods/GrabFix.dll`; game assemblies, local paths, configuration and debug files are excluded.

## Changelog (Nexus "Changelog" field)

1.1.1
- Enemies (standing, stunned or downed) can only be grabbed with your palm within 10 cm of them, never from a
  distance (EnemyGrabRadius). A punch near a face or a body at your feet no longer grabs it.

## Validation

- Oct 2 2026: release build `dotnet build GrabFix/GrabFix.csproj -c Release --no-restore -o feature/GrabFix-1.1.1`, zero warnings or errors; ZIP entry and DLL hash verified. Enemy grab radius tested in play.

- Release build succeeded on September 29, 2026 with zero warnings and zero errors, using existing restored assets: `dotnet build GrabFix/GrabFix.csproj -c Release --no-restore -o feature/GrabFix-1.1.0`.
- Version and defaults were checked against source. ZIP entry and DLL hash were verified.
- Existing playtest evidence in [GUNMAN_CONTRACTS.md](../../GUNMAN_CONTRACTS.md): September 28 session recorded 29 successful buffered pickups out of 29 attempts, with no logged errors. This session preceded the docked-item correction.
- The same document separately records user confirmation of the final local and distance docked-item fixes: "now it seems to be fixed."
- No new VR playtest was performed during packaging. Finger-target priority between detector bags is not verified and is not promised in the description.

## Publishing

- Upload the ZIP and paste the description and summary above.
- Use `GrabFix-Nexus-thumbnail-1600x900.png` as the thumbnail and `GrabFix-Nexus-header-1300x372.png` as the header. Both are based on the supplied in-game screenshot; generation prompts are saved in `GrabFix-Nexus-imagegen-prompts.txt`.
- After publishing, record the actual Nexus URL and update the repository status. Tag the published source version as `grabfix-v1.1.1` when appropriate.
