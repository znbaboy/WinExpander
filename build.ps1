<#
  Builds a portable TextExpander.
  Usage (from the project folder):
    powershell -ExecutionPolicy Bypass -File .\build.ps1            # x64
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Arch arm64
  Output: .\publish\TextExpander\  and  .\TextExpander-portable-<arch>.zip
#>
param(
    [ValidateSet('x64', 'arm64', 'x86')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Test-Net8Sdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    return [bool](dotnet --list-sdks 2>$null | Where-Object { $_ -match '^8\.' })
}

# 1. Make sure the .NET 8 SDK is available (installs locally if missing, no admin needed)
if (-not (Test-Net8Sdk)) {
    Write-Host '.NET 8 SDK not found - installing to .\.dotnet ...' -ForegroundColor Yellow
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 8.0 -InstallDir "$PSScriptRoot\.dotnet"
    $env:PATH = "$PSScriptRoot\.dotnet;$env:PATH"
    $env:DOTNET_ROOT = "$PSScriptRoot\.dotnet"
}
Write-Host "Using .NET SDK $(dotnet --version)" -ForegroundColor Cyan

# 2. Publish portable, self-contained build
$out = Join-Path $PSScriptRoot 'publish\TextExpander'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# WinUI's resource (PRI) tasks ship with Visual Studio, so prefer VS MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = if (Test-Path $vswhere) {
    & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}

if ($msbuild) {
    Write-Host "Using $msbuild" -ForegroundColor Cyan
    & $msbuild TextExpander.csproj /restore /t:Publish /m /v:m `
        /p:Configuration=Release /p:Platform=$Arch /p:RuntimeIdentifier=win-$Arch "/p:PublishDir=$out\"
} else {
    Write-Warning 'Visual Studio / Build Tools not found - trying dotnet publish (may fail on PRI tasks).'
    Write-Warning 'If it fails, install "Visual Studio Build Tools" with the ".NET desktop build tools" workload,'
    Write-Warning 'or just download the prebuilt zip from the GitHub Releases page.'
    dotnet publish TextExpander.csproj -c Release -r "win-$Arch" -p:Platform=$Arch -o $out
}
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }

# 3. Zip it
$zip = Join-Path $PSScriptRoot "TextExpander-portable-$Arch.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip

Write-Host ''
Write-Host "Done!" -ForegroundColor Green
Write-Host "  Folder: $out"
Write-Host "  Zip:    $zip"
Write-Host "  Run:    $out\TextExpander.exe"
