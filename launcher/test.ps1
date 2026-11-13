$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskTestExe = Join-Path $PSScriptRoot 'tests\LauncherCoreTests.exe'
$taskWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $taskCompiler /nologo /target:exe /main:LauncherCoreTests /reference:System.Runtime.Serialization.dll /reference:System.Numerics.dll /reference:System.Xaml.dll ('/reference:' + (Join-Path $taskWpf 'PresentationFramework.dll')) ('/reference:' + (Join-Path $taskWpf 'PresentationCore.dll')) ('/reference:' + (Join-Path $taskWpf 'WindowsBase.dll')) ('/out:' + $taskTestExe) (Join-Path $PSScriptRoot 'src\LauncherCore.cs') (Join-Path $PSScriptRoot 'src\SaveCatalog.cs') (Join-Path $PSScriptRoot 'src\EditionSetup.cs') (Join-Path $PSScriptRoot 'src\LauncherApp.cs') (Join-Path $PSScriptRoot 'tests\LauncherCoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
Push-Location $PSScriptRoot
try {
    & $taskTestExe
    if ($LASTEXITCODE -ne 0) { throw 'Configuration tests failed.' }
} finally { Pop-Location }

$taskOutput = Join-Path $PSScriptRoot 'tests\ui-check'
if (Test-Path -LiteralPath (Join-Path $taskOutput 'ui-error.txt')) { throw 'Inspect the previous ui-check/ui-error.txt and choose a fresh test output directory before rerunning.' }
$taskProcess = Start-Process -FilePath (Join-Path $PSScriptRoot 'RA2CommandCenter.exe') -ArgumentList @('--ui-smoke', ('"' + $taskOutput + '"')) -WindowStyle Hidden -PassThru
$taskProcess.WaitForExit()
if (Test-Path -LiteralPath (Join-Path $taskOutput 'ui-error.txt')) { throw (Get-Content -LiteralPath (Join-Path $taskOutput 'ui-error.txt') -Raw) }
Get-Content -LiteralPath (Join-Path $taskOutput 'ui-results.txt')
