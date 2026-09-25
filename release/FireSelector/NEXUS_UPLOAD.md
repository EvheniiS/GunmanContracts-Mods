# Fire Selector: Nexus upload sheet

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`) → Upload a mod.

| Field | Value |
|---|---|
| Mod name | `Fire Selector` |
| Version | `1.0.0` |
| Author | `Evgeeso` |
| Category | Gameplay (same as Better Bow, if that's where it went) |
| Language | English |
| Summary (max 350 chars) | `A fire selector for automatic weapons: hold A / X with your support hand to switch between full auto, 3-round burst and single shot. Haptic feedback shows the mode, every weapon remembers its choice, and a quick tap still drops the magazine or toggles the flashlight.` |
| Description | paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) (BBCode) |
| Tags | VR, Weapons, Gameplay, Quality of Life |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as Better Bow (source is MIT on GitHub) |

## File

| Field | Value |
|---|---|
| File | `FireSelector-1.0.0.zip` (contains `Mods/FireSelector.dll`) |
| File name | `Fire Selector` |
| Version | `1.0.0` |
| Category | Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |

## Images

Nothing to make these from yet: record a short clip in game first (like the Better Bow videos in
`media/`). Worth capturing: the support hand on the foregrip, a switch, then a 3-round burst into a
target, and a single shot. Then generate the images from a frame of that clip, the same way as Better
Bow's (`release/BetterBow/*-prompt.txt`):

- **Thumbnail (16:9):** the frame with the rifle and support hand, title `FIRE SELECTOR`, small
  `GUNMAN CONTRACTS` label and amber line in the same style as Better Bow's thumbnail, subtitle
  `AUTO / BURST / SINGLE`.
- **Header (1300 × 372):** the Better Bow header prompt with the title changed to `FIRE SELECTOR`
  (FIRE ivory, SELECTOR amber) and the glove holding the rifle instead of the arrow.

## Before uploading

- [x] Played in game: all three modes fire as described, a tap on the foregrip still drops the mag.
- [x] Tag the release: `git tag fireselector-v1.0.0` and push the tag.
- [ ] README status → released, with the Nexus link.
