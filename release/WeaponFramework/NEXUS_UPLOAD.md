# Weapon Framework: Nexus upload sheet (0.3.0)

## Release files

| File | What |
|---|---|
| `WeaponFramework-0.3.0.zip` | Upload package containing `Mods/WeaponFramework.dll` |
| `WeaponFramework.dll` | Current Release build; SHA-256 `0fd255ae3e07656f978d60e7c272f0d70855110e277eefbbd1b1c45b4894a236` |
| `NEXUS_DESCRIPTION.txt` | Nexus page text (BBCode) |

Build from the repository root:

```powershell
dotnet build WeaponFramework/WeaponFramework.csproj -c Release -o feature/WeaponFramework-0.3.0 --no-restore
```

The package contains only Weapon Framework. Install its required `VRHolsterCustomization.dll` and that mod's required
Mod Settings version separately (see the Requirements field). The compiled output also contains the referenced
VRHolsterCustomization assembly; it is not included in the Framework package.

## Mod page fields

| Field | Value |
|---|---|
| Mod name | `Weapon Framework` |
| Author | `Evgeeso` |
| Version | `0.3.0` |
| Category | Utilities / Modders Resources |
| Language | English |
| Tags | VR, Weapons, Modders Resource, Utilities |
| Requirements | Off-site: MelonLoader 0.7.3 or newer; VR Holster Customization (link its Nexus page); Mod Settings (required by VR Holster Customization) |
| File name / category | `Weapon Framework` / Main files |
| File description | `Extract into the game folder. Requires MelonLoader 0.7.3+, VR Holster Customization, and Mod Settings. Adds nothing by itself; install a weapon mod that uses it.` |

**Summary:**
`A framework for weapon mods: register weapons on The Range's arsenal panel, then retrieve them from the wall. Includes shared item helpers and uses VR Holster Customization for mod weapon docking and holsters. Adds nothing on its own.`

## Release checks

- [x] Release build completed with no warnings or errors.
- [ ] Install and in-game test this build with VR Holster Customization and Mod Settings.
- [ ] Confirm a dependent weapon mod registers and retrieves its entry; check the save and removal behavior.
- [ ] Update the Nexus page requirements and description before uploading.
- [ ] Tag and push the release from the appropriate release branch.

This package was built from the current `dev` working tree. It is prepared for review; the new build has not yet been
verified in game.
