$ErrorActionPreference = 'Stop'
$repoDir   = Split-Path $PSScriptRoot -Parent
$tag       = 'v1.0.0'
$name      = 'ReplayMate 1.0.0 (win-x64, self-contained)'
$assetName = 'ReplayMate-1.0.0-win-x64.zip'

# --- token from Git Credential Manager ---
$cred  = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = (($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=','')
if (-not $token) { Write-Output "NO_TOKEN"; exit 2 }

$headers = @{
  Authorization = "Bearer $token"
  Accept        = 'application/vnd.github+json'
  'User-Agent'  = 'ReplayMate-release-script'
}

try {
  $me = Invoke-RestMethod -Uri 'https://api.github.com/user' -Headers $headers -TimeoutSec 30
  Write-Output ("AUTH_OK login=" + $me.login)
} catch {
  Write-Output ("AUTH_FAIL " + $_.Exception.Message)
  exit 3
}

try {
  $r = Invoke-RestMethod -Uri 'https://api.github.com/repos/unioncyclist/ReplayMate' -Headers $headers -TimeoutSec 30
  Write-Output ("REPO_OK private=" + $r.private + " default=" + $r.default_branch)
} catch {
  Write-Output ("REPO_FAIL " + $_.Exception.Message)
  exit 4
}

# --- stage files, excluding personal config and logs ---
$stage = Join-Path $env:TEMP ("rm-rel-" + [guid]::NewGuid().ToString('N').Substring(0,8))
$root  = Join-Path $stage 'ReplayMate-1.0.0'
New-Item -ItemType Directory -Path $root -Force | Out-Null

robocopy (Join-Path $repoDir 'publish') $root /E /XF config.json /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Write-Output ("ROBOCOPY_FAIL " + $LASTEXITCODE); exit 5 }

$zip = Join-Path $env:TEMP $assetName
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $root -DestinationPath $zip -CompressionLevel Optimal
Write-Output ("ZIP_OK " + [math]::Round((Get-Item $zip).Length / 1MB, 1) + " MB")

# --- verify the packaged build actually runs ---
$verifyDir = Join-Path $env:TEMP ("rm-verify-" + [guid]::NewGuid().ToString('N').Substring(0,8))
Expand-Archive -Path $zip -DestinationPath $verifyDir -Force
$exe = Join-Path $verifyDir 'ReplayMate-1.0.0\ReplayMate.exe'
if (-not (Test-Path $exe)) { Write-Output "VERIFY_FAIL exe_missing"; exit 6 }

$p = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru -Wait
$resFile = Join-Path (Split-Path $exe) 'selftest-result.txt'
$first = if (Test-Path $resFile) { (Get-Content $resFile -Encoding UTF8 -TotalCount 1) } else { 'no_result_file' }
Write-Output ("VERIFY exit=" + $p.ExitCode + " | " + $first)
if ($p.ExitCode -ne 0) { Write-Output "VERIFY_FAIL"; exit 7 }

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Output ("SHA256 " + $hash)
Write-Output ("ZIP_PATH " + $zip)

# --- release body from UTF-8 file ---
$bodyPath = Join-Path $repoDir 'release\body.md'
$bodyText = [System.IO.File]::ReadAllText($bodyPath, [System.Text.Encoding]::UTF8).Replace('{HASH}', $hash)

$payload = @{
  tag_name         = $tag
  target_commitish = 'main'
  name             = $name
  body             = $bodyText
  draft            = $false
  prerelease       = $false
} | ConvertTo-Json -Depth 4

try {
  $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/unioncyclist/ReplayMate/releases' `
      -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($payload)) `
      -ContentType 'application/json; charset=utf-8' -TimeoutSec 60
  Write-Output ("RELEASE_OK id=" + $rel.id + " url=" + $rel.html_url)
} catch {
  Write-Output ("RELEASE_FAIL " + $_.Exception.Message)
  exit 8
}

try {
  $uri = "https://uploads.github.com/repos/unioncyclist/ReplayMate/releases/" + $rel.id + "/assets?name=" + $assetName
  $up = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -InFile $zip -ContentType 'application/zip' -TimeoutSec 900
  Write-Output ("ASSET_OK size=" + [math]::Round($up.size / 1MB, 1) + "MB")
  Write-Output ("ASSET_URL " + $up.browser_download_url)
} catch {
  Write-Output ("ASSET_FAIL " + $_.Exception.Message)
  exit 9
}
