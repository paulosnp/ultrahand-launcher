# Publishes the self-contained launcher to dist\UltrahandLauncher (needs the .NET 8 SDK)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'dist\UltrahandLauncher'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $here 'UltrahandLauncher\UltrahandLauncher.csproj') -c Release -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Done: $out\UltrahandLauncher.exe"

# The Linux launcher (UltrahandLauncher.Linux, Avalonia): one self-contained file for Linux x64
$linux = Join-Path $here 'dist\linux'
if (Test-Path $linux) { Remove-Item -Recurse -Force $linux }
dotnet publish (Join-Path $here 'UltrahandLauncher.Linux\UltrahandLauncher.Linux.csproj') -c Release -o $linux
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Move-Item (Join-Path $linux 'UltrahandLauncher') (Join-Path $linux 'UltrahandLauncher-linux-x64')
Write-Host "Done: $linux\UltrahandLauncher-linux-x64"
