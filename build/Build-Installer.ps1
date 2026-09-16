<#
.SYNOPSIS
    Baut FloppyHubSetup-<Version>.exe aus installer\FloppyHub.iss.

.DESCRIPTION
    Sucht den Inno-Setup-Compiler (ISCC.exe), prueft vorher alle Skripte auf
    Syntaxfehler und legt das Ergebnis in dist\ ab.

    Ist Inno Setup nicht installiert, wird der Befehl zum Nachinstallieren
    angezeigt (nichts wird ohne Nachfrage installiert).

.PARAMETER Version
    Versionsnummer fuer das Setup (Vorgabe: aus FloppyHub.iss).

.PARAMETER SkipChecks
    Syntaxpruefung der PowerShell-Dateien ueberspringen.

.EXAMPLE
    .\build\Build-Installer.ps1
.EXAMPLE
    .\build\Build-Installer.ps1 -Version 1.1.0
#>

[CmdletBinding()]
param(
    [string] $Version,
    [switch] $SkipChecks
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$iss  = Join-Path $root 'installer\FloppyHub.iss'
$dist = Join-Path $root 'dist'

function Say { param([string]$t, [string]$c = 'Gray') Write-Host $t -ForegroundColor $c }

Say ''
Say '  FLOPPY HUB  ::  Installer bauen' 'Cyan'
Say '  ---------------------------------------------------------' 'DarkGray'
Say "  Projekt: $root" 'DarkGray'

if (-not (Test-Path -LiteralPath $iss)) { throw "Nicht gefunden: $iss" }

# --- 1) Syntaxpruefung aller PowerShell-Dateien -------------------------
if (-not $SkipChecks) {
    Say ''
    Say '  [1/3] Syntaxpruefung' 'White'
    $bad = 0
    Get-ChildItem -LiteralPath $root -Filter '*.ps1' -File | ForEach-Object {
        $errs = $null; $tokens = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errs)
        if ($errs) {
            $bad++
            Say ("        {0,-24} FEHLER Zeile {1}: {2}" -f $_.Name, $errs[0].Extent.StartLineNumber, $errs[0].Message) 'Red'
        }
        else { Say ("        {0,-24} ok" -f $_.Name) 'DarkGray' }
    }
    if ($bad -gt 0) { throw "$bad Datei(en) mit Syntaxfehlern - Abbruch." }
}
else { Say '  [1/3] Syntaxpruefung uebersprungen' 'DarkYellow' }

# --- 2) Inno-Setup-Compiler finden -------------------------------------
Say ''
Say '  [2/3] Inno Setup suchen' 'White'
$iscc = $null
$candidates = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 5\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
)
foreach ($c in $candidates) {
    if ($c -and (Test-Path -LiteralPath $c -PathType Leaf)) { $iscc = $c; break }
}
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}

if (-not $iscc) {
    Say ''
    Say '  Inno Setup wurde nicht gefunden.' 'Red'
    Say ''
    Say '  Einmalig installieren (eines von beiden):' 'Yellow'
    Say '      winget install --id JRSoftware.InnoSetup -e' 'White'
    Say '      https://jrsoftware.org/isdl.php' 'White'
    Say ''
    Say '  Danach dieses Skript erneut ausfuehren.' 'Yellow'
    Say ''
    Say '  Alternative ohne lokale Installation: per Git-Tag bauen lassen -' 'DarkGray'
    Say '  .github\workflows\release.yml baut das Setup auf GitHub und haengt' 'DarkGray'
    Say '  es an ein Release (siehe README-FloppyHub.md).' 'DarkGray'
    exit 2
}
Say "        gefunden: $iscc" 'DarkGray'

# --- 3) Bauen ----------------------------------------------------------
Say ''
Say '  [3/3] Setup bauen' 'White'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist -Force | Out-Null }

$isccArgs = @("/Qp", "`"$iss`"")
if ($Version) {
    $isccArgs = @("/Qp", "/DAppVersion=$Version", "`"$iss`"")
    Say "        Version: $Version" 'DarkGray'
}

$before = @(Get-ChildItem -LiteralPath $dist -Filter '*.exe' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe ist mit Code $LASTEXITCODE fehlgeschlagen." }

$exe = Get-ChildItem -LiteralPath $dist -Filter '*.exe' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

Say ''
if ($exe) {
    Say '  FERTIG' 'Green'
    Say "      $($exe.FullName)" 'White'
    Say ("      {0:n1} MB" -f ($exe.Length / 1MB)) 'DarkGray'
    Say ''
    Say '  Zum Veroeffentlichen: die Datei an ein GitHub-Release anhaengen,' 'DarkGray'
    Say '  oder einen Tag pushen (git tag v1.0.0 && git push --tags) -' 'DarkGray'
    Say '  dann baut und veroeffentlicht GitHub Actions sie automatisch.' 'DarkGray'
}
else { Say '  Setup gebaut, aber keine EXE in dist\ gefunden?' 'Yellow' }
Say ''
