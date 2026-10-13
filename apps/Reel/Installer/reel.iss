; Reel Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output, plus the trimmed LGPL libmpv the
; player decodes with. Compiled by CI (.github/workflows/reel-installer.yml):
;   ISCC.exe /DAppVersion=<x.y.z> reel.iss
; or locally, after
;   dotnet publish apps/Reel -c Release -r win-x64
; with libmpv-2.dll and its licence report copied into the publish directory.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\Release\net10.0\win-x64\publish"
#endif

[Setup]
AppId={{7C4B2E19-58A3-4F6D-9B21-0D5E8A31C7F4}
AppName=Reel
AppVersion={#AppVersion}
AppPublisher=Fredrik Hult
AppPublisherURL=https://github.com/fredrikhu/hultui-revamped
DefaultDirName={autopf}\Reel
; Per-user by default (no UAC prompt for an unsigned installer; {autopf} then
; resolves to %LocalAppData%\Programs). The dialog still offers an all-users
; install to anyone who elevates.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Reel-Setup-{#AppVersion}
SetupIconFile=..\Assets\reel.ico
UninstallDisplayIcon={app}\Reel.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Shipping libmpv is what brings LGPL-2.1 obligations with it; the report lists
; every component and its licence, and is installed beside the player.
LicenseFile={#PublishDir}\LICENCE-mpv.txt

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; Registering is an offer, not a takeover: it puts Reel under "Open with" and on
; the Settings page. Windows itself asks before anything becomes the default.
Name: "associate"; Description: "Offer Reel as a player for video files"; GroupDescription: "File types:"

[Files]
; One AOT exe plus the native libraries (SDL3, Skia, HarfBuzz) and libmpv. *.dll
; deliberately skips the large .pdb files sitting next to them.
Source: "{#PublishDir}\Reel.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\reel.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "{#PublishDir}\LICENCE-mpv.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Reel"; Filename: "{app}\Reel.exe"
Name: "{autodesktop}\Reel"; Filename: "{app}\Reel.exe"; Tasks: desktopicon

[Run]
; --quiet registers without opening Settings: an installer should not end by
; throwing the user into a control panel. The app offers that itself later.
Filename: "{app}\Reel.exe"; Parameters: "--register --quiet"; Tasks: associate; Flags: runhidden waituntilterminated
Filename: "{app}\Reel.exe"; Description: "{cm:LaunchProgram,Reel}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Leave the registry as it was found. Runs before the files go.
Filename: "{app}\Reel.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterReel"
