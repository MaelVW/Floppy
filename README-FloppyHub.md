# Floppy Hub

Aus Disketten wird eine physische Steam-Library.
Der Launcher selbst bleibt eine eigenständige Datei — der Autostart hängt an
**nichts** von hier.

## Installation

**Fertiges Setup:** [Releases](https://github.com/MaelVW/Floppy/releases/latest) →
`FloppyHubSetup-x.y.z.exe` herunterladen und ausführen. Der Assistent fragt nach
dem Zielordner und richtet auf Wunsch den versteckten Autostart ein.

> Windows/SmartScreen meldet bei unsignierten Setups „Der Computer wurde geschützt" —
> über *Weitere Informationen* → *Trotzdem ausführen*.

**Selbst bauen** (braucht [Inno Setup](https://jrsoftware.org/isdl.php)):

```bash
powershell -ExecutionPolicy Bypass -File .\build\Build-Installer.ps1
```

**Release veröffentlichen** — Tag pushen, GitHub Actions baut und hängt das Setup an:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

**Ohne Installation:** Repo klonen und `FloppyHub.ps1` direkt starten — alle
Skripte finden ihren eigenen Ordner über `$PSScriptRoot`, es muss nichts
angepasst werden.

## Dateien

| Datei | Was | Läuft wann |
|---|---|---|
| `FloppyLauncher.ps1` | Kern: überwacht `A:`, startet Steam-Spiel / EXE / Hub | Autostart, dauerhaft |
| `FloppyLauncher.ini` | Konfiguration (Laufwerk, Timeout, Sicherheitsordner, Theme, Easter Eggs). **Optional** | vom Launcher gelesen |
| `FloppyInterface.ps1` | Retro-**Bestätigungsfenster** (Skin). Fehlt es, nimmt der Launcher seine eingebaute Minimal-Version | als 2. Terminal bei `pcrun=` / Mehrfach-EXE |
| `FloppyHub.ps1` | Retro-**Menü**: Status, Bibliothek, Disc bespielen, Log, Config | nur auf Wunsch |
| `FloppyDisc.ps1` | Disc bespielen/inspizieren per **Kommandozeile** | nur auf Wunsch |
| `FloppyLib.ps1` | gemeinsame Funktionen für Hub + Disc-Tool (**nicht** vom Launcher genutzt) | dot-sourced |
| `library.csv` | Katalog bekannter Disketten | von Hub/Disc-Tool |
| `beispiele\` | fertige `game.txt`-Vorlagen + Regeln (erlaubt/verboten) | zum Kopieren |
| `StartLauncherHidden.vbs` | startet den Launcher **ohne** aufblitzendes Fenster | Autostart |
| `installer\FloppyHub.iss` | Inno-Setup-Skript für `FloppyHubSetup.exe` | beim Bauen |
| `build\Build-Installer.ps1` | baut das Setup lokal | manuell |
| `.github\workflows\release.yml` | baut + veröffentlicht das Setup auf GitHub | bei Tag `v*` |

### Wohin geschrieben wird

Log und `library.csv` liegen normalerweise im Programmordner. Ist der
schreibgeschützt (Installation nach `C:\Program Files`), weichen Launcher **und**
Hub automatisch auf `%LOCALAPPDATA%\FloppyHub` aus — beide auf denselben Pfad.

## Der Twist: den Hub per Diskette öffnen

Eine Diskette mit dieser `game.txt`:

```
hub=1
```

→ beim Einlegen startet der Launcher das Hub-Menü, genau wie er sonst ein Spiel
startet. (Manuell geht auch: `FloppyHub.ps1` direkt starten.)

Schnell erzeugen:

```
powershell -ExecutionPolicy Bypass -File .\FloppyDisc.ps1 -Hub
```

## Typische Befehle

```
# Was würde der Launcher mit der aktuellen Diskette tun?
powershell -ExecutionPolicy Bypass -File .\FloppyDisc.ps1 -Inspect

# Diskette für ein Steam-Spiel vorbereiten (+ in die Bibliothek)
powershell -ExecutionPolicy Bypass -File .\FloppyDisc.ps1 -Steam 220 -AddToLibrary

# Diskette für ein PC-Programm (fragt beim Einlegen nach)
powershell -ExecutionPolicy Bypass -File .\FloppyDisc.ps1 -PcRun "D:\Emu\dosbox\DOSBox.exe"

# Menü öffnen
powershell -ExecutionPolicy Bypass -File .\FloppyHub.ps1
```

## Konfiguration

`FloppyLauncher.ini` ist optional und gut kommentiert. Wichtigste Schalter:

```ini
[drive]        letter = A:            poll_seconds = 3
[security]     blocked_roots = %SystemRoot%   allowed_roots =   confirm_timeout = 90   non_interactive = false
[hub]          enabled = true
[ui]           theme = green          easter_eggs = true
```

Kommandozeilen-Parameter des Launchers haben Vorrang vor der INI.

## Easter Eggs

- **Interface-Fenster**: Feiertags-Themes (Weihnachten, Silvester, Halloween,
  Freitag der 13.), Boot-Flimmern, zwinkernde Diskette, Titel-Sprüche für
  bekannte Spiele, seltener „bit-rot"-Glitch. Rein optisch — die
  Sicherheitsabfrage bleibt unverändert. Abschaltbar: `[ui] easter_eggs = false`.
- **Hub-Menü**: tippe im Menü mal `wopr`, `xyzzy`, `42`, `konami`, `sudo` …

## Sicherheit / Regeln

Siehe [`beispiele/LIESMICH-erlaubt-und-verboten.md`](beispiele/LIESMICH-erlaubt-und-verboten.md).
