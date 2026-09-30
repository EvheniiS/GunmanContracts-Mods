# Throw Assist: Nexus upload sheet

**Status: 0.2.1 packaged release candidate. The 2026-09-30 session selected 25° for head aim. Not uploaded yet.**

The description now mentions the private 0.2.2 knee-assist experiment, explicitly outside the 0.2.1 package.
Keep that experimental build private for now. Its `allowKneeHit` default remains false; no package replacement
or public 0.2.2 release is implied by this description update.

Required by Daredevil's description — upload this one first, or in the same sitting as Daredevil.

## Files in this folder

| File | What |
|---|---|
| `ThrowAssist-0.2.1.zip` | current upload: contains only `Mods/ThrowAssist.dll`; 12,121 bytes; sha256 `81C76E66FB6B8C7A7BE0822617B5D73B6D1F867A5FACEB26E1BD7347799103F9` |
| `ThrowAssist.dll` | current 0.2.1 build; 27,648 bytes; sha256 `BEB3A859A2A5E6F61F926CCA9408C8B964129536CF1104B1D9227E0F92F8FCA3` |
| `ThrowAssist-0.2.0.zip` | previous package; keep for rollback, do not upload as 0.2.1 |
| `NEXUS_DESCRIPTION.txt` | page text for 0.2.1 (BBCode) |

Rebuild:
```powershell
dotnet build ThrowAssist/ThrowAssist.csproj -c Release -o feature/ThrowAssist-0.2.1-release-build --no-restore
```
then copy the DLL here and zip it as `Mods/ThrowAssist.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Throw Assist` |
| Author | `Evgeeso` |
| Version | `0.2.1` |
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
- [ ] Tag: `git tag throwassist-v0.2.1` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`, and backfill it into Daredevil's `NEXUS_DESCRIPTION.txt`.
