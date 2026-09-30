<#
.SYNOPSIS
  Compares every mod's source against its release/ folder and says what is stale.

.DESCRIPTION
  For each release/<Mod>/<Name>.dll it checks:
    - source version (MelonInfo) vs newest release zip version
    - release DLL vs the DLL inside that zip
    - whether source .cs/.csproj changed in git since the release DLL was committed (= changed without a re-release)
    - NEXUS_UPLOAD.md / NEXUS_DESCRIPTION.txt still naming an old version or old DLL hash
  Also lists source mods that have no release folder.

.PARAMETER Build     Also build each mod fresh and compare hashes. Only decisive when the release DLL is uncommitted; otherwise
                 toolchain/reference differences make committed DLLs differ even with unchanged source.
.PARAMETER Mod       Only check these mod names.
.PARAMETER Fix       Rebuild and refresh release DLL + zip (same version) for mods flagged REBUILD NEEDED. Never bumps versions.

.EXAMPLE
  pwsh -File Tools\Check-Releases.ps1
  pwsh -File Tools\Check-Releases.ps1 -Mod ThrowAssist,Gloves
#>
param([switch]$Build, [string[]]$Mod, [switch]$Fix)

$root = Split-Path $PSScriptRoot -Parent
$rel  = Join-Path $root 'release'
$tmp  = Join-Path ([IO.Path]::GetTempPath()) 'check-releases'
Add-Type -A System.IO.Compression.FileSystem

function Get-Sha($p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() }
function Get-SourceVersion($dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\Example\\' }) {
        $m = Select-String -Path $f.FullName -Pattern 'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([\d.]+)"' | Select-Object -First 1
        if ($m) { return $m.Matches[0].Groups[1].Value }
    }
}
function Compare-Ver($a, $b) { ([version]$a).CompareTo([version]$b) }

$rows = @(); $details = @()
$dlls = Get-ChildItem $rel -Recurse -Filter *.dll | Sort-Object Name
if ($Mod) { $dlls = $dlls | Where-Object { $Mod -contains $_.BaseName } }

foreach ($dll in $dlls) {
    $name = $dll.BaseName; $rdir = $dll.DirectoryName
    $src  = Join-Path $root $name
    $notes = @()
    if (-not (Test-Path "$src\$name.csproj")) { $rows += [pscustomobject]@{Mod=$name;Source='-';Zip='-';Verdict='NO SOURCE'}; continue }

    $srcVer = Get-SourceVersion $src
    # newest zip for this mod
    $zip = Get-ChildItem $rdir -Filter "$name-*.zip" | Where-Object { $_.Name -match "^$name-([\d.]+)\.zip$" } |
           Sort-Object { [version]([regex]::Match($_.Name, '([\d.]+)\.zip$').Groups[1].Value) } | Select-Object -Last 1
    $zipVer = if ($zip) { [regex]::Match($zip.Name, '([\d.]+)\.zip$').Groups[1].Value } else { '(none)' }
    $relSha = Get-Sha $dll.FullName
    $verdict = 'OK'

    if ($zip) {
        $z = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try {
            $e = $z.Entries | Where-Object { $_.Name -eq "$name.dll" } | Select-Object -First 1
            if ($e) {
                $ms = New-Object IO.MemoryStream; $s = $e.Open(); $s.CopyTo($ms); $s.Close()
                $zsha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($ms.ToArray())) -replace '-','').ToLower()
                if ($zsha -ne $relSha) { $notes += "release DLL differs from the DLL inside $($zip.Name)"; $verdict = 'ZIP/DLL MISMATCH' }
            } else { $notes += "$($zip.Name) has no $name.dll"; $verdict = 'ZIP/DLL MISMATCH' }
        } finally { $z.Dispose() }
    } else { $notes += 'no zip'; $verdict = 'NO ZIP' }

    if ($srcVer -and $zip -and (Compare-Ver $srcVer $zipVer) -gt 0) { $verdict = 'SOURCE NEWER'; $notes += "source $srcVer > released $zipVer" }
    elseif ($srcVer -and $zip -and (Compare-Ver $srcVer $zipVer) -lt 0) { $notes += "source $srcVer < zip $zipVer (?)" }

    # Did the source change since the release DLL was committed? (git; robust against toolchain hash noise)
    $rel_rel = $dll.FullName.Substring($root.Length + 1).Replace([string][char]92, '/')
    $dirty = git -C $root status --porcelain -- $rel_rel 2>$null
    $base  = git -C $root log -1 --format=%H -- $rel_rel 2>$null
    $codeChanged = $null
    if ($dirty -or -not $base) {
        $notes += 'release DLL not committed / modified in working tree (git baseline unknown)'
        if ($Build) { $codeChanged = 'hash' }
    } else {
        $files  = @(git -C $root diff --name-only $base -- $name 2>$null) + @(git -C $root ls-files --others --exclude-standard -- $name 2>$null)
        $files  = $files | Where-Object { $_ -match '\.(cs|csproj|props)$' -and $_ -notmatch '/(obj|bin)/' -and $_ -notmatch '/Example/' -and $_ -notmatch 'local\.props$' }
        if ($files) {
            $n = @($files).Count
            $notes += "source changed since release DLL was committed ($n file$(if($n -ne 1){'s'})): $((@($files) | Select-Object -First 4 | ForEach-Object { Split-Path $_ -Leaf }) -join ', ')"
            if ($verdict -eq 'OK') { $verdict = 'REBUILD NEEDED' }
        }
    }

    if ($Build -or $Fix) {
        $out = Join-Path $tmp $name
        Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
        $log = dotnet build "$src\$name.csproj" -c Release -o $out 2>&1
        $built = Join-Path $out "$name.dll"
        if (-not (Test-Path $built)) { $notes += 'BUILD FAILED'; $verdict = 'BUILD FAILED'; $details += ($log | Select-Object -Last 8) }
        else {
            $bsha = Get-Sha $built
            if ($bsha -eq $relSha) { $notes += 'fresh build == release DLL' }
            else {
                if ($codeChanged -eq 'hash' -and $verdict -eq 'OK') { $verdict = 'REBUILD NEEDED'; $notes += 'fresh build differs from release DLL' }
                if ($Fix -and $zip -and $verdict -in 'REBUILD NEEDED') {
                    Copy-Item $built $dll.FullName -Force
                    $stage = Join-Path $tmp "$name-zip\Mods"; Remove-Item (Split-Path $stage) -Recurse -Force -ErrorAction SilentlyContinue
                    New-Item -ItemType Directory $stage | Out-Null; Copy-Item $built $stage
                    Compress-Archive -Path $stage -DestinationPath $zip.FullName -Force
                    $notes += "FIXED: refreshed DLL + $($zip.Name) (docs' sha256 not updated)"; $verdict = 'REFRESHED'; $relSha = $bsha
                }
            }
        }
    }

    # docs
    $up = Join-Path $rdir 'NEXUS_UPLOAD.md'; $ds = Join-Path $rdir 'NEXUS_DESCRIPTION.txt'
    if ($srcVer -and (Split-Path $rdir -Leaf) -eq $name) {   # docs are per release folder; skip mods sharing another mod's folder
        if (Test-Path $up) {
            $t = Get-Content $up -Raw
            $m = [regex]::Match($t, '\|\s*Version\s*\|\s*`([\d.]+)`')
            if ($m.Success -and $m.Groups[1].Value -ne $srcVer) { $notes += "NEXUS_UPLOAD Version field = $($m.Groups[1].Value)"; if ($verdict -eq 'OK') { $verdict = 'DOCS STALE' } }
            $m = [regex]::Match($t, 'sha256 `([0-9a-f]{64})`')
            if ($m.Success -and $m.Groups[1].Value -ne $relSha) { $notes += 'NEXUS_UPLOAD sha256 is not the release DLL'; if ($verdict -eq 'OK') { $verdict = 'DOCS STALE' } }
        }
        if (Test-Path $ds) {
            $vs = [regex]::Matches((Get-Content $ds -Raw), "$([regex]::Escape($name))\s+v([\d.]+)") | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
            $bad = $vs | Where-Object { $_ -ne $srcVer }
            if ($bad) { $notes += "NEXUS_DESCRIPTION names v$($bad -join ', v')"; if ($verdict -eq 'OK') { $verdict = 'DOCS STALE' } }
        }
    }

    $rows += [pscustomobject]@{ Mod=$name; Source=$srcVer; Zip=$zipVer; Verdict=$verdict }
    if ($notes) { $details += "$name : " + ($notes -join '; ') }
}

# source mods that have no release folder
if (-not $Mod) {
    $released = $dlls.BaseName
    foreach ($c in Get-ChildItem $root -Directory | Where-Object { (Test-Path "$($_.FullName)\$($_.Name).csproj") -and ($released -notcontains $_.Name) }) {
        $rows += [pscustomobject]@{ Mod=$c.Name; Source=(Get-SourceVersion $c.FullName); Zip='-'; Verdict='NOT RELEASED' }
    }
}

$rows | Format-Table -AutoSize
if ($details) { "Notes:"; $details | ForEach-Object { "  $_" } }
