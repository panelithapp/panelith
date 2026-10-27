; Panelith installer (Inno Setup 6).
;
; Build it with build\publish.ps1, which publishes the app and passes:
;   /DAppVersion=1.2.3        version shown in Add or remove programs
;   /DPublishDir=<folder>     the self-contained win-x64 publish output
;   /DSign                    sign setup and uninstaller with the "panelith" sign tool
;   /Spanelith=<command>      the sign tool command, with $f for the file
;
; The app installs per machine under Program Files, which standard users cannot
; write to. That matters: Panelith can start elevated at sign-in through a
; scheduled task, and an elevated program must never run from a folder a
; standard user could swap files into.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\app"
#endif

#define AppName "Panelith"
#define AppExe "Panelith.exe"
#define TaskName "Panelith"
; The name this app shipped under before; an install of it is upgraded in place.
#define PreviousName "RigGlass"

[Setup]
; Unchanged since RigGlass, so this installer upgrades an existing RigGlass install.
AppId={{6C0B3E52-4D7A-4E0B-9C61-2F3A9B8D7E14}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Bharath Vadlamannati
AppCopyright=Copyright (c) 2026 Bharath Vadlamannati
AppPublisherURL=https://github.com/panelithapp/panelith
AppSupportURL=https://github.com/panelithapp/panelith/issues
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
UsePreviousAppDir=no
UsePreviousGroup=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\src\Panelith.App\Assets\panelith.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=Panelith-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
#ifdef Sign
SignTool=panelith $f
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "pawnio"; Description: "Install the PawnIO driver, so CPU temperature, clocks and power can be read"; GroupDescription: "Hardware access:"; Check: not IsPawnIOInstalled

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Files from an older version that the new one no longer ships.
Type: filesandordirs; Name: "{app}\themes"
; An upgraded RigGlass install: its program folder and shortcuts.
Type: filesandordirs; Name: "{autopf}\{#PreviousName}"
Type: files; Name: "{autoprograms}\{#PreviousName}.lnk"
Type: files; Name: "{autodesktop}\{#PreviousName}.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\tools\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Installing the PawnIO driver..."; Tasks: pawnio; Flags: waituntilterminated
; Windows N editions ship without Media Foundation, which video screens need.
Filename: "{sys}\dism.exe"; Parameters: "/Online /Add-Capability /CapabilityName:Media.MediaFeaturePack~~~~0.0.1.0 /NoRestart /Quiet"; StatusMsg: "Adding the Windows Media Feature Pack for video screens..."; Check: IsMediaFoundationMissing; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; An in-app update passes /RELAUNCH=1. Restart through the start-up task when
; there is one, so a copy that runs elevated comes back elevated.
Filename: "{sys}\schtasks.exe"; Parameters: "/Run /TN ""{#TaskName}"""; Flags: runhidden nowait; Check: ShouldRelaunch and StartupTaskExists
Filename: "{app}\{#AppExe}"; Parameters: "--restarted"; Flags: nowait runasoriginaluser; Check: ShouldRelaunch and not StartupTaskExists

[UninstallDelete]
; The copy of yt-dlp that "Update downloader" keeps for this user.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\tools"

[UninstallRun]
; Remove start at sign-in in both forms the app creates, so nothing points at
; the deleted program afterwards.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#TaskName}"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""{#TaskName}"" /f"; Flags: runhidden; RunOnceId: "RemoveRunKey"

[Code]
function RunHidden(const FileName, Parameters: String): Integer;
begin
  if not Exec(ExpandConstant(FileName), Parameters, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Result := -1;
end;

function IsPawnIOInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO') or
    RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function ShouldRelaunch(): Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

function StartupTaskExists(): Boolean;
begin
  Result := RunHidden('{sys}\schtasks.exe', '/Query /TN "{#TaskName}"') = 0;
end;

function IsMediaFoundationMissing(): Boolean;
begin
  Result := not FileExists(ExpandConstant('{sys}\mfplat.dll'));
end;

procedure StopPanelith();
begin
  RunHidden('{sys}\taskkill.exe', '/IM {#AppExe} /F');
end;

{ The app moves the user's settings folder itself on first launch. Start at
  sign-in is carried across here, because only the installer can replace the
  old elevated task. }
procedure RemovePreviousApp();
var
  Command: String;
begin
  RunHidden('{sys}\taskkill.exe', '/IM {#PreviousName}.exe /F');
  Command := '"' + ExpandConstant('{app}\{#AppExe}') + '" --minimized';

  if RunHidden('{sys}\schtasks.exe', '/Query /TN "{#PreviousName}"') = 0 then
  begin
    RunHidden('{sys}\schtasks.exe', '/Delete /TN "{#PreviousName}" /F');
    RunHidden('{sys}\schtasks.exe', '/Create /TN "{#TaskName}" /TR "\"' + ExpandConstant('{app}\{#AppExe}') +
      '\" --minimized" /SC ONLOGON /RL HIGHEST /F');
  end;

  if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#PreviousName}') then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#PreviousName}');
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#TaskName}', Command);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopPanelith();
  RemovePreviousApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopPanelith();
  Result := True;
end;
