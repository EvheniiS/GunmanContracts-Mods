# Aim Colors (0.1.0, release candidate: tested in game Oct 2 2026)

Renamed from Laser Color, then Gun Colors (the weapons themselves are not recoloured, only what you aim with). Delete the old `LaserColor.dll` / `GunColors.dll` from `Mods`; the file is now `AimColors.dll`. Settings are under `[AimColors]` in `UserData/MelonPreferences.cfg` and on the Mod Settings board, applied within about two seconds. A colour equal to the game's default changes nothing.

| Setting | Default | What it does |
|---|---|---|
| `LaserColor` | `#FF3640` | Laser beam and dot (the game's own red). Works on pistol and bow. |
| `LaserBoost` | `5` | Beam and dot brightness (0.2 to 8). 5 on the default red is much easier to see, even through wireless-stream compression. |
| `SightColor` | `#5A080A` | Default iron sights (dark red; the game's own is `#FFB03B`, yellow-orange). Shared by every gun. |
| `SightBoost` | `1` | Iron-sight glow multiplier (0.2 to 8). Not tuned; left at 1 for people to play with. |
| `ReticleColor` | `#5A080A` | Collimator (red dot sight) reticle (the game's own is `#E8992E`, amber). |
| `ReticleBoost` | `5` | Reticle brightness (0.2 to 8). Dark red x5 comes out as a vivid overbright red. |
| `ReticleSize` | `1.2` | Reticle dot size multiplier (0.3 to 4). |
| `IncludeEnemies` | on | Also recolour enemy weapons' lasers. (Iron sights and reticles are shared materials, so enemy guns follow those settings.) |

Defaults are the tested look. Set `LaserBoost`/`ReticleBoost` to 1 and the colours to the game's own values above to get vanilla back.

## How it works (Attachment Probe dumps, game 0.3.1.1, Oct 2 2026)

- **Laser:** each weapon has its own `lasersource/laserbeam` with one 1-degree spot `Light` (intensity 700000; the dot) and a `VolumetricLightBeamSD` with `colorFromLight = true` (the beam; instanced shader, so a material tint does nothing). The mod sets the Light colour, the beam colour and both intensities.
- **Iron sights:** SpriteRenderers on one shared URP Lit material `sights`, no texture: `_BaseColor` (1, 0.69, 0.23) + `_EmissionColor` (0.51, 0.17, 0). The mod sets both, scaling the emission like the game's own.
- **Collimator reticle:** sight slot AT3 ("Reflexsight", object `holosight`) holds a Burris-style red dot on pistols (`PistolHolosight`) and `NewScope` on the AB15 rifle. The dot is a quad with material `RedDot Sight Glass Pistols` / `RedDot Sight Glass Howler 2`, shader `Vashchuk/RedDot(Unlit_Fixed)`: `_RedDotColor` (0.91, 0.6, 0.18), `_RedDotTex` = `Dot_ANB`, `_RedDotSize` 0.35 (pistol) / 2 (rifle), keyword `FIXED_SIZE`. The shader is unlit, so the colour can go above 1 for extra brightness. The glass is a separate transparent `Glass` material and is left alone.

## Tested (Oct 2 2026, game 0.3.1.1)

Laser colour + boost on pistol and bow; iron-sight tint; reticle colour, boost and size on pistol and AB15 rifle. Not exercised: `SightBoost` above 1, the rifle scopes beyond the collimator, enemy guns during a contract.
