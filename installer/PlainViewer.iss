; Plain Viewer offline installer (Inno Setup 7). Build it with scripts\package.ps1, which passes the defines below.
; Per-user install, no administrator rights. Upgrades install over the previous version (same AppId).
#ifndef AppVersion
  #error Run scripts\package.ps1: it passes AppVersion, PublishDir, LibreOfficeDir and OutputDir.
#endif

[Setup]
; Never change AppId: Windows uses it to recognise upgrades and the uninstaller.
AppId={{3348245C-1DA7-4BEC-8072-123B6E9F1CD0}
AppName=Plain Viewer
AppVersion={#AppVersion}
AppVerName=Plain Viewer {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Plain Viewer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=PlainViewer-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ChangesAssociations=yes
; Close a running Plain Viewer (and its LibreOffice converter, soffice.bin) before files are replaced.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll,*.bin
RestartApplications=no
UninstallDisplayName=Plain Viewer
UninstallDisplayIcon={app}\PlainViewer.exe
SetupLogging=yes
; Code signing needs a certificate (a decision for Zain; see docs/RELEASING.md). With one, add a SignTool
; directive here and SignedUninstaller=yes, so Windows SmartScreen stops warning about an unknown publisher.

[Tasks]
Name: openwith; Description: "Add Plain Viewer to ""Open with"" for PDF, Word, Excel, PowerPoint, CSV, text and Markdown files (your default apps do not change)"
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Upgrades: remove the previous version's LibreOffice and page files first, so no stale files remain.
Type: filesandordirs; Name: "{app}\libreoffice"
Type: filesandordirs; Name: "{app}\Assets"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#LibreOfficeDir}\*"; DestDir: "{app}\libreoffice"; Excludes: "__pycache__,*.pyc"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Plain Viewer"; Filename: "{app}\PlainViewer.exe"
Name: "{autodesktop}\Plain Viewer"; Filename: "{app}\PlainViewer.exe"; Tasks: desktopicon

[Registry]
; "Open with" and Settings > Default apps. Only offers Plain Viewer; Windows lets the user choose defaults.
Root: HKA; Subkey: "Software\PlainViewer"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\PlainViewer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Plain Viewer"; Tasks: openwith
Root: HKA; Subkey: "Software\PlainViewer\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Read-only viewer for PDF, Word, Excel, PowerPoint, CSV, text and Markdown files"; Tasks: openwith
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Plain Viewer"; ValueData: "Software\PlainViewer\Capabilities"; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\PlainViewer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Plain Viewer"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\PlainViewer.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PlainViewer.exe"" ""%1"""; Tasks: openwith
#dim Ext[8]
#define Ext[0] "pdf"
#define Ext[1] "docx"
#define Ext[2] "xlsx"
#define Ext[3] "pptx"
#define Ext[4] "csv"
#define Ext[5] "txt"
#define Ext[6] "md"
#define Ext[7] "markdown"
#dim Kind[8]
#define Kind[0] "PDF document"
#define Kind[1] "Word document"
#define Kind[2] "Excel workbook"
#define Kind[3] "PowerPoint presentation"
#define Kind[4] "CSV file"
#define Kind[5] "Text document"
#define Kind[6] "Markdown document"
#define Kind[7] "Markdown document"
#define i
#sub FileType
Root: HKA; Subkey: "Software\Classes\.{#Ext[i]}\OpenWithProgids"; ValueType: string; ValueName: "PlainViewer.{#Ext[i]}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\PlainViewer.{#Ext[i]}"; ValueType: string; ValueName: ""; ValueData: "{#Kind[i]}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\PlainViewer.{#Ext[i]}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\PlainViewer.exe,0"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\PlainViewer.{#Ext[i]}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PlainViewer.exe"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\PlainViewer.exe\SupportedTypes"; ValueType: string; ValueName: ".{#Ext[i]}"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\PlainViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".{#Ext[i]}"; ValueData: "PlainViewer.{#Ext[i]}"; Tasks: openwith
#endsub
#for {i = 0; i < DimOf(Ext); i++} FileType

[Run]
; Builds the Word/PowerPoint converter's profile now, so the first document opens in seconds. If it fails,
; the app repeats it in the background on its next start.
Filename: "{app}\PlainViewer.exe"; Parameters: "--prepare-converter"; StatusMsg: "Preparing the Word and PowerPoint converter..."; Flags: runhidden waituntilterminated
Filename: "{app}\PlainViewer.exe"; Description: "{cm:LaunchProgram,Plain Viewer}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Plain Viewer's private data: converter profile, temporary work folders and the page view's cache.
; Never touches the user's documents.
Type: filesandordirs; Name: "{localappdata}\PlainViewer"

[Code]
// PDF, Word, Excel and PowerPoint views need the Microsoft Edge WebView2 Runtime, which Windows 11 includes.
// Registry check documented by Microsoft for the Evergreen runtime.
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
      and (Version <> '') and (Version <> '0.0.0.0'))
    or (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version)
      and (Version <> '') and (Version <> '0.0.0.0'));
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not WebView2Installed then
    Result := SuppressibleMsgBox('Plain Viewer needs the Microsoft Edge WebView2 Runtime to show PDF, Word, Excel and PowerPoint files. ' +
      'It is part of Windows 11 but is missing on this PC. Text, CSV and Markdown files will still open.' + #13#10#13#10 +
      'Install Plain Viewer anyway?', mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;
