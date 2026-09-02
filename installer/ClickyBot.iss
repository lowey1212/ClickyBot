#define MyAppName "ClickyBot"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.18"
#endif
#define MyAppPublisher "ClickyBot"
#define MyAppExeName "ClickyBot.exe"
#define InstallerSource "..\artifacts\installer-app-win-x64"
#define InstallerOutput "..\dist"

[Setup]
AppId={{1D0F2D4F-2EB1-4D1F-9D42-0A54D1E19F6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ClickyBot
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir={#InstallerOutput}
OutputBaseFilename=ClickyBot-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#InstallerSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
