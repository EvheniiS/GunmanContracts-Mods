# Daredevil: Nexus upload sheet

**Status: 1.0.0 package includes the same-scene checkpoint loadout fix and A/X club door kick; both tested in game. Not uploaded.**

The source, installed test DLL and current ZIP keep club belt loadout persistence in The Range and
recall loose clubs when the game's player loadout resets within the contract scene.
The earlier pre-fix 1.0.0 package is backed up under `feature/Daredevil-1.0.0-pre-death-fix/`.
The 2026-09-30 session selected 25° for direct club assist.

**Gloves is live** on Nexus (mods/32). **Throw Assist** still needs a confirmed Nexus page
before Daredevil is uploaded, or it needs to be uploaded in the same sitting. Daredevil's
"Required companion mods" section expects both installed. Mod Settings and Weapon Framework
are already live (mods/28, mods/29).
Throw Assist's prepared package is **0.2.2**.

## Files in this folder

| File | What |
|---|---|
| `Daredevil-1.0.0.zip` | rebuilt upload candidate: contains only `Mods/Daredevil.dll`; 3,661,623 bytes; sha256 `2783A56A3DC2FD2A6608D120C02054B69D808E1281332EE86548EBB16834D1A6` |
| `Daredevil.dll` | rebuilt 1.0.0 package DLL, 4,240,896 bytes; sha256 `34C8BD846996E29F0855266E3538B961472A077DBFA4B873A3DF1A063EFDF8C6` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode), already links Mod Settings (mods/28) and Weapon Framework (mods/29) |
| `NEXUS_REQUIREMENTS_DRAFT.md` | which companion mods are required vs. optional, and artwork copy notes |

Rebuild:
```powershell
dotnet build Daredevil/Daredevil.csproj -c Release -o feature/Daredevil-1.0.0-release-build --no-restore
```
then copy the DLL here and zip it as `Mods/Daredevil.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Daredevil` |
| Author | `Evgeeso` |
| Version | `1.0.0` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Weapons, Melee, Gameplay |
| Requirements | Off-site: `MelonLoader 0.7.x`; on-site: **Gloves** (mods/32), **Mod Settings** (mods/28), and **Throw Assist** once its Nexus page exists |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Daredevil` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x, Gloves, Throw Assist and Mod Settings.` |
| Images | `Daredevil-Nexus-thumbnail-v2.png`, `Daredevil-Nexus-header-draft.png` (see `Daredevil-Nexus-imagegen-prompts.txt` for the prompts behind them) |

## Before uploading

- [x] Billy clubs (holsters, throw, ricochet, knockouts) and radar sense (wall vision, dropped-club
      glow) tested well in game.
- [x] Fresh build from current workspace source and package entry verified byte for byte (Sep 30 2026); zero build warnings or errors.
- [x] Direct club assist maximum turn set to the tested 25° default; Throw Assist head aim uses 25°.
- [x] Test death/retry with clubs in the leg holsters: Sep 30 log shows two same-scene checkpoint
      restores, and the user confirmed the fix. Test the separate VR Holster Customization 0.2.2
      back-slot fix as a follow-up.
- [x] A/X club door kick worked repeatedly in game. Some steel doors failed when the game did not
      expose a marked kick point; the page explains the controller aim and game range requirement.
- [x] Link the published Gloves page (mods/32) from `NEXUS_DESCRIPTION.txt`.
- [ ] **Confirm or upload Throw Assist first** (or in the same sitting), then add its real Nexus
      link to `NEXUS_DESCRIPTION.txt` and mark it as an on-site requirement.
- [ ] Check the optional VR Holster Customization link and requirement status when its Nexus page exists.
- [ ] Tag: `git tag daredevil-v1.0.0` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`.
