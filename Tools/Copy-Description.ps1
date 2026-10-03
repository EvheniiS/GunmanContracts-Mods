<#
.SYNOPSIS
  Copies release/<Mod>/NEXUS_DESCRIPTION.txt to the clipboard and opens that mod's edit page, so updating the page text is copy, paste, save.

.DESCRIPTION
  No API can edit a Nexus page description, so this only removes the fiddly part of the manual paste.
  Before copying it asks Nexus for the live description (1 API request) and compares it the same way Check-Nexus.ps1 -Docs does
  (Nexus's own rewriting undone). If the live page already matches, it says so and copies nothing, unless -Force.
  It also warns about leftover {{placeholders}}. It never writes to Nexus.

  Opens https://www.nexusmods.com/<game>/mods/<id>/edit/general in the default browser. On that page: General tab, Description box,
  select all, paste, Save. The Summary and tags are not stored locally and are not touched.

.PARAMETER Mod     Mod folder name (e.g. GrabFix). One mod at a time, because there is one clipboard.
.PARAMETER NoOpen  Copy only; do not open the browser.
.PARAMETER Force   Copy and open even if the live page already matches.

.EXAMPLE
  pwsh -File Tools\Copy-Description.ps1 -Mod GrabFix
#>
param([Parameter(Mandatory)][string]$Mod, [switch]$NoOpen, [switch]$Force)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\Nexus-Common.ps1"
$cfg = Get-Content "$PSScriptRoot\nexus-mods.json" -Raw | ConvertFrom-Json
$id  = $cfg.mods.$Mod
if (-not $id) { Write-Error "$Mod is not in Tools\nexus-mods.json"; exit 1 }
$path = Join-Path $root "release\$Mod\NEXUS_DESCRIPTION.txt"
if (-not (Test-Path $path)) { Write-Error "No page text at $path"; exit 1 }
$text = Get-Content $path -Raw -Encoding utf8

$key = $env:NEXUS_API_KEY
if (-not $key -and (Test-Path "$root\.env")) {
    foreach ($l in Get-Content "$root\.env") { if ($l -match '^\s*NEXUS_API_KEY\s*=\s*(.+?)\s*$') { $key = $Matches[1].Trim('"', "'") } }
}
$state = 'unknown (no API key)'
if ($key) {
    $h = @{ apikey = $key; 'Application-Name' = 'GunmanContractsTools'; 'Application-Version' = '1.0' }
    try {
        $page = Invoke-RestMethod -Uri "https://api.nexusmods.com/v1/games/$($cfg.game)/mods/$id.json" -Headers $h -TimeoutSec 30
        $live = (Norm-Doc $page.description) -join ''; $loc = (Norm-Doc $text) -join ''
        $state = if (($live -replace '\s', '') -ceq ($loc -replace '\s', '')) { 'MATCH' } else { 'DIFFERS' }
    } catch { $state = "unknown ($($_.Exception.Message))" }
}

if ($text -match '\{\{') { Write-Host 'WARNING: placeholders ({{...}}) are still in the text.' -ForegroundColor Yellow }
Write-Host "$Mod  live page vs local text: $state"
if ($state -eq 'MATCH' -and -not $Force) { Write-Host 'The live page already has this text. Nothing to paste (use -Force to copy anyway).'; exit 0 }

Set-Clipboard -Value $text
Write-Host ("Copied {0:N0} characters, {1} lines to the clipboard." -f $text.Length, ($text -split "`n").Count)
$url = "https://www.nexusmods.com/$($cfg.game)/mods/$id/edit/general"
if ($NoOpen) { Write-Host "Edit page: $url" }
else { Start-Process $url; Write-Host "Opened $url" }
Write-Host 'Then: General tab > Description > select all > paste > Save.'
