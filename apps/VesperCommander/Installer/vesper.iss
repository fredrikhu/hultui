; Vesper Commander Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output of the VesperCommander app.
; Compiled by CI (.github/workflows/vesper-installer.yml):
;   ISCC.exe /DAppVersion=<x.y.z> vesper.iss
; or locally, after
;   dotnet publish apps/VesperCommander -c Release -r win-x64

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net10.0\win-x64\publish"
#endif

[Setup]
AppId={{B3F1E7C2-6A94-4D0E-9C5B-1E8A2F4D7C90}
AppName=Vesper Commander
AppVersion={#AppVersion}
AppPublisher=Fredrik Hult
AppPublisherURL=https://github.com/fredrikhu/hultui-revamped
DefaultDirName={autopf}\Vesper Commander
; Per-user install by default (no UAC prompt — sensible for an unsigned
; installer; {autopf} then resolves to %LocalAppData%\Programs). The dialog
; still lets the user elevate for an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=VesperCommander-Setup-{#AppVersion}
SetupIconFile=..\Assets\vesper.ico
UninstallDisplayIcon={app}\VesperCommander.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The publish output is one AOT exe plus native libraries (SDL3, Skia,
; HarfBuzz). *.dll deliberately skips the large AOT/native .pdb files sitting
; next to them. The app loads no external asset files (glyph-based icons).
Source: "{#PublishDir}\VesperCommander.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Vesper Commander"; Filename: "{app}\VesperCommander.exe"
Name: "{autodesktop}\Vesper Commander"; Filename: "{app}\VesperCommander.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VesperCommander.exe"; Description: "{cm:LaunchProgram,Vesper Commander}"; Flags: nowait postinstall skipifsilent
