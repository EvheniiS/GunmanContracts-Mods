# Weapon Framework test helper: prints the saved wall indices and the framework's lines from the last game session.
# Usage: pwsh -File WeaponFramework\Check-WFSave.ps1            (run after quitting the game)
param(
    [string]$Game = "E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone",
    [int]$GameEntries = 6    # Big Guns wall entries the game builds itself (3 rifles + 2 shotguns + bow)
)

$save = Join-Path $env:USERPROFILE "AppData\LocalLow\ANB_Seth\GunmanContracts\Data\SaveData_WeaponSetups.json"
$json = Get-Content $save -Raw
$large = [int]([regex]::Match($json, '"saveSpotLarge":(-?\d+)').Groups[1].Value)
$small = [int]([regex]::Match($json, '"saveSpotSmall":(-?\d+)').Groups[1].Value)
$when = (Get-Item $save).LastWriteTime

Write-Host "Save written $when"
if ($large -lt $GameEntries) { Write-Host "saveSpotLarge = $large  PASS (a game entry, 0-$($GameEntries - 1))" -ForegroundColor Green }
else { Write-Host "saveSpotLarge = $large  FAIL (a mod entry: the save guard did not work)" -ForegroundColor Red }
Write-Host "saveSpotSmall = $small  (pistol wall, not touched by the framework)"

$log = Join-Path $Game "MelonLoader\Latest.log"
Write-Host "`nFramework and holster lines in $log :"
Select-String -Path $log -Pattern "\[Weapon_Framework\]|Weapon Framework v|\[VR_Holster_Customization\]|VR Holster Customization v" | ForEach-Object { $_.Line }
Write-Host "`nErrors mentioning the wall or the framework:"
Select-String -Path $log -Pattern "ANBGunwall|WeaponFramework|ArgumentOutOfRange" | Where-Object { $_.Line -match "Error|Exception|\[E\]" } |
    ForEach-Object { $_.Line }
