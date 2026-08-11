#ifndef AppVersion
  #define AppVersion "2.2.0"
#endif
#ifndef AppSource
  #error AppSource must identify the validated self-contained publish directory.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must identify the release staging directory.
#endif

[Setup]
AppId={{3F3BCA7E-38A1-45D3-B068-B22D25BCECF4}
AppName=OpenSorSe
AppVersion={#AppVersion}
AppVerName=OpenSorSe {#AppVersion}
AppPublisher=OpenSorSe contributors
AppPublisherURL=https://github.com/nishdel/OpenSorSe
AppSupportURL=https://github.com/nishdel/OpenSorSe/issues
AppUpdatesURL=https://github.com/nishdel/OpenSorSe/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=OpenSorSe contributors
VersionInfoDescription=OpenSorSe Windows installer
VersionInfoProductName=OpenSorSe
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\OpenSorSe
DefaultGroupName=OpenSorSe
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=OpenSorSe-v{#AppVersion}-win-x64-setup
SetupIconFile={#AppSource}\OpenSorSe.ico
UninstallDisplayIcon={app}\OpenSorSe.exe
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

[Icons]
Name: "{group}\OpenSorSe"; Filename: "{app}\OpenSorSe.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\OpenSorSe.exe"; Description: "Launch OpenSorSe"; Flags: nowait postinstall skipifsilent
