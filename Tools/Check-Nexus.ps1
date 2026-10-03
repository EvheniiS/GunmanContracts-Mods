<#
.SYNOPSIS
  Shows which version of each mod is live on Nexus, next to the newest release zip and the source version.

.DESCRIPTION
  Reads Tools\nexus-mods.json (mod folder -> Nexus mod id) and asks the Nexus API (v1) for each mod's page and file list.
  The page's own "version" field is a Nexus counter ("1", "2"), not the mod version, so the live version is parsed from the
  name of the MAIN file (e.g. "GrabFix 1.1.1"). Compared with:
    - newest release/<Mod>/<Mod>-<ver>.zip
    - source version (MelonInfo in <Mod>/*.cs), same method as Check-Releases.ps1
  Verdicts: IN SYNC | NOT UPLOADED (zip newer than Nexus) | UNRELEASED (source newer than zip) | NEXUS AHEAD |
            NOT PUBLISHED (page hidden / removed) | NO ZIP | NO NEXUS FILE

  The API key is read from NEXUS_API_KEY in the environment or in <repo>\.env (git- and MEGA-ignored).
  Costs 2 API requests per mod (3 with -Docs). Nexus's real limits, read from the response headers: 2,000/hour and 20,000/day;
  the run prints what is left, so no caching is needed.

.PARAMETER Mod       Only check these mod names.
.PARAMETER Docs      Also compare the live page with release/<Mod>/: description vs NEXUS_DESCRIPTION.txt (line by line, <br /> and
                 HTML entities normalised), whether the live changelog has an entry for the live version, and the Settings block of the live page against the config the shipped DLL was built from
                 (the source at the commit where release/<Mod>/<Mod>.dll was last committed). A default that differs there means the
                 page is wrong. +1 API call per mod.
.PARAMETER Discover  Also probe the next few mod ids after the highest known one and report new pages by you that are not in nexus-mods.json.

.EXAMPLE
  pwsh -File Tools\Check-Nexus.ps1
  pwsh -File Tools\Check-Nexus.ps1 -Mod GrabFix,Daredevil
  pwsh -File Tools\Check-Nexus.ps1 -Discover
#>
param([string[]]$Mod, [switch]$Discover, [switch]$Docs)

$Mod  = @($Mod | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # `pwsh -File ... -Mod A,B` delivers one string "A,B"
$root = Split-Path $PSScriptRoot -Parent
$rel  = Join-Path $root 'release'

# --- key ---
$key = $env:NEXUS_API_KEY
if (-not $key -and (Test-Path "$root\.env")) {
    foreach ($l in Get-Content "$root\.env") {
        if ($l -match '^\s*NEXUS_API_KEY\s*=\s*(.+?)\s*$') { $key = $Matches[1].Trim('"', "'") }
    }
}
if (-not $key) { Write-Error 'NEXUS_API_KEY not found (environment or <repo>\.env)'; exit 1 }

$headers = @{ apikey = $key; 'Application-Name' = 'GunmanContractsTools'; 'Application-Version' = '1.0' }
$cfg  = Get-Content "$PSScriptRoot\nexus-mods.json" -Raw | ConvertFrom-Json
$base = "https://api.nexusmods.com/v1/games/$($cfg.game)"

function Invoke-Nexus($path) {
    try {
        $r = Invoke-RestMethod -Uri "$base/$path" -Headers $headers -TimeoutSec 30 -ResponseHeadersVariable rh
        $script:calls++
        $script:quota = @{ Day = [string]$rh['x-rl-daily-remaining']; Hour = [string]$rh['x-rl-hourly-remaining'] }
        $r
    }
    catch {
        $code = [int]$_.Exception.Response.StatusCode
        if ($code -eq 401) { Write-Error 'Nexus rejected the API key (401)'; exit 1 }
        if ($code -eq 429) { Write-Error 'Nexus rate limit hit (429); try again later'; exit 1 }
        if ($code -eq 404) { return $null }
        throw
    }
}
function Get-SourceVersion($dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\Example\\' }) {
        $m = Select-String -Path $f.FullName -Pattern 'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([\d.]+)"' | Select-Object -First 1
        if ($m) { return $m.Matches[0].Groups[1].Value }
    }
}
function Get-ZipVersion($name) {
    $zip = Get-ChildItem (Join-Path $rel $name) -Filter "$name-*.zip" -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -match "^$name-([\d.]+)\.zip$" } |
           Sort-Object { [version]([regex]::Match($_.Name, '([\d.]+)\.zip$').Groups[1].Value) } | Select-Object -Last 1
    if ($zip) { [regex]::Match($zip.Name, '([\d.]+)\.zip$').Groups[1].Value }
}
function Ver($s) {   # pad to 4 parts so "1.0" == "1.0.0"
    $p = @($s -split '\.' | ForEach-Object { [int]$_ }); while ($p.Count -lt 4) { $p += 0 }
    [version]($p[0..3] -join '.')
}
function Cmp($a, $b) { (Ver $a).CompareTo((Ver $b)) }

$names = $cfg.mods.PSObject.Properties.Name
if ($Mod) { $names = $names | Where-Object { $Mod -contains $_ } }

. "$PSScriptRoot\Nexus-Common.ps1"   # Norm-Doc, Short, settings parsing/comparison, shipped-source lookup

$docRows = @()
$rows = @(); $notes = @()
foreach ($name in $names) {
    $id   = $cfg.mods.$name
    $page = Invoke-Nexus "mods/$id.json"
    $src  = Get-SourceVersion (Join-Path $root $name)
    $zipV = Get-ZipVersion $name
    if (-not $page -or $page.status -ne 'published') {
        $st = if ($page) { $page.status } else { 'missing' }
        $rows += [pscustomobject]@{ Mod=$name; Id=$id; Nexus='-'; Zip=$zipV; Source=$src; Updated='-'; Dl='-'; Endo='-'; Verdict="NOT PUBLISHED ($st)" }
        continue
    }
    $files = (Invoke-Nexus "mods/$id/files.json").files
    $main  = @($files | Where-Object { $_.category_name -eq 'MAIN' })
    $pick  = @($main | Where-Object { $_.is_primary }) + @($main | Sort-Object uploaded_timestamp -Descending) | Select-Object -First 1
    $nexV  = $null
    if ($pick -and $pick.name -match '(\d+(?:\.\d+)+)') { $nexV = $Matches[1] }
    if ($main.Count -gt 1) { $notes += "$name : $($main.Count) MAIN files on Nexus ($(($main | ForEach-Object { $_.name }) -join ', ')); using '$($pick.name)'" }
    if ($pick -and -not $nexV) { $notes += "$name : no version in MAIN file name '$($pick.name)'" }

    $verdict = 'IN SYNC'
    if (-not $nexV)    { $verdict = 'NO NEXUS FILE' }
    elseif (-not $zipV) { $verdict = 'NO ZIP' }
    elseif ((Cmp $zipV $nexV) -gt 0) { $verdict = 'NOT UPLOADED' }
    elseif ((Cmp $zipV $nexV) -lt 0) { $verdict = 'NEXUS AHEAD' }
    elseif ($src -and (Cmp $src $zipV) -gt 0) { $verdict = 'UNRELEASED' }

    if ($Docs) {
        $rdir = Join-Path $rel $name
        $descV = '-'; $chV = '-'
        $dp = Join-Path $rdir 'NEXUS_DESCRIPTION.txt'
        if (Test-Path $dp) {
            $live = Norm-Doc $page.description; $loc = Norm-Doc (Get-Content $dp -Raw)
            # Nexus also moves closing tags like [/code] onto the previous line, so compare with all whitespace removed first.
            $diff = if ((($live -join '') -replace '\s', '') -ceq (($loc -join '') -replace '\s', '')) { @() } else { @(Compare-Object $live $loc) }
            if (-not $diff) { $descV = 'MATCH' }
            else {
                $onN = @($diff | Where-Object SideIndicator -eq '<=').Count; $onL = @($diff | Where-Object SideIndicator -eq '=>').Count
                $descV = "DIFFERS (+$onL local, -$onN Nexus)"
                $f = $diff | Select-Object -First 1
                $notes += "$name description: first difference, $(if ($f.SideIndicator -eq '=>') {'local only'} else {'Nexus only'}): $(Short $f.InputObject)"
            }
        } else { $descV = 'no local file' }
        $cl = Invoke-Nexus "mods/$id/changelogs.json"
        if ($nexV) {
            $hit = $false
            if ($cl) { foreach ($p in $cl.PSObject.Properties) { if ((@($p.Value) -join ' ') -match [regex]::Escape($nexV)) { $hit = $true } } }
            $chV = if ($hit) { "has $nexV" } else { "none for $nexV" }
        }
        # Live page Settings block vs the config the shipped DLL was built from (source at the release DLL's last commit).
        $setV = '-'
        $ship = Get-ShippedSourceTexts $root $name
        if (-not $ship) { $setV = 'no shipped baseline' }
        else {
            $shipSet = Get-CodeSettings $ship.Texts
            $pageSet  = Get-DocSettings ((Norm-Doc $page.description) -join "`n")
            if (-not $shipSet.Count) { $setV = 'no settings in code' }
            else {
                $cmp = Compare-Settings $shipSet $pageSet
                $bad = @()
                if ($cmp.Mismatch) { $bad += "DEFAULT $($cmp.Mismatch.Count)" }
                if ($cmp.Missing)  { $bad += "missing $($cmp.Missing.Count)" }
                if ($cmp.Extra)    { $bad += "extra $($cmp.Extra.Count)" }
                $setV = if ($bad) { $bad -join ', ' } else { "OK ($($cmp.Checked) defaults)" }
                if ($cmp.Mismatch) { $notes += "$name settings: live page default differs from the shipped config (@$($ship.Commit)): $($cmp.Mismatch -join '; ')" }
                if ($cmp.Missing)  { $notes += "$name settings: in the shipped code, not on the live page: $($cmp.Missing -join ', ')" }
                if ($cmp.Extra)    { $notes += "$name settings: on the live page, not in the shipped code: $($cmp.Extra -join ', ')" }
            }
        }
        $docRows += [pscustomobject]@{ Mod = $name; Description = $descV; Changelog = $chV; Settings = $setV }
    }

    $upd = if ($pick) { [DateTimeOffset]::FromUnixTimeSeconds($pick.uploaded_timestamp).LocalDateTime.ToString('yyyy-MM-dd HH:mm') } else { '-' }
    $rows += [pscustomobject]@{ Mod=$name; Id=$id; Nexus=$nexV; Zip=$zipV; Source=$src; Updated=$upd; Dl=$page.mod_downloads; Endo=$page.endorsement_count; Verdict=$verdict }
}

$rows | Format-Table -AutoSize
if ($docRows) { 'Live page vs local docs:'; $docRows | Format-Table -AutoSize }
if ($notes) { 'Notes:'; $notes | ForEach-Object { "  $_" } }
if ($script:quota) { "API: $($script:calls) requests this run; $($script:quota.Hour) left this hour, $($script:quota.Day) left today." }

if ($Discover) {
    $known = @($cfg.mods.PSObject.Properties.Value)
    $max = ($known | Measure-Object -Maximum).Maximum
    $me  = (Invoke-RestMethod -Uri 'https://api.nexusmods.com/v1/users/validate.json' -Headers $headers).name
    'New pages by you not in nexus-mods.json (probing ids above ' + $max + '):'
    $found = 0
    foreach ($id in ($max + 1)..($max + 8)) {
        $p = Invoke-Nexus "mods/$id.json"
        if ($p -and $p.user.name -eq $me -and $known -notcontains $id) { "  $id  $($p.name)  [$($p.status)]"; $found++ }
    }
    if (-not $found) { '  none' }
}
