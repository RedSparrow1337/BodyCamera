param(
    [string]$SptRoot = "D:\SPT"
)

$ErrorActionPreference = "Stop"

$version = "?"
$m = Select-String -Path (Join-Path $PSScriptRoot "Plugin.cs") -Pattern 'BepInPlugin\("[^"]*",\s*"[^"]*",\s*"([^"]+)"\)' | Select-Object -First 1
if ($m) { $version = $m.Matches[0].Groups[1].Value }

$pluginDir = Join-Path $SptRoot "BepInEx\plugins\BodyCamera"
$dll = Join-Path $pluginDir "BodyCamera.dll"
$sounds = Join-Path $pluginDir "sounds"

if (-not (Test-Path $dll)) { throw "BodyCamera.dll not found: $dll (run build.ps1 first)" }

$bytes = [System.IO.File]::ReadAllBytes($dll)
$needle = "Loaded $version."
$u0 = [System.Text.Encoding]::Unicode.GetString($bytes)
$u1 = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
if (-not ($u0.Contains($needle) -or $u1.Contains($needle))) { throw "The installed DLL is not version $version - run build.ps1 first." }

$releaseDir = Join-Path $PSScriptRoot "Release"
$stage = Join-Path $releaseDir "BodyCamera-$version"
$zip = Join-Path $releaseDir "BodyCamera-$version.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

$target = Join-Path $stage "BepInEx\plugins\BodyCamera"
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item $dll $target

if (Test-Path $sounds) {
    Copy-Item $sounds $target -Recurse
} else {
    Write-Host "No sounds folder found ($sounds) - packed without sounds." -ForegroundColor Yellow
}

Copy-Item (Join-Path $releaseDir "README.txt") $stage
$license = Join-Path $PSScriptRoot "LICENSE"
if (Test-Path $license) { Copy-Item $license $stage } else { Write-Host "No LICENSE file in the project folder - packed without it." -ForegroundColor Yellow }

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

Write-Host "Release folder: $stage" -ForegroundColor Green
Write-Host "Archive:        $zip" -ForegroundColor Green
Get-Item $zip | Select-Object FullName, Length, LastWriteTime
