param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
$outputDir = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot '发布' }
$testDir = Join-Path $projectRoot 'tests'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$iconPath = Join-Path $projectRoot 'src\app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $greenBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#21685C'))
    $lightBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#EDF5EF'))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#9BC9AC')),3
    $graphics.FillRectangle($greenBrush,5,3,54,58)
    $graphics.FillRectangle($lightBrush,15,13,35,38)
    $graphics.FillRectangle($greenBrush,18,20,23,4)
    $graphics.FillRectangle($greenBrush,18,30,16,3)
    $graphics.FillRectangle($greenBrush,18,39,20,3)
    $graphics.DrawLine($pen,11,12,11,52)
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    $iconFile = [System.IO.File]::Create($iconPath)
    $icon.Save($iconFile)
    $iconFile.Dispose(); $icon.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $greenBrush.Dispose(); $lightBrush.Dispose(); $pen.Dispose()
}
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$appArguments = @('/nologo','/target:winexe','/optimize+','/codepage:65001','/platform:anycpu',('/out:' + (Join-Path $outputDir '日常账本.exe')),('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')),('/win32icon:' + $iconPath),('/resource:' + (Join-Path $projectRoot 'src\MainWindow.xaml') + ',DailyLedger.MainWindow.xaml'),('/resource:' + $iconPath + ',DailyLedger.app.ico')) + $references + @((Join-Path $projectRoot 'src\Core.cs'),(Join-Path $projectRoot 'src\Theme.cs'),(Join-Path $projectRoot 'src\App.cs'),(Join-Path $projectRoot 'src\Trends.cs'),(Join-Path $projectRoot 'src\TrendsPage.cs'))
& $compilerPath $appArguments
if ($LASTEXITCODE -ne 0) { throw '应用编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot '使用说明.txt') -Destination (Join-Path $outputDir '使用说明.txt') -Force
Write-Output ('已生成：' + (Join-Path $outputDir '日常账本.exe'))
if (Test-Path -LiteralPath (Join-Path $testDir 'CoreTests.cs')) {
    $testArguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + (Join-Path $testDir 'CoreTests.exe'))) + $references + @((Join-Path $projectRoot 'src\Core.cs'),(Join-Path $testDir 'CoreTests.cs'))
    & $compilerPath $testArguments
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
}

foreach ($uiTestName in @('ThemeTests','LedgerTests')) {
    if (-not (Test-Path -LiteralPath (Join-Path $testDir ($uiTestName + '.cs')))) { continue }
    $themeTestArguments = @('/nologo','/target:exe',('/main:' + $uiTestName),'/optimize+','/codepage:65001',('/out:' + (Join-Path $testDir ($uiTestName + '.exe'))),('/resource:' + (Join-Path $projectRoot 'src\MainWindow.xaml') + ',DailyLedger.MainWindow.xaml'),('/resource:' + $iconPath + ',DailyLedger.app.ico')) + $references + @((Join-Path $projectRoot 'src\Core.cs'),(Join-Path $projectRoot 'src\Theme.cs'),(Join-Path $projectRoot 'src\App.cs'),(Join-Path $projectRoot 'src\Trends.cs'),(Join-Path $projectRoot 'src\TrendsPage.cs'),(Join-Path $testDir ($uiTestName + '.cs')))
    & $compilerPath $themeTestArguments
    if ($LASTEXITCODE -ne 0) { throw 'Theme tests failed to compile.' }
}

$trendLogicArguments = @('/nologo','/target:exe','/optimize+','/codepage:65001',('/out:' + (Join-Path $testDir 'TrendLogicTests.exe'))) + $references + @((Join-Path $projectRoot 'src\Core.cs'),(Join-Path $projectRoot 'src\Trends.cs'),(Join-Path $testDir 'TrendLogicTests.cs'))
& $compilerPath $trendLogicArguments
if ($LASTEXITCODE -ne 0) { throw 'Trend logic tests failed to compile.' }

