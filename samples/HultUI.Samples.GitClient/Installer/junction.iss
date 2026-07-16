; Junction Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output of the GitClient sample.
; Compiled by CI (.github/workflows/junction-installer.yml):
;   ISCC.exe /DAppVersion=<x.y.z> junction.iss
; or locally, after
;   dotnet publish samples/HultUI.Samples.GitClient -c Release -r win-x64

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net10.0\win-x64\publish"
#endif

[Setup]
AppId={{6F34B795-A13D-4470-8AAE-F5C656D90EB9}
AppName=Junction
AppVersion={#AppVersion}
AppPublisher=Fredrik Hult
AppPublisherURL=https://github.com/fredrikhu/hului-revamped
DefaultDirName={autopf}\Junction
; Per-user install by default (no UAC prompt — sensible for an unsigned
; installer; {autopf} then resolves to %LocalAppData%\Programs). The dialog
; still lets the user elevate for an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Junction-Setup-{#AppVersion}
SetupIconFile=..\Assets\junction.ico
UninstallDisplayIcon={app}\Junction.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The publish output is one AOT exe plus native libraries (SDL3, Skia,
; HarfBuzz); install the exe under the product name. *.dll deliberately skips
; the large AOT/native .pdb files sitting next to them.
Source: "{#PublishDir}\HultUI.Samples.GitClient.exe"; DestDir: "{app}"; DestName: "Junction.exe"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; Program.cs loads Assets\junction.png (window icon) and the toolbar images
; relative to the exe.
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\Junction"; Filename: "{app}\Junction.exe"
Name: "{autodesktop}\Junction"; Filename: "{app}\Junction.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Junction.exe"; Description: "{cm:LaunchProgram,Junction}"; Flags: nowait postinstall skipifsilent
