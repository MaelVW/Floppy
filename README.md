# Floppy Hub

**Aus Disketten wird eine physische Steam-Library.** Diskette in `A:` einlegen – und das Spiel, Programm oder Minispiel
darauf startet von selbst. Floppy Hub gibt es als **App mit grafischer Oberfläche** (Version 3) und weiterhin als
**Konsolen-Variante** (PowerShell, wie Version 1).

[**Herunterladen**](https://github.com/MaelVW/Floppy/releases/latest) ·
[**Wiki** (Anleitung, Chat, Minispiel, Entwickler-Doku)](https://github.com/MaelVW/Floppy/wiki) ·
[Fehler melden / Wünsche](https://github.com/MaelVW/Floppy/issues)

## Was Floppy Hub kann

- **Disketten starten** – Steam-Spiele (`id=220`), Programme von der Diskette (`run=`) oder vom PC (`pcrun=`),
  das Hub-Menü oder ein Minispiel. Neues wird einmal nachgefragt, Bekanntes startet sofort (Vertrauensliste mit
  Prüfsumme, `C:\Windows` bleibt immer gesperrt).
- **Bibliothek mit Covern** – mit **„Steam durchsuchen“** füllt sie sich auf Knopfdruck mit deinen installierten
  Steam-Spielen (ohne Konto, ohne Internet) – und **Disketten bespielen** mit Vorschau und Prüfung.
- **Laufwerke-Ansicht** für Disketten, USB-Sticks, Speicherkarten, externe Festplatten und CD/DVD/Blu-ray.
  Neben `A:` können bis zu zwei weitere Wechseldatenträger überwacht werden – für alle ohne echtes Diskettenlaufwerk.
- **Chat** – Ende-zu-Ende verschlüsselt, nichts wird gespeichert; online über einen Gratis-Dienst oder direkt im lokalen
  Netz; mit ID-Nummer, Kontakten, Rangliste und **Schach** gegeneinander. Dazu ein **Offener Chat** zum Reinschnuppern
  mit Admin und Moderation.
- **Minispiel „Diskettenlager“** (Sokoban mit Disketten) mit Level-Editor, Rekorden und eigenen Leveln.
- **Automatische Updates** – ein Klick genügt, die App lädt das neue Setup selbst und startet neu.
- Hell-/Dunkelmodus, Deutsch/Englisch, im Stil klassischer Windows-Programme um 2000–2010 (WinRAR, VLC, Audacity).

## Installation

1. [Neueste Version herunterladen](https://github.com/MaelVW/Floppy/releases/latest) (`FloppyHubSetup-x.y.z.exe`) und ausführen.
2. Variante wählen: **App** (empfohlen) oder **Konsole**, Zielordner, optional Autostart.
3. Diskette in `A:` einlegen. Fertig.

Windows meldet bei unsignierten Setups „Der Computer wurde geschützt“ – *Weitere Informationen → Trotzdem ausführen*.
Windows 10/11 (64 Bit). Mehr im [Wiki](https://github.com/MaelVW/Floppy/wiki).

## Selbst bauen

.NET 10 SDK, Godot 4.7 (.NET) mit Exportvorlagen, Visual-Studio-C++-Werkzeuge (für die native Motor-EXE) und Inno Setup:

```bash
dotnet test app/Floppy.sln
powershell -ExecutionPolicy Bypass -File build/Build-Installer.ps1
```

Architektur, Testen und Release-Ablauf stehen im [Wiki](https://github.com/MaelVW/Floppy/wiki) und in
[`docs/APP-PLAN.md`](docs/APP-PLAN.md); die Dateien der Konsolen-Variante in [`README-FloppyHub.md`](README-FloppyHub.md).

## Lizenz

[MIT](LICENSE.txt) – © 2026 MaelVW
