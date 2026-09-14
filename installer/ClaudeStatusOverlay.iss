; Instalator nakładki statusu Claude Code.
;
; Buduje go Build-Release.ps1 (albo workflow GitHuba), podając wersję i katalog
; z gotowymi plikami:
;
;     ISCC.exe /DAppVersion=2.1.0 /DSourceDir=..\publish\app installer\ClaudeStatusOverlay.iss
;
; Instalacja jest per-użytkownik (bez UAC): pliki lądują w
; %LOCALAPPDATA%\Programs\Claude Status Overlay, hooki dopisuje sam
; ClaudeStatusHook.exe, a wpis w "Aplikacje i funkcje" daje deinstalator.

#define AppName "Claude Status Overlay"
#define AppExeName "ClaudeStatusOverlay.exe"
#define HookExeName "ClaudeStatusHook.exe"
#define AppPublisher "Claude Status Overlay"
#define AppUrl "https://github.com/OWNER/REPO"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif

; .NET Desktop Runtime 10 - bez niego nakładka nie wystartuje
#define DotNetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{8E3C5A21-7B4D-4E7A-9F2C-1D6B0A5E4C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\Claude Status Overlay
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\publish
OutputBaseFilename=ClaudeStatusOverlay-Setup-{#AppVersion}
SetupIconFile=..\src\ClaudeStatus.Overlay\app.ico
WizardStyle=modern
WizardSizePercent=100
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=no
ShowLanguageDialog=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
; nakładka trzyma własne pliki otwarte - Restart Manager zamyka ją przed podmianą
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "autostart"; Description: "Uruchamiaj nakładkę przy starcie Windows"; GroupDescription: "Dodatkowo:"
Name: "cleanlegacy"; Description: "Usuń starą wersję PowerShellową (~\.claude\status-overlay)"; GroupDescription: "Dodatkowo:"; Check: LegacyInstallExists

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autostartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#HookExeName}"; Parameters: "--install"; StatusMsg: "Rejestrowanie hooków w Claude Code..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "Uruchom nakładkę"; Flags: nowait postinstall

[UninstallRun]
; najpierw zamknij działającą nakładkę, potem wypisz hooki z settings.json
Filename: "{app}\{#AppExeName}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopOverlay"
Filename: "{app}\{#HookExeName}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveHooks"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  DotNetMissing: Boolean;

{ Czy jest zainstalowany .NET Desktop Runtime 10 (katalog shared\Microsoft.WindowsDesktop.App\10.*) }
function DotNetDesktop10Installed: Boolean;
var
  Roots: TArrayOfString;
  I: Integer;
  Rec: TFindRec;
  Root: String;
begin
  Result := False;
  SetArrayLength(Roots, 2);
  Roots[0] := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  Roots[1] := ExpandConstant('{localappdata}\Microsoft\dotnet\shared\Microsoft.WindowsDesktop.App');

  for I := 0 to GetArrayLength(Roots) - 1 do
  begin
    Root := Roots[I];
    if not DirExists(Root) then
      Continue;
    if FindFirst(Root + '\*', Rec) then
    begin
      try
        repeat
          if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            if Copy(Rec.Name, 1, 3) = '10.' then
            begin
              Result := True;
              Exit;
            end;
        until not FindNext(Rec);
      finally
        FindClose(Rec);
      end;
    end;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
    WizardForm.StatusLabel.Caption := Format('Pobieranie .NET Desktop Runtime 10... %d%%', [(Progress * 100) div ProgressMax]);
  Result := True;
end;

{ Stara wersja PowerShellowa - pokazujemy zadanie sprzątania tylko, gdy naprawdę jest }
function LegacyInstallExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{%USERPROFILE}\.claude\status-overlay\ClaudeStatusOverlay.ps1'))
         or FileExists(ExpandConstant('{%USERPROFILE}\.claude\status-overlay\claude-status-hook.ps1'));
end;

function InitializeSetup: Boolean;
begin
  DotNetMissing := not DotNetDesktop10Installed;
  Result := True;
end;

{ Pobranie i cicha instalacja brakującego runtime'u - przed kopiowaniem plików }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Target: String;
begin
  Result := '';
  if not DotNetMissing then
    Exit;

  if MsgBox('Nakładka potrzebuje .NET Desktop Runtime 10, którego nie ma na tym komputerze.'#13#10#13#10
          + 'Pobrać go teraz z microsoft.com i zainstalować (ok. 55 MB)?', mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := 'Instalacja przerwana: brak .NET Desktop Runtime 10.';
    Exit;
  end;

  Target := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
  try
    DownloadTemporaryFile('{#DotNetUrl}', 'windowsdesktop-runtime.exe', '', @OnDownloadProgress);
  except
    Result := 'Nie udało się pobrać .NET Desktop Runtime 10: ' + GetExceptionMessage;
    Exit;
  end;

  if not Exec(Target, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
  begin
    Result := 'Nie udało się uruchomić instalatora .NET Desktop Runtime 10.';
    Exit;
  end;

  { 3010 = zainstalowano, wymagany restart }
  if (Code <> 0) and (Code <> 3010) then
    Result := 'Instalator .NET Desktop Runtime 10 zakończył się kodem ' + IntToStr(Code) + '.'
  else
    NeedsRestart := NeedsRestart or (Code = 3010);
end;

procedure RemoveLegacyInstall;
var
  LegacyDir: String;
  Startup: String;
begin
  LegacyDir := ExpandConstant('{%USERPROFILE}\.claude\status-overlay');
  Startup := ExpandConstant('{userstartup}\Claude Status Overlay.lnk');

  { skrót autostartu starej wersji odpalałby przy każdym logowaniu nakładkę PowerShellową }
  if FileExists(Startup) then
    DeleteFile(Startup);

  if DirExists(LegacyDir) then
    DelTree(LegacyDir, True, True, True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if WizardIsTaskSelected('cleanlegacy') then
      RemoveLegacyInstall;
end;
