param([switch]$TestOnly,[string]$OutputName = 'CodexQuota.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$nativeDir = Join-Path $PSScriptRoot 'native'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$viewReferences = @('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
$viewSources = @('Model.cs','Analytics.cs','Preferences.cs','Views.cs','LiquidGlassControls.cs','MinimalPanel.cs','CornerWidget.cs','TrayTextIcon.cs','TrayRingIcon.cs','BadgeAttachment.cs','Charts.cs','HeatmapScale.cs','UsageDetailPopup.cs','ChartDetailPresenter.cs','ColorPicker.cs','UpdateCore.cs','UpdateClient.cs','IUpdateView.cs','GlassUpdateView.cs') | ForEach-Object { Join-Path $nativeDir $_ }
& $compiler /nologo /langversion:5 /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'PreferencesTests.exe')) /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $nativeDir 'Preferences.cs') (Join-Path $nativeDir 'PreferencesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Preferences tests failed to compile.' }
& (Join-Path $nativeDir 'PreferencesTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Preferences tests failed.' }
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
foreach ($test in @('WindowTrackerEventTests','WindowTrackerNativeTests','AvatarVisibilityTests')) {
    & $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir ($test + '.exe'))) /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/r:' + (Join-Path $wpf 'UIAutomationClient.dll')) ('/r:' + (Join-Path $wpf 'UIAutomationTypes.dll')) ('/r:' + (Join-Path $wpf 'WindowsBase.dll')) (Join-Path $nativeDir 'WindowTracker.cs') (Join-Path $nativeDir ($test + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw "$test failed to compile." }
    & (Join-Path $nativeDir ($test + '.exe'))
    if ($LASTEXITCODE -ne 0) { throw "$test failed." }
}
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'ChartInteractionTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'ChartInteractionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Chart interaction tests failed to compile.' }
& (Join-Path $nativeDir 'ChartInteractionTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Chart interaction tests failed.' }
& $compiler /nologo /langversion:5 /utf8output /target:exe ('/win32manifest:' + (Join-Path $nativeDir 'app.manifest')) ('/out:' + (Join-Path $nativeDir 'AttachedBadgeTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'AttachedBadgeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Attached badge tests failed to compile.' }
& (Join-Path $nativeDir 'AttachedBadgeTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Attached badge tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UpdateCoreTests.exe')) /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateCoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update core tests failed to compile.' }
& (Join-Path $nativeDir 'UpdateCoreTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Update core tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UpdateClientTests.exe')) /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateClient.cs') (Join-Path $nativeDir 'UpdateClientTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update client tests failed to compile.' }
& (Join-Path $nativeDir 'UpdateClientTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Update client tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UpdateControllerTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'UpdateDialog.cs') (Join-Path $nativeDir 'UpdateController.cs') (Join-Path $nativeDir 'UpdateControllerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update controller tests failed to compile.' }
& (Join-Path $nativeDir 'UpdateControllerTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Update controller tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'IntegratedUpdateTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'UpdateDialog.cs') (Join-Path $nativeDir 'UpdateController.cs') (Join-Path $nativeDir 'IntegratedUpdateTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integrated update tests failed to compile.' }
& (Join-Path $nativeDir 'IntegratedUpdateTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Integrated update tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'GlassPopupTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'GlassPopupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Glass popup tests failed to compile.' }
& (Join-Path $nativeDir 'GlassPopupTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Glass popup tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'TrayRingIconTests.exe')) /r:System.Drawing.dll (Join-Path $nativeDir 'TrayRingIcon.cs') (Join-Path $nativeDir 'TrayRingIconTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tray ring icon tests failed to compile.' }
& (Join-Path $nativeDir 'TrayRingIconTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tray ring icon tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'HeatmapScaleTests.exe')) /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $nativeDir 'Model.cs') (Join-Path $nativeDir 'Analytics.cs') (Join-Path $nativeDir 'HeatmapScale.cs') (Join-Path $nativeDir 'HeatmapScaleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Heatmap scale tests failed to compile.' }
& (Join-Path $nativeDir 'HeatmapScaleTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Heatmap scale tests failed.' }
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $nativeDir 'UsageDetailPopupTests.exe')) @viewReferences @viewSources (Join-Path $nativeDir 'UsageDetailPopupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Usage detail popup tests failed to compile.' }
& (Join-Path $nativeDir 'UsageDetailPopupTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Usage detail popup tests failed.' }
foreach ($test in @('LiquidGlassViewTests','CornerWidgetTests','BadgeVisualTests','PopupInteractionTests','MinimalThemeTests')) {
    & $compiler /nologo /langversion:5 /utf8output /target:exe ('/out:' + (Join-Path $nativeDir ($test + '.exe'))) @viewReferences @viewSources (Join-Path $nativeDir ($test + '.cs'))
    if ($LASTEXITCODE -ne 0) { throw "$test failed to compile." }
    & (Join-Path $nativeDir ($test + '.exe'))
    if ($LASTEXITCODE -ne 0) { throw "$test failed." }
}
if ($TestOnly) { return }
& $compiler /nologo /target:winexe /platform:x64 /utf8output ('/out:' + (Join-Path $PSScriptRoot 'CodexQuota.Updater.exe')) /r:System.Windows.Forms.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $nativeDir 'UpdateCore.cs') (Join-Path $nativeDir 'UpdateHelper.cs')
if ($LASTEXITCODE -ne 0) { throw 'Update helper build failed.' }
& $compiler /nologo /target:winexe /platform:x64 /utf8output ('/win32manifest:' + (Join-Path $nativeDir 'app.manifest')) ('/out:' + (Join-Path $PSScriptRoot $OutputName)) @viewReferences @viewSources ('/r:' + (Join-Path $wpf 'UIAutomationClient.dll')) ('/r:' + (Join-Path $wpf 'UIAutomationTypes.dll')) ('/r:' + (Join-Path $wpf 'WindowsBase.dll')) (Join-Path $nativeDir 'WindowTracker.cs') (Join-Path $nativeDir 'GlassUpdateToast.cs') (Join-Path $nativeDir 'UpdateDialog.cs') (Join-Path $nativeDir 'UpdateController.cs') (Join-Path $nativeDir 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native app build failed.' }
Write-Output 'Native app build PASS'
