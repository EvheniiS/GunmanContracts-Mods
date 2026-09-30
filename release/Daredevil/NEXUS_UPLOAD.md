# Daredevil: Nexus upload sheet

**Status: 0.4.0 packaged release candidate. The 2026-09-30 session selected 25° for direct club assist. Not uploaded yet.**

Requires **Gloves** and **Throw Assist** to be uploaded first (or alongside), since Daredevil's
description links them and its "Required companion mods" section expects them installed. Mod
Settings and Weapon Framework are already live (mods/28, mods/29).

## Files in this folder

| File | What |
|---|---|
| `Daredevil-0.4.0.zip` | current upload: contains only `Mods/Daredevil.dll`; 3,660,338 bytes; sha256 `9DED66B5DB5F6DC8AA6C4BF2325262D9456D0A7BAC1ABE70C82BB1AF4A9E395A` |
| `Daredevil.dll` | current 0.4.0 build, 4,237,312 bytes; sha256 `9CC564EC69DE23ED5858CB95A510F864760FA741D5DDC3DAD1155D7E177F00AE` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode), already links Mod Settings (mods/28) and Weapon Framework (mods/29) |
| `NEXUS_REQUIREMENTS_DRAFT.md` | which companion mods are required vs. optional, and artwork copy notes |

Rebuild:
```powershell
dotnet build Daredevil/Daredevil.csproj -c Release -o feature/Daredevil-0.4.0-release-build --no-restore
```
then copy the DLL here and zip it as `Mods/Daredevil.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Daredevil` |
| Author | `Evgeeso` |
| Version | `0.4.0` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Weapons, Melee, Gameplay |
| Requirements | Off-site: `MelonLoader 0.7.x`; on-site: **Gloves**, **Throw Assist**, **Mod Settings** (once uploaded) |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Daredevil` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x, Gloves, Throw Assist and Mod Settings.` |
| Images | `Daredevil-Nexus-thumbnail-v2.png`, `Daredevil-Nexus-header-draft.png` (see `Daredevil-Nexus-imagegen-prompts.txt` for the prompts behind them) |

## Before uploading

- [x] Billy clubs (holsters, throw, ricochet, knockouts) and radar sense (wall vision, dropped-club
      glow) tested well in game.
- [x] Fresh build from current workspace source and package entry verified byte for byte (Sep 30 2026).
- [x] Direct club assist maximum turn set to the tested 25° default; Throw Assist head aim uses 25°.
- [ ] **Upload Gloves and Throw Assist first** (or in the same sitting) — Daredevil's description
      links their eventual Nexus pages and lists them as required.
- [ ] Backfill the real Gloves/Throw Assist Nexus links into `NEXUS_DESCRIPTION.txt` once those
      pages exist (currently GitHub-repo links, see the "GitHub links open the shared project
      repository" closer line).
- [ ] Tag: `git tag daredevil-v0.4.0` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`.
