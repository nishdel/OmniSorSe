[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.11.0',

    [Parameter()]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision = '',

    [Parameter()]
    [string]$OutputDirectory = '',

    [Parameter()]
    [string]$InnoSetupCompiler = '',

    [Parameter()]
    [switch]$PortableOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows release artifacts must be built on native Windows.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($SourceRevision)) {
    $SourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $SourceRevision -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'An exact 40-character source revision is required for release packaging.'
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot '.artifacts\release'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $outputRoot 'staging\win-x64'))
$expectedPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $stagingRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Windows staging directory escaped the selected release output root.'
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$applicationDirectory = Join-Path $stagingRoot 'OmniSorSe'

$publishArguments = @(
    'publish',
    (Join-Path $repositoryRoot 'src\OpenSorSe.Desktop\OpenSorSe.Desktop.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '--output', $applicationDirectory,
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:PublishSingleFile=false'
    "-p:OmniSorSeVersion=$Version"
    "-p:OmniSorSeFileVersion=$Version.0"
    "-p:SourceRevisionId=$SourceRevision"
    '-p:ContinuousIntegrationBuild=true'
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\dependency-licenses.json') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\INSTALLATION.md') -Destination $applicationDirectory
$releaseNotes = Join-Path $repositoryRoot "docs\RELEASE_NOTES_v$Version.md"
if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) {
    throw "Release notes for v$Version are missing: $releaseNotes"
}
Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $applicationDirectory 'RELEASE_NOTES.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\OpenSorSe.Desktop\Assets\opensorse-app-icon.ico') -Destination (Join-Path $applicationDirectory 'OmniSorSe.ico')
$runtimeConfiguration = Get-Content -Raw -LiteralPath (Join-Path $applicationDirectory 'OmniSorSe.runtimeconfig.json') | ConvertFrom-Json
$runtimeFramework = $runtimeConfiguration.runtimeOptions.includedFrameworks |
    Where-Object { $_.name -eq 'Microsoft.NETCore.App' } |
    Select-Object -First 1
if ($null -eq $runtimeFramework -or [string]::IsNullOrWhiteSpace($runtimeFramework.version)) {
    throw 'The self-contained publish does not identify its bundled .NET runtime.'
}
$runtimeVersion = $runtimeFramework.version
[IO.File]::WriteAllText(
    (Join-Path $applicationDirectory 'OmniSorSe.build.json'),
    ([ordered]@{
        productVersion = $Version
        sourceRevision = $SourceRevision
        configuration = 'Release'
        targetFramework = 'net10.0'
        runtimeIdentifier = 'win-x64'
        runtimeVersion = $runtimeVersion
        selfContained = $true
    } | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false))

Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Force -File -Filter '*.pdb' |
    Remove-Item -Force

$executable = Join-Path $applicationDirectory 'OmniSorSe.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The self-contained Windows publish did not produce OmniSorSe.exe.'
}
foreach ($runtimeAsset in @('coreclr.dll', 'hostfxr.dll', 'libSkiaSharp.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $applicationDirectory $runtimeAsset) -PathType Leaf)) {
        throw "The Windows publish is missing required runtime/native asset '$runtimeAsset'."
    }
}

$forbidden = Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Force -File | Where-Object {
    $_.Extension -in @('.pdb', '.trx', '.db', '.sqlite', '.log', '.oms-state', '.bak', '.cs', '.csproj', '.sln') -or
    $_.Name -match '(?i)\.(db-wal|db-shm)$' -or
    [IO.Path]::GetFileNameWithoutExtension($_.Name) -match '(?i)^(test-results?|secrets?|credentials?|tokens?|settings|operation-journal|change-plans?|saved-views?|recipes?)$'
}
if ($forbidden) {
    throw "Forbidden release payload entries: $($forbidden.FullName -join ', ')"
}

$portableArchive = Join-Path $outputRoot "OmniSorSe-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}
Compress-Archive -Path (Join-Path $applicationDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal

if ($PortableOnly) {
    return [pscustomobject]@{
        PortableArchive = $portableArchive
        Installer = $null
        PublishDirectory = $applicationDirectory
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $compilerCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoSetupCompiler = $compilerCandidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or
    -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 is required to create the Windows installer.'
}

$installerScript = Join-Path $PSScriptRoot 'OpenSorSe.iss'
& $InnoSetupCompiler "/DAppVersion=$Version" "/DAppSource=$applicationDirectory" "/DOutputDirectory=$outputRoot" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installer = Join-Path $outputRoot "OmniSorSe-v$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw 'The Windows installer was not produced at the expected path.'
}

[pscustomobject]@{
    PortableArchive = $portableArchive
    Installer = $installer
    PublishDirectory = $applicationDirectory
}
