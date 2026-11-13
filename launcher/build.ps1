param([string]$OutputPath = '', [switch]$NoGameIcon)
# -NoGameIcon builds the public release: the game's own icon (EA artwork) is not embedded.
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot 'RA2CommandCenter.exe' }
$taskSource = Join-Path $PSScriptRoot 'src'
$taskGame = Split-Path -Parent $PSScriptRoot
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
$taskWpf = Join-Path $taskFramework 'WPF'
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'The Windows .NET Framework compiler is unavailable.' }
$taskArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + $OutputPath),
    ('/win32manifest:' + (Join-Path $taskSource 'app.manifest')),
    ('/resource:' + (Join-Path $taskSource 'CommandCenter.xaml') + ',CommandCenter.xaml'),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll', '/reference:System.Runtime.Serialization.dll', '/reference:System.Numerics.dll',
    ('/reference:' + (Join-Path $taskWpf 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $taskWpf 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $taskWpf 'WindowsBase.dll')),
    (Join-Path $taskSource 'LauncherApp.cs'), (Join-Path $taskSource 'LauncherCore.cs'), (Join-Path $taskSource 'SaveCatalog.cs'), (Join-Path $taskSource 'EditionSetup.cs')
)
$taskIcon = Join-Path $taskGame 'RA2.ICO'
if (!$NoGameIcon -and (Test-Path -LiteralPath $taskIcon)) { $taskArgs += ('/win32icon:' + $taskIcon) }
& $taskCompiler @taskArgs
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Write-Output 'Command Center built successfully.'
