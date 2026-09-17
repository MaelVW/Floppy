; ===========================================================================
;  Floppy Hub  -  Setup (Inno Setup 6)
; ===========================================================================
;  Baut FloppyHubSetup-<Version>.exe: ein Installations-Assistent mit
;    * Varianten-Wahl: grafische APP (Floppy Hub + Motor) oder KONSOLE (V1)
;    * Willkommen- / Lizenz- / Zielordner- / Komponenten-Seite
;    * Startmenue- und Desktop-Verknuepfungen
;    * optionalem Autostart des Floppy Launchers
;    * sauberer Deinstallation (Programme & Features)
;
;  Beide Varianten teilen sich den Namen "Floppy Launcher" und dieselbe
;  Einzelinstanz-Sperre. Wechselt man die Variante, werden die Programmdateien
;  der anderen entfernt - Einstellungen und Bibliothek bleiben.
;
;  Bauen:
;      .\build\Build-Installer.ps1            (baut vorher die App: build\Build-App.ps1)
;
;  Unbeaufsichtigt:
;      FloppyHubSetup-x.exe /VERYSILENT /VARIANT=app      (oder /VARIANT=console)
; ===========================================================================

#define AppName        "Floppy Hub"
; Version: per Kommandozeile ueberschreibbar (ISCC /DAppVersion=1.2.3).
#ifndef AppVersion
  #define AppVersion   "2.0.0-beta.1"
#endif
; Windows-Dateiversion braucht Zahlen: "2.0.0-beta.1" -> "2.0.0"
#if Pos("-", AppVersion) > 0
  #define AppVersionNumeric Copy(AppVersion, 1, Pos("-", AppVersion) - 1)
#else
  #define AppVersionNumeric AppVersion
#endif
#define AppPublisher   "MaelVW"
#define AppURL         "https://github.com/MaelVW/Floppy"
; Quellordner: normal das Repo; der Build legt beim Signieren eine
; signierte Kopie an und uebergibt sie per /DSrcDir=... und /DAppDir=...
#ifndef SrcDir
  #define SrcDir       ".."
#endif
; Fertig gebaute App (build\Build-App.ps1)
#ifndef AppDir
  #define AppDir       "..\build\app-out"
#endif

[Setup]
AppId={{8E2A6D31-4C57-4F9B-9E1C-FL0PPYHUB0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersionNumeric}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; --- Zielordner: frei waehlbar, Vorgabe pro Benutzer (immer schreibbar) ---
DefaultDirName={autopf}\FloppyHub
DefaultGroupName={#AppName}
DisableDirPage=no
DisableProgramGroupPage=no
AllowNoIcons=yes

; Keine Adminrechte noetig -> Installation pro Benutzer.
; Waehlt der Nutzer Programme (x86)/Program Files, fragt Windows selbst nach.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=FloppyHubSetup-{#AppVersion}
SetupIconFile=..\app\src\FloppyHub.App\assets\app\icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={code:UninstallIcon}
LicenseFile=..\LICENSE.txt
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; laufende App/Launcher erkennen und vor dem Ersetzen schliessen
CloseApplications=yes

; --- Digitale Signatur -----------------------------------------------------
; Aktiv nur, wenn der Build mit /DSign und /Sfloppysign=... aufgerufen wird
; (build\Build-Installer.ps1 -Sign). Signiert Setup UND Deinstallationsprogramm.
#ifdef Sign
SignTool=floppysign
SignedUninstaller=yes
SignToolRetryCount=3
SignToolRetryDelay=2000
#endif

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"; InfoBeforeFile: "..\installer\vorher.txt"
Name: "en"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "..\installer\before-en.txt"

[CustomMessages]
de.TypeFull=Vollständige Installation (empfohlen)
de.TypeCustom=Benutzerdefiniert
en.TypeFull=Full installation (recommended)
en.TypeCustom=Custom
de.CompCore=Programmdateien (erforderlich)
de.CompExamples=Beispiel-Disketten (game.txt-Vorlagen)
de.CompDocs=Dokumentation
en.CompCore=Program files (required)
en.CompExamples=Example floppies (game.txt templates)
en.CompDocs=Documentation

de.VariantCaption=Variante wählen
de.VariantDescription=Welche Floppy-Hub-Variante möchtest du installieren?
de.VariantSubCaption=Beide Varianten starten eingelegte Disketten über den „Floppy Launcher“. Du kannst später jederzeit wechseln: Setup erneut ausführen und die andere Variante wählen – Einstellungen und Bibliothek bleiben erhalten.%n%nApp: Fenster mit Bibliothek, Bespielen, Laufwerke-Ansicht, Chat und Minispiel. Neue oder veränderte Programme werden einmal nachgefragt.%nKonsole: die bewährte PowerShell-Variante, genau wie Version 1.
de.VariantApp=Floppy Hub App – grafische Oberfläche (empfohlen)
de.VariantConsole=Konsole – klassische PowerShell-Variante (wie Version 1)
en.VariantCaption=Choose a variant
en.VariantDescription=Which Floppy Hub variant would you like to install?
en.VariantSubCaption=Both variants start inserted floppies through the “Floppy Launcher”. You can switch at any time: run setup again and pick the other variant – settings and library are kept.%n%nApp: a window with library, writing, drives view, chat and mini-game. New or changed programs are confirmed once.%nConsole: the proven PowerShell variant, exactly like version 1.
en.VariantApp=Floppy Hub app – graphical interface (recommended)
en.VariantConsole=Console – classic PowerShell variant (like version 1)

de.TaskGroupStart=Start:
de.TaskGroupIcons=Verknüpfungen:
de.TaskAutostart=Floppy Launcher beim Anmelden automatisch starten (unsichtbar)
de.TaskDesktop=Desktop-Verknüpfung für Floppy Hub anlegen
de.TaskStartNow=Floppy Launcher nach der Installation sofort starten
de.RunHub=Floppy Hub öffnen
de.IconHub=Floppy Hub
de.IconLauncherStart=Floppy Launcher starten
de.IconLauncherStop=Floppy Launcher beenden
de.IconConfig=Konfiguration bearbeiten
de.IconExamples=Beispiel-Disketten
de.IconFolder=Installationsordner
de.OpenExamples=Beispiel-Disketten öffnen
en.TaskGroupStart=Start:
en.TaskGroupIcons=Shortcuts:
en.TaskAutostart=Start Floppy Launcher automatically at sign-in (hidden)
en.TaskDesktop=Create a desktop shortcut for Floppy Hub
en.TaskStartNow=Start Floppy Launcher right after installing
en.RunHub=Open Floppy Hub
en.IconHub=Floppy Hub
en.IconLauncherStart=Start Floppy Launcher
en.IconLauncherStop=Stop Floppy Launcher
en.IconConfig=Edit configuration
en.IconExamples=Example floppies
en.IconFolder=Installation folder
en.OpenExamples=Open example floppies

de.NoPowerShell=Windows PowerShell 5.1 wurde nicht gefunden:%n%1%n%nDie Konsolen-Variante benötigt Windows PowerShell. Wähle die App-Variante oder installiere PowerShell.
de.OldAutostart=Hinweis: Es gab bereits einen Autostart-Eintrag „Floppy Launcher“, der auf einen anderen Ordner zeigte. Er wurde durch den neuen ersetzt.%n%nAlter Eintrag:%n%1
de.KeepUserData=Deine Einstellungen (FloppyLauncher.ini) und die Disketten-Bibliothek (library.csv) wurden behalten – so sind sie nach einer Neuinstallation wieder da.%n%nSollen diese Dateien jetzt ebenfalls gelöscht werden?
de.KeepAppData=Die App hat persönliche Daten gespeichert: Vertrauensliste, Chat-ID und Kontakte, Rekorde und eigene Level (%1).%n%nSollen diese ebenfalls gelöscht werden? Deine Chat-ID ist danach unwiederbringlich weg.
en.NoPowerShell=Windows PowerShell 5.1 was not found:%n%1%n%nThe console variant needs Windows PowerShell. Choose the app variant or install PowerShell.
en.OldAutostart=Note: there already was an autostart entry “Floppy Launcher” pointing to a different folder. It has been replaced by the new one.%n%nOld entry:%n%1
en.KeepUserData=Your settings (FloppyLauncher.ini) and the floppy library (library.csv) were kept – so they're back after reinstalling.%n%nDelete these files now as well?
en.KeepAppData=The app stored personal data: trust list, chat ID and contacts, records and your own levels (%1).%n%nDelete these as well? Your chat ID will be gone for good.

; "full" steht zuerst und ist damit die Vorgabe - auch bei /VERYSILENT.
[Types]
Name: "full";   Description: "{cm:TypeFull}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "core";     Description: "{cm:CompCore}";     Types: full custom; Flags: fixed
Name: "examples"; Description: "{cm:CompExamples}"; Types: full
Name: "docs";     Description: "{cm:CompDocs}";     Types: full

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:TaskGroupStart}"
Name: "desktop";   Description: "{cm:TaskDesktop}";   GroupDescription: "{cm:TaskGroupIcons}"; Flags: unchecked

[InstallDelete]
; Variante gewechselt: Programmdateien der anderen Variante entfernen (Nutzerdaten bleiben).
Type: files;          Name: "{app}\FloppyLauncher.ps1";      Check: IsApp
Type: files;          Name: "{app}\FloppyInterface.ps1";     Check: IsApp
Type: files;          Name: "{app}\FloppyHub.ps1";           Check: IsApp
Type: files;          Name: "{app}\FloppyDisc.ps1";          Check: IsApp
Type: files;          Name: "{app}\FloppyLib.ps1";           Check: IsApp
Type: files;          Name: "{app}\StartLauncherHidden.vbs"; Check: IsApp
Type: files;          Name: "{app}\Stop-FloppyLauncher.ps1"; Check: IsApp
Type: files;          Name: "{app}\FloppyLauncher.exe";      Check: IsConsole
Type: files;          Name: "{app}\FloppyHub.exe";           Check: IsConsole
Type: files;          Name: "{app}\FloppyHub.pck";           Check: IsConsole
Type: filesandordirs; Name: "{app}\data_FloppyHub_windows_x86_64"; Check: IsConsole

[Files]
; --- Konsole (V1) ---
Source: "{#SrcDir}\FloppyLauncher.ps1";        DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\FloppyInterface.ps1";       DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\FloppyHub.ps1";             DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\FloppyDisc.ps1";            DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\FloppyLib.ps1";             DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\StartLauncherHidden.vbs";   DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole
Source: "{#SrcDir}\Stop-FloppyLauncher.ps1";   DestDir: "{app}"; Components: core; Flags: ignoreversion; Check: IsConsole

; --- App: Motor (FloppyLauncher.exe) + Floppy Hub (Godot) ---
Source: "{#AppDir}\*";                         DestDir: "{app}"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsApp

; --- gemeinsam ---
; Konfiguration: vorhandene NICHT ueberschreiben (Einstellungen bleiben).
Source: "{#SrcDir}\FloppyLauncher.ini";        DestDir: "{app}"; Components: core; Flags: onlyifdoesntexist uninsneveruninstall
; Bibliothek: ebenfalls Nutzerdaten - nur anlegen, nie ueberschreiben/loeschen.
; Vorlage mit nur einem Beispiel (NICHT die library.csv aus dem Repo - die ist persoenlich).
Source: "{#SrcDir}\installer\library-template.csv"; DestDir: "{app}"; DestName: "library.csv"; Components: core; Flags: onlyifdoesntexist uninsneveruninstall

; --- Beispiele ---
Source: "{#SrcDir}\beispiele\*";               DestDir: "{app}\beispiele"; Components: examples; Flags: ignoreversion recursesubdirs createallsubdirs

; --- Doku ---
Source: "{#SrcDir}\README-FloppyHub.md";       DestDir: "{app}"; Components: docs; Flags: ignoreversion
Source: "{#SrcDir}\LICENSE.txt";               DestDir: "{app}"; Components: docs; Flags: ignoreversion

[Icons]
; --- App ---
Name: "{group}\{cm:IconHub}";               Filename: "{app}\FloppyHub.exe"; WorkingDir: "{app}"; Comment: "{cm:RunHub}"; Check: IsApp
Name: "{group}\{cm:IconLauncherStart}";     Filename: "{app}\FloppyLauncher.exe"; WorkingDir: "{app}"; Check: IsApp
Name: "{group}\{cm:IconLauncherStop}";      Filename: "{app}\FloppyLauncher.exe"; Parameters: "--stop"; WorkingDir: "{app}"; IconFilename: "{app}\FloppyLauncher.exe"; Check: IsApp
Name: "{autodesktop}\Floppy Hub";           Filename: "{app}\FloppyHub.exe"; WorkingDir: "{app}"; Tasks: desktop; Check: IsApp
; --- Konsole ---
Name: "{group}\{cm:IconHub}";               Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; WorkingDir: "{app}"; Comment: "{cm:RunHub}"; Check: IsConsole
Name: "{group}\{cm:IconLauncherStart}";     Filename: "{sys}\wscript.exe";  Parameters: """{app}\StartLauncherHidden.vbs"""; WorkingDir: "{app}"; Check: IsConsole
Name: "{group}\{cm:IconLauncherStop}";      Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Stop-FloppyLauncher.ps1"""; WorkingDir: "{app}"; Check: IsConsole
Name: "{autodesktop}\Floppy Hub";           Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; WorkingDir: "{app}"; Tasks: desktop; Check: IsConsole
; --- gemeinsam ---
Name: "{group}\{cm:IconConfig}";            Filename: "notepad.exe";        Parameters: """{app}\FloppyLauncher.ini"""; WorkingDir: "{app}"
Name: "{group}\{cm:IconExamples}";          Filename: "{app}\beispiele";    Components: examples; Comment: "{cm:OpenExamples}"
Name: "{group}\{cm:IconFolder}";            Filename: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

[Registry]
; Autostart pro Benutzer - der Name "Floppy Launcher" bleibt bewusst gleich (eine Variante ersetzt die andere).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Floppy Launcher"; \
    ValueData: """{app}\FloppyLauncher.exe"""; \
    Flags: uninsdeletevalue; Tasks: autostart; Check: IsApp
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Floppy Launcher"; \
    ValueData: """{sys}\wscript.exe"" ""{app}\StartLauncherHidden.vbs"""; \
    Flags: uninsdeletevalue; Tasks: autostart; Check: IsConsole

[Run]
; --- App ---
Filename: "{app}\FloppyLauncher.exe"; Description: "{cm:TaskStartNow}"; Flags: postinstall nowait skipifsilent; Check: IsApp
Filename: "{app}\FloppyHub.exe";      Description: "{cm:RunHub}";       Flags: postinstall nowait skipifsilent; Check: IsApp
; --- Konsole ---
Filename: "{sys}\wscript.exe"; Parameters: """{app}\StartLauncherHidden.vbs"""; \
    Description: "{cm:TaskStartNow}"; Flags: postinstall nowait skipifsilent; Check: IsConsole
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; \
    Description: "{cm:RunHub}"; Flags: postinstall nowait skipifsilent unchecked; Check: IsConsole

[UninstallRun]
; Laufenden Launcher beenden, damit die Dateien geloescht werden koennen.
Filename: "{app}\FloppyLauncher.exe"; Parameters: "--stop"; Flags: runhidden waituntilterminated; RunOnceId: "StopMotor"; Check: IsApp
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Stop-FloppyLauncher.ps1"" -Quiet -Folder ""{app}"""; \
    Flags: runhidden; RunOnceId: "StopFloppyLauncher"; Check: IsConsole

[UninstallDelete]
; Laufzeitdateien, die erst nach der Installation entstehen.
Type: files;      Name: "{app}\FloppyLauncher.log"
Type: files;      Name: "{app}\FloppyLauncher.log.old"
Type: dirifempty; Name: "{app}\beispiele"
Type: dirifempty; Name: "{app}"

[Code]
var
  VariantPage: TInputOptionWizardPage;
  OldAutostart: String;

function IsApp(): Boolean;
begin
  { im Deinstallationsprogramm gibt es keine Seite: an den Dateien erkennen }
  if VariantPage = nil then Result := FileExists(ExpandConstant('{app}\FloppyHub.exe'))
  else Result := VariantPage.SelectedValueIndex = 0;
end;

function IsConsole(): Boolean;
begin
  Result := not IsApp();
end;

function PowerShellPath(): String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function UninstallIcon(Param: String): String;
begin
  if IsApp() then Result := ExpandConstant('{app}\FloppyHub.exe')
  else Result := PowerShellPath();
end;

{ Variante: Seite nach der Lizenz. Vorgabe = bisher installierte Variante, sonst App.
  /VARIANT=app oder /VARIANT=console auf der Kommandozeile gewinnt. }
procedure InitializeWizard();
var
  Choice: String;
begin
  VariantPage := CreateInputOptionPage(wpLicense,
    CustomMessage('VariantCaption'), CustomMessage('VariantDescription'),
    CustomMessage('VariantSubCaption'), True, False);
  VariantPage.Add(CustomMessage('VariantApp'));
  VariantPage.Add(CustomMessage('VariantConsole'));

  Choice := LowerCase(GetPreviousData('Variant', 'app'));
  if ExpandConstant('{param:VARIANT|}') <> '' then
    Choice := LowerCase(ExpandConstant('{param:VARIANT|}'));
  if Choice = 'console' then VariantPage.SelectedValueIndex := 1
  else VariantPage.SelectedValueIndex := 0;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if IsApp() then SetPreviousData(PreviousDataKey, 'Variant', 'app')
  else SetPreviousData(PreviousDataKey, 'Variant', 'console');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = VariantPage.ID) and IsConsole() and not FileExists(PowerShellPath()) then
  begin
    MsgBox(FmtMessage(CustomMessage('NoPowerShell'), [PowerShellPath()]), mbCriticalError, MB_OK);
    Result := False;
  end;
end;

{ Vor dem Kopieren: laufende Launcher (beide Varianten) aus DIESEM Ordner beenden. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Dir: String;
  Code: Integer;
begin
  Result := '';
  if IsConsole() and not FileExists(PowerShellPath()) then
  begin
    Result := FmtMessage(CustomMessage('NoPowerShell'), [PowerShellPath()]);
    exit;
  end;

  Dir := WizardDirValue();
  if FileExists(Dir + '\FloppyLauncher.exe') then
  begin
    Exec(Dir + '\FloppyLauncher.exe', '--stop', Dir, SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(1500);   { der Motor prueft das Stop-Signal zwischen zwei Durchlaeufen }
  end;
  if FileExists(Dir + '\Stop-FloppyLauncher.ps1') and FileExists(PowerShellPath()) then
    Exec(PowerShellPath(), '-NoProfile -ExecutionPolicy Bypass -File "' + Dir + '\Stop-FloppyLauncher.ps1" -Quiet -Folder "' + Dir + '"',
         Dir, SW_HIDE, ewWaitUntilTerminated, Code);
end;

{ Alten Autostart-Eintrag merken, BEVOR das Setup ihn ueberschreibt, und
  danach Bescheid sagen - sonst laufen am Ende zwei Launcher nebeneinander. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    if not RegQueryStringValue(HKEY_CURRENT_USER,
             'Software\Microsoft\Windows\CurrentVersion\Run', 'Floppy Launcher', OldAutostart) then
      OldAutostart := '';
  end;

  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('autostart') and (OldAutostart <> '') and
       (Pos(LowerCase(ExpandConstant('{app}')), LowerCase(OldAutostart)) = 0) and not WizardSilent() then
      MsgBox(FmtMessage(CustomMessage('OldAutostart'), [OldAutostart]), mbInformation, MB_OK);
  end;
end;

{ Beim Deinstallieren bleiben Einstellungen, Bibliothek und App-Daten absichtlich liegen.
  Statt sie kommentarlos zurueckzulassen, wird einmal nachgefragt.
  Bei einer unbeaufsichtigten Deinstallation wird NICHT gefragt und NICHTS
  geloescht - Nutzerdaten verschwinden nie ungefragt. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir, AppData: String;
begin
  if CurUninstallStep <> usPostUninstall then exit;
  if UninstallSilent then exit;

  Dir := ExpandConstant('{app}');
  if FileExists(Dir + '\library.csv') or FileExists(Dir + '\FloppyLauncher.ini') then
  begin
    if MsgBox(CustomMessage('KeepUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DeleteFile(Dir + '\library.csv');
      DeleteFile(Dir + '\FloppyLauncher.ini');
      DeleteFile(Dir + '\FloppyLauncher.log');
      DeleteFile(Dir + '\FloppyLauncher.log.old');
      RemoveDir(Dir);
    end;
  end;

  AppData := ExpandConstant('{localappdata}\FloppyHub');
  if DirExists(AppData) then
  begin
    if MsgBox(FmtMessage(CustomMessage('KeepAppData'), [AppData]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(AppData, True, True, True);
  end;
end;
