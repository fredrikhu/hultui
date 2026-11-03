; Meridian Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output of apps/Meridian/Meridian,
; plus the meridian CLI so `meridian run` works from a terminal after install.
; The CLI goes in its own folder: Windows file names ignore case, so beside the
; app its meridian.exe IS Meridian.exe — installers up to 1.6.0 shipped only
; the CLI that way, under the app's shortcut.
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
; [Code] edits PATH; this tells Explorer, so a new terminal sees the change
; without signing out. The uninstaller does the same.
ChangesEnvironment=yes

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
; The CLI: same engine, no window. It needs no DLLs (Meridian.Core references
; nothing), and its own folder is the one PATH gets.
Source: "{#CliPublishDir}\meridian.exe"; DestDir: "{app}\cli"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Meridian"; Filename: "{app}\Meridian.exe"
Name: "{autodesktop}\Meridian"; Filename: "{app}\Meridian.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Meridian.exe"; Description: "{cm:LaunchProgram,Meridian}"; Flags: nowait postinstall skipifsilent

[Code]
{ Per-user PATH only — matching the per-user install default. An all-users
  install writes the same value under the user who ran it, which is the
  conservative choice for an unsigned installer. }
const
  EnvironmentKey = 'Environment';

{ Removes every entry naming Dir, compared without case or a trailing
  backslash, and says whether there was one. Path is left as it was if not. }
function RemovePathEntry(var Path: string; Dir: string): Boolean;
var
  Rest, Entry, Kept: string;
  I: Integer;
begin
  Result := False;
  Kept := '';
  Rest := Path;
  while Rest <> '' do
  begin
    I := Pos(';', Rest);
    if I = 0 then
      I := Length(Rest) + 1;
    Entry := Copy(Rest, 1, I - 1);
    Delete(Rest, 1, I);
    if CompareText(RemoveBackslashUnlessRoot(Entry), RemoveBackslashUnlessRoot(Dir)) = 0 then
      Result := True
    else
    begin
      if Kept <> '' then
        Kept := Kept + ';';
      Kept := Kept + Entry;
    end;
  end;
  if Result then
    Path := Kept;
end;

function HasPathEntry(Path, Dir: string): Boolean;
begin
  Result := RemovePathEntry(Path, Dir);
end;

// Leaves the cli folder on PATH exactly when AddCli asks for it, and the app
// folder never: installers up to 1.6.0 put the app folder there, where
// meridian now starts the window. Writes only on a change, so a reinstall
// keeps PATH's order.
procedure UpdatePath(AddCli: Boolean);
var
  Path, App, Cli: string;
  Changed: Boolean;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path) then
    Path := '';
  App := ExpandConstant('{app}');
  Cli := App + '\cli';
  Changed := RemovePathEntry(Path, App);
  if not AddCli then
    Changed := RemovePathEntry(Path, Cli) or Changed
  else if not HasPathEntry(Path, Cli) then
  begin
    if (Path <> '') and (Copy(Path, Length(Path), 1) <> ';') then
      Path := Path + ';';
    Path := Path + Cli;
    Changed := True;
  end;
  if Changed then
    RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Path);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    UpdatePath(WizardIsTaskSelected('addtopath'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    UpdatePath(False);
end;
