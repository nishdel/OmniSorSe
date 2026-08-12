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

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Windows package validation must run on native Windows.'
}

$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$portableArchive = Join-Path $artifactRoot "OmniSorSe-v$Version-win-x64.zip"
$installer = Join-Path $artifactRoot "OmniSorSe-v$Version-win-x64-setup.exe"
foreach ($artifact in @($portableArchive, $installer)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Required Windows artifact is missing: $artifact"
    }
    if ((Get-Item -LiteralPath $artifact).Length -le 0) {
        throw "Required Windows artifact is empty: $artifact"
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
if ($versionInfo.FileVersion -ne "$Version.0" -or $versionInfo.ProductVersion -notlike "$Version*") {
    throw "OmniSorSe.exe version metadata is inconsistent: file '$($versionInfo.FileVersion)', product '$($versionInfo.ProductVersion)'."
}

$forbidden = Get-ChildItem -LiteralPath $portableRoot -Recurse -Force -File | Where-Object {
    $_.Extension -in @('.pdb', '.trx', '.db', '.log', '.cs', '.csproj', '.sln') -or
    [IO.Path]::GetFileNameWithoutExtension($_.Name) -match '(?i)^(test-results?|secrets?|credentials?|tokens?)$'
}
if ($forbidden) {
    throw "Forbidden portable payload entries: $($forbidden.FullName -join ', ')"
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
