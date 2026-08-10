[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$names = @(
    "OpenSorSe-v$Version-win-x64.zip",
    "OpenSorSe-v$Version-win-x64-setup.exe",
    "OpenSorSe-v$Version-macos-x64.dmg",
    "OpenSorSe-v$Version-macos-arm64.dmg"
)
$lines = foreach ($name in $names) {
    $path = Join-Path $artifactRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release artifact is missing: $name"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
$checksumPath = Join-Path $artifactRoot "OpenSorSe-v$Version-SHA256SUMS.txt"
[IO.File]::WriteAllLines(
    $checksumPath,
    [string[]]$lines,
    [Text.UTF8Encoding]::new($false))

foreach ($line in Get-Content -LiteralPath $checksumPath) {
    $parts = $line -split '\s+', 2
    $actual = (Get-FileHash -LiteralPath (Join-Path $artifactRoot $parts[1]) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $parts[0]) {
        throw "Checksum verification failed for $($parts[1])."
    }
}

Get-Item -LiteralPath $checksumPath
