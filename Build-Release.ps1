[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$versionPath = Join-Path $projectRoot 'version.json'
if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
    throw 'Required file is missing: version.json'
}
$versionDocument = Get-Content -LiteralPath $versionPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $versionDocument.PSObject.Properties['version']) {
    throw 'version.json must contain a version property.'
}
$buildVersion = [string]$versionDocument.version
if ($buildVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'version.json must contain a stable version such as 2.1.0.'
}
if ($PSBoundParameters.ContainsKey('Version') -and $Version -cne $buildVersion) {
    throw "Requested version '$Version' differs from version.json ('$buildVersion'). Rebuild with the intended version before packaging."
}
$Version = $buildVersion

# Only these runtime files enter a release. Local account data and development files never do.
$packageFiles = @(
    'CodexQuota.exe',
    'CodexQuota.Updater.exe',
    'Start-Panel.ps1',
    'Start-Native.ps1',
    'server.mjs',
    'codex-account.mjs',
    'usage-model.mjs',
    'activity-model.mjs',
    'activity-collector.mjs',
    'activity-tracker.mjs',
    'version.json',
    'package.json',
    'public/index.html',
    'public/app.js',
    'public/style.css',
    'update-source.json'
)
foreach ($relativePath in $packageFiles) {
    $sourcePath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required runtime file is missing: $relativePath"
    }
}

$outputDirectory = Join-Path (Join-Path $projectRoot 'dist') ('v' + $Version)
$archiveName = 'CodexQuota-win-x64.zip'
$archiveDestination = Join-Path $outputDirectory $archiveName
$checksumDestination = $archiveDestination + '.sha256'
foreach ($destination in @($archiveDestination, $checksumDestination)) {
    if ((Test-Path -LiteralPath $destination) -and -not $Force) {
        throw "Release output already exists: $destination. Pass -Force to replace these exact release files."
    }
    if ((Test-Path -LiteralPath $destination) -and -not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        throw "Release output is not a file: $destination"
    }
}

$stageName = '.dist-stage-' + [Guid]::NewGuid().ToString('N')
$stagePath = Join-Path $projectRoot $stageName
$payloadPath = Join-Path $stagePath 'payload'
$utf8 = New-Object System.Text.UTF8Encoding($false)

try {
    New-Item -ItemType Directory -Path $payloadPath -ErrorAction Stop | Out-Null
    $manifestFiles = @()
    foreach ($relativePath in $packageFiles) {
        $stagedFile = Join-Path $payloadPath $relativePath
        $parentDirectory = [System.IO.Path]::GetDirectoryName($stagedFile)
        if (-not (Test-Path -LiteralPath $parentDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $parentDirectory -ErrorAction Stop | Out-Null
        }
        Copy-Item -LiteralPath (Join-Path $projectRoot $relativePath) -Destination $stagedFile -ErrorAction Stop
        $manifestFiles += [ordered]@{
            path = $relativePath.Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{ version = $Version; files = $manifestFiles }
    $manifestPath = Join-Path $payloadPath 'update-manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine, $utf8)

    Add-Type -AssemblyName System.IO.Compression
    $stagedArchive = Join-Path $stagePath $archiveName
    $archiveStream = [System.IO.File]::Open($stagedArchive, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $archive = $null
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($archiveStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($relativePath in @($packageFiles) + @('update-manifest.json')) {
            $entry = $archive.CreateEntry($relativePath.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [System.IO.File]::OpenRead((Join-Path $payloadPath $relativePath))
            $entryStream = $null
            try {
                $entryStream = $entry.Open()
                $inputStream.CopyTo($entryStream)
            }
            finally {
                if ($null -ne $entryStream) { $entryStream.Dispose() }
                $inputStream.Dispose()
            }
        }
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        $archiveStream.Dispose()
    }

    $archiveHash = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $stagedChecksum = $stagedArchive + '.sha256'
    [System.IO.File]::WriteAllText($stagedChecksum, "$archiveHash  $archiveName" + [Environment]::NewLine, $utf8)
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $outputDirectory -ErrorAction Stop | Out-Null
    }
    Move-Item -LiteralPath $stagedArchive -Destination $archiveDestination -Force:$Force -ErrorAction Stop
    Move-Item -LiteralPath $stagedChecksum -Destination $checksumDestination -Force:$Force -ErrorAction Stop
    [pscustomobject]@{
        Version = $Version
        Archive = $archiveDestination
        Checksum = $checksumDestination
        SHA256 = $archiveHash
        FileCount = $packageFiles.Count + 1
    }
}
finally {
    if (Test-Path -LiteralPath $stagePath -PathType Container) {
        # Only remove this invocation's freshly created staging directory after checking its resolved path.
        $resolvedStage = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $stagePath).ProviderPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
        $expectedStage = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $stageName))
        $resolvedParent = [System.IO.Path]::GetDirectoryName($resolvedStage)
        if (-not [string]::Equals($resolvedStage, $expectedStage, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($resolvedParent, $projectRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            [System.IO.Path]::GetFileName($resolvedStage) -notmatch '^\.dist-stage-[0-9a-f]{32}$') {
            throw "Staging cleanup refused for an unexpected path: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction Stop
    }
}
