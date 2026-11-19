<#
.SYNOPSIS
    Writes the winget manifests for a BatTray release (to submit to microsoft/winget-pkgs).

.DESCRIPTION
    Reads the version and ProductCode from the MSI, computes its SHA-256 and writes the three manifest files
    (version, installer, default locale) to artifacts/winget/<version>/. The installer URL is the GitHub
    release asset, so upload the MSI to the release first and use exactly that file here.

    Run from the repository root:
        pwsh tools/new-winget-manifest.ps1 -Msi artifacts/BatTray-Setup-1.1.0.msi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Msi
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$msiPath = (Resolve-Path $Msi).Path

function Get-MsiProperty([string]$path, [string]$name) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $db.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $db, @("SELECT Value FROM Property WHERE Property = '$name'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
    return $value
}

$version = Get-MsiProperty $msiPath 'ProductVersion'
$productCode = Get-MsiProperty $msiPath 'ProductCode'
$upgradeCode = Get-MsiProperty $msiPath 'UpgradeCode'
$sha = (Get-FileHash $msiPath -Algorithm SHA256).Hash
$fileName = "BatTray-Setup-$version.msi"
$url = "https://github.com/koratsolutions-code/BatTray/releases/download/v$version/$fileName"
$id = 'KoratSolutions.BatTray'
$schema = '1.6.0'

$out = Join-Path $root "artifacts/winget/$version"
New-Item -ItemType Directory -Force $out | Out-Null

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content (Join-Path $out "$id.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
MinimumOSVersion: 10.0.19041.0
InstallerType: wix
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '$productCode'
AppsAndFeaturesEntries:
- DisplayName: BatTray
  Publisher: Korat Solutions
  ProductCode: '$productCode'
  UpgradeCode: '$upgradeCode'
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content (Join-Path $out "$id.installer.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: Korat Solutions
PublisherUrl: https://github.com/koratsolutions-code
PublisherSupportUrl: https://github.com/koratsolutions-code/BatTray/issues
PackageName: BatTray
PackageUrl: https://github.com/koratsolutions-code/BatTray
License: MIT
LicenseUrl: https://github.com/koratsolutions-code/BatTray/blob/main/LICENSE
ShortDescription: Battery levels of your wireless mice, keyboards, headsets and controllers on the Windows taskbar.
Description: |-
  BatTray shows the battery level of your Bluetooth and 2.4 GHz wireless devices right on the Windows taskbar,
  with an icon and a percentage for each one, low-battery and full-charge notifications and a 7-day history.
Moniker: battray
Tags:
- battery
- bluetooth
- taskbar
- mouse
- keyboard
- headset
- controller
ReleaseNotesUrl: https://github.com/koratsolutions-code/BatTray/releases/tag/v$version
ManifestType: defaultLocale
ManifestVersion: $schema
"@ | Set-Content (Join-Path $out "$id.locale.en-US.yaml") -Encoding utf8

Write-Host "winget manifests for $id $version written to $out"
Write-Host "Installer URL: $url"
Write-Host "SHA-256: $sha"
Write-Host 'Check them with:  winget validate --manifest' $out
