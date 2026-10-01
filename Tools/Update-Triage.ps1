<#
.SYNOPSIS
  Local, reversible game-update diagnostics. See Tools/UPDATE_RECOVERY.md.
.EXAMPLE
  ./Tools/Update-Triage.ps1 Prepare
.EXAMPLE
  ./Tools/Update-Triage.ps1 Collect -Session feature/update-triage/prepare-...
#>
param(
    [ValidateSet('Snapshot', 'Prepare', 'Collect', 'Restore')] [string]$Action = 'Snapshot',
    [string]$GameDir,
    [string]$Session,
    [string]$Output,
    [string]$LogPath,
    [string]$Python,
    [switch]$WithFrameProbe,
    [switch]$VerboseThrows,
    [switch]$ArchiveNative,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
if (-not $Python) {
    foreach ($candidate in @('python', 'python3')) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) { $Python = $command.Source; break }
    }
}
if (-not $Python) {
    $bundled = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
    if (Test-Path -LiteralPath $bundled) { $Python = $bundled }
}
if (-not $Python) { throw 'Python 3 required. Supply -Python <python.exe>.' }
$arguments = @((Join-Path $PSScriptRoot 'update_triage.py'), $Action.ToLowerInvariant())
if ($GameDir) { $arguments += @('--game-dir', $GameDir) }
if ($Session) { $arguments += @('--session', $Session) }
if ($Output) { $arguments += @('--output', $Output) }
if ($LogPath) { $arguments += @('--log-path', $LogPath) }
if ($WithFrameProbe) { $arguments += '--with-frame-probe' }
if ($VerboseThrows) { $arguments += '--verbose-throws' }
if ($ArchiveNative) { $arguments += '--archive-native' }
if ($DryRun) { $arguments += '--dry-run' }
& $Python @arguments
if ($LASTEXITCODE -ne 0) { throw "Update triage failed (exit $LASTEXITCODE)." }
