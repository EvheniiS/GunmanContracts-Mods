# Sound Probe (diagnostic, 0.1.0)

Tested: 0.1.0 (2026-10-03)

Lists every sound the game plays, by clip name, so you can find which clip a holster, a wall pickup, a hit or a footstep makes. A mod can then play that clip by name (VR Holster Customization's `HolsterInSound` / `HolsterOutSound` do), or an audio replacer (AudioReplacer, installed) can swap it for your own file with a matching name. It changes nothing in the game.

Output: `UserData/SoundProbe/session-<date>-<time>.log`, one line per sound: `time | clip | kind | x,y,z`. The time is the same clock as the MelonLoader log, so a line can be matched to what another mod logged at that moment. A position of `0,0,0` is a sound played without a place (the player's own equipment sounds).

`[SoundProbe]` settings:
- `Enabled` (**off by default**): switch on in the cfg or Mod Settings before the run, off afterwards. The log file is only created once a sound is heard while it is on.
- `Hide` (`Footstep,UI Click`): clip names containing any of these words are counted, not listed. Blank lists everything.
- `Only`: list only clips whose name contains this text.

**Claude: for any question about which clip a game sound is, or when changing or replacing sound effects, use this first.** Set `Enabled = true` in `UserData/MelonPreferences.cfg` (game closed), have the user do the one action with a pause between actions, read the newest `UserData/SoundProbe/session-*.log` around the time, then set it back to false.

How to use it: do one thing in the game (draw a katana), note the time, read the lines at that time. Pause a second between actions.

Limit: it hooks HurricaneVR's sound pool (`SFXPlayer.PlaySFX`, `PlaySFXGunfire`, `PlaySFXRandomPitch`), which carries the weapon, impact, equipment and footstep sounds. Sounds an AudioSource plays directly (music, some voices) are not seen.

Found with it (Oct 3 2026): drawing a knife or katana from the belt plays `S_WEP_Knife_Attack_01` and `handgrab`; putting one in the belt holster is silent; taking a katana from the wall plays `EQUIPTact_Equipment Metal Buckle Chain Jangle Latch Flap Belts 03_ESM_SG`; the bow and every gun use `Pistol_Holster` / `Pistol_Unholster`.

Install: `SoundProbe.dll` in the game's `Mods` folder. Needs nothing else.
