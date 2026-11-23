; Bo cai dat Multi DepLao (Inno Setup 6). Goi tu build\package.ps1, khong chay tay truc tiep:
;   ISCC /DAppVersion=0.3.0 /DSourceDir=<app> /DGuideFile=<HUONG-DAN.txt> /DIconFile=<app.ico> /DOutDir=<dir> /DOutBase=<ten> installer.iss
; Cai cho user hien tai, khong can admin. Go cai dat KHONG xoa %LOCALAPPDATA%\MultiDepLao (du lieu tai khoan).

#ifndef AppVersion
  #error Thieu /DAppVersion
#endif
#ifndef DisclaimerFile
  #define DisclaimerFile AddBackslash(SourcePath) + "MIEN-TRU-TRACH-NHIEM.txt"
#endif
#ifndef PrivacyFile
  #define PrivacyFile AddBackslash(SourcePath) + "QUYEN-RIENG-TU.txt"
#endif
#define MyAppName "Multi DépLào"
#define MyAppExe "MultiDepLao.App.exe"

[Setup]
AppId={{6E0B7A52-3C1D-4F8E-9A27-5D4C1B8E2F90}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher=QuânOK
AppContact=okiemquan@gmail.com
AppCopyright=Copyright © 2026 QuânOK
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MultiDepLao
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutDir}
OutputBaseFilename={#OutBase}
SetupIconFile={#IconFile}
; Trang miễn trừ trách nhiệm: người dùng phải đồng ý mới cài được
LicenseFile={#DisclaimerFile}
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; App dang chay (mutex single-instance) -> Setup/Uninstall yeu cau nguoi dung tu dong app. Khong tu tat tien trinh nao.
AppMutex=Local\MultiDepLao.App.SingleInstance
CloseApplications=no
RestartApplications=no
UsedUserAreasWarning=no

[Languages]
; Tieng Viet neu co file ngon ngu (khong co trong ban chinh thuc cua Inno Setup), khong thi tieng Anh.
#if FileExists(AddBackslash(SourcePath) + "Vietnamese.isl")
Name: "vi"; MessagesFile: "{#AddBackslash(SourcePath)}Vietnamese.isl"
#elif FileExists(AddBackslash(CompilerPath) + "Languages\Vietnamese.isl")
Name: "vi"; MessagesFile: "compiler:Languages\Vietnamese.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#GuideFile}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#DisclaimerFile}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PrivacyFile}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#IconFile}"; DestDir: "{app}"; DestName: "app.ico"; Flags: ignoreversion

[Registry]
; Khoa dang ky thong bao toast (AUMID) do app tu ghi o HKCU; xoa khi go cai dat.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\MultiDepLao"; Flags: uninsdeletekey dontcreatekey

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

// Go cai dat: xoa muc "Khoi dong cung Windows" neu no tro vao thu muc cai nay (khong dung muc cua ban portable).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var cmd: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, 'MultiDepLao', cmd) then
      if Pos(Lowercase(AddBackslash(ExpandConstant('{app}'))), Lowercase(cmd)) > 0 then
        RegDeleteValue(HKCU, RunKey, 'MultiDepLao');
end;
