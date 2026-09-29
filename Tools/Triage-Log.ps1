# Jev (TypeSafe AI) log triage: flags MelonLoader log lines that are repetitive/routine noise
# vs. lines carrying distinct information, so a noisy mod's log can be trimmed with evidence
# instead of a guess. Needs TYPESAFE_API_KEY in the environment (new shell after `setx`, or
# pass -ApiKey).
#
# Usage:
#   pwsh -File Tools\Triage-Log.ps1 -LogPath "E:\SteamLibrary\steamapps\common\Gunman Contracts - Stand Alone\MelonLoader\Latest.log" -Tag Daredevil
param(
    [Parameter(Mandatory)] [string]$LogPath,
    [string]$Tag,                       # e.g. "Daredevil" -> matches "[Daredevil]"; omit for the whole file
    [string]$ApiKey = $env:TYPESAFE_API_KEY,
    [string]$Model = "jev-latest",
    [int]$ChunkSize = 40,
    [double]$NoiseThreshold = 0.6
)

if (-not $ApiKey) { $ApiKey = [Environment]::GetEnvironmentVariable("TYPESAFE_API_KEY", "User") }
if (-not $ApiKey) { throw "TYPESAFE_API_KEY not set. setx TYPESAFE_API_KEY <key> in a terminal, then open a new shell." }

$allLines = Get-Content -Path $LogPath
$rows = @()
for ($i = 0; $i -lt $allLines.Count; $i++) {
    if (-not $Tag -or $allLines[$i] -match [regex]::Escape("[$Tag]")) {
        $rows += [pscustomobject]@{ LineNo = $i + 1; Text = $allLines[$i] }
    }
}
if ($rows.Count -eq 0) { Write-Host "No matching lines."; exit }
Write-Host "Matched $($rows.Count) lines. Sending to Jev in chunks of $ChunkSize..."

$headers = @{ Authorization = "Bearer $ApiKey"; "Content-Type" = "application/json" }
$results = @()

for ($start = 0; $start -lt $rows.Count; $start += $ChunkSize) {
    $chunk = $rows[$start..([Math]::Min($start + $ChunkSize, $rows.Count) - 1)]
    $questions = [ordered]@{}
    for ($j = 0; $j -lt $chunk.Count; $j++) {
        $questions["l$j"] = @{
            type         = "noul"
            instructions = "Is `lines[$j]` a repetitive or routine log line (a per-occurrence status/diagnostic detail that repeats in shape every time this event happens) that could safely be compressed, rate-limited, or counted in a summary instead of printed every time? Answer no (low) if it instead carries distinct, one-off information a developer needs to read on its own."
        }
    }
    $body = @{
        state     = @{ lines = $chunk.Text }
        model     = $Model
        questions = $questions
    } | ConvertTo-Json -Depth 10

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $resp = Invoke-RestMethod -Uri "https://api.typesafe.ai/v1/systemone" -Method Post -Headers $headers -Body $body -TimeoutSec 60
        Write-Host "  chunk $($start/$ChunkSize + 1): $($chunk.Count) lines in $($sw.Elapsed.TotalSeconds.ToString('0.0'))s"
    } catch {
        Write-Host "FAILED on chunk starting at $start`: $($_.Exception.Message)"
        if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message }
        continue
    }

    for ($j = 0; $j -lt $chunk.Count; $j++) {
        $score = $resp.answers."l$j".noul
        $results += [pscustomobject]@{ LineNo = $chunk[$j].LineNo; Noise = $score; Text = $chunk[$j].Text }
    }
}

$noisy = $results | Where-Object { $_.Noise -ge $NoiseThreshold } | Sort-Object -Property Noise -Descending
$signal = $results | Where-Object { $_.Noise -lt $NoiseThreshold } | Sort-Object -Property LineNo

Write-Host "`n==== TRIM CANDIDATES (noise >= $NoiseThreshold): $($noisy.Count) of $($results.Count) ====" -ForegroundColor Yellow
$noisy | ForEach-Object { "{0,-5:0.00}  #{1,-5} {2}" -f $_.Noise, $_.LineNo, $_.Text }

Write-Host "`n==== KEEP AS-IS: $($signal.Count) of $($results.Count) ====" -ForegroundColor Green
$signal | ForEach-Object { "{0,-5:0.00}  #{1,-5} {2}" -f $_.Noise, $_.LineNo, $_.Text }
