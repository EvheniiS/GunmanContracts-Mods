<#
.SYNOPSIS
  Test fixture for Mod Settings' "Updated defaults" page. Touches ONLY the throwaway mod "Defaults Test" (its DLL, its
  [DefaultsTest] cfg section, its lines in ModSettings_defaults.txt). Your real mods and their settings are never edited.

.DESCRIPTION
  Two builds of one tiny mod: Defaults Test 1.0.0 (old defaults) and 1.1.0 (new defaults). Seven settings:
    A_Untouched   3 -> 1        B_Customised  3 -> 1        C_Flag  false -> true        D_Text  old -> new
    E_Same        5 -> 5        F_AlreadyNew  4 -> 8        G_NewInUpdate (1.1.0 only)
  What must happen to a setting depends on the value the player has when the update arrives:
    value == old default       -> "auto": moves to the new default by itself, blue row, Undo
    value == new default       -> silent (nothing to decide)
    any other value            -> "asks": value kept, yellow row, Keep / Use
    default did not change / setting is new -> silent
  Update prints these predictions for your actual values, and Check verifies them against the game log.

  Order (game CLOSED for every command; the "start the game" steps are yours):
    1. Setup     builds both versions if needed, backs up the two files, wipes the test mod's state, installs 1.0.0.
                 -> start the game, wait for the main menu (no page may appear), quit.  Change values on the board if you like.
    2. Update    installs 1.1.0.  By default it also sets B/D/F to "customised" values; with -Keep it leaves YOUR values.
                 -> start the game, open the board: it must open on "Updated defaults". Try Revert / Use new / Keep. Quit.
    3. Check     PASS / FAIL per row from the log, plus the record file and cfg values.
    4. ResetCfg  (optional) simulates a deleted settings file: removes only the [DefaultsTest] cfg section.
                 -> start the game: the log and the board must say the Defaults Test settings were reset. Run Check.
  Repeat: Setup again (it resets everything). Finished: Remove.

.PARAMETER Command  Setup | Update | Replay | Check | ResetCfg | Status | Remove
                 Replay = jump to "1.1.0 just arrived" with a mix of values, so ONE game start shows the page again (after Setup + one start once).
.PARAMETER Keep     Update only: leave the cfg values exactly as you set them.
.PARAMETER Rebuild  Rebuild both DLLs even if present.
.PARAMETER GameDir  Game folder (default: read from ModSettings\GameDir.local.props).
#>
param([Parameter(Mandatory)][ValidateSet('Setup', 'Update', 'Replay', 'Check', 'ResetCfg', 'Status', 'Remove')][string]$Command, [switch]$Keep, [switch]$Rebuild, [string]$GameDir)

$here = $PSScriptRoot
if (-not $GameDir) {
    $props = Join-Path $here '..\..\ModSettings\GameDir.local.props'
    if (Test-Path $props) { $m = [regex]::Match((Get-Content $props -Raw), '<GameDir>(.+?)</GameDir>'); if ($m.Success) { $GameDir = $m.Groups[1].Value } }
}
if (-not $GameDir -or -not (Test-Path $GameDir)) { Write-Error 'Game folder not found; pass -GameDir.'; exit 1 }
$ud   = Join-Path $GameDir 'UserData'
$cfg  = Join-Path $ud 'MelonPreferences.cfg'
$rec  = Join-Path $ud 'ModSettings_defaults.txt'
$dll  = Join-Path $GameDir 'Mods\DefaultsTest.dll'
$log  = Join-Path $GameDir 'MelonLoader\Latest.log'
$snapFile = Join-Path $here 'out\update-snapshot.txt'
$utf8 = New-Object Text.UTF8Encoding($false)

# Old and new defaults of the fixture (lower-case, no quotes).
$OldDef = [ordered]@{ A_Untouched = '3'; B_Customised = '3'; C_Flag = 'false'; D_Text = 'old'; E_Same = '5'; F_AlreadyNew = '4' }
$NewDef = [ordered]@{ A_Untouched = '1'; B_Customised = '1'; C_Flag = 'true'; D_Text = 'new'; E_Same = '5'; F_AlreadyNew = '8'; G_NewInUpdate = '7' }

function Assert-GameClosed { if (Get-Process GunmanContracts -ErrorAction SilentlyContinue) { Write-Error 'The game is running. Quit it first.'; exit 1 } }
function Build-Variant($v) {
    $out = Join-Path $here "out\$v"
    if ($Rebuild -or -not (Test-Path "$out\DefaultsTest.dll")) {
        $r = dotnet build "$here\DefaultsTest.csproj" -c Release -p:Variant=$v -p:GameDir="$GameDir" -o $out 2>&1
        if (-not (Test-Path "$out\DefaultsTest.dll")) { ($r | Select-Object -Last 12) -join "`n"; Write-Error "Build of $v failed"; exit 1 }
    }
}
function Backup-Files {
    $b = Join-Path $ud ('DefaultsTest-backup\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $b | Out-Null
    foreach ($f in $cfg, $rec) { if (Test-Path $f) { Copy-Item $f $b } }
    "Backup      $b"
}
# Remove the [DefaultsTest] section from the cfg. Nothing else is touched.
function Remove-CfgSection {
    $out = New-Object System.Collections.Generic.List[string]; $skip = $false
    foreach ($l in [IO.File]::ReadAllLines($cfg)) {
        if ($l -match '^\s*\[') { $skip = ($l -match '^\s*\[DefaultsTest\]') }
        if (-not $skip) { $out.Add($l) }
    }
    [IO.File]::WriteAllLines($cfg, $out, $utf8)
}
# Remove the test mod's lines (and its reset-detection counters) from the record file.
function Remove-RecordLines {
    if (Test-Path $rec) {
        $keep = [IO.File]::ReadAllLines($rec) | Where-Object { $_ -notmatch '^(DefaultsTest\.|@Defaults Test\t|@~custom\.DefaultsTest\t)' }
        [IO.File]::WriteAllLines($rec, $keep, $utf8)
    }
}
function Set-CfgValue($name, $valueToml) {
    $lines = [IO.File]::ReadAllLines($cfg); $in = $false; $done = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\[') { $in = ($lines[$i] -match '^\s*\[DefaultsTest\]') }
        elseif ($in -and $lines[$i] -match "^\s*$name\s*=") { $lines[$i] = "$name = $valueToml"; $done = $true }
    }
    if (-not $done) { Write-Error "$name not found in [DefaultsTest] of the cfg. Did you start the game once after Setup?"; exit 1 }
    [IO.File]::WriteAllLines($cfg, $lines, $utf8)
}
function Get-CfgValues {
    $h = [ordered]@{}; $in = $false
    if (Test-Path $cfg) {
        foreach ($l in [IO.File]::ReadAllLines($cfg)) {
            if ($l -match '^\s*\[') { $in = ($l -match '^\s*\[DefaultsTest\]'); continue }
            if ($in -and $l -match '^\s*([A-Za-z_]+)\s*=\s*(.+?)\s*$') { $h[$Matches[1]] = $Matches[2] }
        }
    }
    $h
}
function Get-RecordValues {
    $h = [ordered]@{}
    if (Test-Path $rec) { foreach ($l in [IO.File]::ReadAllLines($rec)) { if ($l -match '^(DefaultsTest\.[^\t]+|@Defaults Test|@~[^\t]*DefaultsTest[^\t]*)\t(.*)$') { $h[$Matches[1]] = $Matches[2] } } }
    $h
}
function Show($h) { $h.GetEnumerator() | ForEach-Object { "  $($_.Key) = $($_.Value)" } }
function Norm($v) { ([string]$v).Trim().Trim('"').ToLower() }

# What Mod Settings must do with each setting, given the player's value when the update arrives.
function Get-Predictions($values) {
    foreach ($k in $NewDef.Keys) {
        $new = $NewDef[$k]; $old = $OldDef[$k]
        $val = if ($values.Contains($k)) { Norm $values[$k] } else { '?' }
        $class = if ($null -eq $old) { 'silent' }
                 elseif ($old -eq $new) { 'silent' }
                 elseif ($val -eq $new) { 'silent' }
                 elseif ($val -eq $old) { 'auto' }
                 else { 'asks' }
        $text = switch ($class) {
            'auto'  { "AUTO    default $old -> $new, yours is the old default (${val}): moves to $new by itself, blue row, Undo" }
            'asks'  { "ASKS    default $old -> $new, yours is ${val}: kept, yellow row, Keep / Use" }
            default { "silent  default $old -> $new, yours ${val}: not listed" }
        }
        [pscustomobject]@{ Key = $k; Class = $class; Text = $text }
    }
}

switch ($Command) {
    'Setup' {
        Assert-GameClosed
        Build-Variant 'Old'; Build-Variant 'New'
        Backup-Files
        if (Test-Path $cfg) { Remove-CfgSection }
        Remove-RecordLines
        Remove-Item $snapFile -ErrorAction SilentlyContinue
        Copy-Item "$here\out\Old\DefaultsTest.dll" $dll -Force
        'Installed   Defaults Test 1.0.0 (old defaults); test state wiped (cfg section + record lines only)'
        ''
        'NEXT: start the game, wait for the main menu, quit. No page may appear on the board.'
        '      (Optionally change some Defaults Test values on the board or in the cfg first.)'
        'Then: .\Test-Defaults.ps1 Update      (or Update -Keep to keep your own values)'
    }
    'Update' {
        Assert-GameClosed
        if (-not (Get-CfgValues).Count) { Write-Error 'No [DefaultsTest] section yet: run Setup, start the game once, quit, then Update.'; exit 1 }
        Build-Variant 'New'
        Backup-Files
        if (-not $Keep) {
            Set-CfgValue 'B_Customised' '2'
            Set-CfgValue 'D_Text' '"mine"'
            Set-CfgValue 'F_AlreadyNew' '8'
            'Cfg         set B_Customised = 2, D_Text = "mine", F_AlreadyNew = 8 (the rest as they were)'
        } else { 'Cfg         left exactly as you set it (-Keep)' }
        $snap = Get-CfgValues
        New-Item -ItemType Directory -Force (Split-Path $snapFile) | Out-Null
        [IO.File]::WriteAllLines($snapFile, @($snap.GetEnumerator() | ForEach-Object { "$($_.Key)`t$($_.Value)" }), $utf8)
        Copy-Item "$here\out\New\DefaultsTest.dll" $dll -Force
        'Installed   Defaults Test 1.1.0 (new defaults)'
        ''
        'Your values going into the update, and what the rules predict:'
        foreach ($p in Get-Predictions $snap) { '  {0,-14} {1}' -f $p.Key, $p.Text }
        ''
        'NEXT: start the game and open the board. It must open on "Updated defaults" and list exactly the AUTO and ASKS rows.'
        'Silent rows must not be listed. The description reads "Updated: Defaults Test 1.0.0 -> 1.1.0".'
        'Try Revert on an AUTO row, Use new on one ASKS row, Keep on another. Quit, then: .\Test-Defaults.ps1 Check'
    }
    'Replay' {
        Assert-GameClosed
        if (-not (Get-CfgValues).Count) { Write-Error 'No [DefaultsTest] section yet: run Setup, start the game once, quit, then Replay.'; exit 1 }
        Build-Variant 'New'
        Backup-Files
        # cfg: a mix that produces every class: A, C untouched (auto); B, D customised (asks); E differs but its default did not change; F already the new default
        Set-CfgValue 'A_Untouched' '3'; Set-CfgValue 'B_Customised' '2'; Set-CfgValue 'C_Flag' 'false'
        Set-CfgValue 'D_Text' '"mine"'; Set-CfgValue 'E_Same' '4'; Set-CfgValue 'F_AlreadyNew' '8'
        # record: what Mod Settings remembered while 1.0.0 was installed
        Remove-RecordLines
        $add = @("@Defaults Test`t1.0.0") + @($OldDef.GetEnumerator() | ForEach-Object { "DefaultsTest.$($_.Key)`t" + $(if ($_.Key -eq 'C_Flag') { 'False' } else { $_.Value }) })
        [IO.File]::AppendAllLines($rec, [string[]]$add, $utf8)
        $snap = Get-CfgValues
        New-Item -ItemType Directory -Force (Split-Path $snapFile) | Out-Null
        [IO.File]::WriteAllLines($snapFile, @($snap.GetEnumerator() | ForEach-Object { "$($_.Key)`t$($_.Value)" }), $utf8)
        Copy-Item "$here\out\New\DefaultsTest.dll" $dll -Force
        'Replayed    1.1.0 has "just arrived": values set, record rewound to the 1.0.0 defaults, 1.1.0 installed.'
        foreach ($p in Get-Predictions $snap) { '  {0,-14} {1}' -f $p.Key, $p.Text }
        'NEXT: start the game once and open the board. Then: .\Test-Defaults.ps1 Check'
    }
    'Check' {
        $script:fail = 0
        function Expect($ok, $what) { if ($ok) { "  PASS  $what" } else { "  FAIL  $what"; $script:fail++ } }
        $recv = Get-RecordValues
        "Installed   $(if (Test-Path $dll) { 'Defaults Test DLL present' } else { 'DLL NOT installed' }); record file version: $($recv['@Defaults Test'])"
        if (Test-Path $log) {
            $lines = @(Get-Content $log); $txt = $lines -join "`n"
            $mine = @($lines | Where-Object { $_ -match 'DefaultsTest\.|Defaults Test|were reset' })
            if ($mine) { 'Log (last session):'; $mine | ForEach-Object { "  $_" } }
            if ($txt -match 'Defaults Test 1\.0\.0 -> 1\.1\.0' -and (Test-Path $snapFile)) {
                $snap = [ordered]@{}; foreach ($l in [IO.File]::ReadAllLines($snapFile)) { $p = $l -split "`t", 2; $snap[$p[0]] = $p[1] }
                'Expectations for the 1.1.0 session, from the values you had at Update:'
                foreach ($p in Get-Predictions $snap) {
                    $k = $p.Key
                    switch ($p.Class) {
                        'auto'  { Expect ($txt -match "DefaultsTest\.${k}: default .* you had the old default") "$k auto-updated" }
                        'asks'  { Expect ($txt -match "DefaultsTest\.${k}: default .* kept until you decide") "$k kept, waits for a decision" }
                        default { Expect ($txt -notmatch "DefaultsTest\.${k}:") "$k not mentioned" }
                    }
                }
            }
            if ($txt -match 'were reset') { 'Reset notice:'; Expect ($txt -match 'Defaults Test.*were reset|were reset.*Defaults Test') 'log says the Defaults Test settings were reset' }
            if (-not $mine) { '(no Defaults Test lines in the last log: was the game started since the last command?)' }
        } else { "No log at $log" }
        'Record file (what Mod Settings remembers):'; Show $recv
        'Current cfg values:'; Show (Get-CfgValues)
        if ($script:fail) { "RESULT: $($script:fail) check(s) FAILED" } else { 'RESULT: no failed checks' }
    }
    'ResetCfg' {
        Assert-GameClosed
        if (-not (Get-CfgValues).Count) { Write-Error 'No [DefaultsTest] section in the cfg to remove.'; exit 1 }
        Backup-Files
        Remove-CfgSection
        'Removed     the [DefaultsTest] section from MelonPreferences.cfg (record file untouched): a deleted settings file in miniature.'
        'NEXT: start the game. The log must say the Defaults Test settings were reset; the board opens on "Updated defaults" with the notice.'
        'Then: .\Test-Defaults.ps1 Check'
    }
    'Status' {
        "DLL         $(if (Test-Path $dll) { (Get-Item $dll).Length.ToString() + ' bytes, ' + (Get-Item $dll).LastWriteTime } else { 'not installed' })"
        'Record:'; Show (Get-RecordValues)
        'Cfg:'; Show (Get-CfgValues)
    }
    'Remove' {
        Assert-GameClosed
        Backup-Files
        if (Test-Path $cfg) { Remove-CfgSection }
        Remove-RecordLines
        Remove-Item $dll -Force -ErrorAction SilentlyContinue
        'Removed     DefaultsTest.dll, the [DefaultsTest] cfg section and its record lines. Real mods untouched.'
    }
}
