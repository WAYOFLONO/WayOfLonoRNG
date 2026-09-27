Param(
  [string]$Version = "v3.5"
)

$publishDir = Join-Path $PSScriptRoot "dist\WayOfLono-RNG_v3"
$zipOut = Join-Path $PSScriptRoot "WayOfLonoRNG_${Version}_Win64.zip"

# Publish standalone single-file EXE
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

# Zip it up
if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipOut)
Write-Host "Created: $zipOut" -ForegroundColor Green
