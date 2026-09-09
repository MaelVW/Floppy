# Diskette bespielen — was erlaubt ist und was nicht

Auf jeder Diskette liegt (optional) eine **Referenzdatei** im Wurzelverzeichnis.
Standardnamen: `game.txt`, `floppy.txt`, `launch.txt`.
Eine Angabe pro Zeile. `#` oder `;` am Zeilenanfang = Kommentar.

Die fertigen Vorlagen im Ordner `beispiele\` einfach als `game.txt` auf die
Diskette kopieren und anpassen.

---

## ✅ Erlaubt

| Zeile | Wirkung |
|---|---|
| `id=220` | startet `steam://rungameid/220` |
| `steam=220` / `steamid=220` / `gameid=220` | dasselbe wie `id=` |
| `220` (nur die Zahl, eigene Zeile) | wird als Steam-ID gelesen |
| `run=game.exe` | führt `A:\game.exe` aus (Datei liegt auf der Diskette) |
| `run=ordner\game.exe` | Unterordner auf der Diskette ist ok |
| `exe=game.exe` / `program=` / `path=` | Alias für `run=` |
| `pcrun=D:\Spiele\game.exe` | startet ein Programm auf dem PC — **mit J/N-Rückfrage** |
| `localrun=` / `pcexe=` | Alias für `pcrun=` |
| `hub=1` | öffnet das Floppy Hub Menü |
| `hubmenu=1` / `menu=1` / `floppyhub=1` | Alias für `hub=` |
| `args=-fullscreen -novid` | Startargumente, zusätzlich zu `run=` / `pcrun=` |

**Keine Referenzdatei?** Dann sucht der Launcher auf der Diskette nach
ausführbaren Dateien:

- genau **eine** `.exe`/`.bat`/`.cmd` → wird direkt gestartet
- **mehrere** → das Interface-Fenster fragt, welche

---

## ❌ Wird abgelehnt (mit Eintrag im Log)

| Zeile | Warum |
|---|---|
| `run=C:\Spiele\game.exe` | absoluter Pfad — dafür ist `pcrun=` da |
| `run=..\..\Windows\notepad.exe` | zeigt aus der Diskette heraus (Traversal-Schutz) |
| `run=start.ps1` | `.ps1` ist keine erlaubte Endung (`.exe/.bat/.cmd`) |
| `pcrun=C:\Windows\System32\cmd.exe` | liegt unter `C:\Windows` — **immer gesperrt** |
| `pcrun=game.exe` | relativ — `pcrun=` braucht einen absoluten Pfad |
| `pcrun=D:\weg.exe` (Datei fehlt) | Datei existiert nicht |
| `id=abc` | Steam-ID muss aus Ziffern bestehen |

Zusätzlich einschränkbar über `FloppyLauncher.ini`:

```ini
[security]
blocked_roots = %SystemRoot%, C:\Users\Public
allowed_roots = D:\Spiele, %ProgramFiles%\Steam    ; wenn gesetzt: pcrun NUR von hier
```

---

## 🔒 Sicherheits-Kurzfassung

- **Steam-IDs** und **`run=`** (Datei liegt auf der Diskette) starten sofort,
  ohne Rückfrage — du kontrollierst den Disketteninhalt selbst.
- **`pcrun=`** und **mehrere gefundene EXE** öffnen immer erst das zweite
  Terminal mit einer **J/N-Abfrage**. Ohne Antwort: Abbruch nach `confirm_timeout`
  Sekunden (Standard 90).
- Läuft der Launcher **versteckt im Autostart** ohne bedienbares Fenster, wird
  bei `pcrun=` gar nicht gestartet (nur geloggt), bis jemand im Fenster `J`
  drücken kann. Mit `[security] non_interactive = true` entfällt die Frage
  komplett und solche Discs starten nie.
- Der **Autostart** gilt bewusst nur für `FloppyLauncher.ps1`. Der Hub und die
  Werkzeuge laufen nur auf Wunsch und belegen danach kein RAM / keine CPU.
