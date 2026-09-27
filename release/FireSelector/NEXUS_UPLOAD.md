# Fire Selector: Nexus upload sheet

Game page: Gunman Contracts - Stand Alone (`nexusmods.com/gunmancontractsstandalone`).

## 1.1.0 update (current)

1.0.0 is already on Nexus. This is an update: add a new file on the existing mod page, don't create a new mod.

**Mod page → Manage → Files:**

| Field | Value |
|---|---|
| File | `FireSelector-1.1.0.zip` (contains `Mods/FireSelector.dll`) |
| File name | `Fire Selector` |
| Version | `1.1.0` |
| Category | Main files |
| File description | `Extract into the game folder. Needs MelonLoader 0.7.x.` |
| Old 1.0.0 file | move to **Old versions** (or archive it) |

Also set the **mod version** on the Details tab to `1.1.0`.

**Description:** paste [`NEXUS_DESCRIPTION.txt`](NEXUS_DESCRIPTION.txt) again (BBCode). What changed: a "Don't want
burst?" paragraph under *How to use*, `AllowBurst` in the settings list, and `v1.1.0` in the install check.

**Summary (optional, 330 chars):**
`A fire selector for automatic weapons: hold A / X with your support hand to switch between full auto, 3-round burst and single shot (burst can be turned off). Haptic feedback shows the mode, every weapon remembers its choice, and a quick tap still drops the magazine or toggles the flashlight.`

**Changelog (Manage → Changelogs, version `1.1.0`):**
```
New setting AllowBurst (default true). Set it to false in UserData\MelonPreferences.cfg, section [FireSelector], and the selector only switches between automatic and single shot. A weapon that was saved on burst goes back to automatic.
```

**Reply to the user who asked for it (optional):**
```
Added in 1.1.0: set AllowBurst = false in UserData\MelonPreferences.cfg under [FireSelector], and the hold only switches between auto and single. Thanks for the idea!
```

### Before uploading 1.1.0

- [ ] Played in game with `AllowBurst = false`: the hold goes auto → single → auto, and a weapon saved on burst
      comes back as automatic.
- [ ] Played with `AllowBurst = true`: still auto → burst → single.
- [ ] Tag and push: `git tag fireselector-v1.1.0`, `git push origin main fireselector-v1.1.0`.
- [ ] README status → released, with the Nexus link.

The shipping build is `FireSelector.dll` in this folder (16,896 bytes, sha256 `c36f924f…`). It matches the DLL
deployed to `<game>\Mods`, so testing the installed mod tests the upload.

## First release fields (1.0.0, for reference)

| Field | Value |
|---|---|
| Mod name | `Fire Selector` |
| Author | `Evgeeso` |
| Category | Gameplay |
| Language | English |
| Tags | VR, Weapons, Gameplay, Quality of Life |
| Requirements | Off-site: `MelonLoader 0.7.x`, `https://github.com/LavaGang/MelonLoader/releases`, note: `Tested with 0.7.3; 0.6.x does not work` |
| Permissions | Same as Better Bow (source is MIT on GitHub) |
| Images | `FireSelector-Nexus-thumbnail*.png`, `FireSelector-Nexus-header-v2-1300x372.png` (unchanged for 1.1.0) |
