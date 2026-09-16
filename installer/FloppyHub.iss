; ===========================================================================
;  Floppy Hub  -  Setup (Inno Setup 6)
; ===========================================================================
;  Baut FloppyHubSetup.exe: ein echter Installations-Assistent mit
;    * Willkommen- / Lizenz- / Zielordner- / Komponenten-Seite
;    * frei waehlbarem Installationsverzeichnis
;    * Startmenue- und Desktop-Verknuepfungen
;    * optionalem Autostart (versteckt, ueber StartLauncherHidden.vbs)
;    * sauberer Deinstallation (Programme & Features)
;
;  Bauen:
;      .\build\Build-Installer.ps1
;  oder direkt:
;      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\FloppyHub.iss
;
;  Alle Skripte finden ihren eigenen Ordner ueber $PSScriptRoot - es muss also
;  NICHTS an Pfaden angepasst werden, egal wohin installiert wird.
; ===========================================================================

#define AppName        "Floppy Hub"
#define AppVersion     "1.0.0"
#define AppPublisher   "MaelVW"
#define AppURL         "https://github.com/MaelVW/Floppy"
#define AppExeName     "FloppyHub.ps1"
#define SrcDir         ".."

[Setup]
AppId={{8E2A6D31-4C57-4F9B-9E1C-FL0PPYHUB0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
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
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\FloppyHub.ps1
LicenseFile=..\LICENSE.txt
InfoBeforeFile=..\installer\vorher.txt
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.TypeFull=Vollstaendige Installation (empfohlen)
de.TypeCustom=Benutzerdefiniert
en.TypeFull=Full installation (recommended)
en.TypeCustom=Custom
de.CompCore=Programmdateien (erforderlich)
de.CompExamples=Beispiel-Disketten (game.txt-Vorlagen)
de.CompDocs=Dokumentation
de.TaskAutostart=Floppy Launcher beim Anmelden automatisch starten (versteckt)
de.TaskDesktop=Desktop-Verknuepfung fuer das Floppy Hub Menue anlegen
de.TaskStartNow=Floppy Launcher nach der Installation sofort starten
de.RunHub=Floppy Hub Menue oeffnen
de.OpenExamples=Beispiel-Disketten oeffnen
en.CompCore=Program files (required)
en.CompExamples=Example floppies (game.txt templates)
en.CompDocs=Documentation
en.TaskAutostart=Start Floppy Launcher at sign-in (hidden)
en.TaskDesktop=Create a desktop shortcut for the Floppy Hub menu
en.TaskStartNow=Start Floppy Launcher right after installing
en.RunHub=Open the Floppy Hub menu
en.OpenExamples=Open example floppies

; "full" steht zuerst und ist damit die Vorgabe - auch bei /VERYSILENT.
; Ohne [Types] wuerde eine unbeaufsichtigte Installation GAR NICHTS auswaehlen.
[Types]
Name: "full";   Description: "{cm:TypeFull}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom

[Components]
Name: "core";     Description: "{cm:CompCore}";     Types: full custom; Flags: fixed
Name: "examples"; Description: "{cm:CompExamples}"; Types: full
Name: "docs";     Description: "{cm:CompDocs}";     Types: full

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "Start:"
Name: "desktop";   Description: "{cm:TaskDesktop}";   GroupDescription: "Verknuepfungen:"; Flags: unchecked

[Files]
; --- Kern ---
Source: "{#SrcDir}\FloppyLauncher.ps1";        DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\FloppyInterface.ps1";       DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\FloppyHub.ps1";             DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\FloppyDisc.ps1";            DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\FloppyLib.ps1";             DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\StartLauncherHidden.vbs";   DestDir: "{app}"; Components: core; Flags: ignoreversion
Source: "{#SrcDir}\Stop-FloppyLauncher.ps1";   DestDir: "{app}"; Components: core; Flags: ignoreversion

; Konfiguration: vorhandene NICHT ueberschreiben (Einstellungen bleiben).
Source: "{#SrcDir}\FloppyLauncher.ini";        DestDir: "{app}"; Components: core; Flags: onlyifdoesntexist uninsneveruninstall
; Bibliothek: ebenfalls Nutzerdaten - nur anlegen, nie ueberschreiben/loeschen.
Source: "{#SrcDir}\library.csv";               DestDir: "{app}"; Components: core; Flags: onlyifdoesntexist uninsneveruninstall

; --- Beispiele ---
Source: "{#SrcDir}\beispiele\*";               DestDir: "{app}\beispiele"; Components: examples; Flags: ignoreversion recursesubdirs createallsubdirs

; --- Doku ---
Source: "{#SrcDir}\README-FloppyHub.md";       DestDir: "{app}"; Components: docs; Flags: ignoreversion
Source: "{#SrcDir}\LICENSE.txt";               DestDir: "{app}"; Components: docs; Flags: ignoreversion

[Icons]
Name: "{group}\Floppy Hub";                  Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; WorkingDir: "{app}"; Comment: "{cm:RunHub}"
Name: "{group}\Floppy Launcher starten";     Filename: "{sys}\wscript.exe";  Parameters: """{app}\StartLauncherHidden.vbs"""; WorkingDir: "{app}"
Name: "{group}\Floppy Launcher beenden";     Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Stop-FloppyLauncher.ps1"""; WorkingDir: "{app}"
Name: "{group}\Konfiguration bearbeiten";    Filename: "notepad.exe";        Parameters: """{app}\FloppyLauncher.ini"""; WorkingDir: "{app}"
Name: "{group}\Beispiel-Disketten";          Filename: "{app}\beispiele";    Components: examples; Comment: "{cm:OpenExamples}"
Name: "{group}\Installationsordner";         Filename: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Floppy Hub";            Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; WorkingDir: "{app}"; Tasks: desktop

[Registry]
; Autostart pro Benutzer - der Name "Floppy Launcher" bleibt bewusst gleich.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "Floppy Launcher"; \
    ValueData: """{sys}\wscript.exe"" ""{app}\StartLauncherHidden.vbs"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{sys}\wscript.exe"; Parameters: """{app}\StartLauncherHidden.vbs"""; \
    Description: "{cm:TaskStartNow}"; Flags: postinstall nowait skipifsilent
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\FloppyHub.ps1"""; \
    Description: "{cm:RunHub}"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
; Laufenden Launcher beenden, damit die Dateien geloescht werden koennen.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Stop-FloppyLauncher.ps1"" -Quiet"; \
    Flags: runhidden; RunOnceId: "StopFloppyLauncher"

[UninstallDelete]
; Laufzeitdateien, die erst nach der Installation entstehen.
Type: files;      Name: "{app}\FloppyLauncher.log"
Type: files;      Name: "{app}\FloppyLauncher.log.old"
Type: dirifempty; Name: "{app}\beispiele"
Type: dirifempty; Name: "{app}"

[Code]
{ Vor der Installation pruefen, ob PowerShell 5.1 vorhanden ist. }
function InitializeSetup(): Boolean;
var
  PsPath: String;
begin
  PsPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  if not FileExists(PsPath) then
  begin
    MsgBox('Windows PowerShell 5.1 wurde nicht gefunden:' + #13#10 + PsPath + #13#10 + #13#10 +
           'Floppy Hub benoetigt Windows PowerShell. Die Installation wird abgebrochen.',
           mbCriticalError, MB_OK);
    Result := False;
    exit;
  end;
  Result := True;
end;

{ Alten Autostart-Eintrag merken, BEVOR das Setup ihn ueberschreibt, und
  danach Bescheid sagen - sonst laufen am Ende zwei Launcher nebeneinander. }
var
  OldAutostart: String;

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
       (Pos(LowerCase(ExpandConstant('{app}')), LowerCase(OldAutostart)) = 0) then
      MsgBox('Hinweis: Es gab bereits einen Autostart-Eintrag "Floppy Launcher", der auf einen ' +
             'anderen Ordner zeigte. Er wurde durch den neuen ersetzt.' + #13#10 + #13#10 +
             'Alter Eintrag:' + #13#10 + OldAutostart, mbInformation, MB_OK);
  end;
end;

{ Beim Deinstallieren bleiben Einstellungen und Bibliothek absichtlich liegen.
  Statt sie kommentarlos zurueckzulassen, wird einmal nachgefragt.
  Bei einer unbeaufsichtigten Deinstallation wird NICHT gefragt und NICHTS
  geloescht - Nutzerdaten verschwinden nie ungefragt. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then exit;
  if UninstallSilent then exit;

  Dir := ExpandConstant('{app}');
  if not (FileExists(Dir + '\library.csv') or FileExists(Dir + '\FloppyLauncher.ini')) then exit;

  if MsgBox('Deine Einstellungen (FloppyLauncher.ini) und die Disketten-Bibliothek ' +
            '(library.csv) wurden behalten - so sind sie nach einer Neuinstallation ' +
            'wieder da.' + #13#10 + #13#10 +
            'Sollen diese Dateien jetzt ebenfalls geloescht werden?',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DeleteFile(Dir + '\library.csv');
    DeleteFile(Dir + '\FloppyLauncher.ini');
    DeleteFile(Dir + '\FloppyLauncher.log');
    DeleteFile(Dir + '\FloppyLauncher.log.old');
    RemoveDir(Dir);
  end;
end;
