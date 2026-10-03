<#
.SYNOPSIS
  Builds a mod in Release, packages release/<Mod>/<Mod>-<version>.zip, and checks the Nexus page text against the code.

.DESCRIPTION
  1. Reads the source version (MelonInfo) and builds the project: dotnet build -c Release -o feature/<Mod>-<version>
  2. Copies the DLL to release/<Mod>/<Mod>.dll and creates <Mod>-<version>.zip containing only Mods/<Mod>.dll
     (forward-slash entry name), then re-reads the zip and verifies the entry hash.
  3. Lints release/<Mod>/NEXUS_DESCRIPTION.txt (the page text, written with GPT) against the source:
       - no {{placeholders}} left
       - the "Check it works" line names the source version
       - Mods\<Mod>.dll names the right file
       - every MelonPreferences entry in the code appears in the Settings block, and its default matches
         (and nothing is listed that the code does not create)
     plus whether header / thumbnail art exists in the folder.
  4. Prints the commit subjects touching <Mod>/ since the release DLL was last committed, as raw material for the changelog.

  Never uploads and never touches git history. Refuses to overwrite an existing zip of the same version unless -Force.

.PARAMETER Mod      Mod folder name (e.g. GrabFix).
.PARAMETER NoBuild  Skip the build; package the DLL already in release/<Mod>/ (or -Dll).
.PARAMETER Force    Overwrite an existing <Mod>-<version>.zip.
.PARAMETER LintOnly Do not build or zip; only lint the page text, check art, and list commits since the last release.

.EXAMPLE
  pwsh -File Tools\Pack-Release.ps1 -Mod GrabFix
  pwsh -File Tools\Pack-Release.ps1 -Mod DeathDetails -NoBuild
#>
param([Parameter(Mandatory)][string]$Mod, [switch]$NoBuild, [switch]$Force, [switch]$LintOnly)

$root = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $root $Mod
$rdir = Join-Path $root "release\$Mod"
Add-Type -A System.IO.Compression.FileSystem
. "$PSScriptRoot\Nexus-Common.ps1"

if (-not (Test-Path "$src\$Mod.csproj")) { Write-Error "No project at $src\$Mod.csproj"; exit 1 }

function Get-SourceVersion($dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\Example\\' }) {
        $m = Select-String -Path $f.FullName -Pattern 'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([\d.]+)"' | Select-Object -First 1
        if ($m) { return $m.Matches[0].Groups[1].Value }
    }
}
function Sha($p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() }

$ver = Get-SourceVersion $src
if (-not $ver) { Write-Error "No MelonInfo version found in $Mod"; exit 1 }
New-Item -ItemType Directory -Force $rdir | Out-Null
$dllOut = Join-Path $rdir "$Mod.dll"
$zip    = Join-Path $rdir "$Mod-$ver.zip"
$warn   = @()

# --- build ---
if ($LintOnly) { "Version    $ver (lint only: no build, no zip)" }
elseif (-not $NoBuild) {
    $out = Join-Path $root "feature\$Mod-$ver"
    $log = dotnet build "$src\$Mod.csproj" -c Release -o $out 2>&1
    $built = Join-Path $out "$Mod.dll"
    if (-not (Test-Path $built)) { ($log | Select-Object -Last 15) -join "`n"; Write-Error 'BUILD FAILED'; exit 1 }
    $bw = ($log | Select-String -Pattern '(\d+) Warning\(s\)').Matches.Groups[1].Value
    $be = ($log | Select-String -Pattern '(\d+) Error\(s\)').Matches.Groups[1].Value
    Copy-Item $built $dllOut -Force
    "Build      OK ($bw warnings, $be errors)  ->  feature\$Mod-$ver"
} elseif (-not (Test-Path $dllOut)) { Write-Error "-NoBuild but $dllOut does not exist"; exit 1 }

# --- zip ---
if (-not $LintOnly) {
if ((Test-Path $zip) -and -not $Force) { Write-Error "$([IO.Path]::GetFileName($zip)) already exists; use -Force to overwrite (do not overwrite a zip that is live on Nexus)"; exit 1 }
Remove-Item $zip -Force -ErrorAction SilentlyContinue
$za = [IO.Compression.ZipFile]::Open($zip, 'Create')
try { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $dllOut, "Mods/$Mod.dll", 'Optimal') } finally { $za.Dispose() }
$zr = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $e = $zr.Entries | Where-Object { $_.FullName -eq "Mods/$Mod.dll" }
    $ms = New-Object IO.MemoryStream; $s = $e.Open(); $s.CopyTo($ms); $s.Close()
    $zsha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($ms.ToArray())) -replace '-', '').ToLower()
    $entries = $zr.Entries.Count
} finally { $zr.Dispose() }
$dsha = Sha $dllOut
if ($zsha -ne $dsha) { Write-Error 'zip entry hash differs from the DLL'; exit 1 }
$dsize = (Get-Item $dllOut).Length; $zsize = (Get-Item $zip).Length
"DLL        $dsize bytes  sha256 $dsha"
"Zip        $([IO.Path]::GetFileName($zip))  $zsize bytes  ($entries entry: Mods/$Mod.dll, hash verified)"
"Version    $ver"
}

# --- lint the page text ---
$dp = Join-Path $rdir 'NEXUS_DESCRIPTION.txt'
if (-not (Test-Path $dp)) { $warn += 'no NEXUS_DESCRIPTION.txt (the page text has not been added yet)' }
else {
    $d = Get-Content $dp -Raw
    if ($d -match '\{\{') { $warn += 'description still has {{placeholders}}' }
    $m = [regex]::Match($d, 'v(\d+(?:\.\d+)+)\[/b\]\s+among the loaded mods')
    if (-not $m.Success) { $warn += '"Check it works" line (vX.Y.Z among the loaded mods) not found' }
    elseif ($m.Groups[1].Value -ne $ver) { $warn += "Check-it-works line says v$($m.Groups[1].Value), source is $ver" }
    if ($d -notmatch [regex]::Escape("Mods\$Mod.dll")) { $warn += "description never names Mods\$Mod.dll" }

    # settings: code vs description
    $code = Get-CodeSettings (Get-CurrentSourceTexts $src)
    $doc  = Get-DocSettings $d
    $cmp  = Compare-Settings $code $doc
    if ($cmp.Missing) { $warn += "settings in code but not in the description: $($cmp.Missing -join ', ')" }
    if ($cmp.Extra)   { $warn += "settings in the description but not in code: $($cmp.Extra -join ', ')" }
    foreach ($m in $cmp.Mismatch) { $warn += "default differs: $m" }
    "Settings   $($code.Count) in code, $($doc.Count) in the description, $($cmp.Checked) defaults compared"
}

# --- art ---
$files = Get-ChildItem $rdir -File | Select-Object -ExpandProperty Name
$hdr = $files | Where-Object { $_ -match 'header' -and $_ -match '\.(png|jpg)$' }
$thb = $files | Where-Object { $_ -match 'thumb|Tumb' -and $_ -match '\.(png|jpg)$' }
"Art        header: $(if ($hdr) { 'yes' } else { 'MISSING' }), thumbnail: $(if ($thb) { 'yes' } else { 'MISSING' })"

# --- changelog raw material ---
$rel = "release/$Mod/$Mod.dll"
$base = git -C $root log -1 --format=%H -- $rel 2>$null
$commits = if ($base) { @(git -C $root log "$base..HEAD" --format='%s' -- $Mod 2>$null) } else { @() }
$wd = @(git -C $root status --porcelain -- $Mod 2>$null)
if ($wd) { $warn += "$($wd.Count) uncommitted change(s) under $Mod/ (the DLL may include work that is not committed)" }
''
if ($warn) { 'Warnings:'; $warn | ForEach-Object { "  - $_" } } else { 'Warnings: none' }
''
if ($commits) { "Commits touching $Mod/ since the release DLL was last committed ($($commits.Count)):"; $commits | ForEach-Object { "  $_" } }
elseif ($base) { "No commits touching $Mod/ since the release DLL was last committed." }
else { 'Release DLL was never committed; no git baseline for a changelog.' }
