# Throw Assist: Nexus upload sheet

**Status: 0.2.2 packaged release candidate with 3.5 m/s default assist gates. Not uploaded yet.**

Optional knee assist is included but off by default and remains experimental.

Required by Daredevil's description — upload this one first, or in the same sitting as Daredevil.

## Files in this folder

| File | What |
|---|---|
| `ThrowAssist-0.2.2.zip` | current upload candidate: contains only `Mods/ThrowAssist.dll`; 14,362 bytes; sha256 `F95C835501B9E6EE5226F6EC1E68592E46B5BBF6E8971FBF4A6B75B59594EC2E` |
| `ThrowAssist.dll` | current 0.2.2 build; 32,768 bytes; sha256 `EB2A926A1F3E36CED4CE684B7440DC83FC88429E53A3F94B8918A156EDF17872` |
| `ThrowAssist-0.2.0.zip` and `ThrowAssist-0.2.1.zip` | previous packages; keep for rollback |
| `NEXUS_DESCRIPTION.txt` | page text for 0.2.2 (BBCode) |

Rebuild:
```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-0.2.2-release-build --no-restore
```
then copy the DLL here and zip it as `Mods/ThrowAssist.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Throw Assist` |
| Author | `Evgeeso` |
| Version | `0.2.2` |
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

- [x] Pistol assist, thrown-pistol damage/stagger, and real-physics steering tested in prior builds.
- [x] Review 2026-09-30 throw log: 25° head aim selected; assisted knife stabs are consistent. No assisted face-bounce failure was observed.
- [x] Test direct Billy Club throws with the matching Daredevil build and select 25° maximum turn. Ricochet aiming keeps its separate setting.
- [ ] Decide it feels solid enough to publish.
- [ ] Tag: `git tag throwassist-v0.2.2` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`, and backfill it into Daredevil's `NEXUS_DESCRIPTION.txt`.
