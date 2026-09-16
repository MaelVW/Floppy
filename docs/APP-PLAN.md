# Floppy Hub App – Plan

Lebendes Dokument für die grafische Variante (v2). Stand: 2026-09-16, Branch `App`.

## Ziel

Neben der bewährten **Konsolen-Variante (V1)** gibt es eine **richtige App** mit
Oberfläche, Knöpfen und mehreren Funktionen. Im Setup wählt man, welche Variante
installiert wird (Details dazu entscheiden wir später).

## Architektur

```
                 Autostart (einziger!)
                        │
              ┌─────────▼──────────┐        startet / weckt        ┌──────────────────────────┐
  Laufwerk A: │  FloppyLauncher.exe │ ─────────────────────────────▶│  Floppy Hub App          │
  ──────────▶ │  "Motor"            │   Named Pipe (nur Befehle,   │  Godot 4 + C#            │
              │  C#, kein Fenster,  │   nie Pfade)                 │  Oberfläche, Bibliothek, │
              │  wenig RAM/CPU      │                              │  Bestätigung, Bespielen, │
              └─────────┬──────────┘                              │  Laufwerke, Log, ...     │
                        │            beide benutzen                └────────────┬─────────────┘
                        └──────────────────┬───────────────────────────────────┘
                                   ┌───────▼────────┐
                                   │  Floppy.Core    │  game.txt-Regeln, Sicherheit,
                                   │  C#-Bibliothek  │  Vertrauensliste, INI, Log,
                                   │  (mit Tests)    │  library.csv, Steam-Helfer
                                   └────────────────┘
```

| Teil | Aufgabe | Technik |
|---|---|---|
| **Floppy.Core** | Alle Regeln **einmal**: `game.txt` lesen/schreiben, Pfad- und Sicherheitsprüfung, Startplan, Vertrauensliste, `FloppyLauncher.ini`, Log, `library.csv`, Steam-AppID | C# .NET 10, Klassenbibliothek + Unit-Tests |
| **FloppyLauncher.exe** (Motor) | Überwacht **nur `A:`**, wendet Core an, startet Bekanntes sofort, weckt die App wenn gefragt werden muss. **Name bleibt „FloppyLauncher"**, einziger Autostart | C# .NET 10, ohne Fenster |
| **Floppy Hub App** | Alles Sichtbare | Godot 4.7 (.NET) + C# |

**Kompatibel mit V1:** Das `game.txt`-Format, die INI und `library.csv` bleiben
identisch. Eine Diskette funktioniert in beiden Varianten. Motor und V1 teilen
sich die Einzelinstanz-Sperre, sie können also nie gleichzeitig laufen.

## Entscheidungen (2026-09-16)

### 1. Beim Einlegen: sofort starten, aber mit Vertrauensliste

| Diskette | App-Variante |
|---|---|
| Steam-ID | startet sofort |
| `hub=1` | öffnet die App |
| Programm (`run=`, `pcrun=`, einzelne EXE), **bekannt** | startet sofort, auch `pcrun=` |
| Programm, **zum ersten Mal** oder **nicht über die App eingerichtet** | App fragt: *Einmal starten* / *Immer erlauben* / *Abbrechen* |
| Programm **verändert** (Update, ausgetauschte Datei) | fragt erneut und sagt warum |
| Mehrere EXE | Auswahl in der App |
| `C:\Windows` & Co. | bleibt gesperrt, Vertrauen hebt das **nicht** auf |

- „Bekannt" heißt: Eintrag in der **Vertrauensliste** (`%LOCALAPPDATA%\FloppyHub\trust.json`).
  Ein Eintrag gilt für **Pfad + Argumente + SHA-256 der Datei**. Eine andere Datei am
  selben Ort oder andere `args=` gelten also als neu.
- Einträge entstehen durch *Immer erlauben* oder beim **Bespielen über die App**.
- Ist die App nicht installiert, fragt der Motor mit einem einfachen Windows-Dialog.
- Die Konsolen-Variante (V1) bleibt unverändert (fragt bei `pcrun=` immer).

### 2. Cover-Bilder
Ja. Beim ersten Mal aus dem Steam-CDN laden (Internet nötig), danach lokal im Cache
(`%LOCALAPPDATA%\FloppyHub\covers`), funktioniert dann auch offline.

### 3. Sprache
Deutsch und Englisch, Auswahl beim ersten Start. **Zuerst nur Deutsch**, Englisch
kommt auf Befehl dazu. Alle Texte stehen in `lang/de.lang`, eine Übersetzung ist
nur eine zweite Datei.

### 4. Assets
Mael baut eigene Grafiken. Bis dahin **selbst erzeugte Platzhalter** (Pixel-Art
aus Code, damit lizenzfrei) oder lizenzfreie Grafiken. **Kein 3D vorerst**, später
evtl. freie 3D-Modelle.

### 5. Neue Funktionen (nach und nach)
1. **Chat** mit anderen App-Nutzern. Nichts wird gespeichert, der Verlauf ist beim
   Schließen weg. Öffnen nur mit einer **Schlüssel-Diskette** (oder USB-Stick).
2. **Minispiel** von einer Diskette, läuft in der App-Oberfläche.
3. **Laufwerke-Ansicht**: schönere Grafik und Daten (Kapazität, Belegung,
   Dateisystem, ...) für Wechseldatenträger. Hauptsächlich Disketten, aber auch
   USB-Sticks, CDs.

Dazu: **Hell- und Dunkelmodus**.

> Der **Motor** überwacht weiterhin **ausschließlich `A:`**. Laufwerke-Ansicht und
> Schlüssel-Suche lesen andere Datenträger nur, wenn man sie in der App öffnet.

### 6. Chat-Verbindung (entschieden 2026-09-16)

- **Schlüssel-Diskette:** zufälliger Schlüssel, wer eine Kopie hat, ist im selben Raum.
  Nachrichten sind damit Ende-zu-Ende verschlüsselt. Nichts wird gespeichert.
- **Start immer über einen fertigen Gratis-Dienst** (z. B. ntfy.sh, Open Source). Er sieht
  nur verschlüsselten Datensalat. Limits und Nutzungsbedingungen vor dem Bau prüfen.
- **Jederzeit ins gleiche Netzwerk wechseln** (direkt von PC zu PC, ohne Dienst), z. B.
  für längere Gespräche. Das Schulnetz blockiert das nicht. Windows-Firewall fragt beim
  ersten Mal, das ist okay.
- Weg austauschbar bauen: später evtl. derselbe Dienst auf eigenem Server.
- **Später:** Kontakte, denen man Kanalnummern zuteilen kann (Details, wenn es so weit ist).

### 7. Easter Eggs
Fünf bis zehn kleine Easter Eggs in der App (Freundeskreis hat Spaß daran). Abschaltbar
über `[ui] easter_eggs` in der INI. **Nie** in Sicherheits-Rückfragen.

### Vorschläge für später (noch nicht entschieden)

- **Minispiel:** Das Spiel selbst ist in die App eingebaut, die Diskette bringt nur
  **Daten** (Level, Highscores) und keinen Programmcode mit. So kann eine fremde
  Diskette nichts Schädliches in der App ausführen.

## Stil

Richtung: Windows-Programme um 2000–2010, die modern wirken wollten, wie
**WinRAR (32-Bit)**, **VLC** und **Audacity**. „Ein wenig pixelig, ein wenig
altmodisch, aber trotzdem moderne Klicks."

- **Aufbau:** Menüleiste → Werkzeugleiste mit großen Icons + Beschriftung → Liste/Details → Statusleiste
- **Icons:** Pixel-Art 32×32 (Werkzeugleiste) und 16×16 (Listen), leicht plastisch
- **Flächen:** Fenster mit feinen Verläufen, abgeschrägte Knöpfe (Bevel), eingelassene Listenfelder
- **Hell/Dunkel:** gleiche Formen, zwei Farbpaletten (Dunkel ≈ „Audacity dark")
- **Akzente:** Fortschrittsbalken und Pegel wie bei Audacity/VLC, dezente Hover-Effekte
- **Schrift:** Tahoma/Segoe-Charakter (Systemschrift), optional Pixelschrift für Titel
- **Sparsam:** Godot im Kompatibilitäts-Renderer mit *Low Processor Mode*, zeichnet nur bei Änderungen neu

## Ordner

```
app/
  Floppy.sln
  src/Floppy.Core/            Kern-Bibliothek
  src/FloppyLauncher/         Motor
  src/FloppyHub.App/          Godot-Projekt
    assets/icons/             Platzhalter-Icons (werden durch Maels Grafiken ersetzt)
    lang/de.lang              Texte
  tests/Floppy.Core.Tests/    Unit-Tests
  tools/                      Hilfsskripte (z. B. Platzhalter-Icons erzeugen)
```

## Etappen

- [x] **1a Floppy.Core** – V1-Regeln nach C# portiert, Tests inkl. Gegenprobe
- [x] **1b Motor** – `FloppyLauncher.exe`: nur `A:`, Einzelinstanz (gemeinsam mit V1), Log wie V1, Vertrauensliste, Steam/Bekanntes sofort, App wecken, `--stop`. Native EXE: 3 MB, ~4 MB RAM
- [x] **2 App-Grundgerüst** – Fenster im Zielstil, Hell/Dunkel, Skalierung, Sprachdatei (DE), Platzhalter-Icons, Bestätigungsdialog (im Fenster + kleines Extra-Fenster), Willkommensdialog
- [x] **3 Funktionen** – Bibliothek mit Covern, Optionen + Vertrauensliste, Log-Ansicht, **Diskette bespielen** (Vorschau, Prüfung, Vorlage aus Bibliothek, automatische Freigabe, Motor startet Geschriebenes nicht sofort), 9 Easter Eggs
- [ ] **4 Laufwerke-Ansicht** – ✓ Grundversion (Kacheln + Daten) · offen: schönere Grafik
- [ ] **5 Minispiel** – von Diskette, in der App
- [ ] **6 Chat** – Schlüssel-Diskette, nichts gespeichert
- [ ] **7 Setup** – Varianten-Wahl Konsole/App im Installations-Assistenten, alles signiert
- [ ] **8 Englisch & Feinschliff** – Übersetzung auf Befehl, eigene Assets, Animationen, Easter Eggs

Reihenfolge von 4–6 kann Mael jederzeit umstellen.

## Bauen & Ausprobieren

```bash
dotnet test app/Floppy.sln
```

```bash
powershell -ExecutionPolicy Bypass -File app/tools/Start-App.ps1
```

Optionen: `-Theme dark`, `-FirstRun` (Willkommensdialog), `-Drive <Ordner>` (Ordner als Test-Diskette), `-Editor` (Godot-Editor).

Motor als native EXE (braucht die C++-Werkzeuge von Visual Studio; `vswhere.exe` muss im PATH sein):

```bash
dotnet publish app/src/FloppyLauncher -c Release -r win-x64
```

Motor-Parameter für Tests: `--dry-run` (nichts starten), `--once`, `--console`, `--drive <Ordner>`, `--home <Ordner>`, `--stop`.
Der Motor kann nicht gleichzeitig mit `FloppyLauncher.ps1` laufen (gleiche Sperre).

Godot 4.7.2 (.NET) ist per `winget install GodotEngine.GodotEngine.Mono` installiert.

## Technische Notizen

- **Motor → App** nur über die Pipe `FloppyHub.App.<Sitzung>.<Benutzer>` (nur eigenes Konto) mit den Befehlen `show`, `hub`, `confirm`. Die App wertet die Diskette **immer selbst** aus. Es werden nie Pfade übertragen.
- **Bespielen über die App** legt eine kurze Notiz (`motor-skip.txt`) mit dem neuen Disketten-Fingerabdruck ab, damit der Motor das gerade Geschriebene nicht sofort startet.
- **Schärfe bei 150 %:** Linien werden auf echte Bildschirmpixel gelegt. Pixel-Icons werden erst ganzzahlig vergrößert und dann weich auf Zielgröße gebracht.
- **Eigene Icons:** PNG mit gleichem Namen in `app/src/FloppyHub.App/assets/icons/` ersetzen. Die Platzhalter erzeugt `++ --forge-icons` neu.
- **Texte:** `app/src/FloppyHub.App/lang/de.lang`.
