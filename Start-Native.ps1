[CmdletBinding()]
param(
    [ValidateSet('Keep', 'Enable', 'Disable')]
    [string]$Startup = 'Keep',
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

function Set-CodexQuotaLoginStartup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [Parameter(Mandatory = $true)][string]$StartupDirectory,
        [Parameter(Mandatory = $true)][bool]$Enabled
    )

    $installPath = [System.IO.Path]::GetFullPath($InstallDirectory)
    $startupPath = [System.IO.Path]::GetFullPath($StartupDirectory)
    $executablePath = Join-Path $installPath 'CodexQuota.exe'
    $shortcutPath = Join-Path $startupPath 'Codex Quota.lnk'
    if ($Enabled -and -not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
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
            if (-not [string]::Equals([System.IO.Path]::GetFileName($shortcut.TargetPath), 'CodexQuota.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw 'A different application owns Codex Quota.lnk. Its shortcut was left unchanged.'
            }
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
    Set-CodexQuotaLoginStartup -InstallDirectory $PSScriptRoot -StartupDirectory ([Environment]::GetFolderPath('Startup')) -Enabled ($Startup -eq 'Enable')
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
