param([switch]$TestOnly,[string]$OutputName = 'CodexQuota.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nativeDir = Join-Path $PSScriptRoot 'native'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
& $compiler /nologo /target:exe ('/out:' + (Join-Path $nativeDir 'ModelTests.exe')) /r:System.Web.Extensions.dll (Join-Path $nativeDir 'Model.cs') (Join-Path $nativeDir 'ModelTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native data tests failed to compile.' }
& (Join-Path $nativeDir 'ModelTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native data tests failed.' }
& $compiler /nologo /target:exe ('/out:' + (Join-Path $nativeDir 'AnalyticsTests.exe')) /r:System.Web.Extensions.dll (Join-Path $nativeDir 'Model.cs') (Join-Path $nativeDir 'Analytics.cs') (Join-Path $nativeDir 'AnalyticsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Analytics tests failed to compile.' }
& (Join-Path $nativeDir 'AnalyticsTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Analytics tests failed.' }
& $compiler /nologo /target:exe ('/out:' + (Join-Path $nativeDir 'PlacementTests.exe')) /r:System.Drawing.dll ('/r:' + (Join-Path $wpf 'UIAutomationClient.dll')) ('/r:' + (Join-Path $wpf 'UIAutomationTypes.dll')) ('/r:' + (Join-Path $wpf 'WindowsBase.dll')) (Join-Path $nativeDir 'WindowTracker.cs') (Join-Path $nativeDir 'PlacementTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Placement tests failed to compile.' }
& (Join-Path $nativeDir 'PlacementTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Placement tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'ChartInteractionTests.exe')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $nativeDir 'Model.cs') (Join-Path $nativeDir 'Analytics.cs') (Join-Path $nativeDir 'Preferences.cs') (Join-Path $nativeDir 'Views.cs') (Join-Path $nativeDir 'Charts.cs') (Join-Path $nativeDir 'ColorPicker.cs') (Join-Path $nativeDir 'ChartInteractionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Chart interaction tests failed to compile.' }
& (Join-Path $nativeDir 'ChartInteractionTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Chart interaction tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UpdateCoreTests.exe')) /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateCoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update core tests failed to compile.' }
& (Join-Path $nativeDir 'UpdateCoreTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Update core tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UpdateClientTests.exe')) /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateClient.cs') (Join-Path $nativeDir 'UpdateClientTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update client tests failed to compile.' }
& (Join-Path $nativeDir 'UpdateClientTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Update client tests failed.' }
if ($TestOnly) { return }
& $compiler /nologo /target:winexe /platform:x64 /utf8output ('/out:' + (Join-Path $PSScriptRoot 'CodexQuota.Updater.exe')) /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateHelper.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update helper build failed.' }
& $compiler /nologo /target:winexe /platform:x64 /utf8output ('/out:' + (Join-Path $PSScriptRoot $OutputName)) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/r:' + (Join-Path $wpf 'UIAutomationClient.dll')) ('/r:' + (Join-Path $wpf 'UIAutomationTypes.dll')) ('/r:' + (Join-Path $wpf 'WindowsBase.dll')) (Join-Path $nativeDir 'Model.cs') (Join-Path $nativeDir 'Analytics.cs') (Join-Path $nativeDir 'Preferences.cs') (Join-Path $nativeDir 'WindowTracker.cs') (Join-Path $nativeDir 'Views.cs') (Join-Path $nativeDir 'Charts.cs') (Join-Path $nativeDir 'ColorPicker.cs') (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateClient.cs') (Join-Path $nativeDir 'UpdateDialog.cs') (Join-Path $nativeDir 'UpdateController.cs') (Join-Path $nativeDir 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native app build failed.' }
Write-Output 'Native app build PASS'
