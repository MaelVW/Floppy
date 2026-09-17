<#
.SYNOPSIS
    Baut FloppyHubSetup-<Version>.exe aus installer\FloppyHub.iss - auf Wunsch signiert.
    Das Setup enthaelt beide Varianten (App und Konsole).

.DESCRIPTION
    1. prueft alle PowerShell-Skripte auf Syntaxfehler
    2. baut die App-Variante (build\Build-App.ps1: Tests, Motor, Godot-Export)
    3. sucht den Inno-Setup-Compiler (ISCC.exe)
    4. SIGNIERT, sobald ein Zertifikat konfiguriert ist:
         - legt eine Kopie aller Setup-Dateien in build\stage an
           (die Quelldateien im Repo bleiben unveraendert),
         - signiert dort die .ps1-/.vbs-Dateien der Konsole und die EXE/DLL der App,
         - laesst Inno Setup das Setup UND das Deinstallationsprogramm signieren
    5. baut das Setup
    6. prueft die Signatur des fertigen Setups

    Zertifikat konfigurieren: siehe docs\SIGNIERUNG.md bzw.
    build\Sign-FloppyFiles.ps1 (Fingerabdruck oder PFX).

.PARAMETER Version
    Versionsnummer, z. B. 2.0.0-beta.1 (Vorgabe: config/version aus project.godot).

.PARAMETER Sign
    Signieren ist Pflicht - ohne Zertifikat bricht der Build ab.

.PARAMETER NoSign
    Niemals signieren, auch wenn ein Zertifikat konfiguriert ist.

.PARAMETER UseTestCertificate
    Das selbst erstellte Test-Zertifikat aus build\certs verwenden
    (vorher New-FloppyTestCertificate.ps1 ausfuehren). NUR zum Testen.

.PARAMETER NoTimestamp
    Ohne Zeitstempel-Server signieren (offline testen).

.PARAMETER SkipChecks
    Syntaxpruefung ueberspringen.

.PARAMETER SkipAppBuild
    Die App nicht neu bauen, sondern build\app-out verwenden.

.PARAMETER SkipTests
    Beim App-Build die Unit-Tests ueberspringen.

.EXAMPLE
    .\build\Build-Installer.ps1
.EXAMPLE
    .\build\Build-Installer.ps1 -Version 2.0.0 -Sign
.EXAMPLE
    .\build\Build-Installer.ps1 -Sign -UseTestCertificate
#>

[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Sign,
    [switch] $NoSign,
    [switch] $UseTestCertificate,
    [switch] $NoTimestamp,
    [switch] $SkipChecks,
    [switch] $SkipAppBuild,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root      = Split-Path -Parent $PSScriptRoot
$iss       = Join-Path $root 'installer\FloppyHub.iss'
$dist      = Join-Path $root 'dist'
$stage     = Join-Path $PSScriptRoot 'stage'
$appOut    = Join-Path $PSScriptRoot 'app-out'
$signer    = Join-Path $PSScriptRoot 'Sign-FloppyFiles.ps1'
$psExe     = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Say { param([string]$t, [string]$c = 'Gray') Write-Host $t -ForegroundColor $c }

if ($Sign -and $NoSign) { throw '-Sign und -NoSign schliessen sich aus.' }

if (-not $Version) {
    $godotProject = [IO.File]::ReadAllText((Join-Path $root 'app\src\FloppyHub.App\project.godot'))
    if ($godotProject -match 'config/version="([^"]+)"') { $Version = $Matches[1] }
}

Say ''
Say '  FLOPPY HUB  ::  Installer bauen' 'Cyan'
Say '  ---------------------------------------------------------' 'DarkGray'
Say "  Projekt: $root" 'DarkGray'
Say "  Version: $Version" 'DarkGray'
if (-not (Test-Path -LiteralPath $iss)) { throw "Nicht gefunden: $iss" }

# --- 1) Syntaxpruefung -----------------------------------------------------
Say ''
if (-not $SkipChecks) {
    Say '  [1/6] Syntaxpruefung' 'White'
    $bad = 0
    Get-ChildItem -LiteralPath $root -Filter '*.ps1' -File | ForEach-Object {
        $errs = $null; $tokens = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errs)
        if ($errs) {
            $bad++
            Say ("        {0,-26} FEHLER Zeile {1}: {2}" -f $_.Name, $errs[0].Extent.StartLineNumber, $errs[0].Message) 'Red'
        }
        else { Say ("        {0,-26} ok" -f $_.Name) 'DarkGray' }
    }
    if ($bad -gt 0) { throw "$bad Datei(en) mit Syntaxfehlern - Abbruch." }
}
else { Say '  [1/6] Syntaxpruefung uebersprungen' 'DarkYellow' }

# --- 2) App bauen ------------------------------------------------------------
Say ''
if ($SkipAppBuild) {
    Say '  [2/6] App-Build uebersprungen (build\app-out wird verwendet)' 'DarkYellow'
}
else {
    Say '  [2/6] App bauen' 'White'
    $appArgs = @{ Version = $Version }
    if ($SkipTests) { $appArgs['SkipTests'] = $true }
    & (Join-Path $PSScriptRoot 'Build-App.ps1') @appArgs
}
foreach ($required in 'FloppyLauncher.exe', 'FloppyHub.exe', 'FloppyHub.pck') {
    if (-not (Test-Path -LiteralPath (Join-Path $appOut $required))) {
        throw "App-Datei fehlt: build\app-out\$required  (zuerst .\build\Build-App.ps1 ausfuehren)"
    }
}

# --- 3) Inno Setup ---------------------------------------------------------
Say ''
Say '  [3/6] Inno Setup suchen' 'White'
$iscc = $null
foreach ($c in @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
    if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) { $iscc = $c; break }
}
if (-not $iscc) { $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($cmd) { $iscc = $cmd.Source } }
if (-not $iscc) {
    Say '  Inno Setup wurde nicht gefunden.' 'Red'
    Say '      winget install --id JRSoftware.InnoSetup -e' 'White'
    exit 2
}
Say "        gefunden: $iscc" 'DarkGray'

# --- 4) Signieren? -----------------------------------------------------------
Say ''
Say '  [4/6] Signatur' 'White'
if ($UseTestCertificate) {
    $testPfx = Join-Path $PSScriptRoot 'certs\floppy-test.pfx'
    if (-not (Test-Path -LiteralPath $testPfx)) {
        throw 'Kein Test-Zertifikat. Zuerst ausfuehren: .\build\New-FloppyTestCertificate.ps1'
    }
    $env:FLOPPY_SIGN_PFX          = $testPfx
    $env:FLOPPY_SIGN_PFX_PASSWORD = (Get-Content -LiteralPath "$testPfx.password.txt" -Raw).Trim()
    Remove-Item Env:\FLOPPY_SIGN_THUMBPRINT -ErrorAction SilentlyContinue
    Say '        TEST-Zertifikat - Ergebnis ist fuer andere NICHT vertrauenswuerdig!' 'Yellow'
}
if ($NoTimestamp) { $env:FLOPPY_SIGN_NO_TIMESTAMP = '1' }

$doSign = $false
if (-not $NoSign) {
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $signer -CheckOnly
    $doSign = ($LASTEXITCODE -eq 0)
}
if ($Sign -and -not $doSign) {
    throw 'Signieren verlangt (-Sign), aber kein Zertifikat konfiguriert. Siehe docs\SIGNIERUNG.md.'
}
if ($doSign) { Say '        Zertifikat gefunden - Setup, Skripte und App werden signiert.' 'Green' }
else         { Say '        kein Zertifikat konfiguriert - alles bleibt UNSIGNIERT.' 'DarkYellow' }

# --- 5) Setup bauen (bei Signatur aus signierter Kopie) -------------------------
Say ''
Say '  [5/6] Setup bauen' 'White'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

$isccArgs = @('/Qp')
if ($Version) { $isccArgs += "/DAppVersion=$Version" }
$isccArgs += "/DAppDir=$appOut"

if ($doSign) {
    # Alle Quelldateien, die das Setup einpackt, direkt aus dem .iss ermitteln.
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    $sources = [regex]::Matches((Get-Content -LiteralPath $iss -Raw), 'Source:\s*"\{#SrcDir\}\\([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    foreach ($rel in $sources) {
        if ($rel -match '^(.*)\\\*$') {
            $sub = $Matches[1]
            Copy-Item -LiteralPath (Join-Path $root $sub) -Destination (Join-Path $stage $sub) -Recurse -Force
        }
        else {
            $dest = Join-Path $stage $rel
            $destDir = Split-Path -Parent $dest
            if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
            Copy-Item -LiteralPath (Join-Path $root $rel) -Destination $dest -Force
        }
    }
    $stageApp = Join-Path $stage 'app-out'
    Copy-Item -LiteralPath $appOut -Destination $stageApp -Recurse -Force
    Say "        $($sources.Count) Eintraege + App nach build\stage kopiert" 'DarkGray'

    # Konsole: alle Skripte. App: eigene EXE/DLL (nicht die .NET-Laufzeit von Microsoft).
    $toSign = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object {
            $_.Extension -in '.ps1', '.vbs' -or $_.Name -in 'FloppyLauncher.exe', 'FloppyHub.exe', 'FloppyHub.dll', 'Floppy.Core.dll'
        } | Select-Object -ExpandProperty FullName)
    Say "        signiere $($toSign.Count) Dateien ..." 'DarkGray'
    # Im selben Prozess aufrufen: ueber powershell.exe -File wuerde das Array
    # in Einzelargumente zerfallen.
    & $signer -Path $toSign
    if ($LASTEXITCODE -ne 0) { throw 'Signieren fehlgeschlagen.' }

    $signCmd = '$q' + $psExe + '$q -NoProfile -ExecutionPolicy Bypass -File $q' + $signer + '$q -Quiet -Path $f'
    $isccArgs = @($isccArgs | Where-Object { $_ -notlike '/DAppDir=*' })
    $isccArgs += @("/DSrcDir=$stage", "/DAppDir=$stageApp", '/DSign', "/Sfloppysign=$signCmd")
}

$isccArgs += $iss
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe ist mit Code $LASTEXITCODE fehlgeschlagen." }

$exe = Get-ChildItem -LiteralPath $dist -Filter "FloppyHubSetup-$Version.exe" -File | Select-Object -First 1
if (-not $exe) { $exe = Get-ChildItem -LiteralPath $dist -Filter '*.exe' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1 }
if (-not $exe) { throw 'Setup gebaut, aber keine EXE in dist\ gefunden.' }

# --- 6) Ergebnis pruefen -----------------------------------------------------------
Say ''
Say '  [6/6] Ergebnis' 'White'
$sig = Get-AuthenticodeSignature -FilePath $exe.FullName
if ($doSign) {
    if (-not $sig.SignerCertificate) { throw 'Setup sollte signiert sein, hat aber keine Signatur.' }
    $who = $sig.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    Say "        signiert von : $who" 'Green'
    Say "        Status       : $($sig.Status)$(if ("$($sig.Status)" -ne 'Valid') { '  (nicht vertrauenswuerdig - bei Test-Zertifikat normal)' })" 'DarkGray'
    Say "        Zeitstempel  : $(if ($sig.TimeStamperCertificate) { 'ja' } else { 'nein' })" 'DarkGray'
}
else {
    Say '        unsigniert (Windows zeigt "Unbekannter Herausgeber")' 'DarkYellow'
}

$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
Say ''
Say '  FERTIG' 'Green'
Say "      $($exe.FullName)" 'White'
Say ("      {0:n1} MB   SHA256 {1}" -f ($exe.Length / 1MB), $hash) 'DarkGray'
Say ''
