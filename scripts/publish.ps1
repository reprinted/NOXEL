# SPDX-License-Identifier: GPL-3.0-only
<#
.SYNOPSIS
  Builds, tests and publishes NOXEL.exe (self-contained, single file, win-x64) into artifacts/release, then
  NOXEL-Setup.exe into artifacts/installer. The version comes from Directory.Build.props (the only place it is set).
.EXAMPLE
  ./scripts/publish.ps1
  ./scripts/publish.ps1 -Version 1.0.1-test -OutRoot C:\temp\noxel-1.0.1   # throwaway build for update testing
#>
param(
  [string]$Developer = "",
  [string]$Build = "",
  [string]$Version = "",          # override only for local update tests; releases use Directory.Build.props
  [string]$OutRoot = "",          # default: <repo>\artifacts
  [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = if ($OutRoot) { $OutRoot } else { Join-Path $root "artifacts" }
$out = Join-Path $artifacts "release"
$project = Join-Path $root "src\NOXEL.App\NOXEL.App.csproj"
$props = @()
if ($Developer) { $props += "-p:NoxelDeveloper=`"$Developer`"" }
if ($Build) { $props += "-p:NoxelBuild=$Build" }
if ($Version) { $props += "-p:Version=$Version" }
$appVersion = if ($Version) { $Version } else { (dotnet msbuild $project -getProperty:Version).Trim() }
"Version: $appVersion"

if (-not $SkipTests) {
  dotnet test (Join-Path $root "tests\NOXEL.Tests\NOXEL.Tests.csproj") -c Release
  if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

if (Test-Path $out) { Get-ChildItem $out -Exclude .gitkeep | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

dotnet publish $project -c Release -r win-x64 -o $out @props
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Get-ChildItem $out -Filter *.pdb | Remove-Item -Force
Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE.txt") -Force
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.txt"), (Join-Path $root "README.md") $out -Force
Copy-Item (Join-Path $root "docs\KNOWN-LIMITATIONS.md"), (Join-Path $root "docs\RELEASE-NOTES.md") $out -Force
$exe = Join-Path $out "NOXEL.exe"
$product = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $product.StartsWith($appVersion)) { throw "NOXEL.exe reports version $product, expected $appVersion." }
"NOXEL.exe        $([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB  SHA256 $((Get-FileHash $exe -Algorithm SHA256).Hash)"

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
  $instOut = Join-Path $artifacts "installer"
  New-Item -ItemType Directory -Force $instOut | Out-Null
  & $iscc /Q "/DAppVersion=$appVersion" "/DSourceExe=$exe" "/O$instOut" (Join-Path $root "installer\NOXEL.iss")
  if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
  $setup = Join-Path $instOut "NOXEL-Setup.exe"
  "NOXEL-Setup.exe  $([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB  SHA256 $((Get-FileHash $setup -Algorithm SHA256).Hash)"
} else { "Inno Setup 6 not found: skipped NOXEL-Setup.exe." }
