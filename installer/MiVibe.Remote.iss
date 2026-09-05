#ifndef MyAppVersion
  #define MyAppVersion "0.3.0-alpha.2"
#endif

#ifndef SourceRoot
  #error SourceRoot must point to the published application directory.
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

#define MyAppName "MiVibe Remote"
#define MyAppPublisher "Fanatical-Naturalist"
#define MyAppURL "https://github.com/Fanatical-Naturalist/MiVibe-Remote"
#define MyAppExeName "MiVibe.Remote.Tray.exe"
#define MyAppMutexes "Local\MiVibe.Remote.Tray-2717-32B8,Local\MiVibe.Remote.GattProbe-2717-32B8,Local\MiVibe.Remote.KeyBridge-2717-32B8"

[Setup]
AppId=MiVibe.Remote.2717.32B8
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoDescription={#MyAppName} hardware preview installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=0.3.0.0
VersionInfoVersion=0.3.0.0
DefaultDirName={autopf}\MiVibe Remote
DefaultGroupName=MiVibe Remote
DisableProgramGroupPage=yes
PrivilegesRequired=admin
MinVersion=10.0.26100
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=MiVibe-Remote-Setup-{#MyAppVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
AppMutex={#MyAppMutexes}
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
InfoBeforeFile=..\docs\QUICK_START.md

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "keymapping"; Description: "Apply the required remote key mapping (administrator approval and a Windows restart are required)"; GroupDescription: "Remote keys:"; Flags: unchecked

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MiVibe Remote"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-status-window"
Name: "{group}\Quick Start"; Filename: "{app}\QUICK_START.md"
Name: "{group}\Enable Remote Key Mapping"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\keyboard-remap\Enable-SplitVoiceRemap.ps1"""; WorkingDir: "{app}"; IconFilename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"
Name: "{group}\Disable Remote Key Mapping"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\keyboard-remap\Disable-VoiceRemap.ps1"""; WorkingDir: "{app}"; IconFilename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"
Name: "{group}\MiVibe Remote on GitHub"; Filename: "{#MyAppURL}"
Name: "{autodesktop}\MiVibe Remote"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--show-status-window"; Tasks: desktopicon

[Registry]
Root: HKLM64; Subkey: "SYSTEM\CurrentControlSet\Control\Keyboard Layout"; ValueType: binary; ValueName: "Scancode Map"; ValueData: "00 00 00 00 00 00 00 00 03 00 00 00 35 e0 5e e0 64 00 3f 00 00 00 00 00"; Tasks: keymapping; BeforeInstall: RememberKeyMappingState; AfterInstall: CaptureKeyMappingResult

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MiVibe Remote"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser; Check: CanLaunchAfterSetup

[Code]
const
  ScancodeMapSubkey =
    'SYSTEM\CurrentControlSet\Control\Keyboard Layout';
  ScancodeMapValueName = 'Scancode Map';

  SplitVoiceMapHex =
    '00000000000000000300000035E05EE064003F0000000000';
  F5ToNumpadDivideMapHex =
    '00000000000000000200000035E03F0000000000';
  LegacyF13MapHex =
    '00000000000000000200000064003F0000000000';

var
  KeyMappingWasCurrentBeforeRun: Boolean;
  KeyMappingExistedBeforeRun: Boolean;
  KeyMappingValueBeforeRun: AnsiString;
  KeyMappingNeedsInstallRollback: Boolean;
  KeyMappingChangedDuringInstall: Boolean;
  KeyMappingShouldBeRemovedDuringUninstall: Boolean;
  KeyMappingRemovedDuringUninstall: Boolean;
  SetupCompletedSuccessfully: Boolean;

function BinaryToHex(const Value: AnsiString): String;
var
  Index: Integer;
begin
  Result := '';
  for Index := 1 to Length(Value) do
    Result := Result + Format('%.2x', [Ord(Value[Index])]);
end;

function TryReadScancodeMapRaw(var RawValue: AnsiString): Boolean;
begin
  Result := RegQueryBinaryValue(
    HKLM64,
    ScancodeMapSubkey,
    ScancodeMapValueName,
    RawValue);
end;

function TryReadScancodeMap(var HexValue: String): Boolean;
var
  RawValue: AnsiString;
begin
  HexValue := '';
  Result := TryReadScancodeMapRaw(RawValue);

  if Result then
    HexValue := BinaryToHex(RawValue);
end;

function IsCurrentSplitVoiceMap: Boolean;
var
  HexValue: String;
begin
  Result :=
    TryReadScancodeMap(HexValue) and
    (CompareText(HexValue, SplitVoiceMapHex) = 0);
end;

function IsExactMiVibeMap: Boolean;
var
  HexValue: String;
begin
  Result :=
    TryReadScancodeMap(HexValue) and
    (
      (CompareText(HexValue, SplitVoiceMapHex) = 0) or
      (CompareText(HexValue, F5ToNumpadDivideMapHex) = 0) or
      (CompareText(HexValue, LegacyF13MapHex) = 0)
    );
end;

procedure RememberKeyMappingState;
var
  ExistingHex: String;
begin
  KeyMappingExistedBeforeRun :=
    TryReadScancodeMapRaw(KeyMappingValueBeforeRun);
  KeyMappingWasCurrentBeforeRun := False;

  if KeyMappingExistedBeforeRun then
  begin
    ExistingHex := BinaryToHex(KeyMappingValueBeforeRun);
    if (CompareText(ExistingHex, SplitVoiceMapHex) <> 0) and
       (CompareText(ExistingHex, F5ToNumpadDivideMapHex) <> 0) and
       (CompareText(ExistingHex, LegacyF13MapHex) <> 0) then
      RaiseException(
        'A third-party Windows Scancode Map appeared during installation. ' +
        'MiVibe Remote did not overwrite it.');

    KeyMappingWasCurrentBeforeRun :=
      CompareText(ExistingHex, SplitVoiceMapHex) = 0;
  end;

  KeyMappingNeedsInstallRollback := not KeyMappingWasCurrentBeforeRun;
end;

procedure CaptureKeyMappingResult;
begin
  if not IsCurrentSplitVoiceMap then
    RaiseException('Windows did not apply the MiVibe key mapping.');

  KeyMappingChangedDuringInstall :=
    (not KeyMappingWasCurrentBeforeRun) and
    IsCurrentSplitVoiceMap;
end;

function CanLaunchAfterSetup: Boolean;
begin
  Result := not KeyMappingChangedDuringInstall;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingHex: String;
begin
  Result := '';
  if not WizardIsTaskSelected('keymapping') then
    exit;

  { An unknown system-wide map may belong to another application. Stop before
    installation rather than overwrite it or report a false success. }
  if TryReadScancodeMap(ExistingHex) and (not IsExactMiVibeMap) then
    Result :=
      'A third-party Windows Scancode Map is already active. MiVibe Remote ' +
      'will not overwrite it. Go back and clear "Apply the required remote ' +
      'key mapping", or remove the conflicting mapping before installing.';
end;

function NeedRestart: Boolean;
begin
  Result := KeyMappingChangedDuringInstall;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
    SetupCompletedSuccessfully := True;
end;

procedure DeinitializeSetup;
var
  Restored: Boolean;
begin
  if SetupCompletedSuccessfully or
     (not KeyMappingNeedsInstallRollback) then
    exit;

  { Roll back only if the current value is still MiVibe's exact map. If another
    program replaced it while Setup was running, leave that value untouched. }
  if not IsCurrentSplitVoiceMap then
    exit;

  if KeyMappingExistedBeforeRun then
    Restored := RegWriteBinaryValue(
      HKLM64,
      ScancodeMapSubkey,
      ScancodeMapValueName,
      KeyMappingValueBeforeRun)
  else
    Restored := RegDeleteValue(
      HKLM64,
      ScancodeMapSubkey,
      ScancodeMapValueName);

  if not Restored then
  begin
    Log('ERROR: Failed to roll back the MiVibe key mapping after Setup failed.');
    if not WizardSilent then
      MsgBox(
        'Setup could not restore the previous Windows key mapping. ' +
        'Use the MiVibe key-mapping repair shortcut or contact support ' +
        'before restarting Windows.',
        mbError,
        MB_OK);
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  KeyMappingShouldBeRemovedDuringUninstall := False;
  KeyMappingRemovedDuringUninstall := False;

  { This must run before touching the key map because InitializeUninstall
    occurs before Inno's normal AppMutex check. }
  if CheckForMutexes('{#MyAppMutexes}') then
  begin
    if not UninstallSilent then
      MsgBox(
        'MiVibe Remote or its voice bridge is still running. ' +
        'Use Safe Exit from the tray window, then uninstall again.',
        mbError,
        MB_OK);
    Result := False;
    exit;
  end;

  { No map or a third-party map must never be changed. }
  if not IsExactMiVibeMap then
    exit;

  if (not UninstallSilent) and
     (MsgBox(
       'An exact MiVibe-managed system key mapping is active. ' +
       'Uninstall will restore the original Power and F5 keys, and ' +
       'Windows must then restart. Continue?',
       mbConfirmation,
       MB_YESNO) <> IDYES) then
  begin
    Result := False;
    exit;
  end;

  { Do not mutate the registry here. Inno's standard uninstall confirmation
    still follows InitializeUninstall, so the user must be able to cancel
    without changing the system key map. }
  KeyMappingShouldBeRemovedDuringUninstall := True;
end;

procedure InitializeUninstallProgressForm;
begin
  if not KeyMappingShouldBeRemovedDuringUninstall then
    exit;

  { Re-check immediately before changing the registry. If another program
    replaced the map after confirmation, leave that unknown map untouched. }
  if not IsExactMiVibeMap then
    exit;

  if not RegDeleteValue(
    HKLM64,
    ScancodeMapSubkey,
    ScancodeMapValueName) then
  begin
    if not UninstallSilent then
      MsgBox(
        'The exact MiVibe key mapping could not be removed. ' +
        'Uninstall was cancelled and no application files were removed.',
        mbError,
        MB_OK);
    RaiseException('Unable to remove the MiVibe key mapping.');
  end;

  if IsExactMiVibeMap then
  begin
    if not UninstallSilent then
      MsgBox(
        'The MiVibe key mapping is still present. Uninstall was cancelled.',
        mbError,
        MB_OK);
    RaiseException('The MiVibe key mapping is still present.');
  end;

  KeyMappingRemovedDuringUninstall := True;
end;

function UninstallNeedRestart: Boolean;
begin
  Result := KeyMappingRemovedDuringUninstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RegisteredCommand: String;
  InstalledCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '"';
    if RegQueryStringValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'MiVibe Remote',
      RegisteredCommand) then
    begin
      if CompareText(RegisteredCommand, InstalledCommand) = 0 then
        RegDeleteValue(
          HKCU,
          'Software\Microsoft\Windows\CurrentVersion\Run',
          'MiVibe Remote');
    end;
  end;
end;
