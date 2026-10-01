<#
.SYNOPSIS
  Does the release DLL still match the source? Compares compiled code, not bytes.

.DESCRIPTION
  A fresh build never hashes the same as the committed DLL (PE timestamp, MVID, and the git commit hash
  that the SDK embeds in the informational version all change), so a byte or hash compare always says
  "different". This builds the mod's current source in a scratch copy and compares what matters:
    1. the IL of every method (SHA1 per method; a changed method or a different method count shows up)
    2. every printable string in the file (ASCII and UTF-16 at both byte alignments), which catches
       changed log lines / config descriptions / defaults that don't alter the IL hash
  Verdict: SAME CODE, or the list of differences. Version-label noise (the +<commit> suffix) is ignored.
  Also checks that the newest release zip holds a byte-identical copy of the release DLL.

.PARAMETER Mod      Mod folder name (e.g. ThrowAssist, Daredevil). Source = <root>\<Mod>, release = <root>\release\<Mod>.
.PARAMETER Dll      Release DLL file name if it differs from <Mod>.dll.
.PARAMETER Built    Skip the build and compare this already-built DLL instead.

.EXAMPLE
  pwsh -File Tools\Compare-ReleaseDll.ps1 -Mod ThrowAssist
  pwsh -File Tools\Compare-ReleaseDll.ps1 -Mod VRHolsterCustomization

.NOTES
  Builds a copy of the mod folder (minus bin/obj) so the real obj/ is never touched and its stale
  AssemblyInfo files don't cause duplicate-attribute errors. Needs the mod's GameDir.local.props (copied
  with it). A mod whose csproj references files outside its own folder may need -Built instead.
#>
param(
  [Parameter(Mandatory)][string]$Mod,
  [string]$Dll,
  [string]$Built
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path $PSScriptRoot -Parent
$dllName = if ($Dll) { $Dll } else { "$Mod.dll" }
# The release DLL may live in another mod's folder (e.g. EnemyAwarenessLog ships in release\EnemyAwarenessFix).
$relDll = Get-ChildItem (Join-Path $root 'release') -Recurse -Filter $dllName | Select-Object -First 1 -ExpandProperty FullName
if (-not $relDll) { throw "No $dllName anywhere under release\" }
$relDir = Split-Path $relDll -Parent

if (-not $Built) {
  $src = Join-Path $root $Mod
  $csproj = Get-ChildItem $src -Filter *.csproj | Select-Object -First 1
  if (-not $csproj) { throw "No csproj in $src" }
  # Copy the whole source tree (minus heavy / irrelevant folders) so ProjectReferences to sibling mods and
  # ../BlenderRefs/out embedded resources resolve, and the real obj/ is never touched.
  $tmp = Join-Path ([IO.Path]::GetTempPath()) "compare-release-$Mod"
  if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
  robocopy $root $tmp /E /NFL /NDL /NJH /NJS /NP /XD .git media release feature il2cpp_tools bin obj __pycache__ (Join-Path $root 'BlenderRefs\game') | Out-Null
  Write-Host "Building $Mod from source ..."
  $log = & dotnet build (Join-Path $tmp "$Mod\$($csproj.Name)") -c Release 2>&1
  if ($LASTEXITCODE -ne 0) { $log | Select-Object -Last 15; throw "Build failed" }
  $Built = Join-Path $tmp "$Mod\bin\Release\$dllName"
  if (-not (Test-Path $Built)) { $Built = Get-ChildItem (Join-Path $tmp $Mod) -Filter $dllName -Recurse | Where-Object { $_.FullName -like '*\bin\Release\*' } | Select-Object -First 1 -ExpandProperty FullName }
  if (-not $Built) { throw "Built DLL $dllName not found under $tmp\$Mod" }
}

function Get-IL($path) {
  $fs = [IO.File]::OpenRead($path)
  try {
    $pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    foreach ($h in $md.MethodDefinitions) {
      $m = $md.GetMethodDefinition($h)
      if ($m.RelativeVirtualAddress -eq 0) { continue }
      $name = $md.GetString($md.GetTypeDefinition($m.GetDeclaringType()).Name) + '::' + $md.GetString($m.Name)
      $il = [System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe, $m.RelativeVirtualAddress).GetILBytes()
      "$name " + [BitConverter]::ToString([Security.Cryptography.SHA1]::HashData([byte[]]$il))
    }
  } finally { $fs.Dispose() }
}

# Printable runs in ASCII and UTF-16 (both alignments). The informational version's "+<commit>" suffix is stripped.
function Get-Strings($path) {
  $b = [IO.File]::ReadAllBytes($path)
  $found = foreach ($o in 0, 1) {
    $n = $b.Length - $o; $n -= $n % 2
    [regex]::Matches([Text.Encoding]::Unicode.GetString($b, $o, $n), '[\x20-\x7e]{6,}') | ForEach-Object Value
  }
  $found += [regex]::Matches([Text.Encoding]::Latin1.GetString($b), '[\x20-\x7e]{6,}') | ForEach-Object Value
  $found | ForEach-Object { $_ -replace '\+[0-9a-f]{40}$', '' } | Sort-Object -Unique
}

$ilA = @(Get-IL $Built);  $ilB = @(Get-IL $relDll)
$stA = @(Get-Strings $Built); $stB = @(Get-Strings $relDll)
Write-Host ("Methods: source build {0}, release {1}" -f $ilA.Count, $ilB.Count)

$ilDiff = Compare-Object $ilA $ilB
# The informational-version string is read as a UTF-16/ASCII run with a leading char in some alignments; drop pure-version runs.
$stDiff = Compare-Object $stA $stB | Where-Object { $_.InputObject -notmatch '^\.?\d+\.\d+\.\d+(\.\d+)?$' }

if (-not $ilDiff -and -not $stDiff) { Write-Host "SAME CODE: the release DLL matches the current source." -ForegroundColor Green }
else {
  Write-Host "DIFFERENT: the release DLL is out of date with the source." -ForegroundColor Yellow
  if ($ilDiff) { Write-Host "Changed methods (<= source, => release):"; $ilDiff | Format-Table -AutoSize | Out-String | Write-Host }
  if ($stDiff) { Write-Host "Changed strings (<= source, => release):"; $stDiff | Select-Object -First 25 | Format-Table -AutoSize | Out-String | Write-Host }
}

# Newest zip must hold the same DLL as the release folder.
Add-Type -A System.IO.Compression.FileSystem
$zip = Get-ChildItem $relDir -Filter "$Mod-*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if ($zip) {
  $za = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
  try {
    $e = $za.Entries | Where-Object { $_.Name -eq $dllName } | Select-Object -First 1
    if (-not $e) { Write-Host "$($zip.Name): no $dllName inside" -ForegroundColor Yellow }
    else {
      $ms = New-Object IO.MemoryStream; $s = $e.Open(); $s.CopyTo($ms); $s.Dispose()
      $zh = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($ms.ToArray()))
      $rh = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($relDll)))
      if ($zh -eq $rh) { Write-Host "$($zip.Name): contains the same DLL as release\$Mod." -ForegroundColor Green }
      else { Write-Host "$($zip.Name): DLL inside differs from release\$Mod\$dllName." -ForegroundColor Yellow }
    }
  } finally { $za.Dispose() }
}
