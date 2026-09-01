#define AppName "Xiaobai Recorder"
#define AppVersion "1.0.0"
#define AppExeName "XiaobaiRecorder.exe"

[Setup]
AppId={{5C6A42C6-A978-46D3-9F71-A02C6F4CC9EA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\Xiaobai Recorder
DisableDirPage=no
AppendDefaultDirName=no
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\..\artifacts\packaging\installer
OutputBaseFilename=XiaobaiRecorder-Setup-{#AppVersion}
SetupIconFile=..\..\XbPreview.Host\Assets\XiaobaiLu.AppIcon.ico
VersionInfoVersion=1.0.0.0
VersionInfoProductVersion=1.0.0.0
VersionInfoDescription={#AppName} Setup
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=yes
DisableProgramGroupPage=auto
AllowNoIcons=no
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[Messages]
english.SelectLanguageTitle=选择安装语言 / Select Setup Language
zhcn.SelectLanguageTitle=选择安装语言 / Select Setup Language
english.SelectLanguageLabel=选择安装时使用的语言。%nChoose the language to use during installation.
zhcn.SelectLanguageLabel=选择安装时使用的语言。%nChoose the language to use during installation.
english.ButtonOK=OK
zhcn.ButtonOK=OK
english.ButtonCancel=Cancel
zhcn.ButtonCancel=Cancel

[Files]
Source: "..\..\artifacts\packaging\xiaobai-recorder-1.0.0\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\gstreamer\gio-modules"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{group}\Xiaobai Recorder"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Languages: english
Name: "{group}\小白录"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Languages: zhcn
Name: "{autodesktop}\Xiaobai Recorder"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon; Languages: english
Name: "{autodesktop}\小白录"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon; Languages: zhcn

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{#AppExeName}"; ValueType: dword; ValueName: "DumpType"; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\{#AppExeName}"; ValueType: dword; ValueName: "DumpCount"; ValueData: "3"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
