<#
.SYNOPSIS
    Erzeugt ein SELBST-SIGNIERTES Test-Zertifikat zum Ausprobieren der Signierung.

.DESCRIPTION
    !!! NUR ZUM TESTEN !!!

    Ein selbst erstelltes Zertifikat macht Downloads fuer andere NICHT
    vertrauenswuerdig - Windows, SmartScreen und Browser kennen es nicht.
    Es beweist nur, dass die Signier-Kette (Build-Skript, Inno Setup,
    GitHub Actions) funktioniert, bevor ein echtes Zertifikat gekauft wird.

    Das Zertifikat wird komplett im Speicher erzeugt und als PFX-Datei
    abgelegt. Der Windows-Zertifikatsspeicher wird NICHT veraendert und es
    wird nichts als vertrauenswuerdig eingetragen.

    Ergebnis (per .gitignore vom Repo ausgeschlossen):
        build\certs\floppy-test.pfx
        build\certs\floppy-test.pfx.password.txt
        build\certs\floppy-test.cer              (nur oeffentlicher Teil)

.PARAMETER Name
    Name im Zertifikat. Vorgabe: git user.name.

.PARAMETER Years
    Gueltigkeit in Jahren (Vorgabe 1).

.PARAMETER Force
    Vorhandenes Test-Zertifikat ersetzen.

.EXAMPLE
    .\build\New-FloppyTestCertificate.ps1
#>

[CmdletBinding()]
param(
    [string] $Name,
    [ValidateRange(1, 5)] [int] $Years = 1,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$dir   = Join-Path $PSScriptRoot 'certs'
$pfx   = Join-Path $dir 'floppy-test.pfx'
$pwdTx = "$pfx.password.txt"
$cer   = Join-Path $dir 'floppy-test.cer'

if (-not $Name) {
    try { $Name = (& git -C $root config user.name 2>$null) } catch { }
    if (-not $Name) { $Name = $env:USERNAME }
}

if ((Test-Path -LiteralPath $pfx) -and -not $Force) {
    Write-Host "  Test-Zertifikat existiert bereits: $pfx  (-Force zum Ersetzen)" -ForegroundColor Yellow
    exit 0
}
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$X509 = 'System.Security.Cryptography.X509Certificates'
if (-not ("$X509.CertificateRequest" -as [type])) {
    throw 'CertificateRequest-API nicht verfuegbar (.NET Framework 4.7.2+ noetig).'
}

$rsa = [System.Security.Cryptography.RSA]::Create(3072)
$dn  = New-Object "$X509.X500DistinguishedName" ("CN=$Name (TEST - nicht vertrauenswuerdig), O=Floppy Hub Test")
$req = New-Object "$X509.CertificateRequest" (
    $dn, $rsa,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

$req.CertificateExtensions.Add((New-Object "$X509.X509BasicConstraintsExtension" ($false, $false, 0, $true)))
$req.CertificateExtensions.Add((New-Object "$X509.X509KeyUsageExtension" (
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true)))
$ekus = New-Object System.Security.Cryptography.OidCollection
[void]$ekus.Add((New-Object System.Security.Cryptography.Oid '1.3.6.1.5.5.7.3.3'))   # Code Signing
$req.CertificateExtensions.Add((New-Object "$X509.X509EnhancedKeyUsageExtension" ($ekus, $false)))
$req.CertificateExtensions.Add((New-Object "$X509.X509SubjectKeyIdentifierExtension" ($req.PublicKey, $false)))

$now  = [DateTimeOffset]::Now
$cert = $req.CreateSelfSigned($now.AddMinutes(-5), $now.AddYears($Years))

# Zufaelliges Passwort, nur lokal abgelegt.
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = [Convert]::ToBase64String($bytes)

[System.IO.File]::WriteAllBytes($pfx, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
[System.IO.File]::WriteAllText($pwdTx, $password)
[System.IO.File]::WriteAllBytes($cer, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

Write-Host ''
Write-Host '  TEST-Zertifikat erstellt (NICHT vertrauenswuerdig fuer andere!)' -ForegroundColor Yellow
Write-Host "    Name        : $($cert.Subject)" -ForegroundColor Gray
Write-Host "    Fingerabdr. : $($cert.Thumbprint)" -ForegroundColor Gray
Write-Host "    gueltig bis : $($cert.NotAfter.ToString('yyyy-MM-dd'))" -ForegroundColor Gray
Write-Host "    PFX         : $pfx" -ForegroundColor Gray
Write-Host "    Passwort    : $pwdTx" -ForegroundColor Gray
Write-Host ''
Write-Host '  Benutzen fuer einen Test-Build:' -ForegroundColor Cyan
Write-Host '    .\build\Build-Installer.ps1 -Sign -UseTestCertificate' -ForegroundColor White
Write-Host ''
