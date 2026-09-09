# Floppy Hub

Erweiterungen rund um **FloppyLauncher.ps1** (Disketten → physische Steam-Library).
Der Launcher selbst bleibt eine eigenständige Datei — der Autostart hängt an
**nichts** von hier.

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
