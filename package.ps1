#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the tests, builds Release and writes the installable mod zip to artifacts/.
.DESCRIPTION
    The zip has modinfo.json and the DLL at its root, which is what the game and the
    mod DB expect. Drop it into %APPDATA%\VintagestoryData\Mods as is.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'src/VintageStoryHDR/VintageStoryHDR.csproj'
$tests = Join-Path $repoRoot 'tests/VintageStoryHDR.Tests/VintageStoryHDR.Tests.csproj'

if (-not $SkipTests) {
    dotnet test $tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
}

dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$modFolder = Join-Path $repoRoot 'src/VintageStoryHDR/bin/Release/net10.0/mod'
$modInfo = Get-Content (Join-Path $modFolder 'modinfo.json') -Raw | ConvertFrom-Json

# The game shows modinfo's version; the DLL carries the csproj's. Releasing with the two out of step is a mistake.
$dllVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $modFolder 'VintageStoryHDR.dll')).ProductVersion -replace '\+.*$'
if ($dllVersion -ne $modInfo.version) {
    throw "modinfo.json says $($modInfo.version) but the csproj built $dllVersion. Bump both."
}

$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$zip = Join-Path $artifacts "$($modInfo.modid)-$($modInfo.version).zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $modFolder '*') -DestinationPath $zip

Write-Host "Packaged $zip"
