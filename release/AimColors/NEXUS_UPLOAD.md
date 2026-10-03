# Aim Colors: Nexus upload sheet

**Status: 0.1.0 release candidate (tested in game Oct 2 2026). New mod page. Not uploaded.**
Named Laser Color, then Gun Colors, then Aim Colors (Oct 2 2026): "Gun Colors" read as weapon skins, but only the laser, iron sights and reticle are recoloured.

## Files in this folder

| File | What |
|---|---|
| `AimColors-0.1.0.zip` | the upload: contains `Mods/AimColors.dll` |
| `AimColors.dll` | the shipping build, 15,360 bytes, sha256 `00285091b74d80aea3a72ba58afa36e4b9a54208b4cd15e47032be2f741990bc` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Not made yet: thumbnail (1600x900) and header (1300x372) artwork. Good screenshots: pistol laser dot on a wall, the red dot reticle through the pistol sight, the iron sights, and the game's amber vs the default red.

Rebuild:
```powershell
dotnet build AimColors/AimColors.csproj -c Release -o feature/AimColors-build
```
then copy the DLL here and zip it as `Mods/AimColors.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Aim Colors` |
| Author | `Evgeeso` |
| Version | `0.1.0` |
| Category | Gameplay / Utilities |
| Language | English |
| Tags | VR, Cosmetic, Utility, Weapons |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Aim Colors` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Mod Settings is optional (live in-VR colour picker).` |
| Images | none yet (see above) |

**Summary (330 chars):**
`Recolours what you aim with: the weapon laser (beam and dot), the iron sights and the red dot reticle. Set the colour, make each brighter (much easier to see through wireless-stream compression) and resize the reticle. Dark red by default. Live from Mod Settings or the config. Guns themselves are untouched.`

## Before uploading

- [x] Tested in game Oct 2 2026: laser colour + boost on pistol and bow; iron-sight tint; reticle colour, boost and size on pistol and AB15 rifle.
- [ ] Not exercised: `SightBoost` above 1, rifle scopes beyond the collimator, enemy guns during a contract (`IncludeEnemies`).
- [ ] Thumbnail + header artwork.
- [ ] Merge `dev` → `main`, tag `aimcolors-v0.1.0`, push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in `release/NEXUS_TEMPLATE.txt`.
- [ ] Testers who had `GunColors.dll` or `LaserColor.dll`: delete it; the config section is now `[AimColors]` (old `[GunColors]` values are not read).
