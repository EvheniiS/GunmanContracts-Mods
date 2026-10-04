<#
Compare the defaults the source ships (MelonPreferences.CreateEntry in each mod's .cs) with the values
in the game's live MelonPreferences.cfg (= the user's preferred settings). Read-only.
  Check-Defaults.ps1                    # every mod
  Check-Defaults.ps1 -Mod BetterBow     # one or more mods (top-level folder names, partial ok)
  Check-Defaults.ps1 -Quiet             # only the summary lines per mod

Reports, per mod:
  DIFF   Section.Key   source=<shipped default>  yours=<value in your cfg>   (debug keys excluded)
  DEBUG  Section.Key   source default is true: debug / log switches must ship as false
  PROBE  same, but for the record-only probe mods (EnemyAwarenessLog, GrabLog, FrameProbe...): informational
  (Saved* keys are runtime state the mods write themselves and are skipped.)
  NOCFG  keys in source that the cfg has no line for (mod never run since the key was added): not compared
Exit code: 2 = a debug switch ships on, 1 = DIFFs only, 0 = clean.
A DIFF is a question, not an error: either update the source default to your preference, or leave it
if the cfg value is only an experiment. The cfg keeps whatever it saved, so a DIFF also means a player
with no cfg gets different behaviour from you.
#>
param(
  [string[]]$Mod,
  [switch]$Quiet,
  [string]$Cfg  = 'E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\UserData\MelonPreferences.cfg',
  [string]$Root = (Split-Path $PSScriptRoot -Parent)
)
# Same switches as Set-DebugLogs.ps1 (keep in sync); probe mods' Enabled only records data.
$logKeys   = 'DebugLog','SwingLog','PerfLog','LogCutTiming','LogPerformanceStats','LogAppliedSettings','LogSounds','LogStateChanges','LogAlerts','FollowThroughVerboseLog'
$probeSecs = 'EnemyAwarenessLog','GrabLog','FrameProbe','SoundProbe','AttachmentProbe'
$skipDirs  = 'Tools','feature','release','il2cpp_tools','BlenderRefs','media','bin','obj','Tests','.git','.claude'

function IsDebug($sec, $key) {
  if ($logKeys -contains $key) { return $true }
  if ($key -eq 'Enabled' -and $probeSecs -contains $sec) { return $true }
  return ($key -match '(?i)^(debug|log[A-Z])')   # catches new log keys nobody added to the list
}

# --- live cfg: section -> key -> raw value ---
if (-not (Test-Path $Cfg)) { Write-Error "cfg not found: $Cfg"; exit 3 }
$live = @{}; $sec = ''
foreach ($l in [IO.File]::ReadAllLines($Cfg)) {
  if ($l -match '^\[(.+)\]\s*$') { $sec = $Matches[1]; if (-not $live[$sec]) { $live[$sec] = @{} }; continue }
  if ($l -match '^(\w+)\s*=\s*(.*?)\s*$' -and $sec) { $live[$sec][$Matches[1]] = $Matches[2] }
}

function Norm($raw, [bool]$isFloat) {
  $r = "$raw".Trim()
  if ($r -match '^"(.*)"$') { return $Matches[1] }
  if ($r -match '^(true|false)$') { return $r.ToLower() }
  $r = $r -replace 'f$',''
  $d = 0.0
  if ([double]::TryParse($r, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$d)) {
    return ([single]$d).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
  }
  return $r
}

# --- source: every CreateEntry with a literal key and default ---
$lit  = '(true|false|-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?f?|"(?:[^"\\]|\\.)*")'
$entryRx = [regex]"(\w+)\s*\.\s*CreateEntry(?:<[\w\s,\.]+>)?\(\s*""(\w+)""\s*,\s*$lit\s*[,)]"
$catRx   = [regex]'(?:(?:var\s+)?(\w+)\s*=\s*)(?:(\w+)\s*=\s*)?MelonPreferences\s*\.\s*CreateCategory\(\s*"(\w+)"'

$mods = Get-ChildItem $Root -Directory | Where-Object { $skipDirs -notcontains $_.Name -and (Get-ChildItem $_.FullName -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) }
if ($Mod) { $mods = $mods | Where-Object { $n = $_.Name; $Mod | Where-Object { $n -like "*$_*" } } }

$totDiff = 0; $totDebug = 0
foreach ($m in $mods) {
  $files = Get-ChildItem $m.FullName -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj|Tests)\\' }
  $modCats = @($files | ForEach-Object { $catRx.Matches([IO.File]::ReadAllText($_.FullName)) } | ForEach-Object { $_.Groups[3].Value } | Select-Object -Unique)
  $diff = @(); $dbg = @(); $probe = @(); $nocfg = @(); $n = 0; $unmapped = @()
  foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName)
    $cats = @{}
    foreach ($cm in $catRx.Matches($text)) { foreach ($i in 1,2) { if ($cm.Groups[$i].Success) { $cats[$cm.Groups[$i].Value] = $cm.Groups[3].Value } } }
    $onlyCat = if (($cats.Values | Select-Object -Unique).Count -eq 1) { @($cats.Values)[0] } else { $null }
    foreach ($em in $entryRx.Matches($text)) {
      $var = $em.Groups[1].Value; $key = $em.Groups[2].Value; $def = $em.Groups[3].Value
      $section = if ($cats.ContainsKey($var)) { $cats[$var] } else { $onlyCat }
      if (-not $section) {
        # category made in another file: take the one section (among this mod's categories) whose cfg block has the key
        $hits = @($modCats | Where-Object { $live[$_] -and $live[$_].ContainsKey($key) })
        if ($hits.Count -eq 1) { $section = $hits[0] }
        elseif ($modCats.Count -eq 1) { $section = $modCats[0] }
        else { $unmapped += "$($f.Name):$var.$key"; continue }
      }
      if ($key -like 'Saved*') { continue }   # runtime state the mod writes itself, not a preference
      $n++
      $isFloat = $def -match 'f$' -or $def -match '\.'
      if (IsDebug $section $key) {
        if ((Norm $def $false) -eq 'true') { if ($probeSecs -contains $section) { $probe += "$section.$key" } else { $dbg += "$section.$key" } }
        continue
      }
      if (-not ($live[$section] -and $live[$section].ContainsKey($key))) { $nocfg += "$section.$key"; continue }
      $a = Norm $def $isFloat; $b = Norm $live[$section][$key] $isFloat
      if ($a -ne $b) { $diff += [pscustomobject]@{ Name = "$section.$key"; Src = $def; Yours = $live[$section][$key] } }
    }
  }
  $totDiff += $diff.Count; $totDebug += $dbg.Count
  $tag = if ($dbg.Count) { 'DEBUG-ON' } elseif ($diff.Count) { 'DIFFS' } else { 'ok' }
  "{0,-22} {1,3} entries  {2}" -f $m.Name, $n, $(if ($tag -eq 'ok') { 'ok' } else { "$tag ($($diff.Count) diff, $($dbg.Count) debug on)" })
  if ($Quiet) { continue }
  foreach ($d in $probe) { "    PROBE  $d   probe/log mod ships on (fine for a dev tool, check it is not in a release)" }
  foreach ($d in $dbg)  { "    DEBUG  $d   source default is true" }
  foreach ($d in $diff) { "    DIFF   {0}   source={1}  yours={2}" -f $d.Name, $d.Src, $d.Yours }
  if ($nocfg.Count)     { "    NOCFG  $($nocfg.Count) key(s) not in your cfg yet: $($nocfg -join ', ')" }
  if ($unmapped.Count)  { "    ?      could not map to a category: $($unmapped -join ', ')" }
}
""
"TOTAL: $totDiff diff(s), $totDebug debug switch(es) shipping on"
if ($totDebug) { exit 2 } elseif ($totDiff) { exit 1 } else { exit 0 }
