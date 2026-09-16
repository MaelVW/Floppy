# Digitale Signatur

Ab jetzt kann jeder Release **mit deinem Namen signiert** werden. Die Technik ist
fertig eingebaut, es fehlt nur noch das Zertifikat.

## Was eine Signatur bringt – und was nicht

| | ohne Signatur | mit echtem Zertifikat |
|---|---|---|
| Herausgeber im Windows-Dialog | „Unbekannter Herausgeber" | **dein Name** |
| Datei nach dem Download verändert? | nicht erkennbar | Signatur wird ungültig → sofort sichtbar |
| SmartScreen „Computer wurde geschützt" | erscheint | erscheint **anfangs trotzdem**, verschwindet, sobald genug Leute dein signiertes Setup geladen haben |

Wichtig: SmartScreen vergibt Vertrauen über die Zeit. Das gilt auch für bezahlte
Zertifikate, einen Sofort-Durchlass gibt es nicht. Weil sich das Vertrauen am
Zertifikat sammelt, wird es mit jedem Release besser.

## Ein Zertifikat auf deinen Namen bekommen

Ein **selbst erstelltes** Zertifikat hilft anderen nicht: Windows kennt es nicht.
Ein vertrauenswürdiges Zertifikat gibt es nur von einer **Zertifizierungsstelle**
(CA). Die prüft deine Identität (Ausweis). Seit 2023 liegt der private Schlüssel
dabei immer auf einem **USB-Token oder in der Cloud** des Anbieters, nicht mehr
als lose Datei.

Übliche Wege für Einzelpersonen:

1. **Open-Source-Zertifikat** – der günstigste Weg zu einem Zertifikat mit deinem
   Namen. Einige CAs (z. B. Certum) bieten es für Open-Source-Entwickler an.
   Voraussetzung: Das Repo ist **öffentlich**.
2. **Reguläres Code-Signing-Zertifikat (OV) für Einzelpersonen** – das Repo darf
   privat bleiben, kostet aber deutlich mehr pro Jahr.

Preise und Bedingungen ändern sich. Vor dem Kauf direkt beim Anbieter nachsehen.

## Signieren mit dem echten Zertifikat (lokal)

Die Software des Anbieters (Token-Treiber bzw. Cloud-Client) blendet das
Zertifikat in den Windows-Zertifikatsspeicher ein. Dann:

1. Fingerabdruck finden:
   ```powershell
   Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint, NotAfter
   ```
2. Einmalig hinterlegen, **eine** der beiden Varianten:
   - Umgebungsvariable: `setx FLOPPY_SIGN_THUMBPRINT 0123ABCD...`
   - Datei `build\signing.local.psd1` (ist per `.gitignore` ausgeschlossen):
     ```powershell
     @{ Thumbprint = '0123ABCD...' }
     ```
3. Bauen:
   ```powershell
   .\build\Build-Installer.ps1 -Version 1.1.0 -Sign
   ```
   `-Sign` bricht ab, falls kein Zertifikat gefunden wird. So entsteht nie
   versehentlich ein unsignierter Release.

Signiert werden **alle** Skripte (`.ps1`, `.vbs`), das **Setup** und das
**Deinstallationsprogramm**, jeweils mit Zeitstempel. Dank Zeitstempel bleibt die
Signatur gültig, auch wenn das Zertifikat später abläuft. Die Dateien im Repo
bleiben unverändert, signiert wird eine Kopie in `build\stage`.

## Signieren in GitHub Actions

Der Release-Workflow signiert automatisch, **wenn** diese Secrets gesetzt sind
(*Settings → Secrets and variables → Actions*):

| Secret | Inhalt |
|---|---|
| `FLOPPY_SIGN_PFX_BASE64` | PFX-Datei als Base64 |
| `FLOPPY_SIGN_PFX_PASSWORD` | Passwort der PFX |

Ohne Secrets wird wie bisher unsigniert gebaut.

> **Einschränkung:** Echte Zertifikate von heute gibt es meist **nicht** als
> PFX-Datei (Schlüssel auf Token/Cloud). Dann geht es so:
> - **lokal** signiert bauen (siehe oben) und die EXE von Hand an den Release hängen, oder
> - das Cloud-Signierwerkzeug des Anbieters im Workflow einbinden. Das lässt sich
>   ergänzen, sobald feststeht, welcher Anbieter es wird.

## Die Kette testen, ohne etwas zu kaufen

```powershell
.\build\New-FloppyTestCertificate.ps1          # erzeugt build\certs\floppy-test.pfx
.\build\Build-Installer.ps1 -Sign -UseTestCertificate
```

Das Setup trägt dann „Mael (TEST – nicht vertrauenswürdig)" als Signatur. Es
beweist, dass alles funktioniert, **ist aber nicht zum Weitergeben gedacht**.
Das Test-Zertifikat wird nur als Datei angelegt. Der Windows-Zertifikatsspeicher
wird nicht angefasst und nichts als vertrauenswürdig eingetragen.

## Sicherheit

- PFX-Dateien, Passwörter und `signing.local.psd1` **nie committen**. `.gitignore`
  schließt `build/certs/`, `*.pfx` und `signing.local.psd1` bereits aus.
- In GitHub Actions wird die PFX nur in den temporären Runner-Ordner geschrieben
  und am Ende des Laufs gelöscht, auch bei Fehlern.
