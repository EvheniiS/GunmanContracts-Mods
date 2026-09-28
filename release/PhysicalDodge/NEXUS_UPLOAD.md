# Physical Dodge: Nexus upload sheet

**Status: release candidate (0.4.2). New mod page.** Only fine-tuning of the default preference
values is left, and that's personal taste — not a blocker for release.

## Files in this folder

| File | What |
|---|---|
| `PhysicalDodge-0.4.2.zip` | the upload: contains `Mods/PhysicalDodge.dll` |
| `PhysicalDodge.dll` | the shipping build, 18,944 bytes, sha256 `6db46e9f1dbc0293adb8a3e814660ec848eac36f03aa56463233fae79c00e3d5` |
| `NEXUS_DESCRIPTION.txt` | the page text (BBCode) |

Rebuild:
```powershell
dotnet build PhysicalDodge/PhysicalDodge.csproj -c Release -o feature/PhysicalDodge-build
```
then copy the DLL here and zip it as `Mods/PhysicalDodge.dll`.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Physical Dodge` |
| Author | `Evgeeso` |
| Version | `0.4.2` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Gameplay, Difficulty |
| Requirements | Off-site: `MelonLoader 0.7.x`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as the other mods (source is MIT on GitHub) |
| File name / category | `Physical Dodge` / Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |
| Images | a clip/screenshot of ducking or stepping out of a shot, ideally with the dodge buzz + slow-mo caught |

**Summary (330 chars):**
`Enemies aim where you were a moment ago, not where you are. Stand still and get hit; step, lean, duck or move and their shots go past, with a buzz and a brief slow motion on a real dodge. Only shots that would genuinely have hit count, and a dodge is pushed clear so it can never clip you.`

## Before uploading

- [x] Aim lag, spread fairness, rush dodges, haptics and slow motion all tested across many waves.
- [ ] Decide the default `AimLagSeconds` / `DodgeMargin` / slow-motion feel is final (personal taste —
      not a blocker, can be tuned post-release via Mod Settings anyway).
- [ ] Tag: `git tag physicaldodge-v0.4.2` and push the tag.
- [ ] README status → released, with the Nexus link; add the link to the table in
      `release/NEXUS_TEMPLATE.txt`.
