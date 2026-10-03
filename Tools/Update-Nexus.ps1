<#
.SYNOPSIS
  Pushes a new version of an EXISTING mod file to Nexus through the v3 Upload API: zip, version, file description, changelog; then verifies.

.DESCRIPTION
  New mod PAGES are not created here (the API cannot), and page text / summary / images cannot be edited by any API.
  This only does what the Files tab does when you press "Upload new version":
    1. create an upload (size, filename, md5)          POST /v3/uploads
    2. PUT the zip to the presigned URL                 (headers Content-Disposition + Content-MD5)
    3. finalise and wait until state = available        POST /v3/uploads/{id}/finalise, GET /v3/uploads/{id}
    4. create the new file version                      POST /v3/mod-files/{fileId}/versions
         (name "<Mod> <ver>" in the live naming pattern, version, file description, category main,
          update_mod_version, archive previous main file as old, mod manager download)
    5. post the changelog                               POST /v3/mods/{modId}/changelogs   (append-only; skipped if v1 already has one for this version)
    6. verify: the version chain, the v1 file list and the changelog show what was intended

  DRY RUN BY DEFAULT: without -Apply it only reads (a few GETs) and prints the plan plus every check. A public upload cannot be
  quietly undone, so -Apply is for after the user has confirmed this exact mod and version.
  -UploadOnly is a rehearsal: steps 1-3 only. The file sits unattached in Nexus's upload store and no page changes.

  Needs the v3 ids in Tools\nexus-mods.json ("v3" block). Fill them with:  Update-Nexus.ps1 -Resolve [-Mod X]
  Key: NEXUS_API_KEY from the environment or <repo>\.env. Never printed.

.PARAMETER Resolve         Look up the v3 mod id and mod-file id for every mod (or -Mod) and store them in nexus-mods.json. Read-only on Nexus.
.PARAMETER Mod             Mod folder name (e.g. GrabFix). Required for an update.
.PARAMETER Version         Version to upload. Default: the newest release/<Mod>/<Mod>-x.y.z.zip.
.PARAMETER Changelog       Changelog text (plain text, newlines kept). Or use -ChangelogFile.
.PARAMETER ChangelogFile   File holding the changelog text.
.PARAMETER NoChangelog     Apply without posting a changelog.
.PARAMETER FileDescription Text for the file's description field. Default: the standard "Extract into the game folder..." line.
.PARAMETER KeepOld         Do not archive the previous main file (it stays a main file next to the new one).
.PARAMETER Apply           Really upload. Without it nothing is written.
.PARAMETER UploadOnly      With -Apply: stop after the upload is available (no page change).

.EXAMPLE
  pwsh -File Tools\Update-Nexus.ps1 -Resolve
  pwsh -File Tools\Update-Nexus.ps1 -Mod GrabFix -ChangelogFile .\gf-1.1.2.txt            # dry run
  pwsh -File Tools\Update-Nexus.ps1 -Mod GrabFix -ChangelogFile .\gf-1.1.2.txt -Apply     # after the user said yes
#>
param(
    [switch]$Resolve,
    [string]$Mod,
    [string]$Version,
    [string]$Changelog,
    [string]$ChangelogFile,
    [switch]$NoChangelog,
    [string]$FileDescription,
    [switch]$KeepOld,
    [switch]$Apply,
    [switch]$UploadOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cfgPath = Join-Path $PSScriptRoot 'nexus-mods.json'
Add-Type -A System.IO.Compression.FileSystem
Add-Type -A System.Net.Http

# --- key ---
$key = $env:NEXUS_API_KEY
if (-not $key -and (Test-Path "$root\.env")) {
    foreach ($l in Get-Content "$root\.env") {
        if ($l -match '^\s*NEXUS_API_KEY\s*=\s*(.+?)\s*$') { $key = $Matches[1].Trim('"', "'") }
    }
}
if (-not $key) { Write-Error 'NEXUS_API_KEY not found (environment or <repo>\.env)'; exit 1 }
$headers = @{ apikey = $key; 'Application-Name' = 'GunmanContractsTools'; 'Application-Version' = '1.0' }
$cfg  = Get-Content $cfgPath -Raw | ConvertFrom-Json
$v3   = 'https://api.nexusmods.com/v3'
$v1   = "https://api.nexusmods.com/v1/games/$($cfg.game)"
$script:calls = 0; $script:quota = $null

function Invoke-Api([string]$Method, [string]$Url, $Body) {
    $p = @{ Uri = $Url; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true; TimeoutSec = 60 }
    if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 5 -Compress); $p.ContentType = 'application/json; charset=utf-8' }
    $r = Invoke-WebRequest @p
    $script:calls++
    $h = $r.Headers['x-rl-hourly-remaining']; $d = $r.Headers['x-rl-daily-remaining']
    if ($h -and $d) { $script:quota = "$(@($h)[0]) left this hour, $(@($d)[0]) left today" }
    $text = [string]$r.Content
    $json = $null
    if ($text) { try { $json = $text | ConvertFrom-Json } catch { } }
    $status = [int]$r.StatusCode
    if ($status -eq 401) { Write-Error 'Nexus rejected the API key (401)'; exit 1 }
    if ($status -eq 429) { Write-Error 'Nexus rate limit hit (429); try again later'; exit 1 }
    [pscustomobject]@{
        Ok = ($status -lt 400); Status = $status
        Data = $json.data
        Detail = if ($status -ge 400) { if ($json.detail) { "$($json.title): $($json.detail)" } else { $text } }
    }
}
function Fail($msg) { Write-Host "STOP: $msg" -ForegroundColor Red; exit 1 }
function Ver($s) { $p = @($s -split '\.' | ForEach-Object { [int]$_ }); while ($p.Count -lt 4) { $p += 0 }; [version]($p[0..3] -join '.') }

# =================================================== -Resolve
if ($Resolve) {
    $names = @($cfg.mods.PSObject.Properties.Name)
    if ($Mod) { $want = @($Mod -split ',' | Where-Object { $_ }); $names = $names | Where-Object { $want -contains $_ } }
    $store = [ordered]@{}
    if ($cfg.PSObject.Properties['v3']) { foreach ($p in $cfg.v3.PSObject.Properties) { $store[$p.Name] = $p.Value } }
    $rows = foreach ($n in $names) {
        $page = Invoke-Api GET "$v3/games/$($cfg.game)/mods/$($cfg.mods.$n)"
        if (-not $page.Ok) { [pscustomobject]@{ Mod = $n; Mod3 = ''; File = ''; Name = ''; Note = "page lookup failed ($($page.Status))" }; continue }
        $files = Invoke-Api GET "$v3/mods/$($page.Data.id)/files"
        if (-not $files.Ok) { [pscustomobject]@{ Mod = $n; Mod3 = $page.Data.id; File = ''; Name = ''; Note = "files lookup failed: $($files.Detail)" }; continue }
        $active = @($files.Data.mod_files | Where-Object { $_.is_active })
        if ($active.Count -eq 1) {
            $store[$n] = [ordered]@{ mod = [string]$page.Data.id; file = [string]$active[0].id }
            [pscustomobject]@{ Mod = $n; Mod3 = $page.Data.id; File = $active[0].id; Name = $active[0].name; Note = "$($active[0].versions_count) versions" }
        }
        elseif (@($active | Where-Object { $_.name -like "$n*" }).Count -eq 1) {
            $pick = @($active | Where-Object { $_.name -like "$n*" })[0]
            $store[$n] = [ordered]@{ mod = [string]$page.Data.id; file = [string]$pick.id }
            [pscustomobject]@{ Mod = $n; Mod3 = $page.Data.id; File = $pick.id; Name = $pick.name; Note = "picked by name; page also has: " + (($active | Where-Object { $_.id -ne $pick.id }).name -join ', ') }
        }
        elseif ($active.Count -eq 0) { [pscustomobject]@{ Mod = $n; Mod3 = $page.Data.id; File = ''; Name = ''; Note = 'no active file' } }
        else { [pscustomobject]@{ Mod = $n; Mod3 = $page.Data.id; File = ($active.id -join ','); Name = ($active.name -join ' | '); Note = 'MORE THAN ONE active file: not stored, pick by hand' } }
    }
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    $out = [ordered]@{ game = $cfg.game; mods = [ordered]@{} }
    foreach ($p in $cfg.mods.PSObject.Properties) { $out.mods[$p.Name] = $p.Value }
    $out.v3 = $store
    ($out | ConvertTo-Json -Depth 5) + "`n" | Set-Content $cfgPath -Encoding utf8NoBOM
    Write-Host "Stored $($store.Count) v3 entries in Tools\nexus-mods.json."
    Write-Host "API: $script:calls requests; $script:quota."
    exit 0
}

# =================================================== update
if (-not $Mod) { Fail 'Give -Mod <name> (or -Resolve).' }
$ids = if ($cfg.PSObject.Properties['v3']) { $cfg.v3.$Mod } else { $null }
if (-not $ids) { Fail "No v3 ids for $Mod in nexus-mods.json. Run: pwsh -File Tools\Update-Nexus.ps1 -Resolve -Mod $Mod" }
$modId3 = [string]$ids.mod; $fileId = [string]$ids.file
$v1id = $cfg.mods.$Mod

$rdir = Join-Path $root "release\$Mod"
if (-not $Version) {
    $z = Get-ChildItem $rdir -Filter "$Mod-*.zip" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "^$Mod-([\d.]+)\.zip$" } |
         Sort-Object { Ver ([regex]::Match($_.Name, '([\d.]+)\.zip$').Groups[1].Value) } | Select-Object -Last 1
    if (-not $z) { Fail "No release zip for $Mod in $rdir. Run Pack-Release.ps1 first." }
    $Version = [regex]::Match($z.Name, '([\d.]+)\.zip$').Groups[1].Value
}
$zip = Join-Path $rdir "$Mod-$Version.zip"
$checks = [System.Collections.Generic.List[object]]::new()
function Check($ok, $what, $detail) { $checks.Add([pscustomobject]@{ Ok = [bool]$ok; What = $what; Detail = $detail }) }

# --- local checks ---
if (-not (Test-Path $zip)) { Fail "Zip not found: $zip" }
$arc = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($arc.Entries | Where-Object { $_.Length -gt 0 })
    $okLayout = ($entries.Count -eq 1 -and $entries[0].FullName -ceq "Mods/$Mod.dll")
    Check $okLayout 'zip layout' ("entries: " + (($arc.Entries | ForEach-Object FullName) -join ', '))
    if ($entries.Count -ge 1) {
        $ms = [IO.MemoryStream]::new(); $s = $entries[0].Open(); $s.CopyTo($ms); $s.Close()
        $inZip = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($ms.ToArray())).Replace('-', '').ToLower()
        $dll = Join-Path $rdir "$Mod.dll"
        if (Test-Path $dll) {
            $onDisk = (Get-FileHash $dll -Algorithm SHA256).Hash.ToLower()
            Check ($inZip -eq $onDisk) 'zip DLL = release\<Mod>.dll' "sha256 $($inZip.Substring(0,12))"
        }
    }
} finally { $arc.Dispose() }
$size = (Get-Item $zip).Length
$md5hex = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()
$md5b64 = [Convert]::ToBase64String([Convert]::FromHexString($md5hex))
$srcVer = $null
foreach ($f in Get-ChildItem (Join-Path $root $Mod) -Filter *.cs -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notmatch '\\Example\\' }) {
    $m = Select-String -Path $f.FullName -Pattern 'MelonInfo\(typeof\([^)]*\),\s*"[^"]*",\s*"([\d.]+)"' | Select-Object -First 1
    if ($m) { $srcVer = $m.Matches[0].Groups[1].Value; break }
}
if ($srcVer) { Check ((Ver $srcVer) -ge (Ver $Version)) 'zip not newer than source' "source $srcVer, zip $Version" }

# --- changelog text ---
$clText = $null
if ($ChangelogFile) { if (-not (Test-Path $ChangelogFile)) { Fail "Changelog file not found: $ChangelogFile" }; $clText = (Get-Content $ChangelogFile -Raw).Trim() }
elseif ($Changelog) { $clText = $Changelog.Trim() }
if ($clText) { Check ($clText.Length -le 65535) 'changelog length' "$($clText.Length) chars" }
elseif (-not $NoChangelog) { Check $false 'changelog' 'none given (-Changelog / -ChangelogFile), or say -NoChangelog' }

# --- live state ---
$chain = Invoke-Api GET "$v3/mod-files/$fileId/versions"
if (-not $chain.Ok) { Fail "Could not read the file's version chain: $($chain.Detail)" }
$versions = @($chain.Data.versions | Sort-Object { [decimal]$_.position } -Descending)
$latest = $versions | Select-Object -First 1
$liveVer = if ($latest) { [regex]::Match($latest.name, '(\d+(\.\d+)*)\s*$').Groups[1].Value } else { '' }
if ($liveVer) { Check (((Ver $Version) -gt (Ver $liveVer)) -or $UploadOnly) 'newer than live' "live '$($latest.name)' ($liveVer), uploading $Version$(if ($UploadOnly) { ' (rehearsal: not attached, so not enforced)' })" }
else { Check $true 'newer than live' "could not read a version from '$($latest.name)'" }
$newName = if ($latest -and $liveVer -and $latest.name -match [regex]::Escape($liveVer)) { $latest.name -replace [regex]::Escape($liveVer), $Version } else { "$Mod $Version" }
Check ($newName.Length -le 50 -and $newName -match "^[a-zA-Z0-9 _'().-]+$") 'file name valid' "'$newName'"
Check ($Version.Length -le 50 -and $Version -match '^[a-zA-Z0-9.-]+$') 'version valid' $Version

$hasCl = $false
$clRaw = Invoke-WebRequest -Uri "$v1/mods/$v1id/changelogs.json" -Headers $headers -SkipHttpErrorCheck
$script:calls++
if ($clRaw.StatusCode -eq 200) { try { $hasCl = $null -ne (([string]$clRaw.Content | ConvertFrom-Json).PSObject.Properties[$Version]) } catch { } }
if ($clText -and $hasCl) { Check $false 'changelog already on Nexus' "an entry for $Version exists; the API only appends, so it would be doubled" }

if (-not $FileDescription) { $FileDescription = "Extract into the game folder. Contains Mods/$Mod.dll. Requires MelonLoader 0.7.3 or newer." }

# --- report ---
Write-Host ''
Write-Host "== Update $Mod -> $Version  ($(if ($Apply) { if ($UploadOnly) { 'APPLY, upload only' } else { 'APPLY' } } else { 'DRY RUN' })) ==" -ForegroundColor Cyan
Write-Host ("Zip        {0}  ({1:N0} bytes, md5 {2})" -f (Split-Path $zip -Leaf), $size, $md5hex)
Write-Host "Mod file   $fileId  (page $v1id, v3 mod $modId3)"
Write-Host "New name   $newName    version $Version    category main"
Write-Host "Previous   $(if ($latest) { "'$($latest.name)'" } else { '-' })  ->  $(if ($KeepOld) { 'stays a main file' } else { 'archived (API category ARCHIVED; the site shows it under Previous files)' })"
Write-Host "File text  $FileDescription"
Write-Host "Changelog  $(if ($clText) { ($clText -split "`n").Count.ToString() + ' lines' } else { 'none' })"
Write-Host ''
foreach ($c in $checks) { Write-Host ("  [{0}] {1}: {2}" -f $(if ($c.Ok) { 'ok' } else { 'FAIL' }), $c.What, $c.Detail) -ForegroundColor $(if ($c.Ok) { 'Gray' } else { 'Red' }) }
$bad = @($checks | Where-Object { -not $_.Ok })
Write-Host ''
if ($bad.Count) { Write-Host "$($bad.Count) check(s) failed; nothing was uploaded." -ForegroundColor Red; Write-Host "API: $script:calls requests; $script:quota."; exit 1 }
if (-not $Apply) {
    Write-Host 'Dry run only. Nothing was uploaded. Re-run with -Apply after the user confirms this mod and version.' -ForegroundColor Yellow
    Write-Host "API: $script:calls requests; $script:quota."
    exit 0
}

# =================================================== apply
Write-Host '1/6 create upload' -ForegroundColor Cyan
$up = Invoke-Api POST "$v3/uploads" @{ size_bytes = $size; filename = (Split-Path $zip -Leaf); md5 = $md5hex }
if (-not $up.Ok) { Fail "create upload: $($up.Detail)" }
$uploadId = $up.Data.id; $presigned = $up.Data.presigned_url
Write-Host "    upload $uploadId"

Write-Host '2/6 PUT the zip' -ForegroundColor Cyan
$http = [System.Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromMinutes(10)   # no apikey header: the URL is presigned
$fs = [IO.File]::OpenRead($zip)
try {
    $content = [System.Net.Http.StreamContent]::new($fs)
    [void]$content.Headers.TryAddWithoutValidation('Content-Disposition', "attachment; filename=`"$(Split-Path $zip -Leaf)`"")
    [void]$content.Headers.TryAddWithoutValidation('Content-MD5', $md5b64)
    [void]$content.Headers.TryAddWithoutValidation('Content-Type', 'application/octet-stream')   # signed into the URL (SignedHeaders has content-type); the spec does not name the value, this one is accepted
    $resp = $http.PutAsync($presigned, $content).GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) { Fail "PUT failed: HTTP $([int]$resp.StatusCode) $($resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())" }
    Write-Host "    HTTP $([int]$resp.StatusCode)"
} finally { $fs.Dispose(); $http.Dispose() }

Write-Host '3/6 finalise and wait' -ForegroundColor Cyan
$fin = Invoke-Api POST "$v3/uploads/$uploadId/finalise"
if (-not $fin.Ok) { Fail "finalise: $($fin.Detail) (upload $uploadId)" }
$state = $fin.Data.state
for ($i = 0; $i -lt 60 -and $state -ne 'available'; $i++) {
    Start-Sleep -Seconds 2
    $g = Invoke-Api GET "$v3/uploads/$uploadId"
    if (-not $g.Ok) { Fail "poll: $($g.Detail) (upload $uploadId)" }
    $state = $g.Data.state
}
if ($state -ne 'available') { Fail "upload $uploadId not available after 2 minutes (state $state). Nothing is attached to the page." }
Write-Host "    state available"
if ($UploadOnly) {
    Write-Host "`nUpload-only rehearsal finished: $uploadId is available and not attached to any mod file. No page changed." -ForegroundColor Green
    Write-Host "API: $script:calls requests; $script:quota."
    exit 0
}

Write-Host '4/6 create file version' -ForegroundColor Cyan
$body = [ordered]@{
    upload_id = $uploadId; name = $newName; version = $Version; file_category = 'main'; description = $FileDescription
    update_mod_version = $true; archive_existing_file = (-not $KeepOld)
    primary_mod_manager_download = $true; allow_mod_manager_download = $true
}
if ($latest) { $body.previous_version_id = [string]$latest.id }
$cv = Invoke-Api POST "$v3/mod-files/$fileId/versions" $body
if (-not $cv.Ok) { Fail "create version: $($cv.Detail)  (upload $uploadId is available but NOT attached; re-run is safe)" }
Write-Host "    version $($cv.Data.version.id) at position $($cv.Data.version.position)"

Write-Host '5/6 changelog' -ForegroundColor Cyan
if ($clText) {
    $cr = Invoke-Api POST "$v3/mods/$modId3/changelogs" @{ version = $Version; changelog = $clText }
    if (-not $cr.Ok) { Write-Host "    changelog FAILED: $($cr.Detail). The file version is live; add the changelog by hand." -ForegroundColor Red }
    else { Write-Host '    posted' }
} else { Write-Host '    skipped' }

Write-Host '6/6 verify' -ForegroundColor Cyan
$after = Invoke-Api GET "$v3/mod-files/$fileId/versions"
$vs = @($after.Data.versions | Sort-Object { [decimal]$_.position } -Descending)
$top = $vs | Select-Object -First 1; $prev = $vs | Select-Object -Skip 1 -First 1
$res = @()
$res += [pscustomobject]@{ Ok = ($top.name -eq $newName -and $top.category -eq 'main'); What = "newest version is '$newName' (main)"; Got = "'$($top.name)' $($top.category)" }
$res += [pscustomobject]@{ Ok = [bool]$top.is_primary; What = 'it is the primary download'; Got = "$($top.is_primary)" }
if ($prev -and -not $KeepOld) { $res += [pscustomobject]@{ Ok = ($prev.category -in 'old_version', 'archived'); What = "previous file no longer main (the API's archive flag sets category 'archived', the site's manual flow 'old_version')"; Got = "'$($prev.name)' $($prev.category)" } }
$f1 = Invoke-WebRequest -Uri "$v1/mods/$v1id/files.json" -Headers $headers -SkipHttpErrorCheck; $script:calls++   # v1 has no "data" wrapper
if ($f1.StatusCode -eq 200) {
    $main1 = @(([string]$f1.Content | ConvertFrom-Json).files | Where-Object { $_.category_name -eq 'MAIN' } | Sort-Object uploaded_timestamp -Descending | Select-Object -First 1)
    $res += [pscustomobject]@{ Ok = ($main1.name -eq $newName); What = 'v1 file list shows it as MAIN'; Got = "'$($main1.name)'" }
}
if ($clText) {
    $c2 = Invoke-WebRequest -Uri "$v1/mods/$v1id/changelogs.json" -Headers $headers -SkipHttpErrorCheck; $script:calls++
    $has2 = $false; if ($c2.StatusCode -eq 200) { try { $has2 = $null -ne (([string]$c2.Content | ConvertFrom-Json).PSObject.Properties[$Version]) } catch { } }
    $res += [pscustomobject]@{ Ok = $has2; What = "v1 changelog has $Version"; Got = "$has2" }
}
foreach ($r in $res) { Write-Host ("  [{0}] {1}  ({2})" -f $(if ($r.Ok) { 'ok' } else { 'FAIL' }), $r.What, $r.Got) -ForegroundColor $(if ($r.Ok) { 'Green' } else { 'Yellow' }) }
if (@($res | Where-Object { -not $_.Ok }).Count) { Write-Host 'Nexus can lag a minute or two: re-run  Check-Nexus.ps1 -Mod ' $Mod ' -Docs  before calling it a failure.' -ForegroundColor Yellow }
Write-Host "`nDone. The page text is untouched (re-paste NEXUS_DESCRIPTION.txt by hand if Check-Nexus -Docs says it differs)."
Write-Host "API: $script:calls requests; $script:quota."
