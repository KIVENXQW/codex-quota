$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'CodexQuota.exe'
if (-not (Test-Path -LiteralPath $executable)) { & (Join-Path $PSScriptRoot 'Build-Native.ps1') }
Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Write-Output 'Codex quota badge and system tray started.'
