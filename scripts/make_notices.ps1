# SPDX-License-Identifier: GPL-3.0-only
<#
.SYNOPSIS
  Regenerates THIRD-PARTY-NOTICES.txt from the exact license and notice files of the packages NOXEL ships.
  Run after changing dependency versions. Texts are copied verbatim; nothing is paraphrased.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$nuget = Join-Path $env:USERPROFILE ".nuget\packages"
$fonts = Join-Path $root "src\NOXEL.App\Assets\Fonts"

$components = @(
  @{ Name = ".NET 10 runtime and libraries (Microsoft.NETCore.App.Runtime.win-x64 10.0.11)"; Use = "Bundled runtime (self-contained build)";
     Files = @("$nuget\microsoft.netcore.app.runtime.win-x64\10.0.11\LICENSE.TXT", "$nuget\microsoft.netcore.app.runtime.win-x64\10.0.11\THIRD-PARTY-NOTICES.TXT") },
  @{ Name = "Microsoft Windows App SDK 2.6.5 (WinUI 3, Foundation, Interactive Experiences, Base, Runtime)"; Use = "User interface framework and runtime";
     Files = @("$nuget\microsoft.windowsappsdk.winui\2.6.5\license.txt", "$nuget\microsoft.windowsappsdk.runtime\2.6.5\NOTICE.txt") },
  @{ Name = "Microsoft Edge WebView2 SDK loader 1.0.3719.77 (dependency of WinUI; NOXEL does not display web content)"; Use = "Redistributed loader DLLs";
     Files = @("$nuget\microsoft.web.webview2\1.0.3719.77\LICENSE.txt", "$nuget\microsoft.web.webview2\1.0.3719.77\NOTICE.txt") },
  @{ Name = "Inter typeface (variable) — The Inter Project Authors"; Use = "Interface font"; Files = @("$fonts\OFL-Inter.txt") },
  @{ Name = "Rajdhani typeface (SemiBold, Bold) — Indian Type Foundry"; Use = "Heading font"; Files = @("$fonts\OFL-Rajdhani.txt") }
)

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("NOXEL — THIRD-PARTY SOFTWARE NOTICES AND INFORMATION")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("NOXEL is free software under the GNU General Public License v3 only (GPL-3.0-only; see LICENSE). It")
[void]$sb.AppendLine("incorporates the third-party components listed below, each under its own license, reproduced here verbatim.")
[void]$sb.AppendLine("These licenses apply to those components only; they do not")
[void]$sb.AppendLine("relicense those components under the GPL, and the GPL does not restrict rights their own licenses grant.")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("Also used, not redistributed: Microsoft Windows SDK C#/WinRT projections (Microsoft.Windows.SDK.NET; Windows SDK")
[void]$sb.AppendLine("license https://aka.ms/WinSDKLicenseURL), the NVIDIA driver's nvapi64.dll (installed by the NVIDIA driver; NVIDIA is")
[void]$sb.AppendLine("a trademark of NVIDIA Corporation and does not endorse NOXEL), and Windows system DLLs. Build-time only: Windows SDK")
[void]$sb.AppendLine("BuildTools, xUnit and Microsoft.NET.Test.Sdk (not shipped).")
[void]$sb.AppendLine("")
$i = 1
foreach ($c in $components) { [void]$sb.AppendLine(("{0}. {1} — {2}" -f $i++, $c.Name, $c.Use)) }
foreach ($c in $components) {
  [void]$sb.AppendLine(""); [void]$sb.AppendLine(("=" * 100)); [void]$sb.AppendLine($c.Name); [void]$sb.AppendLine(("=" * 100))
  foreach ($f in $c.Files) {
    if (-not (Test-Path $f)) { throw "Missing license file: $f (restore packages first)" }
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("--- " + (Split-Path $f -Leaf) + " ---"); [void]$sb.AppendLine((Get-Content $f -Raw).TrimEnd())
  }
}
$out = Join-Path $root "THIRD-PARTY-NOTICES.txt"
[IO.File]::WriteAllText($out, $sb.ToString(), [Text.UTF8Encoding]::new($false))
"Wrote $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
