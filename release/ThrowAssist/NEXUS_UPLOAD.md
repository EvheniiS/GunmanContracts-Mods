# Throw Assist: Nexus upload sheet

**Status: solid release candidate (0.2.0) — could ship as 1.0. New mod page.**

Required by Daredevil's description — upload this one first, or in the same sitting as Daredevil.

## Files in this folder

| File | What |
|---|---|
| `ThrowAssist-0.2.0.zip` | the upload: contains `Mods/ThrowAssist.dll` |
| `ThrowAssist.dll` | the shipping build, 22,016 bytes, sha256 `913f63d1ba64d7d78e30f07f03baac87173602c0d7a9be75e9414c1165a757b6` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Rebuild:
```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-build
```
then copy the DLL here and zip it as `Mods/ThrowAssist.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Throw Assist` |
| Author | `Evgeeso` |
| Version | `0.2.0` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Weapons, Gameplay |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work`; needs the game's own Assisted Throw option on |
| Permissions | Same as the other mods (source is MIT on GitHub); required by Daredevil |
| File name / category | `Throw Assist` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x. Stands alone; also required by Daredevil.` |
| Images | a clip/screenshot of a thrown pistol homing onto and staggering an enemy |

**Summary (330 chars):**
`Real-physics steering for the game's own assisted throw (keeps spin and collisions instead of dragging), plus pistol throw assist the game ships switched off, with real thrown-pistol damage and stagger. Billy Clubs (Daredevil) fly their own throw and are left alone by this mod.`

## Before uploading

- [x] Pistol assist, thrown-pistol damage/stagger, and real-physics steering tested.
- [ ] Decide it feels solid enough to publish.
- [ ] Tag: `git tag throwassist-v0.2.0` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`, and backfill it into Daredevil's `NEXUS_DESCRIPTION.txt`.
