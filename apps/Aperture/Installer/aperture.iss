; Aperture Windows installer (Inno Setup 6).
;
; Packages the win-x64 Native AOT publish output, plus the trimmed LGPL libmpv it
; plays through. Compiled by CI (.github/workflows/aperture-installer.yml):
;   ISCC.exe /DAppVersion=<x.y.z> aperture.iss
; or locally, after
;   dotnet publish apps/Aperture/Aperture -c Release -r win-x64
; with libmpv-2.dll and its licence report copied into the publish directory.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\Aperture\bin\Release\net10.0\win-x64\publish"
#endif

[Setup]
AppId={{2F8D6A47-91C3-4B5E-8D7A-6C4B93E15A82}
AppName=Aperture
AppVersion={#AppVersion}
AppPublisher=Fredrik Hult
AppPublisherURL=https://github.com/fredrikhu/hultui-revamped
DefaultDirName={autopf}\Aperture
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Aperture-Setup-{#AppVersion}
SetupIconFile=..\Aperture\Assets\aperture.ico
UninstallDisplayIcon={app}\Aperture.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile={#PublishDir}\LICENCE-mpv.txt

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The AOT exe, the native libraries (SDL3, Skia, HarfBuzz, libmpv), and the PNG the
; window takes its runtime icon from. *.dll skips the .pdb files beside them.
Source: "{#PublishDir}\Aperture.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\aperture.png"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "{#PublishDir}\LICENCE-mpv.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Aperture"; Filename: "{app}\Aperture.exe"
Name: "{autodesktop}\Aperture"; Filename: "{app}\Aperture.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Aperture.exe"; Description: "{cm:LaunchProgram,Aperture}"; Flags: nowait postinstall skipifsilent
