param([switch]$OpenBrowser)
$ErrorActionPreference = 'Stop'
$panelUrl = 'http://127.0.0.1:43761'
$running = $false
try {
  $health = Invoke-RestMethod -Uri "$panelUrl/health" -TimeoutSec 2
  if ($health.app -ne 'codex-usage-panel') { throw 'Port 43761 is used by a different service.' }
  $running = $true
} catch {
  if ($_.Exception.Message -like '*different service*') { throw }
}
if (-not $running) {
  $nodeCandidates = @(
    (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'),
    (Join-Path $env:USERPROFILE '.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\node\bin\node.exe')
  )
  $nodePath = $nodeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  if (-not $nodePath) { $nodePath = (Get-Command node -ErrorAction Stop).Source }
  $serverPath = Join-Path $PSScriptRoot 'server.mjs'
  $stateDir = Join-Path $PSScriptRoot '.run'
  New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
  Start-Process -FilePath $nodePath -ArgumentList @('"' + $serverPath + '"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $stateDir 'server.log') -RedirectStandardError (Join-Path $stateDir 'server-error.log') | Out-Null
  for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Milliseconds 200
    try {
      $health = Invoke-RestMethod -Uri "$panelUrl/health" -TimeoutSec 1
      if ($health.app -eq 'codex-usage-panel') { $running = $true; break }
    } catch {}
  }
  if (-not $running) { throw 'Panel did not start. Check .run/server-error.log.' }
}
if ($OpenBrowser) { Start-Process $panelUrl }
Write-Output $panelUrl
