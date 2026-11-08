<#
.SYNOPSIS
  Builds, tests and publishes NOXEL.exe (self-contained, single file, win-x64) into artifacts/release.
.EXAMPLE
  ./scripts/publish.ps1
  ./scripts/publish.ps1 -Developer "Your Name" -Build 42
#>
param(
  [string]$Developer = "",
  [string]$Build = "",
  [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\release"
$props = @()
if ($Developer) { $props += "-p:NoxelDeveloper=`"$Developer`"" }
if ($Build) { $props += "-p:NoxelBuild=$Build" }

if (-not $SkipTests) {
  dotnet test (Join-Path $root "tests\NOXEL.Tests\NOXEL.Tests.csproj") -c Release
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

if (Test-Path $out) { Get-ChildItem $out -Exclude *.md, *.txt | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish (Join-Path $root "src\NOXEL.App\NOXEL.App.csproj") -c Release -r win-x64 -o $out @props
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Single-file publish may leave debug symbols or nothing else; keep only what users need.
Get-ChildItem $out -Filter *.pdb | Remove-Item -Force
Copy-Item (Join-Path $root "LICENSE.txt"), (Join-Path $root "THIRD-PARTY-NOTICES.md"), (Join-Path $root "README.md") $out -Force
Copy-Item (Join-Path $root "docs\KNOWN-LIMITATIONS.md"), (Join-Path $root "docs\RELEASE-NOTES.md"), (Join-Path $root "docs\BUILD.md") $out -Force
$exe = Join-Path $out "NOXEL.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
"NOXEL.exe  $([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB  SHA256 $hash"
"$hash  NOXEL.exe" | Set-Content (Join-Path $out "NOXEL.exe.sha256") -Encoding ascii

# Installer (optional): built when Inno Setup 6 is installed.
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
  & $iscc /Q (Join-Path $root "installer\NOXEL.iss")
  if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
  $setup = Join-Path $root "artifacts\installer\NOXEL-Setup.exe"
  $shash = (Get-FileHash $setup -Algorithm SHA256).Hash
  "NOXEL-Setup.exe  $([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB  SHA256 $shash"
  "$shash  NOXEL-Setup.exe" | Set-Content "$setup.sha256" -Encoding ascii
} else { "Inno Setup 6 not found: skipped NOXEL-Setup.exe." }
