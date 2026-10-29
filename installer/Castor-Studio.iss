#define AppName "Castor Studio"

#ifndef Version
  #define Version "undefined"
#endif

#define AppVersion Version
#define AppPublisher "Castor Team"
#define AppExeName "Castor-Studio.exe"

[Setup]
AppId={{A1B2C3D4-CASTOR-STUDIO-APP}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}

OutputDir=..\artifacts\installer
OutputBaseFilename=Castor-Studio-Setup-{#AppVersion}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

Compression=lzma2
SolidCompression=yes

WizardStyle=modern

PrivilegesRequired=admin

UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "..\artifacts\publish\*"; Excludes: "\ai"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Local AI worker (scripts\fetch-ai-worker.ps1), without models: it downloads them on first use.
; Listed on its own so that a publish without the worker fails to compile.
Source: "..\artifacts\publish\ai\*"; DestDir: "{app}\ai"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; GroupDescription: "Options supplémentaires:"

[Icons]
Name: "{group}\Castor"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Castor"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Lancer Castor"; Flags: nowait postinstall skipifsilent
