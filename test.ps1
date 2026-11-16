param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
foreach ($name in @('CoreTests','CsvTests','ThemeTests','LedgerTests','TrendLogicTests')) {
    & (Join-Path $PSScriptRoot ('tests\' + $name + '.exe'))
    if ($LASTEXITCODE -ne 0) { throw ($name + ' failed.') }
}
Write-Output 'All test suites passed.'
