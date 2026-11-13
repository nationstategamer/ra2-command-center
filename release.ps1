param(
    [Parameter(Mandatory = $true)][string]$Version,
    # Optional prebuilt, already play-tested binaries to ship instead of a fresh native build.
    [string]$NativeDll = '',
    [string]$LoaderDll = ''
)
# Builds Command Center and the in-game extension, then writes dist\RA2CommandCenter-<Version>.zip
# and dist\SHA256SUMS.txt. The zip holds one RA2CommandCenter folder to extract into the game folder.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$app = Join-Path $stage 'RA2CommandCenter'
$package = Join-Path $app 'package\EnhancedRA2'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $package | Out-Null

& (Join-Path $root 'launcher\build.ps1') -OutputPath (Join-Path $app 'RA2CommandCenter.exe') -NoGameIcon
if ($NativeDll -and $LoaderDll) {
    Copy-Item -LiteralPath $NativeDll -Destination (Join-Path $package 'RA2NativeUI.dll')
    Copy-Item -LiteralPath $LoaderDll -Destination (Join-Path $package 'dsound.dll')
} else {
    & (Join-Path $root 'native\build.ps1')
    Copy-Item -LiteralPath (Join-Path $root 'native\build\RA2NativeUI.dll') -Destination $package
    Copy-Item -LiteralPath (Join-Path $root 'native\build\dsound.dll') -Destination $package
}
Copy-Item -LiteralPath (Join-Path $root 'ai\ai-additions.json') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'package\RA2NativeUI.ini') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'package\Enhanced-RA2-README.txt') -Destination $package
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $package 'Phobos-LICENSE.md')
Copy-Item -LiteralPath (Join-Path $root 'native\LICENSE-loader.txt') -Destination (Join-Path $package 'RA2NativeUI-LICENSE.txt')
foreach ($name in 'LICENSE', 'COPYING', 'THIRD-PARTY-NOTICES.md') { Copy-Item -LiteralPath (Join-Path $root $name) -Destination $app }
$readme = @"
Red Alert 2 Command Center $Version  (unofficial fan project)

1. Put this RA2CommandCenter folder inside your Steam Red Alert 2 game folder,
   next to game.exe and gamemd.exe
   (Steam: right-click the game > Manage > Browse local files).
2. Run RA2CommandCenter\RA2CommandCenter.exe.
3. For the enhanced edition, click "Enhanced Red Alert 2" in the sidebar and
   let Command Center set it up. Your original game files are not changed.

To uninstall, delete this folder. Full documentation and source:
https://github.com/nationstategamer/ra2-command-center
"@
[IO.File]::WriteAllText((Join-Path $app 'README.txt'), $readme.Replace("`r`n", "`n").Replace("`n", "`r`n"))

$zip = Join-Path $dist ("RA2CommandCenter-$Version.zip")
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path $app -DestinationPath $zip
$lines = @("$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())  $(Split-Path $zip -Leaf)")
foreach ($file in Get-ChildItem -LiteralPath $app -Recurse -File | Sort-Object FullName) {
    $lines += "$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($file.FullName.Substring($stage.Length + 1).Replace('\', '/'))"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $lines)
Get-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt')
