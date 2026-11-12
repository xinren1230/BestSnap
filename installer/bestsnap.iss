; BestSnap one-click installer (Inno Setup 6.5+).
; Payload is assembled by build.ps1 -Installer: self-contained BestSnap.exe,
; LICENSE/NOTICE, third-party notices, and pix2text-mfr models.
; Do not copy a plugins\ folder next to the exe. OCR + OnnxRuntime live inside
; the single-file publish; a partial plugins\ directory hides the embedded copy.
;
; AppId is the same GUID as the SnapForge 1.1.0 installer so Inno treats this as
; an upgrade: previous install is uninstalled/overwritten, Start Menu/desktop
; shortcuts are replaced, and the old SnapForge.exe is deleted.

#define MyAppName "BestSnap"
#define MyAppPublisher "Xinren Chen"
#define MyAppURL "https://github.com/xinren1230/BestSnap"
#define MyAppExeName "BestSnap.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\dist\payload"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{A7E3C4B1-9F2D-4E8A-B6C1-5D0F8A3E2B19}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.18362
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; LEGACY MIGRATION: same mutex as the WPF app (and the previous SnapForge.exe) so Setup can prompt to close either.
AppMutex=SnapForge_SingleInstance_Mutex
CloseApplications=yes
CloseApplicationsFilter=BestSnap.exe,SnapForge.exe
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
OutputDir={#OutputDir}
OutputBaseFilename=BestSnap-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
SetupLogging=yes
UsePreviousAppDir=yes
DisableDirPage=auto
LicenseFile={#PayloadDir}\LICENSE
InfoAfterFile={#PayloadDir}\NOTICE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Single-file self-contained exe (OCR plugin, OnnxRuntime, ScreenRecorderLib, PP-OCRv4 models).
Source: "{#PayloadDir}\BestSnap.exe"; DestDir: "{app}"; Flags: ignoreversion
; Whisper.net natives must sit next to the exe. The loader looks in {app}\runtimes\win-x64\,
; not the .NET single-file extract directory under %TEMP%\.net.
Source: "{#PayloadDir}\runtimes\win-x64\*"; DestDir: "{app}\runtimes\win-x64"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
; THIRD-PARTY-NOTICES.txt (PaddleOCR Apache-2.0, pix2text-mfr MIT, ONNX Runtime MIT, …)
Source: "{#PayloadDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
; Formula models for offline first launch. Incomplete plugins\ must never be created here.
Source: "{#PayloadDir}\models\pix2text-mfr\*"; DestDir: "{app}\models\pix2text-mfr"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; LEGACY MIGRATION: remove leftover SnapForge.exe / shortcuts from the previous product name.
Type: files; Name: "{app}\SnapForge.exe"
Type: files; Name: "{userprograms}\SnapForge.lnk"
Type: files; Name: "{commonprograms}\SnapForge.lnk"
Type: files; Name: "{userdesktop}\SnapForge.lnk"
Type: files; Name: "{commondesktop}\SnapForge.lnk"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{cm:AppComment}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{cm:AppComment}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\BestSnap\runtime"
; LEGACY MIGRATION: leftover JIT/runtime extract dir from SnapForge builds.
Type: filesandordirs; Name: "{localappdata}\SnapForge\runtime"

[CustomMessages]
english.AppComment=Offline OCR and formula to LaTeX
chinesesimplified.AppComment=离线 OCR 与公式转 LaTeX
