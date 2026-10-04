<#
Turn Gunman Contracts mod debug logs on/off in MelonPreferences.cfg.
  Set-DebugLogs.ps1 Status                 # list every log switch and its value
  Set-DebugLogs.ps1 Off                    # all logs off (incl. probe mods)
  Set-DebugLogs.ps1 On                     # all mod logs on (probe mods only with -Probes)
  Set-DebugLogs.ps1 On  -Mod BillyClubs    # one section (partial, case-insensitive; comma list ok)
Close the game first: MelonLoader rewrites the file on exit and would undo the edit.
#>
param(
  [Parameter(Mandatory)][ValidateSet('On','Off','Status')][string]$State,
  [string[]]$Mod,
  [switch]$Probes,
  [string]$Cfg = 'E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\UserData\MelonPreferences.cfg'
)
$logKeys   = 'DebugLog','SwingLog','PerfLog','LogCutTiming','LogPerformanceStats','LogAppliedSettings','LogSounds','LogStateChanges','LogAlerts','FollowThroughVerboseLog'
# Sections whose "Enabled" only records data (no gameplay effect)
$probeSecs = 'EnemyAwarenessLog','GrabLog','FrameProbe','SoundProbe'

if ((Get-Process GunmanContracts -ErrorAction SilentlyContinue) -and $State -ne 'Status') {
  Write-Warning 'GunmanContracts.exe is running; it will overwrite this on exit. Close it first.'
}
$val = if ($State -eq 'On') { 'true' } else { 'false' }
$lines = [System.IO.File]::ReadAllLines($Cfg)
$sec = ''; $changed = 0; $rows = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match '^\[(.+)\]\s*$') { $sec = $Matches[1]; continue }
  if ($lines[$i] -notmatch '^(\w+)\s*=\s*(true|false)\s*$') { continue }
  $k = $Matches[1]; $cur = $Matches[2]
  $isProbeEnabled = ($k -eq 'Enabled' -and $probeSecs -contains $sec)
  if (-not ($logKeys -contains $k -or $isProbeEnabled)) { continue }
  if ($Mod -and -not ($Mod | Where-Object { $sec -like "*$_*" })) { continue }
  $isProbe = $probeSecs -contains $sec
  $rows += [pscustomobject]@{ Section = $sec; Key = $k; Value = $cur }
  if ($State -eq 'Status') { continue }
  if ($State -eq 'On' -and $isProbe -and -not $Probes -and -not $Mod) { continue }
  if ($cur -ne $val) { $lines[$i] = "$k = $val"; $changed++ }
}
if ($State -eq 'Status') { $rows | Format-Table -AutoSize | Out-String -Width 200; return }
if ($changed) { [System.IO.File]::WriteAllLines($Cfg, $lines) }
"$changed switch(es) set to $val in $(Split-Path $Cfg -Leaf)"
