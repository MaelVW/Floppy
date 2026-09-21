# Floppy Hub App – Plan

Lebendes Dokument für die grafische Variante. Stand: 2026-09-21, Version **3.2.0** (nach dem ersten richtigen Release 3.0.0 und den Betas 2.0.0-beta.1 bis beta.6).
Die Anleitung für Nutzer und die Entwickler-Doku stehen im [Wiki](https://github.com/MaelVW/Floppy/wiki); dieses Dokument hält die Planungs- und Entscheidungsgeschichte fest.

> **Nachträge nach den Beta-Entscheidungen unten:** Der Motor überwacht neben `A:` bis zu zwei weitere Wechseldatenträger
> (`[drive] extra_letters` in `FloppyLauncher.ini`, Auswahl in den Optionen; die Konsolen-Variante bleibt bei `A:`).
> Dazu kamen Schach im Chat, Admin/Moderation im Offenen Chat (`ChatAdmins`, signierte Sperren) und das automatische
> Update (`UpdateDownloader`, `UpdateFlow`, Setup-Schalter `/RELAUNCH=`). Details im Wiki.

## Ziel

Neben der bewährten **Konsolen-Variante (V1)** gibt es eine **richtige App** mit
Oberfläche, Knöpfen und mehreren Funktionen. Im Setup wählt man, welche Variante
installiert wird (siehe Etappe 7).

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
Deutsch und Englisch, Auswahl beim ersten Start (Vorgabe: Sprache von Windows) und jederzeit
unter „Optionen“. Alle Texte stehen in `lang/de.lang` und `lang/en.lang` – ein Test prüft, dass
beide dieselben Schlüssel und Platzhalter haben. Meldungen aus dem Kern tragen einen Code und
werden ebenfalls übersetzt (`CORE_…`, `LEVEL_…`). Das **Launcher-Log bleibt deutsch** (wie V1).

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

**Details (entschieden 2026-09-16, vor dem Bau):**
- **Öffnen:** In der App auf „Chat“ → die App fragt „Bitte gib eine Verschlüsselung ein“
  → nach der Eingabe wird automatisch verbunden. Eine Diskette mit Schlüssel wird beim
  Einlegen wie eine **normale Diskette** gelesen, es öffnet sich nichts automatisch.
  (Umsetzung: Schlüssel eintippen oder per Knopf von Diskette/USB-Stick laden.)
- **Wechsel ins lokale Netzwerk:** Jemand schlägt den Wechsel vor. Wer auf den
  Haken (Zustimmen) drückt, wechselt mit in den lokalen Raum. Wer nicht zustimmt, bleibt
  noch **10 Sekunden** im Online-Raum und fliegt dann raus, weil der Kanal auf lokal
  gewechselt hat. Lehnen **alle** ab, wird die Anfrage abgelehnt und es bleibt online.
- **Kanal = Verschlüsselung + ID:** Jede Installation hat eine eigene **ID-Nummer**, an der
  man bei erneutem Kontakt erkennt, wer wer ist. Verschlüsselung und ID kann man als
  **Kontakt speichern**. Damit ist auch die Namensfrage beantwortet.
- **Rangliste im Chatraum:** Im Raum eine Rangliste für die Minispiele öffnen: wer wie viele
  Punkte hat (**Zeit + Effizienz**). Auch eigene Level (**Level-Editor**) bekommen eine
  **ID**; stimmen die IDs überein, kann man sich dort ebenfalls vergleichen.
- **Zukunft:** weitere kleine Spiele.
- **Offener Chat (2026-09-16, umbenannt 2026-09-17):** Ein **Offener Chat** ohne eigene
  Verschlüsselung, für alle Nutzer – Standardraum zum Reinschnuppern, dauerhaft vorhanden
  (kein Debug-Feature mehr, das vor Release abgeschaltet werden müsste). Schalter bleibt:
  `ChatRoomKey.OpenRoomAvailable` in `app/src/Floppy.Core/Chat/ChatRoomKey.cs`.

**Umsetzung (Etappe 6):**

| Teil | Wie |
|---|---|
| Raum | Verschlüsselung → PBKDF2 (200 000 Runden) → getrennte Schlüssel für AES-256-GCM, ntfy-Thema, Raum-Kennung und LAN-Suche. **Prüfzahl** (z. B. `JTM-BA2`) zum Vergleichen |
| ID-Nummer | Pro Installation ein Schlüsselpaar (ECDSA P-256), per Windows-Datenschutz (DPAPI) gespeichert. ID = 12 Ziffern aus dem öffentlichen Schlüssel, z. B. `4827-1935-0062`. Jede Nachricht ist unterschrieben – IDs lassen sich nicht fälschen |
| Online | ntfy.sh: `POST` mit `Cache: no` (Dienst speichert nichts) und `Firebase: no`, Empfang als JSON-Stream. Eigener ntfy-Server möglich: `[chat] server` in `app.ini` |
| Lokal | TCP (Port 45817) von PC zu PC, Suche per UDP-Broadcast (Port 45816), Handschlag mit HMAC. Nur private Adressen (10.x, 172.16–31.x, 192.168.x). Nachrichten werden weitergereicht – fällt einer aus, läuft der Rest weiter |
| Betreten | Erst kurz im lokalen Netz nach dem Raum suchen, sonst online |
| Wechsel | Vorschlag (30 s) → wer den Haken drückt, verbindet sich lokal → sobald einer da ist, ist der Raum gewechselt, alle anderen haben **10 s** („Doch mitkommen") → getrennt. Alle lehnen ab / niemand antwortet → bleibt online. Im anderen Netzwerk zählt „Haken" als „nein" |
| Kontakte | Name + ID (Fingerabdruck) + optional die Verschlüsselung (DPAPI) in `%LOCALAPPDATA%\FloppyHub\chat\contacts.json`. Doppelklick verbindet |
| Schlüssel-Diskette | `chat-key.txt` (`chatkey = …`) im Wurzelverzeichnis. „Von Datenträger laden" sucht auf Disketten/USB-Sticks, „Neue Verschlüsselung" erzeugt `XXXX-XXXX-XXXX-XXXX-XXXX` |
| Rangliste | Punkte = 1000 × Disketten − 5 × Züge − 5 × Schübe − 2 × Sekunden. Beim Öffnen schicken alle ihre besten Ergebnisse (aus den lokalen Bestenlisten), verglichen über die **Level-ID** (12 Hex-Zeichen aus dem Level-Aufbau) |
| Level-Editor | Im Minispiel: malen, testen, speichern nach `%LOCALAPPDATA%\FloppyHub\levels\eigene-level.txt`, „Auf Diskette…" |

**Grenzen von ntfy.sh (Stand 2026-09):** ohne Konto etwa **250 Nachrichten pro Tag** und 30 offene
Verbindungen **pro IP-Adresse** – eine Schule teilt sich oft eine IP. Die App sendet darum keine
regelmäßigen Lebenszeichen online, bremst auf 15 Nachrichten pro Minute und empfiehlt bei Limit
den Wechsel ins lokale Netz. Für viele Nutzer später: eigener ntfy-Server.

### 7. Easter Eggs
Fünf bis zehn kleine Easter Eggs in der App (Freundeskreis hat Spaß daran). Abschaltbar
über `[ui] easter_eggs` in der INI. **Nie** in Sicherheits-Rückfragen.

### 8. Nach Version 3.0 (entschieden 2026-09-21)

- **Sofort beenden (umgesetzt, Zweig `Feature-Erweiterungen`):** In den Optionen legt man eine
  Tastenkombination fest, die die App **ohne Rückfrage sofort schließt** (`[app] quit_hotkey` in
  `app.ini`, Vorgabe: keine). Sie wirkt überall im Fenster, auch im Chat-Eingabefeld und mit offenem
  Dialog, solange das Fenster im Vordergrund ist; der Motor läuft weiter. **Nicht wählbar:** Alt+F4,
  Kombinationen mit der Windows-Taste, was Windows selbst belegt (Alt+Tab, Strg+Esc, …), einzelne
  Zeichentasten (würden beim Tippen schließen), Strg+Alt+Zeichentaste (das ist AltGr, z. B. für @)
  und die Kürzel der App (F1, F5). Regeln: `Floppy.Core/KeyChord.cs` (mit Tests), Kürzel der App:
  `AppShortcuts.InUse`.
- **Chat anpassen (umgesetzt, Zweig `Chat`):** Im Chat rechts bei „Deine ID“ → „Anpassen…“ gibt es fünf
  Kleinigkeiten. Für **alle im Raum sichtbar:** (1) **Anzeigename** und (2) **Namensfarbe** (8 Farben, hell/dunkel
  getrennt). Im Chat steht immer die ID-Endung dahinter („Tom #1417“), selbst benannte Kontakte gehen vor. Nur
  **für einen selbst:** (3) **Benachrichtigung** (Ton + blinkende Taskleiste, wenn der Chat nicht im Blick ist:
  aus / nur Erwähnungen / jede Nachricht), (4) **Erwähnungen hervorheben** (Nachricht mit dem eigenen Namen
  bekommt einen Hintergrund) und (5) **Schriftgröße** im Verlauf. Name und Farbe reisen als Felder `a`/`c`
  in Beitritt, Lebenszeichen und Text mit (ältere Versionen ignorieren sie); fremde Namen werden beim Empfang
  streng geprüft (nur Buchstaben, Ziffern und ` -_.'!?+*~`, höchstens 20 Zeichen, keine Klammern/Emojis/
  unsichtbaren Zeichen, „Admin“, „Moderator“, „System“, „Floppy Hub“ nur für Admins bzw. die App) – die
  Admin-Kennzeichnung hängt weiter nur am Fingerabdruck. Regeln: `Floppy.Core/Chat/ChatProfile.cs` (mit Tests),
  Einstellungen in `app.ini` unter `[chat]` (`alias`, `color`, `notify`, `highlight_mentions`, `font`).
- **Steam-Bibliothek durchsuchen (umgesetzt, Zweig `Feature-Erweiterungen`):** In der Bibliothek füllt der Knopf
  **„Steam durchsuchen…“** die Liste automatisch mit den installierten Steam-Spielen, statt jedes einzeln
  einzutragen. Es werden nur Dateien gelesen, die Steam selbst anlegt – **kein Konto, kein Internet, an Steam
  wird nichts verändert**: `steamapps\libraryfolders.vdf` (alle Bibliotheksordner/Laufwerke, alte und neue
  Schreibweise) und je Spiel `steamapps\appmanifest_<AppID>.acf` (Name, AppID, Größe, Zustand). Den Steam-Ordner
  findet die App über die Registrierung (danach Standardordner); sonst kann man ihn im Fenster selbst wählen
  (`[library] steam_path` in `app.ini`). Ein Fenster zeigt die Treffer: **Neues ist vorausgewählt**, schon
  Vorhandenes (nach AppID, auch als Store-Link eingetragen) abgeblendet, Spiele, die Steam noch lädt, sind
  nicht vorausgewählt; Steam-Laufzeiten („Steamworks Common Redistributables“, Proton, Linux Runtime) werden
  ausgelassen. „Hinzufügen“ hängt nur an, was fehlt – bestehende Einträge (auch selbst geänderte Namen/
  Notizen) bleiben unangetastet; die Datei wird frisch gelesen, atomar geschrieben (erst nebenan, dann
  getauscht) und vorher als `library.csv.bak` gesichert. Gestartet wird wie bisher über `steam://rungameid/`.
  Bewusst **nicht** enthalten: Spiele, die man nur besitzt, aber nicht installiert hat (stehen in keiner
  Steam-Datei; dafür wäre ein Web-API-Schlüssel oder eine Anmeldung nötig), und ein stilles Abgleichen beim
  Start (würde Zeilen, die man bewusst aus `library.csv` gelöscht hat, immer wieder anlegen; dafür bräuchte es
  erst eine „Ausgeblendet“-Liste). Einträge lassen sich in der App weiterhin nicht löschen – die Vorauswahl im
  Fenster und `library.csv.bak` fangen Fehlgriffe auf. Regeln: `Floppy.Core/Vdf.cs` (Parser für Valves Textformat,
  hart gegen kaputte Dateien), `Floppy.Core/SteamScanner.cs`, Dialog `Ui/SteamScanDialog.cs`; Tests in
  `SteamScannerTests.cs`.
- **Code-Duell (Bot-Arena, noch nicht umgesetzt):** wird ein **Unterspiel im Bereich „Minispiel“** und
  **kein eigener Reiter** in der Werkzeugleiste. Die Ansicht „Minispiel“ bekommt dafür eine
  Spielauswahl (Diskettenlager, Code-Duell, …). Skizze vom 2026-09-18: Arena-Regeln in `Floppy.Core`
  wie bei den anderen Spielen, Darstellung in 2D-Pixel-Art, Arena gemeinsam bauen (Editor wie beim
  Diskettenlager); Bots hinter einer gemeinsamen Schnittstelle, die je Runde denselben JSON-Zug
  liefert – entweder als **externes Programm** (beliebige Sprache) oder über einen **Block-Editor**
  (kleine, spielspezifische Blocksprache ohne Prozessstart).

### Vorschläge für später (noch nicht entschieden)

- **Minispiel (umgesetzt):** Das Spiel ist in die App eingebaut, die Diskette bringt nur
  **Daten** (Level, Rekorde) und keinen Programmcode mit. Format siehe
  `app/src/FloppyHub.App/games/diskettenlager.txt` und `beispiele/minispiel.game.txt`.
  Später denkbar: weitere Spieltypen mit demselben Prinzip.

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
    lang/de.lang, en.lang     Texte
    export_presets.cfg        Godot-Export "Windows Desktop"
    FloppyHub.csproj + .sln   Projekt + eigene Solution (Godot sucht beim Export <Assembly>.sln)
  tests/Floppy.Core.Tests/    Unit-Tests
  tools/                      Hilfsskripte (Start-App.ps1)
build/
  Build-App.ps1               Tests + Motor (NativeAOT) + Godot-Export -> build/app-out
  Build-Installer.ps1         App + Setup mit beiden Varianten, signiert mit Zertifikat
installer/FloppyHub.iss       Inno Setup mit Varianten-Seite
docs/releases/                Release-Texte (v<Version>.md)
```

## Etappen

- [x] **1a Floppy.Core** – V1-Regeln nach C# portiert, Tests inkl. Gegenprobe
- [x] **1b Motor** – `FloppyLauncher.exe`: nur `A:`, Einzelinstanz (gemeinsam mit V1), Log wie V1, Vertrauensliste, Steam/Bekanntes sofort, App wecken, `--stop`. Native EXE: 3 MB, ~4 MB RAM
- [x] **2 App-Grundgerüst** – Fenster im Zielstil, Hell/Dunkel, Skalierung, Sprachdatei (DE), Platzhalter-Icons, Bestätigungsdialog (im Fenster + kleines Extra-Fenster), Willkommensdialog
- [x] **3 Funktionen** – Bibliothek mit Covern, Optionen + Vertrauensliste, Log-Ansicht, **Diskette bespielen** (Vorschau, Prüfung, Vorlage aus Bibliothek, automatische Freigabe, Motor startet Geschriebenes nicht sofort), 9 Easter Eggs
- [x] **4 Laufwerke-Ansicht** – alle Wechseldatenträger: Disketten (inkl. Format 3,5″/5,25″), USB-Sticks, Speicherkarten, externe USB-Festplatten, CD/DVD/Blu-ray/Audio-CD, virtuelle Laufwerke. Anschluss, Gerät, Seriennummer, Clustergröße, XP-Kuchendiagramm. Optional interne Laufwerke
- [x] **5 Minispiel „Diskettenlager"** – Sokoban mit Disketten; Levelpaket als Text auf der Diskette (`minigame=levels.txt`), 8 eingebaute Level (per Löser geprüft), Rekorde auf Diskette + lokal, LCD-Zähler, Spiel-Disketten über „Bespielen"
- [x] **6 Chat** – Verschlüsselung eintippen oder von Diskette/USB laden, Ende-zu-Ende verschlüsselt, nichts gespeichert; online über ntfy.sh, Wechsel ins lokale Netz mit Abstimmung + 10-s-Regel; ID-Nummer pro Installation, Kontakte; Rangliste im Raum; Minispiel mit Zeit, Punkten, Level-IDs und **Level-Editor**; **Offener Chat** als dauerhafter Standard-Einstieg ohne Verschlüsselung
- [x] **7 Setup** – Varianten-Seite im Assistenten (App empfohlen / Konsole wie V1). Wechsel jederzeit: Programmdateien der anderen Variante werden entfernt, INI + Bibliothek bleiben, die gewählte Variante wird gemerkt. `/VARIANT=app|console` für unbeaufsichtigt, Autostart je Variante, Deinstallation fragt nach App-Daten. Signiert (sobald ein Zertifikat da ist): Skripte, beide EXE, eigene DLLs, Setup, Deinstaller – mit Test-Zertifikat geprüft. GitHub-Workflow baut App + Setup, Versionen mit `-` werden Pre-Release. Setup ≈ 53 MB
- [x] **8 Englisch & Feinschliff** – komplette englische Oberfläche inkl. Kern-Meldungen und Levelnamen, Umschalten in Optionen/Willkommen, Dialoge blenden weich ein, Programm-Icon für App, Motor und Setup. (Eigene Grafiken macht Mael später.)

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

Godot 4.7.2 (.NET) ist per `winget install GodotEngine.GodotEngine.Mono` installiert, die
Windows-Exportvorlagen liegen in `%APPDATA%\Godot\export_templates\4.7.2.stable.mono`.

Setup bauen (Tests, Motor, Godot-Export, Inno Setup – signiert automatisch, wenn ein Zertifikat da ist):

```bash
powershell -ExecutionPolicy Bypass -File build/Build-Installer.ps1
```

Optionen: `-SkipAppBuild` (vorhandenes `build\app-out` nehmen), `-SkipTests`, `-Sign`,
`-UseTestCertificate`, `-Version 2.0.0-beta.2`. App testweise in anderer Sprache: `++ --lang en`.

Chat ausprobieren:
- **Zwei Fenster auf einem PC:** `Start-App.ps1` normal und ein zweites Mal mit `-SecondWindow` starten (eigene Chat-ID, Benutzerordner `%LOCALAPPDATA%\FloppyHub-Zweitfenster`), beide in den Offenen Chat.
- **Vorschau ohne Netzwerk** (zwei Mitspieler im Speicher, für Bildschirmfotos): `++ --screenshot x.png --view chat-proposal` – außerdem `chat-prompt`, `chat-countdown`, `chat-local`, `chat-scores`, `chat-open` (echter Offener Chat), `editor`.
- **Echter Test gegen ntfy.sh:** `FLOPPY_NET_TESTS=1` setzen, dann `dotnet test app/Floppy.sln` (zählt aufs Tageslimit).

## Technische Notizen

- **Motor → App** nur über die Pipe `FloppyHub.App.<Sitzung>.<Benutzer>` (nur eigenes Konto) mit den Befehlen `show`, `hub`, `confirm`. Die App wertet die Diskette **immer selbst** aus. Es werden nie Pfade übertragen.
- **Bespielen über die App** legt eine kurze Notiz (`motor-skip.txt`) mit dem neuen Disketten-Fingerabdruck ab, damit der Motor das gerade Geschriebene nicht sofort startet.
- **Schärfe bei 150 %:** Linien werden auf echte Bildschirmpixel gelegt. Pixel-Icons werden erst ganzzahlig vergrößert und dann weich auf Zielgröße gebracht.
- **Eigene Icons:** PNG mit gleichem Namen in `app/src/FloppyHub.App/assets/icons/` ersetzen. Die Platzhalter erzeugt `++ --forge-icons` neu.
- **Texte:** `app/src/FloppyHub.App/lang/de.lang` und `en.lang`. Neue Schlüssel immer in beide Dateien (sonst schlägt der Test fehl).
- **Chat bleibt verbunden**, wenn Farbschema oder Größe gewechselt werden (die Sitzung lebt in `AppServices.Chat`, nicht in der Ansicht). Beim Beenden sagt die App „tschüss".
- **Firewall:** Beim ersten Wechsel ins lokale Netz fragt Windows für FloppyHub.exe nach (TCP 45817, UDP 45816). Das Setup legt bewusst keine Regeln an (bräuchte Adminrechte).
- **Export:** `lang/*.lang` und `games/*.txt` stehen im Export-Filter (keine Godot-Ressourcen). Die PCK liegt getrennt neben der EXE, damit die EXE signiert werden kann.
- **Setup beendet nur Launcher aus dem eigenen Installationsordner** (`Stop-FloppyLauncher.ps1 -Folder`) – nie eine Entwicklungs-Kopie wie `C:\Floppy`.
- **Bibliothek im Setup:** `installer/library-template.csv` (nur ein Beispiel) – nicht die persönliche `library.csv` aus dem Repo.
- **Konsolen-Variante unverändert** (entschieden 2026-09-17): sie beachtet die Schreib-Notiz der App nicht und läuft wie vor der App.
