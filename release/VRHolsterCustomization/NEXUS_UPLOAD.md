# VR Holster Customization: Nexus upload sheet

Status: release packages prepared locally, not uploaded. No Nexus mod page URL assigned here.

| Field | Value |
|---|---|
| Mod name | VR Holster Customization |
| Version | 0.2.2 |
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
| VRHolsterCustomization-0.2.2.zip | Main files | VR Holsters only. Requires Mod Settings 0.4.1 or newer installed separately. Extract Mods into the game folder. |
| VRHolsterCustomization-0.2.2-With-ModSettings.zip | Optional files | Includes VR Holsters 0.2.2 and required Mod Settings 1.0.0. Use instead of standalone. Choose standalone if you already have newer Mod Settings. |

Standalone contents: Mods/VRHolsterCustomization.dll, INSTALL.txt.
Bundle contents: Mods/VRHolsterCustomization.dll, Mods/ModSettings.dll, INSTALL.txt.
Neither includes personal preferences, saved loadouts, game assemblies or assets.

## Checksums

- VRHolsterCustomization.dll: 50,176 bytes; SHA256 166265efd33d4e2934d6d1c0646a7856b72439ce2ce490bd4eb5cfe30412daaa
- ModSettings.dll in bundle: 53,248 bytes; SHA256 54cca5a143e599302cbf2d22080efa2c3aebbfc5cb049bc98c825b6060b5e982
- Standalone ZIP: 23,440 bytes; SHA256 985d8eea39d4b2fb08996f0f766FDA207B8796BA8D7B0B973C379E784E06B756
- Bundle ZIP: 49,742 bytes; SHA256 ffd532938e8c0a927c23540015629cbf85abd30a72d667caf88018b8ae7cf5b1

## Release notes

- Mod Settings is now a hard MelonLoader dependency.
- Move each game holster along three axes using readable centimetre controls.
- Optional grip + trigger hold-to-move gesture for empty game holsters, with coloured markers and haptics.
- Default colour preserves native game colours; custom hex colours are available.
- Automatic loadout saving is limited to The Range, avoiding contract saves that erase a drawn pistol's prepared slot.
- Registered mod-item back slots restore the prepared item after same-scene checkpoint retry. The user confirmed this with the crowbar.
- Weapon Framework and Daredevil remain optional integrations.

## Validation

Both release builds passed without warnings; every DLL inside every ZIP was checked against its release DLL by SHA256.
The user confirmed the VR menu, direct holster adjustment, and back-slot checkpoint reset work well.
Native-colour reset and the contract → main menu → restart loadout fix still need an in-game regression pass.
Missing-dependency loader behaviour has not been run in a separate game session; the dependency is declared using MelonAdditionalDependencies("ModSettings").

Do not mark these local files as uploaded, publish the page, or tag a release based on this sheet alone.
