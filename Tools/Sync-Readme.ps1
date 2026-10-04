<#
.SYNOPSIS
  Rewrites the Status column of the root README's mod table from facts: source version, the newest release zip, the mod
  README's `Tested:` line, and the Nexus page id. Run it when `dev` goes into `main`; it is not part of normal dev work.

.DESCRIPTION
  Status cell = "<version>, <state>" + optional note from Tools\readme-notes.json:
    source == newest zip, mod has a Nexus id : "1.2.0, [on Nexus](url)"        (+ "(last tested x)" when x differs)
    source newer than the zip                : "1.3.0, tested" | "1.3.0, untested (last tested 1.2.0)"  + "; 1.2.0 on [Nexus](url)"
    no Nexus page                            : "0.1.0, tested" | "0.1.0, untested"
  The mod folder is read from the row's link (Folder/...). Only the Status cell changes; the "What it does" text is hand-written.
  Dry run by default (prints the changed rows). -Apply writes README.md and refuses unless the branch is `main`
  (-AllowDev overrides, for testing). Never commits.

.PARAMETER Apply     Write the file.
.PARAMETER AllowDev  Let -Apply run on a branch other than main.
.PARAMETER ReadmePath  Table to rewrite (default: root README.md). For tests.
#>
param([switch]$Apply, [switch]$AllowDev, [string]$ReadmePath)

$root = Split-Path $PSScriptRoot -Parent
if (-not $ReadmePath) { $ReadmePath = Join-Path $root 'README.md' }
$rel = Join-Path $root 'release'
. "$PSScriptRoot\Nexus-Common.ps1"
$cfg   = Get-Content "$PSScriptRoot\nexus-mods.json" -Raw | ConvertFrom-Json
$notes = (Get-Content "$PSScriptRoot\readme-notes.json" -Raw | ConvertFrom-Json).notes

if ($Apply -and -not $AllowDev) {
    $branch = (git -C $root branch --show-current).Trim()
    if ($branch -ne 'main') { Write-Error "Refusing -Apply on '$branch': the root README is updated only when going into main (-AllowDev to override)."; exit 1 }
}

function Get-ZipVersion($name) {
    $zip = Get-ChildItem (Join-Path $rel $name) -Filter "$name-*.zip" -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -match "^$name-([\d.]+)\.zip$" } |
           Sort-Object { Ver ([regex]::Match($_.Name, '([\d.]+)\.zip$').Groups[1].Value) } | Select-Object -Last 1
    if ($zip) { [regex]::Match($zip.Name, '([\d.]+)\.zip$').Groups[1].Value }
}

$raw = [IO.File]::ReadAllText($ReadmePath)
$nl  = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
$out = New-Object System.Collections.Generic.List[string]
$changed = 0; $skipped = @()
foreach ($line in ($raw -split "`r?`n")) {
    $m = [regex]::Match($line, '^(\| \[[^\]]+\]\(([A-Za-z]+)/[^)]*\) \|.*\| )([^|]*) \|$')
    if (-not $m.Success -or -not (Test-Path (Join-Path $root $m.Groups[2].Value))) { $out.Add($line); continue }
    $name = $m.Groups[2].Value
    $src  = Get-SourceVersion (Join-Path $root $name)
    if (-not $src) { $skipped += "$name (no MelonInfo version)"; $out.Add($line); continue }
    $zipV = Get-ZipVersion $name
    $tst  = Get-TestedStatus (Join-Path $root $name)
    $id   = $cfg.mods.$name
    $url  = if ($id) { "https://www.nexusmods.com/$($cfg.game)/mods/$id" } else { $null }
    $label = Get-TestLabel $src $tst
    if (-not $tst) { $skipped += "$name (README has no Tested line)" }

    if ($url -and $zipV -and (Cmp $src $zipV) -eq 0) {
        $cell = "$src, [on Nexus]($url)"
        if ($label -ne 'tested') { $cell += " ($label)" }
    } else {
        $cell = "$src, $label"
        if ($url -and $zipV) { $cell += "; $zipV on [Nexus]($url)" }
    }
    if ($notes.$name) { $cell += "; $($notes.$name)" }
    $new = $m.Groups[1].Value + $cell + ' |'
    if ($new -ne $line) { $changed++; "{0,-24} {1}  ->  {2}" -f $name, $m.Groups[3].Value.Trim(), $cell }
    $out.Add($new)
}
if ($skipped) { "Skipped/flagged: $($skipped -join '; ')" }
if (-not $changed) { 'Status column already up to date.' }
elseif ($Apply) { [IO.File]::WriteAllText($ReadmePath, ($out -join $nl), (New-Object Text.UTF8Encoding($false))); "Wrote $changed row(s) to $ReadmePath. Review the diff and commit it with the merge." }
else { "$changed row(s) would change. Re-run with -Apply on main to write them." }
