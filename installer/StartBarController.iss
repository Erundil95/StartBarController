; Inno Setup script for Start Bar Controller.
; Build: ISCC.exe /DAppVersion=0.9.0 /DSourceDir=..\out\standalone installer\StartBarController.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\standalone"
#endif

[Setup]
AppId={{6F3C2B9E-4A1D-4C8E-9B57-2D8A1E5F0C73}
AppName=Start Bar Controller
AppVersion={#AppVersion}
AppVerName=Start Bar Controller {#AppVersion}
AppPublisher=Start Bar Controller contributors
AppPublisherURL=https://github.com/Erundil95/StartBarController
DefaultDirName={autopf}\Start Bar Controller
DefaultGroupName=Start Bar Controller
DisableProgramGroupPage=yes
; Installs for the current user without admin rights; "Install for all users" is offered as an option.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out\installer
OutputBaseFilename=StartBarController-Setup-{#AppVersion}
SetupIconFile=..\src\StartBarController.App\Assets\app.ico
UninstallDisplayIcon={app}\StartBarController.exe
UninstallDisplayName=Start Bar Controller
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes

[Tasks]
Name: "autostart"; Description: "Start with Windows (in the notification area)"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\StartBarController.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Start Bar Controller"; Filename: "{app}\StartBarController.exe"
Name: "{autodesktop}\Start Bar Controller"; Filename: "{app}\StartBarController.exe"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" switch writes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "StartBarController"; \
  ValueData: """{app}\StartBarController.exe"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\StartBarController.exe"; Description: "Open Start Bar Controller"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Puts the taskbar back the way it was before the app changed it, and removes the autostart entry.
Filename: "{app}\StartBarController.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreTaskbar"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\StartBarController"
