# Gloves: Nexus upload sheet

**Status: solid release candidate (0.1.0) — could ship as 1.0. New mod page.**

Required by Daredevil's description — upload this one first, or in the same sitting as Daredevil.

## Files in this folder

| File | What |
|---|---|
| `Gloves-0.1.0.zip` | the upload: contains `Mods/Gloves.dll` |
| `Gloves.dll` | the shipping build, 8,192 bytes, sha256 `05ceabcff45f95fd2bc454e49f02b1eb974d5b66e3189c6b431e404288241d2b` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Rebuild:
```powershell
dotnet build Gloves/Gloves.csproj -c Release -o feature/Gloves-build
```
then copy the DLL here and zip it as `Mods/Gloves.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Gloves` |
| Author | `Evgeeso` |
| Version | `0.1.0` |
| Category | Gameplay / Utilities |
| Language | English |
| Tags | VR, Cosmetic, Utility |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub); required by Daredevil |
| File name / category | `Gloves` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Stands alone; also required by Daredevil.` |
| Images | a screenshot of the dark red default gloves in VR |

**Summary (330 chars):**
`Recolours the player's gloves to any colour, applied at once. Dark red by default (the Daredevil look). Edit the config or change it live on the Mod Settings board's colour picker, no restart needed. Only the gloves change; sleeves are untouched. Required by Daredevil, but works standing alone.`

## Before uploading

- [x] Tinting tested: default dark red, colour changes via Mod Settings board apply at once.
- [ ] Decide it feels solid enough to publish.
- [ ] Tag: `git tag gloves-v0.1.0` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`, and backfill it into Daredevil's `NEXUS_DESCRIPTION.txt`.
