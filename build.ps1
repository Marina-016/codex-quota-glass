param([switch]$Test)
$ErrorActionPreference = 'Stop'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkPath 'csc.exe'
$wpfPath = Join-Path $frameworkPath 'WPF'
$outputPath = Join-Path $PSScriptRoot 'outputs\CodexQuotaGlass'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$references = @('System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll',"$wpfPath\PresentationFramework.dll","$wpfPath\PresentationCore.dll","$wpfPath\WindowsBase.dll",'System.Xaml.dll') | ForEach-Object { '/reference:' + $_ }
$sourceFiles = @((Join-Path $PSScriptRoot 'src\QuotaClient.cs'), (Join-Path $PSScriptRoot 'src\App.cs'))
& $compiler /nologo /target:winexe /main:Program /platform:anycpu /optimize+ "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$outputPath\CodexQuotaGlass.exe" @references @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination (Join-Path $outputPath 'CodexQuotaGlass.exe.config') -Force
if ($Test) {
    $testExe = Join-Path $outputPath 'QuotaTests.exe'
    & $compiler /nologo /target:exe /main:QuotaTests "/out:$testExe" @references @sourceFiles (Join-Path $PSScriptRoot 'tests\QuotaTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
Write-Output "Built: $outputPath\CodexQuotaGlass.exe"
