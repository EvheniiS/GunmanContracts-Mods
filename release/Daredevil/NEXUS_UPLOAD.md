# Daredevil: Nexus upload sheet

**Status: solid release candidate (0.3.3) — could ship as 1.0. New mod page.**

Requires **Gloves** and **Throw Assist** to be uploaded first (or alongside), since Daredevil's
description links them and its "Required companion mods" section expects them installed. Mod
Settings and Weapon Framework are already live (mods/28, mods/29).

## Files in this folder

| File | What |
|---|---|
| `Daredevil-0.3.3.zip` | the upload: contains `Mods/Daredevil.dll` |
| `Daredevil.dll` | the shipping build, 4,232,704 bytes, sha256 `3d79a4fe8e00378d45596b206f34472cfc04028522455316a613cea0f689b1ae` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode), already links Mod Settings (mods/28) and Weapon Framework (mods/29) |
| `NEXUS_REQUIREMENTS_DRAFT.md` | which companion mods are required vs. optional, and artwork copy notes |

Rebuild:
```powershell
dotnet build Daredevil/Daredevil.csproj -c Release -o feature/Daredevil-build
```
then copy the DLL here and zip it as `Mods/Daredevil.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Daredevil` |
| Author | `Evgeeso` |
| Version | `0.3.3` |
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
- [x] Fresh build matches current `dev`/`main` source (Sep 28 2026, post BillyClubs/RadarSense merge).
- [ ] **Upload Gloves and Throw Assist first** (or in the same sitting) — Daredevil's description
      links their eventual Nexus pages and lists them as required.
- [ ] Backfill the real Gloves/Throw Assist Nexus links into `NEXUS_DESCRIPTION.txt` once those
      pages exist (currently GitHub-repo links, see the "GitHub links open the shared project
      repository" closer line).
- [ ] Tag: `git tag daredevil-v0.3.3` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`.
