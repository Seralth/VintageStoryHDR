#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the mod and installs it into the Vintage Story mods folder.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$StopGame
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src/VintageStoryHDR/VintageStoryHDR.csproj'

if ($StopGame) {
    Get-Process -Name 'Vintagestory' -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Stopping Vintage Story (pid $($_.Id))..."
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(5000)) { $_.Kill() }
    }
}

Write-Host "Building $Configuration..."
dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$modFolder = Join-Path $repoRoot "src/VintageStoryHDR/bin/$Configuration/net10.0/mod"
if (-not (Test-Path $modFolder)) { throw "Built mod folder not found at $modFolder." }

$dataDir = if ($env:VINTAGE_STORY_DATA) { $env:VINTAGE_STORY_DATA } else { Join-Path $env:APPDATA 'VintagestoryData' }
$target = Join-Path $dataDir 'Mods/vshdr'

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $modFolder '*') -Destination $target -Recurse -Force

Write-Host "Installed to $target"
