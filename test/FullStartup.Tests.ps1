$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Start-Native.ps1')
$testId = [Guid]::NewGuid().ToString('N')
$work = Join-Path ([IO.Path]::GetTempPath()) ('codex-quota-login-' + $testId)
$folder = Join-Path $work 'Startup'
$approval = 'Software\KivenTests\' + $testId
$ctx = @{ InstallDirectory = $work; StartupDirectory = $folder; ApprovalSubKey = $approval }
function Check([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
try {
    [void][IO.Directory]::CreateDirectory($folder)
    $exe = Join-Path $work 'CodexQuota.exe'
    $weekly = Join-Path $work 'CodexWeekly.exe'
    [IO.File]::WriteAllBytes($exe,[byte[]](1))
    [IO.File]::WriteAllBytes($weekly,[byte[]](2))
    $shell = New-Object -ComObject WScript.Shell
    $neighbor = Join-Path $folder 'Codex Weekly.lnk'
    $lnk = $shell.CreateShortcut($neighbor); $lnk.TargetPath = $weekly; $lnk.Save()
    $before = (Get-FileHash -LiteralPath $neighbor).Hash
    $path = Join-Path $folder 'Codex Quota.lnk'
    Check (-not (Set-CodexQuotaLoginStartup @ctx -StatusOnly).StartupEnabled) 'Initially enabled'
    Check (-not (Test-Path -LiteralPath $path)) 'Checking should not create a shortcut'
    Check ((Set-CodexQuotaLoginStartup @ctx -Enabled $true).StartupEnabled) 'Enable failed'
    Check ($shell.CreateShortcut($path).TargetPath -eq $exe) 'Incorrect target'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($approval)
    $key.SetValue('Codex Quota.lnk', [byte[]](3,0,0,0,0,0,0,0,0,0,0,0),[Microsoft.Win32.RegistryValueKind]::Binary)
    $key.SetValue('Codex Weekly.lnk',[byte[]](3,0,0,0),[Microsoft.Win32.RegistryValueKind]::Binary)
    $key.Dispose()
    Check (-not (Set-CodexQuotaLoginStartup @ctx -StatusOnly).StartupEnabled) 'Task Manager disabled entry shown as enabled'
    Check ((Set-CodexQuotaLoginStartup @ctx -Enabled $true).StartupEnabled) 'Cannot re-enable after Task Manager disable'
    Check (-not (Set-CodexQuotaLoginStartup @ctx -Enabled $false).StartupEnabled) 'Disable failed'
    Check (-not (Test-Path -LiteralPath $path)) 'Own link remains'
    $other = $shell.CreateShortcut($path); $other.TargetPath = $weekly; $other.Save()
    foreach ($enabled in @($false,$true)) {
        $blocked = $false
        try { Set-CodexQuotaLoginStartup @ctx -Enabled $enabled | Out-Null } catch { $blocked = $true }
        Check $blocked 'Foreign link not protected'
    }
    Check ($shell.CreateShortcut($path).TargetPath -eq $weekly) 'Foreign target changed'
    Check ((Get-FileHash -LiteralPath $neighbor).Hash -eq $before) 'Neighbor link changed'
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($approval)
    try { Check ($key.GetValue('Codex Weekly.lnk')[0] -eq 3) 'Neighbor startup approval changed' } finally { $key.Dispose() }
    Write-Output 'Full edition startup checks passed (enable, disable, Windows approval, foreign link and weekly isolation).'
} finally {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($approval,$false)
    if ((Test-Path -LiteralPath $work) -and (Resolve-Path -LiteralPath $work).Path -eq (Join-Path ([IO.Path]::GetTempPath()) ('codex-quota-login-' + $testId))) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
