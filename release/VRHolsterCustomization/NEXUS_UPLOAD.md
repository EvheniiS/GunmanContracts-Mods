# VR Holster Customization: Nexus upload sheet

Status: release packages prepared locally, not uploaded. No Nexus mod page URL assigned here.

| Field | Value |
|---|---|
| Mod name | VR Holster Customization |
| Version | 0.2.1 |
| Author | Evgeeso |
| Description | Paste NEXUS_DESCRIPTION.txt (BBCode) |
| Category | Gameplay |
| Tags | VR, Gameplay, Utility |
| Nexus requirement | Mod Settings — https://www.nexusmods.com/gunmancontractsstandalone/mods/28 |
| Requirement notes | Required, not optional. Install Mod Settings 0.4.1 or newer; included in the bundle. |
| Off-site requirement | MelonLoader — https://github.com/LavaGang/MelonLoader/releases — tested with 0.7.3; 0.6.x unsupported |
| Source | https://github.com/EvheniiS/GunmanContracts-Mods (MIT) |

Summary:
Reposition the game's pistol, knife and back holsters live, or hold grip + trigger to move an empty holster by hand. Choose original or custom colours. Saves your Range loadout and supports registered mod-item back slots. Requires Mod Settings.

## Files — install one

| Package | Category | Upload description |
|---|---|---|
| VRHolsterCustomization-0.2.1.zip | Main files | VR Holsters only. Requires Mod Settings 0.4.1 or newer installed separately. Extract Mods into the game folder. |
| VRHolsterCustomization-0.2.1-With-ModSettings.zip | Optional files | Includes VR Holsters 0.2.1 and required Mod Settings 1.0.0. Use instead of standalone. Choose standalone if you already have newer Mod Settings. |

Standalone contents: Mods/VRHolsterCustomization.dll, INSTALL.txt.
Bundle contents: Mods/VRHolsterCustomization.dll, Mods/ModSettings.dll, INSTALL.txt.
Neither includes personal preferences, saved loadouts, game assemblies or assets.

## Checksums

- VRHolsterCustomization.dll: 48,128 bytes; SHA256 cf488d59c767821d84feefdf56ad2ea2352a7d22ed5821b738330675cb405377
- ModSettings.dll in bundle: 53,248 bytes; SHA256 54cca5a143e599302cbf2d22080efa2c3aebbfc5cb049bc98c825b6060b5e982
- Standalone ZIP: 22,887 bytes; SHA256 259bd359a1d873503d21a690e3bbae22875975567fcb5d1bfcf4f64e383e7421
- Bundle ZIP: 49,236 bytes; SHA256 ebd16cf27d7c33eeb5a4df5410c655ccdf13eb507b809bc1893460866f48fa0b

## Release notes

- Mod Settings is now a hard MelonLoader dependency.
- Move each game holster along three axes using readable centimetre controls.
- Optional grip + trigger hold-to-move gesture for empty game holsters, with coloured markers and haptics.
- Default colour preserves native game colours; custom hex colours are available.
- Automatic loadout saving is limited to The Range, avoiding contract saves that erase a drawn pistol's prepared slot.
- Weapon Framework and Daredevil remain optional integrations.

## Validation

Both release builds passed without warnings; every DLL inside every ZIP was checked against its release DLL by SHA256.
The user confirmed the VR menu and direct holster adjustment work well.
Native-colour reset and the contract → main menu → restart loadout fix still need an in-game regression pass.
Missing-dependency loader behaviour has not been run in a separate game session; the dependency is declared using MelonAdditionalDependencies("ModSettings").

Do not mark these local files as uploaded, publish the page, or tag a release based on this sheet alone.
