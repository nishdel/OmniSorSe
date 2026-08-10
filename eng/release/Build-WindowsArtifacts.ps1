[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '2.0.0',

    [Parameter()]
    [string]$OutputDirectory = '',

    [Parameter()]
    [string]$InnoSetupCompiler = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows release artifacts must be built on native Windows.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
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
$applicationDirectory = Join-Path $stagingRoot 'OpenSorSe'

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
)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\dependency-licenses.json') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\INSTALLATION.md') -Destination $applicationDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\RELEASE_NOTES_v2.0.0.md') -Destination (Join-Path $applicationDirectory 'RELEASE_NOTES.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\OpenSorSe.Desktop\Assets\opensorse-app-icon.ico') -Destination (Join-Path $applicationDirectory 'OpenSorSe.ico')

Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Force -File -Filter '*.pdb' |
    Remove-Item -Force

$executable = Join-Path $applicationDirectory 'OpenSorSe.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The self-contained Windows publish did not produce OpenSorSe.exe.'
}

$forbidden = Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Force -File | Where-Object {
    $_.Extension -in @('.pdb', '.trx', '.db', '.log', '.cs', '.csproj', '.sln') -or
    [IO.Path]::GetFileNameWithoutExtension($_.Name) -match '(?i)^(test-results?|secrets?|credentials?|tokens?)$'
}
if ($forbidden) {
    throw "Forbidden release payload entries: $($forbidden.FullName -join ', ')"
}

$portableArchive = Join-Path $outputRoot "OpenSorSe-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $portableArchive) {
    Remove-Item -LiteralPath $portableArchive -Force
}
Compress-Archive -Path (Join-Path $applicationDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal

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

$installer = Join-Path $outputRoot "OpenSorSe-v$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw 'The Windows installer was not produced at the expected path.'
}

[pscustomobject]@{
    PortableArchive = $portableArchive
    Installer = $installer
    PublishDirectory = $applicationDirectory
}
