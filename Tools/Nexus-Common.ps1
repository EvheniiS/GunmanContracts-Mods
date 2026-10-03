# Shared helpers for Check-Nexus.ps1, Pack-Release.ps1 and Sync-Readme.ps1 (dot-source this file).

# --- mod version and test status ---------------------------------------------------------------------
function Get-SourceVersion($dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\Example\\' }) {
        $m = Select-String -Path $f.FullName -Pattern 'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([\d.]+)"' | Select-Object -First 1
        if ($m) { return $m.Matches[0].Groups[1].Value }
    }
}
# The mod README carries one line in its first 40 lines:  `Tested: 1.1.0 (2026-10-02)`  or  `Tested: none`  (date optional).
# It names the newest version somebody actually played. Returns $null when the line is missing, else Version ($null for none) + Date.
function Get-TestedStatus($modDir) {
    $readme = Join-Path $modDir 'README.md'
    if (-not (Test-Path $readme)) { return $null }
    foreach ($l in (Get-Content $readme -TotalCount 40)) {
        if ($l -match '^Tested:\s*none\b') { return [pscustomobject]@{ Version = $null; Date = $null } }
        if ($l -match '^Tested:\s*(\d+(?:\.\d+)+)(?:\s*\(([^)]*)\))?') { return [pscustomobject]@{ Version = $Matches[1]; Date = $Matches[2] } }
    }
    $null
}
# Pads to 4 parts so "1.0" == "1.0.0".
function Ver($s) {
    $p = @($s -split '\.' | ForEach-Object { [int]$_ }); while ($p.Count -lt 4) { $p += 0 }
    [version]($p[0..3] -join '.')
}
function Cmp($a, $b) { (Ver $a).CompareTo((Ver $b)) }
# 'tested' = the given version itself was played; otherwise says what was last played.
function Get-TestLabel($ver, $tested) {
    if (-not $tested) { return 'no Tested line' }
    if (-not $tested.Version) { return 'untested' }
    $c = if ($ver) { Cmp $tested.Version $ver } else { 0 }
    if ($c -eq 0) { return 'tested' }
    if ($c -gt 0) { return "Tested line ahead of the version ($($tested.Version))" }
    "untested, last tested $($tested.Version)"
}

# --- Nexus page text ---------------------------------------------------------------------------------
function Norm-Doc($s) {
    # Nexus stores the BBCode with <br /> per line, numeric/named HTML entities, and an explicit [/*] closing each list item.
    $s = $s -replace '<br\s*/?>', '' -replace '\[/\*\]', ''
    $s = [regex]::Replace($s, '&#(\d+);', { param($m) [string][char][int]$m.Groups[1].Value })
    $s = $s -replace '&quot;', '"' -replace '&lt;', '<' -replace '&gt;', '>' -replace '&amp;', '&'
    @($s -split "\r?\n" | ForEach-Object { ($_ -replace '\s+', ' ').Trim() } | Where-Object { $_ })
}
function Short($s) { if ($s.Length -gt 90) { $s.Substring(0, 87) + '...' } else { $s } }

# --- settings: code vs page --------------------------------------------------------------------------
# Code side: every MelonPreferences CreateEntry("Name", default, ...) in the given source texts -> name -> default expression.
function Get-CodeSettings([string[]]$texts) {
    $h = @{}
    foreach ($t in $texts) {
        foreach ($m in [regex]::Matches($t, 'CreateEntry\(\s*"(?<n>[^"]+)"\s*,\s*(?<d>"[^"]*"|-?[\w.]+)')) { $h[$m.Groups['n'].Value] = $m.Groups['d'].Value }
    }
    $h
}
# Page side: rows shaped "Name   = value   description" at the start of a line (the Settings [code] block) -> name -> first token.
function Get-DocSettings([string]$text) {
    $h = @{}
    # The value stops at whitespace or a '[': Nexus can glue a closing [/code] onto the text that follows.
    foreach ($m in [regex]::Matches($text, '(?m)^(?<n>[A-Za-z][A-Za-z0-9_]*)\s+=\s+(?<v>"[^"]*"|[^\s\[]+)')) { $h[$m.Groups['n'].Value] = $m.Groups['v'].Value }
    $h
}
function Norm-SettingValue($v) {
    $v = $v.Trim('"')
    if ($v -match '^(-?\d*\.?\d+)[fFdDmM]?$') { [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture).ToString([Globalization.CultureInfo]::InvariantCulture) } else { $v.ToLower() }
}
# Only literal defaults (string, number, true/false) can be compared; computed ones are counted as Unchecked.
function Compare-Settings($code, $doc) {
    $mismatch = @(); $checked = 0; $unchecked = 0
    foreach ($k in ($code.Keys | Where-Object { $doc.ContainsKey($_) } | Sort-Object)) {
        if ($code[$k] -match '^("[^"]*"|-?\d*\.?\d+[fFdDmM]?|true|false)$') {
            $checked++
            if ((Norm-SettingValue $code[$k]) -ne (Norm-SettingValue $doc[$k])) { $mismatch += "$k (page $($doc[$k]), code $($code[$k].Trim('"')))" }
        } else { $unchecked++ }
    }
    [pscustomobject]@{
        Missing   = @($code.Keys | Where-Object { -not $doc.ContainsKey($_) } | Sort-Object)   # in code, not on the page
        Extra     = @($doc.Keys  | Where-Object { -not $code.ContainsKey($_) } | Sort-Object)  # on the page, not in code
        Mismatch  = $mismatch
        Checked   = $checked
        Unchecked = $unchecked
    }
}

# --- source texts -------------------------------------------------------------------------------------
$script:SrcExclude = '(^|/|\\)(obj|bin|Example|Examples|Tests)(/|\\)'
function Get-CurrentSourceTexts($srcDir) {
    @(Get-ChildItem $srcDir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch $script:SrcExclude } | ForEach-Object { Get-Content $_.FullName -Raw })
}
# The source as it was when release/<Mod>/<Mod>.dll was last committed = the config that DLL shipped with.
# Returns $null when the DLL was never committed.
function Get-ShippedSourceTexts($root, $mod) {
    $base = git -C $root log -1 --format=%H -- "release/$mod/$mod.dll" 2>$null
    if (-not $base) { return $null }
    $files = @(git -C $root ls-tree -r --name-only $base -- $mod 2>$null | Where-Object { $_ -match '\.cs$' -and $_ -notmatch $script:SrcExclude })
    [pscustomobject]@{ Commit = $base.Substring(0, 7); Texts = @($files | ForEach-Object { (git -C $root show "${base}:$_" 2>$null) -join "`n" }) }
}
