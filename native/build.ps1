param([switch]$TestsOnly)
# Builds the in-game extension (RA2NativeUI.dll), its DirectSound loader proxy (dsound.dll) and
# runs the native test suites. Needs Visual Studio 2019 or later (or Build Tools) with the
# "Desktop development with C++" workload; everything is compiled for 32-bit x86.
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$output = Join-Path $project 'build'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio (with C++ tools) was not found.' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio was found, but not its C++ x86 tools.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$cl = 'cl /nologo /W4 /O2 /MT /EHsc /arch:SSE2 /Zi'
$link = '/link /MACHINE:X86 /DEBUG /PDBALTPATH:%%_PDB%% user32.lib advapi32.lib'
$commands = @('@echo off', ('set "PATH=' + (Split-Path $vswhere) + ';%PATH%"'), ('call "' + $vcvars + '" x86 >nul'), 'if errorlevel 1 exit /b 1',
    "$cl /DNATIVE_UI_TEST /Fe:NativeTests.exe ..\src\NativeUI.cpp ..\tests\NativeTests.cpp $link", 'if errorlevel 1 exit /b 1',
    "$cl /DNATIVE_UI_TEST /Fe:SettingsTests.exe ..\src\NativeUI.cpp ..\src\SessionSettings.cpp ..\tests\SettingsTests.cpp $link", 'if errorlevel 1 exit /b 1',
    "$cl /DNATIVE_UI_TEST /Fe:FeatureTests.exe ..\src\NativeUI.cpp ..\src\Economy.cpp ..\src\Commands.cpp ..\src\GameClock.cpp ..\src\HealthBars.cpp ..\src\RangeCircles.cpp ..\tests\FeatureTests.cpp $link", 'if errorlevel 1 exit /b 1',
    '.\NativeTests.exe', 'if errorlevel 1 exit /b 1', '.\SettingsTests.exe', 'if errorlevel 1 exit /b 1', '.\FeatureTests.exe', 'if errorlevel 1 exit /b 1')
if (-not $TestsOnly) {
    $commands += @(
        "$cl /LD /Fe:RA2NativeUI.dll ..\src\NativeUI.cpp ..\src\Tooltips.cpp ..\src\Economy.cpp ..\src\Placement.cpp ..\src\SessionSettings.cpp ..\src\Commands.cpp ..\src\GameClock.cpp ..\src\HealthBars.cpp ..\src\RangeCircles.cpp $link", 'if errorlevel 1 exit /b 1',
        "cl /nologo /W4 /O2 /MT /EHsc /Zi /LD /Fe:dsound.dll ..\src\Loader.cpp /link /MACHINE:X86 /DEBUG /PDBALTPATH:%%_PDB%% user32.lib", 'if errorlevel 1 exit /b 1',
        'cl /nologo /W4 /O2 /MT /EHsc /Fe:ProxyTests.exe ..\tests\ProxyTests.cpp /link /MACHINE:X86', 'if errorlevel 1 exit /b 1',
        '.\ProxyTests.exe', 'if errorlevel 1 exit /b 1')
}
$commands += 'exit /b 0'
[IO.File]::WriteAllLines((Join-Path $output 'compile.cmd'), $commands, [Text.Encoding]::ASCII)
Push-Location -LiteralPath $output
try {
    # Explicit .\ so the build also works when NoDefaultCurrentDirectoryInExePath is set.
    & $env:ComSpec /d /c .\compile.cmd
    if ($LASTEXITCODE -ne 0) { throw "Native build or tests failed ($LASTEXITCODE)." }
} finally { Pop-Location }
Write-Output 'Native extension built and all native tests passed.'

