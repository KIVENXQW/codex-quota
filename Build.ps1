param([switch]$TestOnly, [string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 C# compiler not found.' }
$build = Join-Path $PSScriptRoot '.build'
[void][IO.Directory]::CreateDirectory($build)
$state = Join-Path $PSScriptRoot 'native\WeeklyState.cs'
$tests = Join-Path $PSScriptRoot 'native\WeeklyStateTests.cs'
$testExe = Join-Path $build 'WeeklyStateTests.exe'
& $compiler /nologo /langversion:5 /utf8output /target:exe ('/out:' + $testExe) /r:System.Web.Extensions.dll $state $tests
if ($LASTEXITCODE -ne 0) { throw 'Native tests did not compile.' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Native state tests failed.' }
$nativeDir = Join-Path $PSScriptRoot 'native'
$startup = Join-Path $nativeDir 'WeeklyLoginStartup.cs'
$startupTests = Join-Path $build 'WeeklyLoginStartupTests.exe'
& $compiler /nologo /langversion:5 /utf8output /target:exe /platform:x64 ('/out:' + $startupTests) $startup (Join-Path $nativeDir 'WeeklyLoginStartupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Login startup tests did not compile.' }
& $startupTests
if ($LASTEXITCODE -ne 0) { throw 'Login startup tests failed.' }
$wpf = Join-Path (Split-Path -Parent $compiler) 'WPF'
$nativeRefs = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll')
foreach ($library in @('UIAutomationClient.dll', 'UIAutomationTypes.dll', 'WindowsBase.dll')) {
    $nativeRefs += '/r:' + (Join-Path $wpf $library)
}
$sidebarSources = @('WeeklyRing.cs', 'WindowTracker.cs', 'BadgeAttachment.cs', 'WeeklySidebar.cs') | ForEach-Object { Join-Path $nativeDir $_ }
$sidebarSources += $startup
$ringTests = Join-Path $build 'WeeklyRingTests.exe'
& $compiler /nologo /langversion:5 /utf8output /target:exe ('/out:' + $ringTests) /r:System.Drawing.dll (Join-Path $nativeDir 'WeeklyRing.cs') (Join-Path $nativeDir 'WeeklyRingTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Ring tests did not compile.' }
& $ringTests
if ($LASTEXITCODE -ne 0) { throw 'Ring drawing tests failed.' }
$sidebarTests = Join-Path $build 'WeeklySidebarTests.exe'
& $compiler /nologo /langversion:5 /utf8output /target:exe /main:Kiven.WeeklyQuota.WeeklySidebarTests ('/win32manifest:' + (Join-Path $nativeDir 'app.manifest')) ('/out:' + $sidebarTests) @nativeRefs @sidebarSources $state (Join-Path $nativeDir 'WeeklySidebarTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sidebar tests did not compile.' }
& $sidebarTests
if ($LASTEXITCODE -ne 0) { throw 'Sidebar attachment tests failed.' }
$popupTests = Join-Path $build 'PopupPlacementTests.exe'
& $compiler /nologo /langversion:5 /utf8output /target:exe /main:Kiven.WeeklyQuota.PopupPlacementTests ('/out:' + $popupTests) @nativeRefs @sidebarSources $state (Join-Path $nativeDir 'Program.cs') (Join-Path $nativeDir 'PopupPlacementTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Popup tests did not compile.' }
& $popupTests
if ($LASTEXITCODE -ne 0) { throw 'Popup placement or background check failed.' }
$node = Get-Command node.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $node) { $node = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' }
if (-not (Test-Path -LiteralPath $node)) { throw 'Node.js is required for the weekly reader.' }
Push-Location $PSScriptRoot
try {
    & $node --test 'test/weekly-model.test.mjs' 'test/weekly-update.test.mjs'
    if ($LASTEXITCODE -ne 0) { throw 'Weekly quota data tests failed.' }
} finally { Pop-Location }
if ($TestOnly) { return }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$output = Join-Path $OutputDirectory 'CodexWeekly.exe'
$version = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'version.json') | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$assemblyInfo = Join-Path $build 'WeeklyAssemblyInfo.cs'
[IO.File]::WriteAllText($assemblyInfo, ('[assembly:System.Reflection.AssemblyFileVersion("{0}.0")] [assembly:System.Reflection.AssemblyInformationalVersion("{0}")]' -f $version))
& $compiler /nologo /langversion:5 /utf8output /target:winexe /platform:x64 ('/out:' + $output) ('/win32manifest:' + (Join-Path $PSScriptRoot 'native\app.manifest')) ('/win32icon:' + (Join-Path $PSScriptRoot 'assets\launcher.ico')) @nativeRefs @sidebarSources $assemblyInfo $state (Join-Path $PSScriptRoot 'native\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'The weekly panel did not compile.' }
Write-Output $output
