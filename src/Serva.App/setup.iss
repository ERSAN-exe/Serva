#define MyAppName "Serva"
#ifndef MyAppVersion
  #define MyAppVersion "0.3.0"
#endif
#define MyAppPublisher "ERSAN-exe"
#define MyAppExeName "Serva.exe"

#define BuildArch "ARM64"
#define BuildArch "x64"

#if BuildArch == "ARM64"
  #define MyRid "win-arm64"
#else
  #define MyRid "win-x64"
#endif

#define PublishDir "bin\" + BuildArch + "\Release\net10.0-windows10.0.26100.0\" + MyRid

[Setup]
AppId={{8F4B3C2A-1D5E-4A9B-8C7D-6E5F4A3B2C1D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
SetupIconFile=Assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ShowLanguageDialog=yes
UsePreviousLanguage=no

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Installer
OutputBaseFilename=Serva_Setup_v{#MyAppVersion}_{#MyRid}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64compatible arm64
ArchitecturesInstallIn64BitMode=x64compatible arm64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml,publish\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent