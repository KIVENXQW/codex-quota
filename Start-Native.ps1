[CmdletBinding()]
param(
    [ValidateSet('Keep', 'Enable', 'Disable', 'Status')]
    [string]$Startup = 'Keep',
    [switch]$NoLaunch,
    [switch]$ReportState
)

$ErrorActionPreference = 'Stop'

function Set-CodexQuotaLoginStartup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [Parameter(Mandatory = $true)][string]$StartupDirectory,
        [Parameter(Mandatory = $true, ParameterSetName = 'Set')][bool]$Enabled,
        [Parameter(Mandatory = $true, ParameterSetName = 'Read')][switch]$StatusOnly,
        [string]$ApprovalSubKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
    )

    $installPath = [System.IO.Path]::GetFullPath($InstallDirectory)
    $startupPath = [System.IO.Path]::GetFullPath($StartupDirectory)
    $executablePath = Join-Path $installPath 'CodexQuota.exe'
    $shortcutPath = Join-Path $startupPath 'Codex Quota.lnk'
    if (-not $StatusOnly -and $Enabled -and -not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw 'CodexQuota.exe is missing. Extract the complete release before enabling login startup.'
    }
    if ((Test-Path -LiteralPath $shortcutPath) -and -not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
        throw "The startup shortcut path is not a file: $shortcutPath"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            if (-not [string]::Equals([System.IO.Path]::GetFileName($shortcut.TargetPath), 'CodexQuota.exe', [System.StringComparison]::OrdinalIgnoreCase) -or $shortcut.Arguments) {
                throw 'A different application owns Codex Quota.lnk. Its shortcut was left unchanged.'
            }
        }
        $valueName = [System.IO.Path]::GetFileName($shortcutPath)
        if ($StatusOnly) {
            $active = $null -ne $shortcut -and (Test-Path -LiteralPath $shortcut.TargetPath -PathType Leaf)
            if ($active) {
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($ApprovalSubKey)
                if ($null -ne $key) {
                    try {
                        $flag = $key.GetValue($valueName)
                        if ($flag -is [byte[]] -and $flag.Length -ge 4) {
                            $active = ($flag[0] -band 1) -eq 0
                        }
                    } finally { $key.Dispose() }
                }
            }
            return [pscustomobject]@{ StartupEnabled = [bool]$active; Shortcut = $shortcutPath; InstallDirectory = $installPath }
        }
        if ($Enabled) {
            if (-not (Test-Path -LiteralPath $startupPath -PathType Container)) {
                New-Item -ItemType Directory -Path $startupPath -ErrorAction Stop | Out-Null
            }
            if ($null -eq $shortcut) { $shortcut = $shell.CreateShortcut($shortcutPath) }
            $shortcut.TargetPath = $executablePath
            $shortcut.WorkingDirectory = $installPath
            $shortcut.Arguments = ''
            $shortcut.Description = 'Start Codex Quota when signing in to Windows'
            $shortcut.IconLocation = $executablePath + ',0'
            $shortcut.WindowStyle = 7
            $shortcut.Save()
            # Windows remembers Task Manager's disable by shortcut filename.
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($ApprovalSubKey, $true)
            if ($null -ne $key) {
                try { $key.DeleteValue($valueName, $false) } finally { $key.Dispose() }
            }
        }
        elseif (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
            Remove-Item -LiteralPath $shortcutPath -ErrorAction Stop
        }
    }
    finally {
        if ($null -ne $shortcut) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
    [pscustomobject]@{ StartupEnabled = $Enabled; Shortcut = $shortcutPath; InstallDirectory = $installPath }
}

# Importing the script exposes the configuration helper without starting the app.
if ($MyInvocation.InvocationName -eq '.') { return }

if ($Startup -ne 'Keep') {
    $context = @{ InstallDirectory = $PSScriptRoot; StartupDirectory = [Environment]::GetFolderPath('Startup') }
    if ($Startup -ne 'Status') { Set-CodexQuotaLoginStartup @context -Enabled ($Startup -eq 'Enable') | Out-Null }
    $verified = Set-CodexQuotaLoginStartup @context -StatusOnly
    if ($Startup -ne 'Status' -and $verified.StartupEnabled -ne ($Startup -eq 'Enable')) { throw 'Windows did not apply the login startup choice.' }
    if ($ReportState -or $Startup -eq 'Status') {
        if ($verified.StartupEnabled) { 'FULL_STARTUP=1' } else { 'FULL_STARTUP=0' }
    } else { $verified }
    if ($Startup -eq 'Status') { return }
}
if ($NoLaunch) { return }

$executable = Join-Path $PSScriptRoot 'CodexQuota.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    $buildScript = Join-Path $PSScriptRoot 'Build-Native.ps1'
    if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
        throw 'CodexQuota.exe is missing. Extract the complete release before starting the app.'
    }
    & $buildScript
}
Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Write-Output 'Codex quota badge and system tray started.'
