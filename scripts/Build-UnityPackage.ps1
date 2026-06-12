<#
.SYNOPSIS
Builds the Unity UPM package (com.gamelattice.lattice) for a given version.

.DESCRIPTION
Compiles packaging/unity/Lattice.UnityBundle.csproj to collect the full Lattice
dependency closure, stages the UPM layout from packaging/unity/upm, generates
deterministic Unity .meta files (GUID = MD5 of package-relative path, so GUIDs are
stable across releases and machines), and emits:

  <OutputDir>/com.gamelattice.lattice-<Version>.tgz   (Package Manager tarball)
  <OutputDir>/upm-staging/package/                    (layout for the upm branch)

Runs on Windows PowerShell 7+ and pwsh on Linux CI.

.EXAMPLE
pwsh scripts/Build-UnityPackage.ps1 -Version 0.1.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z\.-]+)?$')]
    [string]$Version,

    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts' }
$packageName = 'com.gamelattice.lattice'

Write-Host "Building dependency closure..."
dotnet build (Join-Path $repoRoot 'packaging/unity/Lattice.UnityBundle.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$binDir = Join-Path $repoRoot 'packaging/unity/bin/Release/netstandard2.1'
$staging = Join-Path $OutputDir 'upm-staging'
$pkgDir = Join-Path $staging 'package'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$runtimeDir = New-Item -ItemType Directory -Force (Join-Path $pkgDir 'Runtime')

# Template files, with the version stamped into package.json.
$manifest = Get-Content (Join-Path $repoRoot 'packaging/unity/upm/package.json') -Raw
$manifest = $manifest -replace '"version": "0\.0\.0-dev"', ('"version": "' + $Version + '"')
Set-Content -Path (Join-Path $pkgDir 'package.json') -Value $manifest -NoNewline
Copy-Item (Join-Path $repoRoot 'packaging/unity/upm/README.md') $pkgDir
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $pkgDir 'LICENSE.md')

# Bundle every assembly except the collector project's own output. Lattice pdbs ride
# along for usable stack traces; dependency pdbs don't exist in the NuGet lib folders.
Get-ChildItem $binDir -File |
    Where-Object { $_.Name -notlike 'Lattice.UnityBundle.*' -and $_.Extension -in '.dll', '.pdb' } |
    Copy-Item -Destination $runtimeDir

# --- Unity .meta generation -------------------------------------------------------
function Get-DeterministicGuid([string]$RelativePath) {
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes("$packageName/$RelativePath")
        return ([System.BitConverter]::ToString($md5.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $md5.Dispose() }
}

$folderMeta = @'
fileFormatVersion: 2
guid: {0}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'@

$dllMeta = @'
fileFormatVersion: 2
guid: {0}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'@

$defaultMeta = @'
fileFormatVersion: 2
guid: {0}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'@

$textMeta = @'
fileFormatVersion: 2
guid: {0}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'@

$manifestMeta = @'
fileFormatVersion: 2
guid: {0}
PackageManifestImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
'@

function Write-Meta([System.IO.FileSystemInfo]$Item) {
    $rel = [System.IO.Path]::GetRelativePath($pkgDir, $Item.FullName).Replace('\', '/')
    $guid = Get-DeterministicGuid $rel
    $template = if ($Item.PSIsContainer) { $folderMeta }
    elseif ($Item.Extension -eq '.dll') { $dllMeta }
    elseif ($Item.Name -eq 'package.json') { $manifestMeta }
    elseif ($Item.Extension -in '.md', '.txt', '.json') { $textMeta }
    else { $defaultMeta }
    Set-Content -Path "$($Item.FullName).meta" -Value ($template -f $guid)
}

# Snapshot before generating, or the loop enumerates the .meta files it creates.
$items = @(Get-ChildItem $pkgDir -Recurse | Where-Object { $_.Extension -ne '.meta' })
$items | ForEach-Object { Write-Meta $_ }

# --- Tarball (UPM format: gzipped tar with a top-level "package/" folder) ----------
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$tarball = Join-Path $OutputDir "$packageName-$Version.tgz"
if (Test-Path $tarball) { Remove-Item $tarball -Force }
tar -czf $tarball -C $staging package
if ($LASTEXITCODE -ne 0) { throw "tar failed" }

Write-Host "UPM tarball: $tarball"
Write-Host "UPM staging (for upm branch): $pkgDir"
