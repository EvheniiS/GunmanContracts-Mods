# Death Details: Nexus upload sheet

**Status: 0.7.1 release candidate (tested in game Oct 2 2026). New mod page. Not uploaded.**
Was Close Eyes until 0.7.0 (never published under that name). The name already fits (eyes, mouth, pain face, finishing hits), so it was kept.

## Files in this folder

| File | What |
|---|---|
| `DeathDetails-0.7.1.zip` | the upload: contains `Mods/DeathDetails.dll` |
| `DeathDetails.dll` | the shipping build, 24,064 bytes, sha256 `558c6a7cc0595656ca87b9fc03bd09bf40067e71174ce2d7fc3f18d1427144b2` (rebuilt from the same source as the tested DLL; the bytes differ only by build) |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Not made yet: thumbnail (1600x900) and header (1300x372) artwork. Good screenshots: a corpse's closed eyes up close, the same body in vanilla (open eyes, gaping mouth), a writhing enemy's pain face.

Rebuild:
```powershell
dotnet build DeathDetails/DeathDetails.csproj -c Release -o feature/DeathDetails-build
```
then copy the DLL here and zip it as `Mods/DeathDetails.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Death Details` |
| Author | `Evgeeso` |
| Version | `0.7.1` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Cosmetic, Gameplay, AI |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Death Details` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Mod Settings is optional.` |
| Images | none yet (see above) |

**Summary (330 chars):**
`Dead enemies close their eyes and keep their mouth slightly parted instead of gaping. Enemies writhing on the floor keep their pain face until they die, and a melee hit to the head (club, crowbar, gun bash, fist) finishes them with a head-hit sound. Corpse faces cost less than in vanilla. Optional bleed-out.`

## Before uploading

- [x] Tested in game Oct 2 2026: eyes close (`JawOpen` 0.1), pain face while writhing, corpse faces frozen, a melee head hit finishes a writhing enemy with the BluntHitHead sound.
- [ ] Not exercised: `BleedOut` and `AnyHitEndsPain` (off by default; described as experimental on the page).
- [ ] Load not measured (README: one field read per corpse every 0.1 s, should be lower than vanilla). Measure before claiming "cheaper" in a headline; the page currently says it once, in the bullet list.
- [ ] Thumbnail + header artwork.
- [ ] Merge `dev` → `main`, tag `deathdetails-v0.7.1`, push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in `release/NEXUS_TEMPLATE.txt`.
- [ ] Anyone with `CloseEyes.dll` installed: delete it; the config section is now `[DeathDetails]`.
- [ ] Flat mode: not tested. Face changes are enemy-side and should work; the head-hit finish needs VR weapons (flat melee is the knife slash, which isn't hooked). Don't claim flat support on the page.
