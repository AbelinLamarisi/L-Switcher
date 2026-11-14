param([switch]$Test, [switch]$UISmoke, [switch]$WordSmoke, [switch]$ClipboardSmoke, [switch]$DpiSmoke)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools using .vsconfig first.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'MSBuild was not found. Install the components from .vsconfig.' }
Push-Location $PSScriptRoot
try {
    & $msbuild LSwitcher.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    if ($Test -or $UISmoke -or $WordSmoke -or $ClipboardSmoke -or $DpiSmoke) {
        & (Join-Path $PSScriptRoot 'tests\BuildVersion.Tests.ps1')
        & $msbuild tests\LSwitcher.Tests.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
        $testArguments = @()
        if ($UISmoke) { $testArguments += '--ui-smoke' }
        if ($WordSmoke) { $testArguments += '--word-smoke' }
        if ($ClipboardSmoke) { $testArguments += '--clipboard-smoke' }
        if ($DpiSmoke) { $testArguments += '--dpi-smoke' }
        & .\tests\bin\Release\LSwitcher.Tests.exe @testArguments
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
} finally { Pop-Location }
