[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision,

    [Parameter()]
    [switch]$PortableOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$baseVersion = $Version.Split('-', 2)[0]
$fileVersion = "$baseVersion.0"

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows package validation must run on native Windows.'
}

$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$portableArchive = Join-Path $artifactRoot "OmniSorSe-v$Version-win-x64.zip"
$installer = Join-Path $artifactRoot "OmniSorSe-v$Version-win-x64-setup.exe"
$requiredArtifacts = @($portableArchive)
if (-not $PortableOnly) {
    $requiredArtifacts += $installer
}
foreach ($artifact in $requiredArtifacts) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Required Windows artifact is missing: $artifact"
    }
    if ((Get-Item -LiteralPath $artifact).Length -le 0) {
        throw "Required Windows artifact is empty: $artifact"
    }
}
if (-not $PortableOnly) {
    $installerVersionInfo = (Get-Item -LiteralPath $installer).VersionInfo
    if ($installerVersionInfo.FileVersion -ne $fileVersion -or
        $installerVersionInfo.ProductVersion -ne $Version -or
        $installerVersionInfo.ProductMajorPart -ne [int]$baseVersion.Split('.')[0] -or
        $installerVersionInfo.ProductMinorPart -ne [int]$baseVersion.Split('.')[1] -or
        $installerVersionInfo.ProductBuildPart -ne [int]$baseVersion.Split('.')[2] -or
        $installerVersionInfo.ProductPrivatePart -ne 0) {
        throw "Installer version metadata is inconsistent: file '$($installerVersionInfo.FileVersion)', product '$($installerVersionInfo.ProductVersion)'."
    }
}

$existingInstallation = Get-ChildItem -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Where-Object {
        $properties = Get-ItemProperty -LiteralPath $_.PSPath
        $properties.DisplayName -like 'OpenSorSe*' -or $properties.DisplayName -like 'OmniSorSe*'
    } |
    Select-Object -First 1
if ($null -ne $existingInstallation) {
    throw 'Windows package validation will not replace or uninstall an existing OpenSorSe or OmniSorSe installation.'
}

$validationRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot 'validation\windows'))
$expectedPrefix = $artifactRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $validationRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Windows validation directory escaped the artifact root.'
}
if (Test-Path -LiteralPath $validationRoot) {
    Remove-Item -LiteralPath $validationRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $validationRoot -Force | Out-Null

$portableRoot = Join-Path $validationRoot 'portable'
Expand-Archive -LiteralPath $portableArchive -DestinationPath $portableRoot
$portableExecutable = Join-Path $portableRoot 'OmniSorSe.exe'
if (-not (Test-Path -LiteralPath $portableExecutable -PathType Leaf)) {
    throw 'The Windows portable archive does not contain OmniSorSe.exe at its root.'
}
$versionInfo = (Get-Item -LiteralPath $portableExecutable).VersionInfo
if ($versionInfo.FileVersion -ne $fileVersion -or $versionInfo.ProductVersion -notlike "$Version*") {
    throw "OmniSorSe.exe version metadata is inconsistent: file '$($versionInfo.FileVersion)', product '$($versionInfo.ProductVersion)'."
}
$provenancePath = Join-Path $portableRoot 'OmniSorSe.build.json'
if (-not (Test-Path -LiteralPath $provenancePath -PathType Leaf)) {
    throw 'The portable package is missing its build provenance manifest.'
}
$provenance = Get-Content -Raw -LiteralPath $provenancePath | ConvertFrom-Json
if ($provenance.productVersion -ne $Version -or
    $provenance.baseVersion -ne $baseVersion -or
    $provenance.sourceRevision -ne $SourceRevision -or
    $provenance.configuration -ne 'Release' -or
    $provenance.targetFramework -ne 'net10.0' -or
    $provenance.runtimeIdentifier -ne 'win-x64' -or
    $provenance.runtimeVersion -notmatch '^10\.' -or
    $provenance.selfContained -ne $true -or
    $versionInfo.ProductVersion -notlike "$Version+$SourceRevision*") {
    throw 'Package filename, binary metadata, and build provenance do not identify the same source.'
}
if ($Version -ne $baseVersion) {
    $validationNoticePath = Join-Path $portableRoot 'VALIDATION_BUILD.md'
    if (-not (Test-Path -LiteralPath $validationNoticePath -PathType Leaf)) {
        throw 'The prerelease package is missing its validation-build notice.'
    }
    $validationNotice = Get-Content -Raw -LiteralPath $validationNoticePath
    if ($validationNotice -notlike "*OmniSorSe $Version validation build*" -or
        $validationNotice -notlike "*$SourceRevision*" -or
        $validationNotice -notlike '*not a published release*' -or
        $validationNotice -notlike '*unsigned*' -or
        $validationNotice -notlike '*disposable machine/profile*' -or
        $validationNotice -notlike '*can replace an existing OmniSorSe installation*' -or
        $validationNotice -notlike '*migrate the retained OpenSorSe profile and schema*') {
        throw 'The prerelease package validation notice does not match its version, source, or release boundary.'
    }
}
$runtimeConfiguration = Get-Content -Raw -LiteralPath (Join-Path $portableRoot 'OmniSorSe.runtimeconfig.json') | ConvertFrom-Json
if ($runtimeConfiguration.runtimeOptions.tfm -ne 'net10.0') {
    throw 'The Windows package runtime configuration is not net10.0.'
}
foreach ($runtimeAsset in @('coreclr.dll', 'hostfxr.dll', 'libSkiaSharp.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $portableRoot $runtimeAsset) -PathType Leaf)) {
        throw "The Windows package is missing required runtime/native asset '$runtimeAsset'."
    }
}

$forbidden = Get-ChildItem -LiteralPath $portableRoot -Recurse -Force -File | Where-Object {
    $_.Extension -in @('.pdb', '.trx', '.db', '.sqlite', '.log', '.oms-state', '.bak', '.cs', '.csproj', '.sln') -or
    $_.Name -match '(?i)\.(db-wal|db-shm)$' -or
    [IO.Path]::GetFileNameWithoutExtension($_.Name) -match '(?i)^(test-results?|secrets?|credentials?|tokens?|settings|operation-journal|change-plans?|saved-views?|recipes?)$'
}
if ($forbidden) {
    throw "Forbidden portable payload entries: $($forbidden.FullName -join ', ')"
}

if ($PortableOnly) {
    $portableSmokeRoot = Join-Path $validationRoot 'portable-user-data'
    $portableSmoke = Start-Process -FilePath $portableExecutable -ArgumentList @(
        '--package-smoke-test', ('"' + $portableSmokeRoot.Replace('"', '\"') + '"')
    ) -WorkingDirectory $portableRoot -WindowStyle Hidden -Wait -PassThru
    if ($portableSmoke.ExitCode -ne 0 -or
        -not (Test-Path -LiteralPath $portableSmokeRoot -PathType Container)) {
        throw "The Windows portable package smoke failed with exit code $($portableSmoke.ExitCode)."
    }

    return [pscustomobject]@{
        PortableExecutableVersion = $versionInfo.FileVersion
        RuntimeVersion = $provenance.runtimeVersion
        PackageSmokeExitCode = $portableSmoke.ExitCode
        InstallerValidation = 'Not requested'
    }
}

$installRoot = Join-Path $validationRoot 'installed'
$installerLog = Join-Path $validationRoot 'installer.log'
$quotedInstallRoot = '"' + $installRoot.Replace('"', '\"') + '"'
$quotedInstallerLog = '"' + $installerLog.Replace('"', '\"') + '"'
try {
$install = Start-Process -FilePath $installer -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$quotedInstallRoot",
    "/LOG=$quotedInstallerLog"
) -WindowStyle Hidden -Wait -PassThru
if ($install.ExitCode -ne 0) {
    throw "The Windows installer failed with exit code $($install.ExitCode)."
}

$installedExecutable = Join-Path $installRoot 'OmniSorSe.exe'
if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
    throw 'The installed application does not contain OmniSorSe.exe.'
}
$uninstaller = Get-ChildItem -LiteralPath $installRoot -Filter 'unins*.exe' -File | Select-Object -First 1
if ($null -eq $uninstaller) {
    throw 'The installed application has no registered Inno Setup uninstaller.'
}
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'OmniSorSe\OmniSorSe.lnk'
if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
    throw 'The Windows installer did not create the expected Start Menu shortcut.'
}
$uninstallRegistryPath = Get-ChildItem -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
    Where-Object {
        $properties = Get-ItemProperty -LiteralPath $_.PSPath
        $properties.DisplayName -eq "OmniSorSe $Version" -and
        $properties.DisplayVersion -eq $Version -and
        -not [string]::IsNullOrWhiteSpace($properties.InstallLocation) -and
        [IO.Path]::GetFullPath($properties.InstallLocation).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) -eq
            [IO.Path]::GetFullPath($installRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    } |
    Select-Object -First 1
if ($null -eq $uninstallRegistryPath) {
    throw 'The Windows installer did not create the expected per-user uninstall entry.'
}

$smokeDataRoot = Join-Path $validationRoot 'user-data'
$processInfo = [Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = $installedExecutable
$escapedSmokeDataRoot = $smokeDataRoot.Replace('"', '\"')
$processInfo.Arguments = "--package-smoke-test `"$escapedSmokeDataRoot`""
$processInfo.UseShellExecute = $false
$processInfo.WorkingDirectory = $installRoot
$smoke = [Diagnostics.Process]::Start($processInfo)
if ($null -eq $smoke -or -not $smoke.WaitForExit(60000)) {
    if ($null -ne $smoke -and -not $smoke.HasExited) {
        $smoke.Kill($true)
    }
    throw 'The installed application package smoke test did not finish within 60 seconds.'
}
if ($smoke.ExitCode -ne 0) {
    throw "The installed application package smoke test failed with exit code $($smoke.ExitCode)."
}
if (-not (Test-Path -LiteralPath $smokeDataRoot -PathType Container)) {
    throw 'The package smoke test did not use its isolated application-data root.'
}
$userDataMarker = Join-Path $smokeDataRoot 'preserve-on-uninstall.marker'
Set-Content -LiteralPath $userDataMarker -Value 'OmniSorSe user data is preserved by uninstall.' -Encoding UTF8

$uninstall = Start-Process -FilePath $uninstaller.FullName -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
) -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0) {
    throw "The Windows uninstaller failed with exit code $($uninstall.ExitCode)."
}
if (Test-Path -LiteralPath $installedExecutable) {
    throw 'The Windows uninstaller left application-owned installation files behind.'
}
if (-not (Test-Path -LiteralPath $userDataMarker -PathType Leaf)) {
    throw 'The Windows uninstaller removed application data that policy requires preserving.'
}
if (Test-Path -LiteralPath $startMenuShortcut) {
    throw 'The Windows uninstaller left its Start Menu shortcut behind.'
}
if (Test-Path -LiteralPath $uninstallRegistryPath.PSPath) {
    throw 'The Windows uninstaller left its uninstall entry behind.'
}

[pscustomobject]@{
    PortableExecutableVersion = $versionInfo.FileVersion
    InstallerExitCode = $install.ExitCode
    PackageSmokeExitCode = $smoke.ExitCode
    UninstallerExitCode = $uninstall.ExitCode
    StartMenuShortcutRemoved = $true
    UninstallEntryRemoved = $true
    UserDataPreserved = $true
}
}
catch {
    # A failed assertion must not leave the validation-only installation behind.
    $cleanupUninstaller = Get-ChildItem -LiteralPath $installRoot -Filter 'unins*.exe' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $cleanupUninstaller) {
        try {
            $cleanup = Start-Process -FilePath $cleanupUninstaller.FullName -ArgumentList @(
                '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
            ) -WindowStyle Hidden -Wait -PassThru
            if ($cleanup.ExitCode -ne 0) {
                Write-Warning "Validation cleanup uninstaller exited with code $($cleanup.ExitCode)."
            }
        }
        catch {
            Write-Warning "Validation cleanup could not run: $($_.Exception.Message)"
        }
    }
    throw
}
