<#
.SYNOPSIS
    Publishes Google Photo Wallpaper as a single .exe.

.DESCRIPTION
    Defaults to a self-contained build so the result runs on a machine with no .NET installed -
    which is the point of a redistributable build. Pass -FrameworkDependent for a ~330KB exe that
    requires the .NET 8 Desktop Runtime instead.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'publish'
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

$project = 'src/GooglePhotoWallpaper/GooglePhotoWallpaper.csproj'

Write-Host 'Running rotation tests...' -ForegroundColor Cyan
dotnet run --project tests/RotationTests/RotationTests.csproj -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Rotation tests failed - not publishing."
}

if (Test-Path $OutputDirectory) {
    Remove-Item $OutputDirectory -Recurse -Force
}

$selfContained = -not $FrameworkDependent

Write-Host ''
Write-Host "Publishing ($(if ($selfContained) { 'self-contained' } else { 'framework-dependent' }))..." -ForegroundColor Cyan

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $OutputDirectory,
    '--nologo', '-v', 'q',
    "--self-contained", "$selfContained",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none'
)

if ($selfContained) {
    $arguments += '-p:EnableCompressionInSingleFile=true'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Publish failed.'
}

$exe = Join-Path $OutputDirectory 'GooglePhotoWallpaper.exe'
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ''
Write-Host "Built $exe ($sizeMb MB)" -ForegroundColor Green
if (-not $selfContained) {
    Write-Host 'Target machines need the .NET 8 Desktop Runtime.' -ForegroundColor Yellow
}
