param(
    [string]$SptRoot = "D:\SPT"
)

$ErrorActionPreference = "Stop"

$version = "?"
$pluginCs = Join-Path $PSScriptRoot "Plugin.cs"
if (Test-Path $pluginCs) {
    $m = Select-String -Path $pluginCs -Pattern 'BepInPlugin\("[^"]*",\s*"[^"]*",\s*"([^"]+)"\)' | Select-Object -First 1
    if ($m) { $version = $m.Matches[0].Groups[1].Value }
}

$pluginDir = Join-Path $SptRoot "BepInEx\plugins\BodyCamera"
$builtDll = Join-Path $PSScriptRoot "bin\Release\netstandard2.1\BodyCamera.dll"
$targetDll = Join-Path $pluginDir "BodyCamera.dll"

if (-not (Test-Path (Join-Path $SptRoot "BepInEx\core\BepInEx.dll"))) {
    throw "SPT root not found or BepInEx.dll missing: $SptRoot"
}

Write-Host "Building BodyCamera $version..." -ForegroundColor Cyan
dotnet build (Join-Path $PSScriptRoot "BodyCamera.csproj") -c Release -p:SptRoot="$SptRoot"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed. The game DLL was not replaced."
}

if (-not (Test-Path $builtDll)) {
    throw "Build succeeded but output DLL was not found: $builtDll"
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $builtDll $targetDll -Force
Write-Host "Installed BodyCamera $version to: $targetDll" -ForegroundColor Green
Get-Item $targetDll | Select-Object FullName, Length, LastWriteTime
