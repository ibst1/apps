# Build Fermata for Windows: one self-contained exe (no .NET install, no admin).
# Output: dist\Fermata.exe + "Starta Fermata.cmd", zipped as dist\Fermata-Windows.zip,
# and a copy in %USERPROFILE%\Program\Fermata (the folder locked LU computers allow).
param([switch]$NoInstall)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$localDotnet = Join-Path $env:LOCALAPPDATA "dotnet\dotnet.exe"
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { "dotnet" }
$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
& $dotnet publish (Join-Path $root "Fermata.csproj") -c Release -o $dist --nologo
if ($LASTEXITCODE) { throw "publish failed" }
Copy-Item (Join-Path $root "Starta Fermata.cmd") $dist
Get-ChildItem $dist -Exclude "Fermata.exe", "Starta Fermata.cmd" | Remove-Item -Recurse -Force
Compress-Archive -Path (Join-Path $dist "Fermata.exe"), (Join-Path $dist "Starta Fermata.cmd") -DestinationPath (Join-Path $dist "Fermata-Windows.zip") -Force
if (-not $NoInstall) {
  $dest = Join-Path $env:USERPROFILE "Program\Fermata"
  New-Item -ItemType Directory -Force $dest | Out-Null
  Get-Process Fermata -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 500
  Copy-Item (Join-Path $dist "Fermata.exe"), (Join-Path $dist "Starta Fermata.cmd") $dest -Force
  "Installed to $dest"
}
Get-ChildItem $dist | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
