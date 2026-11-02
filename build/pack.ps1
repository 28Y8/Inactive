<#
.SYNOPSIS
    Builds the Inactive installer.

.DESCRIPTION
    Publishes the app as a self-contained win-x64 folder and packs it with vpk. The installer
    and update packages land in artifacts\Releases, where a previous release's package
    (from vpk download github) is used to generate a delta update.

    Code signing is off unless VPK_SIGN_PARAMS or VPK_SIGN_TEMPLATE is set in the environment.
    vpk reads both directly. See .github\workflows\release.yml.

.PARAMETER Version
    The version to build, such as 1.2.0. Defaults to the version in Directory.Build.props.

.PARAMETER Clean
    Deletes artifacts\Releases first. vpk refuses a version that is not newer than what is
    already there, so rebuilding the same version needs this.

.EXAMPLE
    ./build/pack.ps1 -Version 1.2.0 -Clean
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$packId = 'Inactive'
$channel = 'win'
$runtime = 'win-x64'

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$releasesDir = Join-Path $root 'artifacts\Releases'

if (-not $Version) {
    [xml]$buildProps = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $buildProps.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

# dotnet finds global.json and the tool manifest from the current folder.
Push-Location $root
try {
    # Files left over from an earlier publish would otherwise end up in the package.
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    if ($Clean -and (Test-Path $releasesDir)) {
        Remove-Item $releasesDir -Recurse -Force
    }

    Invoke-Checked dotnet @('tool', 'restore')

    Invoke-Checked dotnet @(
        'publish', 'src/Inactive/Inactive.csproj',
        '--configuration', 'Release',
        '--runtime', $runtime,
        '--self-contained',
        '--output', $publishDir,
        "-p:Version=$Version"
    )

    Invoke-Checked dotnet @(
        'vpk', 'pack',
        '--packId', $packId,
        '--packVersion', $Version,
        '--packDir', $publishDir,
        '--mainExe', "$packId.exe",
        '--packTitle', $packId,
        '--packAuthors', '28Y8',
        '--icon', 'src/Inactive/Assets/Inactive.ico',
        '--runtime', $runtime,
        '--channel', $channel,
        '--shortcuts', 'StartMenuRoot',
        '--noPortable',
        '--outputDir', $releasesDir
    )
}
finally {
    Pop-Location
}

$setup = Join-Path $releasesDir "$packId-$channel-Setup.exe"
if (-not (Test-Path $setup)) {
    throw "vpk finished but $setup does not exist."
}

Write-Host "Installer: $setup"
