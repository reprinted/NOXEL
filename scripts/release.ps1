# SPDX-License-Identifier: GPL-3.0-only
<#
.SYNOPSIS
  Prepares a NOXEL release locally. It never uploads anything.

  1. Validates the version (semantic version, equal to Directory.Build.props, not already prepared).
  2. Runs the automated tests.
  3. Builds NOXEL.exe (Release, self-contained) and NOXEL-Setup.exe.
  4. Writes SHA256SUMS.txt and the release notes for this version.
  5. Collects everything in artifacts\release-v<version>\ and prints the publishing steps.
.EXAMPLE
  ./scripts/release.ps1 -Version 1.0.0
#>
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Use a stable semantic version like 1.0.1 (no 'v', no suffix)." }
$propsVersion = (dotnet msbuild (Join-Path $root "src\NOXEL.App\NOXEL.App.csproj") -getProperty:Version).Trim()
if ($propsVersion -ne $Version) { throw "Directory.Build.props says $propsVersion. Set <Version>$Version</Version> there first (it is the only version source)." }
$notesFile = Join-Path $root "docs\RELEASE-NOTES.md"
$notesText = Get-Content $notesFile -Raw
$m = [regex]::Match($notesText, "(?ms)^## $([regex]::Escape($Version))\b.*?(?=^## |\z)")
if (-not $m.Success) { throw "docs\RELEASE-NOTES.md has no '## $Version' section. Write the release notes first." }
if ((git -C $root status --porcelain).Length -gt 0) { Write-Warning "The working tree has uncommitted changes. Commit them so the release matches a commit." }

& (Join-Path $PSScriptRoot "publish.ps1")
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { throw "Build failed." }

$dest = Join-Path $root "artifacts\release-v$Version"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory $dest | Out-Null
Copy-Item (Join-Path $root "artifacts\release\NOXEL.exe"), (Join-Path $root "artifacts\installer\NOXEL-Setup.exe"), (Join-Path $root "THIRD-PARTY-NOTICES.txt") $dest
Copy-Item (Join-Path $root "LICENSE") (Join-Path $dest "LICENSE.txt")
foreach ($f in "NOXEL.exe", "NOXEL-Setup.exe") {
  $v = (Get-Item (Join-Path $dest $f)).VersionInfo.ProductVersion
  if (-not $v.StartsWith($Version)) { throw "$f reports version $v, expected $Version." }
}
$sums = foreach ($f in "NOXEL.exe", "NOXEL-Setup.exe") { "{0}  {1}" -f (Get-FileHash (Join-Path $dest $f) -Algorithm SHA256).Hash.ToLower(), $f }
[IO.File]::WriteAllLines((Join-Path $dest "SHA256SUMS.txt"), $sums)
$sourceLine = "`n`n---`nSource code for this release (GPL-3.0-only): https://github.com/reprinted/NOXEL/tree/v$Version`n"
[IO.File]::WriteAllText((Join-Path $dest "RELEASE-NOTES-v$Version.md"), ($m.Value -replace "^## ", "# NOXEL ").Trim() + $sourceLine)

""
"Release v$Version prepared in $dest"
Get-ChildItem $dest | ForEach-Object { "  {0,-28} {1,10:N0} bytes" -f $_.Name, $_.Length }
""
"To publish (only after testing):"
"  1. Tag the source and publish it FIRST (GPL: the matching source must be available):"
"       git tag v$Version; git push origin main v$Version   (origin = github.com/reprinted/NOXEL)"
"  2. Open https://github.com/reprinted/NOXEL-Releases/releases/new"
"  3. Tag: v$Version   Title: NOXEL v$Version   Notes: paste RELEASE-NOTES-v$Version.md"
"  4. Attach NOXEL.exe, NOXEL-Setup.exe, SHA256SUMS.txt, LICENSE.txt and THIRD-PARTY-NOTICES.txt"
"  5. Leave 'Set as a pre-release' unticked; Publish release."
"  6. Check https://api.github.com/repos/reprinted/NOXEL-Releases/releases/latest shows v$Version and each asset has a sha256 digest."
"  Or with GitHub CLI:  gh release create v$Version `"$dest\NOXEL.exe`" `"$dest\NOXEL-Setup.exe`" `"$dest\SHA256SUMS.txt`" --repo reprinted/NOXEL-Releases --title `"NOXEL v$Version`" --notes-file `"$dest\RELEASE-NOTES-v$Version.md`""
