# Mod Settings: Nexus upload sheet

Status: release files prepared locally, not uploaded.
Existing page: https://www.nexusmods.com/gunmancontractsstandalone/mods/28

| Field | Value |
|---|---|
| Version | 1.0.0 |
| File | ModSettings-1.0.0.zip |
| File name | Mod Settings |
| Category | Main files |
| File description | Movable VR board, pointing hands and laser selection, per-setting Reset and default indicators. Mouse menu in flat mode. Requires MelonLoader 0.7.x. |
| Description | Paste NEXUS_DESCRIPTION.txt (BBCode) |
| Requirement | MelonLoader; tested with 0.7.3, 0.6.x unsupported |

Summary:
A movable VR settings board for MelonLoader mods, with pointing hands, fingertip laser selection, per-setting Reset and clear default indicators. Open from the phone or Ctrl+M. Flat mode uses an on-screen mouse menu.

Changelog for 1.0.0:
- Larger board with a grip bar to move and tilt it.
- Native menu pointing pose and fingertip laser; trigger selection at a distance.
- Individual Reset controls and DEFAULT / CHANGED indicators.
- Default colour option for mods such as VR Holsters, preserving original game colours.
- Includes the flat-mode mouse menu introduced in 0.3.0.

Package: Mods/ModSettings.dll and INSTALL.txt. No config or game assets.
DLL: 53,248 bytes; SHA256 54cca5a143e599302cbf2d22080efa2c3aebbfc5cb049bc98c825b6060b5e982
ZIP SHA256: dc6a6f024bdfcb1dad342e93447e4d1485fe093ea20faf2d3fc75501bf756b3f

Validation: Release build passed without warnings; packaged DLL hash matches the build.
User confirmed the improved VR menu/holster adjustment is usable. Native colour and contract-to-restart save regression checks remain pending.

Existing thumbnail/header depict an older board; replace with a current screenshot when available.
This package is also included in VR Holsters' With-ModSettings ZIP.

1.0.0 restores the Reset section footer in VR and flat mode while retaining per-setting Reset. Release build passed; section-reset headset regression pending.
