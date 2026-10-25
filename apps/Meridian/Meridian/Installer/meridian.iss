; Meridian Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output of apps/Meridian/Meridian,
; plus the meridian CLI so `meridian run` works from a terminal after install.
; Compiled by CI (.github/workflows/meridian-installer.yml):
;   ISCC.exe /DAppVersion=<x.y.z> meridian.iss
; or locally, after
;   dotnet publish apps/Meridian/Meridian -c Release -r win-x64
;   dotnet publish apps/Meridian/Meridian.Cli -c Release -r win-x64

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net10.0\win-x64\publish"
#endif
#ifndef CliPublishDir
  #define CliPublishDir "..\..\Meridian.Cli\bin\Release\net10.0\win-x64\publish"
#endif

[Setup]
AppId={{B2E5C8D1-4F7A-4E63-9C21-7A6D3F9E5B84}
AppName=Meridian
AppVersion={#AppVersion}
AppPublisher=Fredrik Hult
AppPublisherURL=https://github.com/fredrikhu/hultui
DefaultDirName={autopf}\Meridian
; Per-user install by default (no UAC prompt — sensible for an unsigned
; installer; {autopf} then resolves to %LocalAppData%\Programs). The dialog
; still lets the user elevate for an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Meridian-Setup-{#AppVersion}
SetupIconFile=..\Assets\meridian.ico
UninstallDisplayIcon={app}\Meridian.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; The CLI is the pipeline half of the product, so offer it on PATH — but as a
; choice: putting a binary called "meridian" on someone's PATH without asking
; is not a decision an installer gets to make silently.
Name: "addtopath"; Description: "Add the meridian CLI to PATH"; GroupDescription: "Command line"

[Files]
; The publish output is one AOT exe plus native libraries (SDL3, Skia,
; HarfBuzz); install the exe under the product name. *.dll deliberately skips
; the large AOT/native .pdb files sitting next to them.
Source: "{#PublishDir}\Meridian.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
; Program.cs loads Assets\meridian.png (window icon) relative to the exe.
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs
; The CLI: same engine, no window.
Source: "{#CliPublishDir}\meridian.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Meridian"; Filename: "{app}\Meridian.exe"
Name: "{autodesktop}\Meridian"; Filename: "{app}\Meridian.exe"; Tasks: desktopicon

[Registry]
; Per-user PATH only — matching the per-user install default. An all-users
; install writes the same value under the user who ran it, which is the
; conservative choice for an unsigned installer.
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; Tasks: addtopath; \
    Check: NeedsAddPath(ExpandConstant('{app}'))

[Run]
Filename: "{app}\Meridian.exe"; Description: "{cm:LaunchProgram,Meridian}"; Flags: nowait postinstall skipifsilent

[Code]
{ Appending blindly would grow PATH by one entry per reinstall. }
function NeedsAddPath(Dir: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Path) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0;
end;
