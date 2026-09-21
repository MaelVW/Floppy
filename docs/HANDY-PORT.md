# Handy-Port: Floppy Chat für Android (und danach iOS)

Der Chat aus Floppy Hub als eigene Handy-App. Stand: **Vorlage 0.1.0 für Android** (Branch `Handy-Port`).
PC und Handy landen im selben Raum, wenn beide dieselbe Verschlüsselung eingeben – Ende-zu-Ende verschlüsselt und
unterschrieben wie bisher, nichts wird gespeichert.

## Was die Vorlage kann

- **Offener Chat** und **private Räume** per Verschlüsselung (auch neue Verschlüsselung erzeugen, kopieren, teilen).
- Verlauf mit Namen und Namensfarben, Erwähnungen farbig hinterlegt, alle Hinweise des Chats im gleichen Wortlaut wie am PC.
- **Personen** im Raum, Verbindungsstand (online / lokal), **Wechsel ins lokale Netz** vorschlagen und beantworten.
- Einstellungen: Anzeigename, Namensfarbe (mit Vorschau), Schriftgröße, Erwähnungen, Bildschirm im Chat anlassen, eigener ntfy-Dienst.
- Hell/Dunkel folgt dem Handy, Deutsch/Englisch auch. Aussehen wie am PC: Kanten, Titelleiste, Pixel-Icons.
- **Eigene Chat-ID pro Handy** (siehe unten). Der private Schlüssel liegt nur AES-verschlüsselt in der App; der AES-Schlüssel steckt
  im Android-Keystore.

Noch nicht dabei (bewusst, kommt schrittweise): Schach, Rangliste, Kontakte, Benachrichtigungen bei geschlossener App
(braucht einen Hintergrunddienst), iOS.

## Aufbau

| Projekt | Was |
| --- | --- |
| `app/src/Floppy.Core` | Der Chat-Kern (Verschlüsselung, Raum, Sitzung) – derselbe wie am PC. Neu: `ISecretProtector` (Ersatz für Windows-DPAPI). |
| `app/src/Floppy.Chat.Client` | Chat-Logik ohne Oberfläche: Namen, Farben, fertige Verlaufszeilen, Verbindungstext, Texte (`Loc`). Am PC testbar. |
| `app/tests/Floppy.Chat.Client.Tests` | Tests dazu (laufen mit `dotnet test app/Floppy.sln` mit). |
| `app/src/FloppyChat.Mobile` | Die Handy-App (.NET MAUI). **Nicht** in `Floppy.sln`, damit der PC-Build keine Android-Werkzeuge braucht. |

Die Texte kommen aus denselben Dateien wie am PC (`app/src/FloppyHub.App/lang/de.lang`, `en.lang`); nur Handy-eigene Texte
stehen in `app/src/Floppy.Chat.Client/Lang/mobile.*.lang` (Schlüssel `M_…`). Ein Test prüft, dass jeder im Code benutzte
Schlüssel auf Deutsch und Englisch existiert.

## Bauen (Android)

Einmalig (Administrator-Terminal):

```powershell
dotnet workload install maui-android
```

Danach (normales Terminal) das Android-SDK holen und die Lizenz annehmen:

```powershell
dotnet build app/src/FloppyChat.Mobile -t:InstallAndroidDependencies -f net10.0-android `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" -p:AcceptAndroidSDKLicenses=True
```

Die APK bauen:

```powershell
dotnet publish app/src/FloppyChat.Mobile -f net10.0-android -c Release
```

Ergebnis: `app/src/FloppyChat.Mobile/bin/Release/net10.0-android/publish/io.github.maelvw.floppychat-Signed.apk`.

> Die APK ist mit dem **Debug-Schlüssel** dieses PCs signiert – gut zum Testen. Ein späterer Play-Store-Release braucht einen
> eigenen Schlüssel (`AndroidKeyStore=true` + Schlüsseldatei; nie ins Repo). Wird eine APK mit anderem Schlüssel über eine
> bestehende gespielt, muss die alte App vorher deinstalliert werden (dabei geht die Chat-ID dieses Handys verloren).

## Aufs Handy bringen

**Per USB (empfohlen):** Am Handy *Einstellungen → Über das Telefon → Build-Nummer* 7× antippen (Entwickleroptionen),
dann *Entwickleroptionen → USB-Debugging* einschalten, Handy per Kabel an den PC, die Rückfrage „USB-Debugging zulassen“
bestätigen. Dann:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" devices
& "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe" install -r <Pfad zur APK>
```

**Ohne Kabel:** APK aufs Handy kopieren (Cloud, Chat-App, USB-Stick), dort öffnen und „Aus dieser Quelle installieren“ erlauben.

## Testen (Checkliste)

1. App starten → Startbild → Raumwahl mit **Deiner ID**.
2. Am PC in Floppy Hub die Chat-Ansicht öffnen, am Handy denselben **Offenen Chat** betreten → beide sehen „… ist dem Raum beigetreten“.
3. Nachricht vom Handy → erscheint am PC (mit Handy-Name/ID); umgekehrt ebenso.
4. Privater Raum: am Handy „Neue Verschlüsselung…“, Wert am PC eingeben → gleiche **Prüfzahl** im Titel/Raumnamen.
5. Einstellungen: Anzeigename + Farbe ändern → am PC erscheint der neue Name sofort.
6. Am PC „Ins lokale Netz“ vorschlagen (Handy im selben WLAN) → Banner am Handy „Mitkommen / Online bleiben“.
7. Handy drehen, Hell/Dunkel wechseln, App in den Hintergrund und zurück: Chat bleibt (oder verbindet neu).

## Entscheidungen (Stand Vorlage)

- **Technik: .NET MAUI.** Nutzt `Floppy.Core` 1:1 (derselbe, getestete Chat-Kern), eine Codebasis für Android **und** iOS.
  Godot-C# lässt sich nur experimentell nach Android exportieren und nicht nach iOS; eine Web-App bräuchte den Kern ein zweites Mal.
- **Identität: eigene ID pro Handy.** Der private Schlüssel verlässt nie das Gerät; „ID vom PC mitnehmen“ hieße, den Schlüssel
  zu exportieren. Wer den Handy-Nutzer dauerhaft erkennen will, speichert ihn (später: Kontakte) wie jeden anderen.
- **Name „Floppy Chat“ ist vorläufig** (Rebranding steht an). Die App-Kennung `io.github.maelvw.floppychat` ist der interne
  Ausweis der App; wird sie geändert, ist es für Android eine neue App.
- **Kein Hintergrunddienst.** Solange die App im Vordergrund ist (Bildschirm bleibt an), ist man verbunden; im Hintergrund
  pausiert Android das Netz, danach verbindet die App neu (Nachrichten dazwischen gehen verloren – der Chat speichert nichts).

## iOS (danach)

Derselbe Code. iOS-Apps lassen sich nur auf einem Mac bauen – dafür gibt es bei GitHub Actions einen macOS-Runner (kein eigener
Mac nötig). Installieren aufs iPhone geht ohne bezahltes Entwicklerkonto nur per Sideloading mit einer gratis Apple-ID
(Programme wie AltStore/Sideloadly, Ablauf nach 7 Tagen); TestFlight/App Store brauchen das Apple-Developer-Programm (99 USD/Jahr).
Die Schritte folgen, sobald die Android-Vorlage getestet ist.
