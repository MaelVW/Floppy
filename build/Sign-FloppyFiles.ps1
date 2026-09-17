<#
.SYNOPSIS
    Signiert Floppy-Hub-Dateien (Setup-EXE, Uninstaller, .ps1, .vbs, spaeter die App)
    mit einem Code-Signing-Zertifikat.

.DESCRIPTION
    Zentrale Signier-Stelle fuer Build-Skript, Inno Setup und GitHub Actions.

    ZERTIFIKAT - eine der beiden Quellen (Reihenfolge: Parameter > Umgebung >
    build\signing.local.psd1):

      1) Zertifikat im Windows-Zertifikatsspeicher, per Fingerabdruck
           -Thumbprint  /  $env:FLOPPY_SIGN_THUMBPRINT
         -> Weg fuer ECHTE Zertifikate. Seit 2023 liegen die Schluessel auf
            USB-Token oder in der Cloud des Anbieters; deren Software blendet
            das Zertifikat in den Speicher ein. Nutzt signtool.exe, falls
            vorhanden (noetig fuer manche Cloud-/Token-Loesungen).

      2) PFX-Datei mit Passwort
           -PfxPath / -PfxPassword  /  $env:FLOPPY_SIGN_PFX + $env:FLOPPY_SIGN_PFX_PASSWORD
         -> fuer Test-Zertifikate (New-FloppyTestCertificate.ps1) und CI.
            Das Passwort landet nie auf einer Kommandozeile.

    ZEITSTEMPEL: Standard http://timestamp.digicert.com. Damit bleibt die
    Signatur gueltig, auch wenn das Zertifikat spaeter ablaeuft.
    -TimestampServer '' schaltet ihn ab (nur zum Offline-Testen).

.PARAMETER Path
    Eine oder mehrere Dateien.

.PARAMETER CheckOnly
    Nichts signieren, nur melden, ob eine Signier-Konfiguration vorhanden ist
    (Exitcode 0 = ja, 2 = nein). Wird vom Build-Skript benutzt.

.EXAMPLE
    .\build\Sign-FloppyFiles.ps1 -Path dist\FloppyHubSetup-1.0.0.exe -Thumbprint 0123ABCD...
.EXAMPLE
    $env:FLOPPY_SIGN_PFX = 'build\certs\floppy-test.pfx'
    $env:FLOPPY_SIGN_PFX_PASSWORD = Get-Content build\certs\floppy-test.pfx.password.txt
    .\build\Sign-FloppyFiles.ps1 -Path FloppyLauncher.ps1
#>

[CmdletBinding()]
param(
    [string[]] $Path,
    [string]   $Thumbprint,
    [string]   $PfxPath,
    [string]   $PfxPassword,
    [string]   $TimestampServer,
    [switch]   $CheckOnly,
    [switch]   $Quiet
)

$ErrorActionPreference = 'Stop'

function Say { param([string]$t, [string]$c = 'Gray') if (-not $Quiet) { Write-Host $t -ForegroundColor $c } }

# --- Konfiguration zusammensuchen ---------------------------------------
$local = Join-Path $PSScriptRoot 'signing.local.psd1'
$cfg = @{}
if (Test-Path -LiteralPath $local) {
    try { $cfg = Import-PowerShellDataFile -LiteralPath $local }
    catch { Say "  signing.local.psd1 unlesbar: $($_.Exception.Message)" 'Yellow' }
}
function Pick {
    foreach ($v in $args) { if (-not [string]::IsNullOrWhiteSpace("$v")) { return "$v" } }
    return $null
}

$Thumbprint  = Pick $Thumbprint  $env:FLOPPY_SIGN_THUMBPRINT   $cfg['Thumbprint']
$PfxPath     = Pick $PfxPath     $env:FLOPPY_SIGN_PFX          $cfg['PfxPath']
$PfxPassword = Pick $PfxPassword $env:FLOPPY_SIGN_PFX_PASSWORD $cfg['PfxPassword']
if (-not $PSBoundParameters.ContainsKey('TimestampServer')) {
    $TimestampServer = Pick $env:FLOPPY_SIGN_TIMESTAMP $cfg['TimestampServer'] 'http://timestamp.digicert.com'
}
if ($env:FLOPPY_SIGN_NO_TIMESTAMP -eq '1' -and -not $PSBoundParameters.ContainsKey('TimestampServer')) { $TimestampServer = '' }
if ($Thumbprint) { $Thumbprint = ($Thumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant() }
if ($PfxPath -and -not [System.IO.Path]::IsPathRooted($PfxPath)) {
    $PfxPath = Join-Path (Split-Path -Parent $PSScriptRoot) $PfxPath
}

$configured = [bool]($Thumbprint -or $PfxPath)
if ($CheckOnly) { if ($configured) { exit 0 } else { exit 2 } }

if (-not $configured) {
    Say '  Kein Signier-Zertifikat konfiguriert (FLOPPY_SIGN_THUMBPRINT oder FLOPPY_SIGN_PFX).' 'Red'
    exit 2
}
if (-not $Path -or $Path.Count -eq 0) { Say '  -Path fehlt.' 'Red'; exit 1 }

# --- Zertifikat laden ---------------------------------------------------
$cert = $null
if ($PfxPath) {
    if (-not (Test-Path -LiteralPath $PfxPath)) { Say "  PFX nicht gefunden: $PfxPath" 'Red'; exit 1 }
    try {
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $PfxPath, $PfxPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
    }
    catch { Say "  PFX konnte nicht geladen werden (Passwort?): $($_.Exception.Message)" 'Red'; exit 1 }
}
else {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
    if (-not $cert) { Say "  Zertifikat $Thumbprint nicht im Zertifikatsspeicher gefunden." 'Red'; exit 1 }
}

if (-not $cert.HasPrivateKey) { Say '  Zum Zertifikat gehoert kein privater Schluessel - signieren unmoeglich.' 'Red'; exit 1 }
$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$ekus = @($cert.Extensions |
        Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
if ($ekus.Count -gt 0 -and $ekus -notcontains $codeSigningOid) {
    Say '  Zertifikat ist nicht fuer Code-Signing zugelassen.' 'Red'; exit 1
}
if ($cert.NotAfter -lt (Get-Date)) { Say "  Zertifikat ist abgelaufen ($($cert.NotAfter))." 'Red'; exit 1 }

# --- signtool nur fuer Speicher-Zertifikate (Token/Cloud) ----------------
$signtool = $null
if (-not $PfxPath) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName |
        Select-Object -Last 1 -ExpandProperty FullName
}

$subject = $cert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
Say "  Signiere als: $subject   (gueltig bis $($cert.NotAfter.ToString('yyyy-MM-dd')))" 'Cyan'
if (-not $TimestampServer) { Say '  ACHTUNG: ohne Zeitstempel - Signatur verfaellt mit dem Zertifikat.' 'Yellow' }

$failed = 0
foreach ($p in $Path) {
    $file = (Resolve-Path -LiteralPath $p -ErrorAction SilentlyContinue).ProviderPath
    if (-not $file) { Say "  FEHLT: $p" 'Red'; $failed++; continue }
    $name = [System.IO.Path]::GetFileName($file)

    $ok = $false
    for ($try = 1; $try -le 3 -and -not $ok; $try++) {
        try {
            if ($signtool) {
                $sa = @('sign', '/fd', 'sha256', '/sha1', $Thumbprint)
                if ($TimestampServer) { $sa += @('/tr', $TimestampServer, '/td', 'sha256') }
                $sa += $file
                $out = & $signtool @sa 2>&1
                if ($LASTEXITCODE -ne 0) { throw ($out -join ' ') }
            }
            else {
                $sp = @{ FilePath = $file; Certificate = $cert; HashAlgorithm = 'SHA256' }
                if ($TimestampServer) { $sp['TimestampServer'] = $TimestampServer }
                $r = Set-AuthenticodeSignature @sp
                if (-not $r.SignerCertificate -or "$($r.Status)" -in @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat')) {
                    throw "Status $($r.Status): $($r.StatusMessage)"
                }
            }
            $ok = $true
        }
        catch {
            if ($try -lt 3) {
                Say "  Versuch $try fehlgeschlagen ($($_.Exception.Message)) - neuer Versuch ..." 'Yellow'
                Start-Sleep -Seconds (2 * $try)
            }
            else { Say "  FEHLER bei ${name}: $($_.Exception.Message)" 'Red' }
        }
    }
    if (-not $ok) { $failed++; continue }

    # Kontrolle: Signatur vorhanden, unveraendert und vom richtigen Zertifikat?
    $check = Get-AuthenticodeSignature -FilePath $file
    if (-not $check.SignerCertificate -or $check.SignerCertificate.Thumbprint -ne $cert.Thumbprint -or "$($check.Status)" -eq 'HashMismatch') {
        Say "  PRUEFUNG FEHLGESCHLAGEN: $name ($($check.Status))" 'Red'
        $failed++
        continue
    }
    $trust = if ("$($check.Status)" -eq 'Valid') { 'vertrauenswuerdig' } else { "signiert, nicht vertrauenswuerdig ($($check.Status))" }
    $ts    = if ($check.TimeStamperCertificate) { 'mit Zeitstempel' } else { 'ohne Zeitstempel' }
    Say ("  ok  {0,-28} {1}, {2}" -f $name, $trust, $ts) 'DarkGray'
}

if ($failed -gt 0) { exit 1 }
exit 0
