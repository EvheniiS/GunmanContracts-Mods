# Weapon Framework: Nexus upload sheet (release candidate 0.1.1)

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`). **New mod page.**

## Files in this folder

| File | What |
|---|---|
| `WeaponFramework-0.1.1.zip` | the upload: contains `Mods/WeaponFramework.dll` (git-ignored, rebuild with the command below) |
| `WeaponFramework.dll` | the shipping build, 18,944 bytes, sha256 `cce1c87cb92294fd…` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Rebuild:
```powershell
dotnet build WeaponFramework/WeaponFramework.csproj -c Release -o feature/WeaponFramework-0.1.1
```
then copy the DLL here and zip it as `Mods/WeaponFramework.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Weapon Framework` |
| Author | `Evgeeso` |
| Version | `0.1.1` |
| Category | Utilities (or Modders Resources, if the game page has it) |
| Language | English |
| Tags | VR, Weapons, Modders Resource, Utilities |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as Better Bow (source is MIT on GitHub); allow use in other mods (it is meant to be required by them) |
| File name / category | `Weapon Framework` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Adds nothing on its own: install a weapon mod that uses it.` |
| Images | a screenshot of the panel on "BILLY CLUBS 007 / 007" and one of the pair on the wall (`images/1.jpg`-style captures from the Sep 28 tests) |

**Summary (330 chars):**
`A framework for weapon mods: other mods can put their own weapons on the arsenal panel in The Range. Page to the weapon, press Retrieve and take it off the wall, no keys or debug tools. Mod weapons are free, and the save never remembers a mod slot, so removing mods can't break it. Adds nothing on its own.`

## Before uploading

- [x] Panel entry, Retrieve, grabbing, wall placement tested in game (Sep 28 2026, with Daredevil 0.3.3).
- [ ] **Save guard:** retrieve Billy Clubs last, quit, restart: the Big Guns wall must start on one of the game's
      weapons. (Logged working: `save: wall index 6 is a mod entry - saved as 0`; the restart itself not yet seen.)
- [ ] **Uninstall check:** remove `WeaponFramework.dll`, start, visit The Range: arsenal shows 006 / 006 and loads fine.
- [x] This exact DLL is installed in `<game>\Mods` (Sep 28 14:58). Your cfg keeps `DebugLog = true` from testing;
      the shipped default is false. Play one retrieve with it before uploading.
- [ ] Release together with, or after, a weapon mod that uses it (Daredevil), or the page has nothing to show.
- [ ] Tag and push from `main`: `git tag weaponframework-v0.1.1`, `git push origin main weaponframework-v0.1.1`.
- [ ] README status → released, with the Nexus link; add the link to the table in `release/NEXUS_TEMPLATE.txt`.
