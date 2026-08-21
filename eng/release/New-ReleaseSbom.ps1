[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$baseVersion = $Version.Split('-', 2)[0]

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$inventoryPath = Join-Path $repositoryRoot 'docs\dependency-licenses.json'
$inventory = Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json
$packages = @{}
foreach ($group in $inventory.groups) {
    foreach ($package in $group.packages) {
        $separator = $package.LastIndexOf('@')
        if ($separator -le 0 -or $separator -eq $package.Length - 1) {
            throw "Dependency inventory contains an invalid package coordinate: $package"
        }

        $name = $package.Substring(0, $separator)
        $packageVersion = $package.Substring($separator + 1)
        $key = "$name@$packageVersion"
        if (-not $packages.ContainsKey($key)) {
            $packages[$key] = [ordered]@{
                type = 'library'
                name = $name
                version = $packageVersion
                licenses = @([ordered]@{ license = [ordered]@{ name = $group.license } })
                purl = "pkg:nuget/$([Uri]::EscapeDataString($name))@$packageVersion"
            }
        }
    }
}

$bom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.6'
    version = 1
    metadata = [ordered]@{
        component = [ordered]@{
            type = 'application'
            name = 'OmniSorSe'
            version = $Version
        }
        properties = @(
            [ordered]@{ name = 'omnisorse:sourceRevision'; value = $SourceRevision },
            [ordered]@{ name = 'omnisorse:baseVersion'; value = $baseVersion },
            [ordered]@{ name = 'omnisorse:targetFramework'; value = 'net10.0' },
            [ordered]@{ name = 'omnisorse:inventorySource'; value = 'docs/dependency-licenses.json' }
        )
    }
    components = @($packages.GetEnumerator() |
        Sort-Object Key |
        ForEach-Object { $_.Value })
}

$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$outputPath = Join-Path $artifactRoot "OmniSorSe-v$Version-sbom.cdx.json"
[IO.File]::WriteAllText(
    $outputPath,
    ($bom | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

$verified = Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
if ($verified.bomFormat -ne 'CycloneDX' -or
    $verified.specVersion -ne '1.6' -or
    $verified.metadata.component.version -ne $Version -or
    ($verified.metadata.properties | Where-Object { $_.name -eq 'omnisorse:sourceRevision' }).value -ne $SourceRevision -or
    ($verified.metadata.properties | Where-Object { $_.name -eq 'omnisorse:baseVersion' }).value -ne $baseVersion -or
    @($verified.components).Count -ne $packages.Count) {
    throw 'Generated CycloneDX SBOM failed validation.'
}

Get-Item -LiteralPath $outputPath
