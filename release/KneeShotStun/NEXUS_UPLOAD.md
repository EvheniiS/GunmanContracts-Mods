# Knee Shot Stun: Nexus upload sheet

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`) → Upload a mod.

| Field | Value |
|---|---|
| Mod name | `Knee Shot Stun` |
| Version | `1.0.0` |
| Author | `Evgeeso` |
| Category | Gameplay (same as Better Bow, if that's where it went) |
| Language | English |
| Summary (max 350 chars) | `Shoot an enemy in the leg and they stay down on one knee for 3 seconds longer (adjustable) instead of getting straight back up. Only the kneel is lengthened, so it still looks like the game's own animation, and the enemy stays stunned until back on their feet.` |
| Description | paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) (BBCode) |
| Tags | VR, Combat, Gameplay, AI |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as Better Bow (source is MIT on GitHub) |

## File

| Field | Value |
|---|---|
| File | `KneeShotStun-1.0.0.zip` (contains `Mods/KneeShotStun.dll`) |
| File name | `Knee Shot Stun` |
| Version | `1.0.0` |
| Category | Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |

## Images

Record a short clip in game first: a leg shot, the enemy kneeling for the whole hold, then getting
up (or a finishing shot and the kneeling death). Then generate the images from a frame of it, the
same way as Better Bow's (`release/BetterBow/*-prompt.txt`):

- **Thumbnail (16:9):** the frame with the kneeling enemy, title `KNEE SHOT STUN`, small
  `GUNMAN CONTRACTS` label and amber line in the same style as Better Bow's thumbnail, subtitle
  `DOWN / STUNNED / FINISH`.
- **Header (1300 × 372):** the Better Bow header prompt with the title changed to `KNEE SHOT STUN`
  (KNEE SHOT ivory, STUN amber) and the kneeling enemy instead of the glove and arrow.

## Before uploading

- [x] Played in game: the kneel looks natural (not frozen) and the enemy doesn't shoot from the knee.
- [x] Tag the release: `git tag kneeshotstun-v1.0.0` and push the tag.
- [ ] README status → released, with the Nexus link.
