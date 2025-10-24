Param(
  [string]$Version = "v3.5",
  [switch]$Sign,
  [string]$CertThumbprint = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Info($msg) { Write-Host "[INFO] $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[OK]   $msg" -ForegroundColor Green }
function Write-Err($msg)  { Write-Host "[ERR]  $msg" -ForegroundColor Red }

# 1) Locate the .csproj
$proj = Get-ChildItem -Filter *.csproj -Recurse | Select-Object -First 1
if (-not $proj) { Write-Err "Couldn't find a .csproj in this folder."; exit 1 }
$projPath = $proj.FullName
$root = Split-Path $projPath -Parent
Set-Location $root

Write-Info "Project: $($proj.Name)"
Write-Info "Version: $Version"

# 2) Clean publish folder
$publishDir = Join-Path $root "bin\publish\win-x64"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

# 3) Publish single-file, self-contained EXE
Write-Info "Publishing self-contained single-file exe (win-x64)..."
dotnet publish $projPath -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -p:PublishTrimmed=false `
  -o $publishDir | Out-Null

# 4) Locate the exe
$exe = Get-ChildItem $publishDir -Filter *.exe | Select-Object -First 1
if (-not $exe) { Write-Err "No exe produced in $publishDir"; exit 1 }

# 5) Optional code signing (uses Windows cert store + signtool or Authenticode)
if ($Sign) {
  if ([string]::IsNullOrWhiteSpace($CertThumbprint)) {
    Write-Err "Signing requested but -CertThumbprint not provided."
    exit 1
  }
  try {
    Write-Info "Attempting Authenticode signature..."
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My\$CertThumbprint -ErrorAction Stop
    Set-AuthenticodeSignature -FilePath $exe.FullName -Certificate $cert | Out-Null
    Write-Ok  "Signed $($exe.Name) with cert thumbprint $CertThumbprint"
  } catch {
    Write-Err "Signing failed (you can re-run without -Sign or install proper signing tools). $_"
    exit 1
  }
}

# 6) Stage a clean folder that has ONLY the exe (root of zip)
$stage = Join-Path $root "release_stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $exe.FullName (Join-Path $stage "WayOfLonoRNG.exe") -Force

# 7) Zip it (root contains just WayOfLonoRNG.exe)
$zipOut = Join-Path $root ("WayOfLonoRNG_{0}_Win64.zip" -f $Version)
if (Test-Path $zipOut) { Remove-Item $zipOut -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipOut)

# 8) SHA256 for integrity (next to the zip)
$hash = Get-FileHash -Algorithm SHA256 $zipOut
$hashFile = "$zipOut.sha256.txt"
"{0}  {1}" -f $hash.Hash, (Split-Path $zipOut -Leaf) | Out-File -Encoding ascii $hashFile

# 9) Done
Write-Ok "Created: $zipOut"
Write-Ok "SHA256:  $($hash.Hash)"
Write-Info "Users can unzip and double-click WayOfLonoRNG.exe. No .NET install required."
