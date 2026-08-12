#ifndef AppVersion
  #define AppVersion "2.4.0"
#endif
#ifndef AppSource
  #error AppSource must identify the validated self-contained publish directory.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must identify the release staging directory.
#endif

[Setup]
AppId={{3F3BCA7E-38A1-45D3-B068-B22D25BCECF4}
AppName=OmniSorSe
AppVersion={#AppVersion}
AppVerName=OmniSorSe {#AppVersion}
AppPublisher=OmniSorSe contributors
AppPublisherURL=https://github.com/nishdel/OpenSorSe
AppSupportURL=https://github.com/nishdel/OpenSorSe/issues
AppUpdatesURL=https://github.com/nishdel/OpenSorSe/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=OmniSorSe contributors
VersionInfoDescription=OmniSorSe Windows installer
VersionInfoProductName=OmniSorSe
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\OpenSorSe
DefaultGroupName=OmniSorSe
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=OmniSorSe-v{#AppVersion}-win-x64-setup
SetupIconFile={#AppSource}\OmniSorSe.ico
UninstallDisplayIcon={app}\OmniSorSe.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
LicenseFile={#AppSource}\LICENSE

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; AppId and install directory intentionally remain stable. Remove v2.3 entrypoint files and
; its visible Start Menu group during an in-place rename upgrade, without touching user data.
Type: files; Name: "{app}\OpenSorSe.exe"
Type: files; Name: "{app}\OpenSorSe.dll"
Type: files; Name: "{app}\OpenSorSe.deps.json"
Type: files; Name: "{app}\OpenSorSe.runtimeconfig.json"
Type: filesandordirs; Name: "{userprograms}\OpenSorSe"

[Icons]
Name: "{group}\OmniSorSe"; Filename: "{app}\OmniSorSe.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\OmniSorSe.exe"; Description: "Launch OmniSorSe"; Flags: nowait postinstall skipifsilent
