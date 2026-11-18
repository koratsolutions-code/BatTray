<#
.SYNOPSIS
    Publishes BatTray (self-contained, win-x64) and builds the per-user MSI installer.

.DESCRIPTION
    Output: artifacts/BatTray-Setup-<version>.msi
    Run from the repository root:  pwsh tools/build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

# Version comes from Directory.Build.props.
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'Version not found in Directory.Build.props' }

Write-Host "Publishing BatTray $version..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src/BatTray.App/BatTray.App.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host 'Building installer...'
dotnet build (Join-Path $root 'installer/BatTray.Installer.wixproj') `
    -c $Configuration -p:PublishDir=$publish -p:ProductVersion=$version
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }

$msi = Get-ChildItem (Join-Path $root 'installer/bin') -Recurse -Filter 'BatTray-Setup.msi' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$target = Join-Path $artifacts "BatTray-Setup-$version.msi"
Copy-Item $msi.FullName $target -Force
Write-Host "Installer: $target ($([math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"
