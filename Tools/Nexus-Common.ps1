# Shared helpers for Check-Nexus.ps1 and Pack-Release.ps1 (dot-source this file).

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
