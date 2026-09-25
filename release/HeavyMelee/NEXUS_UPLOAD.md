# Heavy Melee: Nexus upload sheet

**Status: release candidate (1.0.0), not released.** Evhenii is still deciding whether it feels right with
this game's enemies. Not tagged; tag only when it's actually uploaded.

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`) → Upload a mod.

| Field | Value |
|---|---|
| Mod name | `Heavy Melee` |
| Version | `1.0.0` |
| Author | `Evgeeso` |
| Category | Gameplay (same as Better Bow, if that's where it went) |
| Language | English |
| Summary (max 350 chars) | `Pistol-whip enemies: guns and bows hit like melee weapons and make them stagger. Punches hit harder, and a knocked-down enemy takes every hit, stays down and can be knocked out. Open hand = no hit, so grabs and the shoulder throw still work. No one-hit kills.` |
| Description | paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) (BBCode) |
| Tags | VR, Combat, Gameplay, Melee |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as Better Bow (source is MIT on GitHub) |

## File

| Field | Value |
|---|---|
| File | `HeavyMelee-1.0.0.zip` (contains `Mods/HeavyMelee.dll`) |
| File name | `Heavy Melee` |
| Version | `1.0.0` |
| Category | Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |

`HeavyMelee.dll` 21,504 bytes, sha256 `991eab2b05d8b2c6272468a117c5ada9ef247f47a9055b0dd8aafd5b07b3a3db`
(= the deployed `<game>\Mods\HeavyMelee.dll`).

## Images

Record a short clip first: a pistol-whip that staggers an enemy, a follow-up punch that drops them, and a
hit on the ground that keeps them down. Then generate the images from a frame, the same way as the other
mods (`release/*/…-imagegen-prompts.txt`):

- **Thumbnail (16:9):** the pistol-whip frame, title `HEAVY MELEE`, small `GUNMAN CONTRACTS` label and amber
  line in the house style, subtitle `WHIP / DROP / KEEP DOWN`.
- **Header (1300 × 372):** the house header with `HEAVY` ivory, `MELEE` amber.

## Before uploading

- [x] Weapon hits, stronger fists, downed hits, keep-down, knock back down, knockout (0.1.0–0.3.0 logs).
- [x] Open hand ignored, grabs and shoulder throw unaffected, kicked debris ignored (0.3.0 log).
- [ ] **Open-palm strike at 8 m/s: not yet played** (added in 0.4.0; the last log is 0.3.0). With `DebugLog`
      on, look for `open-palm strike (game path)` / `open-palm strike hit`.
- [ ] Decide it feels good enough to publish.
- [ ] Tag: `git tag heavymelee-v1.0.0` and push the tag.
- [ ] README status → released, with the Nexus link.
