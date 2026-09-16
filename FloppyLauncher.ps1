<#
.SYNOPSIS
    Floppy Launcher - wartet auf eine Diskette in Laufwerk A: und startet das
    darauf hinterlegte Steam-Spiel oder Programm.

.DESCRIPTION
    Vereint die frueheren zwei Programme (PowerShell-Steam-Launcher +
    C#-EXE-Launcher) in einem Skript. Es wird ausschliesslich das
    Diskettenlaufwerk A: geprueft (alle 3 Sekunden).

    Referenzdatei im Wurzelverzeichnis der Diskette (Standard: game.txt,
    floppy.txt, launch.txt). Schluessel (eine Angabe pro Zeile,
    '#' oder ';' = Kommentar):

        id=1234567             -> startet  steam://rungameid/1234567
        steam=1234567          -> Alias fuer id=
        run=Ordner\Spiel.exe   -> EXE RELATIV ZUR DISKETTE (startet sofort)
        exe=Spiel.exe          -> Alias fuer run=
        pcrun=C:\Spiele\x.exe  -> EXE AUF DEM PC (absoluter Pfad, mit Rueckfrage)
        args=-fullscreen       -> optionale Startargumente

    Nur die reine Zahl in der Datei ("1234567") gilt ebenfalls als Steam-ID.
    Ohne Referenzdatei wird die Diskette nach ausfuehrbaren Dateien
    (.exe/.bat/.cmd) durchsucht: genau eine -> Start; mehrere -> Rueckfrage.

    SICHERHEIT bei pcrun= / mehreren gefundenen EXE:
      * Pfade unterhalb von C:\Windows (bzw. -BlockedRoots) werden abgelehnt.
      * Optional: mit -AllowedRoots eine Positivliste erzwingen.
      * Die Rueckfrage laeuft IMMER in einem eigenen, zweiten Terminalfenster
        ("Floppy-Interface", Retro-Konsole). Das Hauptfenster ueberwacht nur
        weiter die Diskette. Bei mehreren EXE zeigt das Interface eine Liste
        zur Auswahl. Ohne Antwort bricht es nach -ConfirmTimeout Sekunden ab.
      * Steam-IDs und run= von der Diskette starten immer ohne Rueckfrage.
      * Mit -NonInteractive wird gar nicht gefragt: solche Starts entfallen.

.PARAMETER FloppyDrive       Laufwerksbuchstabe der Diskette. Standard: A:
.PARAMETER BlockedRoots      Gesperrte Ordner fuer pcrun=. Standard: C:\Windows
.PARAMETER AllowedRoots      Wenn gesetzt: pcrun= NUR unterhalb dieser Ordner.
.PARAMETER PollSeconds       Pruefintervall. Standard: 3 Sekunden.
.PARAMETER ConfirmTimeout    Auto-Abbruch des Interface-Fensters. Standard: 90 s.
.PARAMETER NonInteractive    Nie fragen (kein Interface) -> bestaetigungs-
                             pflichtige Starts werden uebersprungen.
.PARAMETER DryRun            Nur anzeigen, was gestartet WUERDE. Nichts starten.
.PARAMETER RunOnce           Nur ein Durchlauf (zum Testen).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\FloppyLauncher.ps1

.NOTES
    Dateiname bewusst unveraendert: FloppyLauncher.ps1
#>

[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z]:?$')]
    [string]   $FloppyDrive          = 'A:',
    [string[]] $ReferenceFileNames   = @('game.txt', 'floppy.txt', 'launch.txt'),
    [string[]] $ExecutableExtensions = @('.exe', '.bat', '.cmd'),
    [string[]] $BlockedRoots         = @("$env:SystemRoot"),
    [string[]] $AllowedRoots         = @(),
    [ValidateRange(1, 3600)]
    [int]      $PollSeconds          = 3,
    [ValidateRange(5, 3600)]
    [int]      $ConfirmTimeout       = 90,
    [string]   $LogFile,
    [ValidateRange(0, 1048576)]
    [int]      $LogMaxKB             = 512,
    [switch]   $NonInteractive,
    [switch]   $DryRun,
    [switch]   $RunOnce,
    [switch]   $AllowMultiple
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Den modalen Windows-Dialog "Kein Datentraeger im Laufwerk. Bitte Datentraeger
# in Laufwerk A: einlegen" unterdruecken - der blockiert sonst das ganze Skript,
# wenn beim Pruefen kurz kein Medium im Diskettenlaufwerk ist.
try {
    if (-not ('Win32.FloppyErr' -as [type])) {
        Add-Type -Namespace Win32 -Name FloppyErr -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetErrorMode(uint uMode);
'@ -ErrorAction Stop 3>$null 4>$null 5>$null 6>$null
    }
    # SEM_FAILCRITICALERRORS(1) | SEM_NOGPFAULTERRORBOX(2) | SEM_NOOPENFILEERRORBOX(0x8000)
    [void][Win32.FloppyErr]::SetErrorMode(0x8003)
} catch { }

# Ein unerwarteter Fehler soll das Skript nicht STILL beenden, sondern eine
# Zeile hinterlassen (Terminal + Log), damit man die Ursache sieht.
trap {
    $msg = "$($_.Exception.Message)"
    $pos = "$($_.InvocationInfo.PositionMessage)".Trim()
    try { Write-Host "FLOPPY LAUNCHER - ABBRUCH: $msg" -ForegroundColor Red } catch { }
    try {
        if ($LogFile) {
            $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
            Add-Content -LiteralPath $LogFile -Encoding UTF8 -Value @(
                "$stamp [ERROR] TRAP: $msg"
                "$stamp [ERROR]   $pos"
                "$stamp [ERROR]   $($_.ScriptStackTrace)"
            )
        }
    } catch { }
    continue
}

# Standard-Logpfad erst hier bestimmen (verhindert Fehler, wenn $PSScriptRoot
# leer ist, z. B. beim Einfuegen des Skripts in die Konsole).
# Ist der Programmordner schreibgeschuetzt (Installation nach C:\Program Files),
# wird automatisch auf %LOCALAPPDATA%\FloppyHub ausgewichen.
function Get-WritableDir {
    param([string] $Preferred)
    try {
        $probe = Join-Path $Preferred (".floppy-write-test-" + [guid]::NewGuid().ToString('N'))
        [System.IO.File]::WriteAllText($probe, 'x')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        return $Preferred
    }
    catch {
        $fallback = Join-Path $env:LOCALAPPDATA 'FloppyHub'
        try { if (-not (Test-Path -LiteralPath $fallback)) { New-Item -ItemType Directory -Path $fallback -Force | Out-Null } } catch { }
        return $fallback
    }
}

if (-not $PSBoundParameters.ContainsKey('LogFile')) {
    $baseDir =
        if     ($PSScriptRoot)  { $PSScriptRoot }
        elseif ($PSCommandPath) { Split-Path -Parent $PSCommandPath }
        else                    { (Get-Location).Path }
    $LogFile = Join-Path (Get-WritableDir $baseDir) 'FloppyLauncher.log'
}

$DriveLetter = $FloppyDrive.Substring(0, 1).ToUpper()   # "A"
$DriveRoot   = "$DriveLetter`:\"                          # "A:\"

# Sperr-/Positivlisten aufloesen. Ein kaputter Eintrag darf den Start NICHT
# abbrechen - er wird uebersprungen (und spaeter im Log gemeldet).
$script:RootListProblems = @()
function Resolve-RootList {
    param([string[]] $List, [string] $Label)
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $List) {
        if ([string]::IsNullOrWhiteSpace($entry)) { continue }
        $e = [Environment]::ExpandEnvironmentVariables("$entry".Trim().Trim('"'))
        try   { $out.Add(([System.IO.Path]::GetFullPath($e)).TrimEnd('\')) }
        catch { $script:RootListProblems += "$Label-Eintrag ungueltig und ignoriert: '$entry'" }
    }
    return , $out.ToArray()
}
$BlockedRootsFull = Resolve-RootList -List $BlockedRoots -Label 'BlockedRoots'
$AllowedRootsFull = Resolve-RootList -List $AllowedRoots -Label 'AllowedRoots'

# Echte PowerShell-EXE fuer das zweite Terminal ermitteln.
# WICHTIG: NICHT den aktuellen Host nehmen. Unter VS Code / ISE ist das nicht
# powershell.exe, und "Start-Process <host> -EncodedCommand ..." wuerde dann
# haengen (genau das Symptom: startet, keine weitere Ausgabe).
$SelfExe = $null
foreach ($cand in @(
        (Join-Path $PSHOME 'powershell.exe')
        (Join-Path $PSHOME 'pwsh.exe')
        "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
        "$env:SystemRoot\SysNative\WindowsPowerShell\v1.0\powershell.exe"
    )) {
    if ($cand -and (Test-Path -LiteralPath $cand -PathType Leaf)) { $SelfExe = $cand; break }
}
if (-not $SelfExe) {
    $gc = Get-Command -Name 'powershell.exe', 'pwsh.exe' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $SelfExe = if ($gc) { $gc.Source } else { 'powershell.exe' }
}

# ---------------------------------------------------------------------------
# [Floppy Hub] Basisordner + optionale Zusatzdateien (rein additiv, der
# Launcher laeuft auch ohne sie).
#   FloppyInterface.ps1 = Retro-Skin fuers Bestaetigungsfenster
#   FloppyHub.ps1       = Menue, das eine Diskette mit "hub=1" oeffnet
# ---------------------------------------------------------------------------
$FloppyHome =
    if     ($PSScriptRoot)  { $PSScriptRoot }
    elseif ($PSCommandPath) { Split-Path -Parent $PSCommandPath }
    else                    { (Get-Location).Path }
$SkinScript = Join-Path $FloppyHome 'FloppyInterface.ps1'
$HubScript  = Join-Path $FloppyHome 'FloppyHub.ps1'
$HubKeys    = @('hub', 'hubmenu', 'menu', 'floppyhub')

# ---------------------------------------------------------------------------
# Hilfsfunktionen
# ---------------------------------------------------------------------------

$script:LogWriteCount = 0

function Limit-LogSize {
    # Notbremse gegen endlos wachsende Logs: ab $LogMaxKB wird einmalig nach
    # <name>.old rotiert. (Der Floppy Hub leert das Log zusaetzlich beim
    # Schliessen - das hier greift auch ohne Hub.)
    if (-not $LogFile -or $LogMaxKB -le 0) { return }
    try {
        $item = Get-Item -LiteralPath $LogFile -ErrorAction Stop
        if ($item.Length -lt ($LogMaxKB * 1KB)) { return }
        Copy-Item -LiteralPath $LogFile -Destination "$LogFile.old" -Force -ErrorAction Stop
        Set-Content -LiteralPath $LogFile -Value @() -Encoding UTF8 -Force -ErrorAction Stop
        Add-Content -LiteralPath $LogFile -Encoding UTF8 -ErrorAction SilentlyContinue -Value (
            '{0} [INFO ] Log rotiert (war {1} KB), alte Zeilen in {2}.old' -f `
                (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), [math]::Round($item.Length / 1KB, 1), (Split-Path -Leaf $LogFile))
    }
    catch { }
}

function Write-Log {
    param(
        [Parameter(Mandatory)][string] $Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR', 'ASK')][string] $Level = 'INFO'
    )
    $line = '{0} [{1,-5}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    $color = switch ($Level) {
        'OK'    { 'Green' }
        'WARN'  { 'Yellow' }
        'ERROR' { 'Red' }
        'ASK'   { 'Magenta' }
        default { 'Cyan' }
    }
    try { Write-Host $line -ForegroundColor $color } catch { }
    if ($LogFile) {
        try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch { }
        # Groesse nur alle 50 Zeilen pruefen (spart Datei-I/O).
        $script:LogWriteCount++
        if ($script:LogWriteCount % 50 -eq 0) { Limit-LogSize }
    }
}

function Get-InterfaceScript {
    # Der Inhalt des ZWEITEN Terminals ("Floppy-Interface", Retro-Konsole).
    # Laeuft als eigener powershell-Prozess. Kommunikation nur ueber
    # Umgebungsvariablen (Eingabe) und den Exitcode (Ausgabe):
    #   Exitcode 0      = abgebrochen / nichts waehlen
    #   Exitcode 1..N   = Kandidat Nr. N soll gestartet werden
    return @'
$ErrorActionPreference = "SilentlyContinue"
$WIDTH = 74

try {
    $ui = $Host.UI.RawUI
    $ui.WindowTitle     = "FLOPPY LAUNCHER  ::  disketten-interface"
    $ui.BackgroundColor = "Black"
    $ui.ForegroundColor = "Green"
    try { $ui.BufferSize = New-Object System.Management.Automation.Host.Size (($WIDTH + 6), 900) } catch { }
    try { $ui.WindowSize = New-Object System.Management.Automation.Host.Size (($WIDTH + 6), 34) } catch { }
} catch { }
Clear-Host

$BAR = [char]0x2550
$VBAR = [char]0x2551
function Frame { param([string]$l, [string]$r) Write-Host ("  " + $l + ([string]$BAR * ($WIDTH + 2)) + $r) -ForegroundColor DarkGreen }
function Row   { param([string]$s, [string]$c = "Green")
    if ($s.Length -gt $WIDTH) { $s = $s.Substring(0, $WIDTH) }
    Write-Host ("  " + $VBAR + " ") -NoNewline -ForegroundColor DarkGreen
    Write-Host ($s.PadRight($WIDTH))  -NoNewline -ForegroundColor $c
    Write-Host (" " + $VBAR)          -ForegroundColor DarkGreen
}
function Gap { Row "" }
function Short { param([string]$p, [int]$max = 62)
    if ($p.Length -le $max) { return $p }
    return "..." + $p.Substring($p.Length - ($max - 3))
}

$list = @()
if ($env:FLOPPY_IF_LIST) { $list = $env:FLOPPY_IF_LIST -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
if ($list.Count -eq 0) { exit 0 }
$argline = $env:FLOPPY_IF_ARGS
$drive   = $env:FLOPPY_IF_DRIVE
$timeout = 90 ; [void][int]::TryParse($env:FLOPPY_IF_TIMEOUT, [ref]$timeout)

Write-Host ""
Frame ([char]0x2554) ([char]0x2557)
Row "   _______  FLOPPY  LAUNCHER" "Cyan"
Row "  /_____ /|  physische steam-library" "DarkCyan"
Row "  |      ||  disketten-interface  ::  sicherheitsabfrage" "DarkCyan"
Row "  |______|/" "DarkCyan"
Frame ([char]0x2560) ([char]0x2563)
Gap
Row ("  datentraeger : {0}" -f $drive) "Green"
if ($list.Count -eq 1) {
    Row "  anforderung  : ein programm auf dem PC soll gestartet werden" "Yellow"
} else {
    Row ("  anforderung  : {0} ausfuehrbare dateien gefunden" -f $list.Count) "Yellow"
    Row "                 bitte GENAU EINE auswaehlen" "Yellow"
}
if ($argline) { Row ("  argumente    : {0}" -f (Short $argline 60)) "DarkYellow" }
Gap
Frame ([char]0x255F) ([char]0x2562)
Gap
for ($i = 0; $i -lt $list.Count; $i++) {
    Row ("    [ {0} ]   {1}" -f ($i + 1), (Short $list[$i])) "White"
}
Gap
Frame ([char]0x255A) ([char]0x255D)
Write-Host ""
Write-Host "   WARNUNG: nur bestaetigen, wenn du dieser diskette vertraust." -ForegroundColor Red
Write-Host ""
if ($list.Count -eq 1) {
    Write-Host "     " -NoNewline
    Write-Host " J " -NoNewline -ForegroundColor Black -BackgroundColor Green
    Write-Host " = starten        " -NoNewline -ForegroundColor Green
    Write-Host " N " -NoNewline -ForegroundColor Black -BackgroundColor Red
    Write-Host " = abbrechen" -ForegroundColor Red
} else {
    Write-Host ("     Zahl 1-{0} = starten         " -f $list.Count) -NoNewline -ForegroundColor Green
    Write-Host " N " -NoNewline -ForegroundColor Black -BackgroundColor Red
    Write-Host " = abbrechen" -ForegroundColor Red
}
Write-Host ""

$deadline = (Get-Date).AddSeconds($timeout)
$choice = 0
try {
    while ((Get-Date) -lt $deadline) {
        $rem = [int][math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        Write-Host ("`r   [ auto-abbruch in {0,3} s ]   >>> " -f $rem) -NoNewline -ForegroundColor DarkYellow
        if ([Console]::KeyAvailable) {
            $k = [Console]::ReadKey($true)
            $c = ([string]$k.KeyChar).ToUpper()
            if ($k.Key -eq "Escape" -or $c -eq "N") { $choice = 0; break }
            if ($list.Count -eq 1) {
                if ($c -eq "J" -or $c -eq "Y") { $choice = 1; break }
            } elseif ($c -match "^[1-9]$" -and [int]$c -le $list.Count) {
                $choice = [int]$c; break
            }
        }
        Start-Sleep -Milliseconds 120
    }
} catch {
    # Kein Tastatur-Zugriff (Eingabe umgeleitet o. Ae.). Sicherste Vorgabe:
    # NICHT starten. Keine blockierende Read-Host-Abfrage an dieser Stelle.
    $choice = 0
}

Write-Host ""
Write-Host ""
if ($choice -ge 1) {
    Write-Host ("   >> STARTE:  {0}" -f $list[$choice - 1]) -ForegroundColor Black -BackgroundColor Green
} else {
    Write-Host "   >> ABGEBROCHEN - es wird nichts ausgefuehrt." -ForegroundColor White -BackgroundColor Red
}
Start-Sleep -Milliseconds 1100
exit $choice
'@
}

function Read-ChoiceInline {
    # Notfall-Fallback im Hauptfenster, falls kein zweites Terminal moeglich ist.
    param([Parameter(Mandatory)][string[]] $Candidates)

    # Ohne bedienbares Terminal (versteckter Autostart) NICHT blockieren.
    $interactive = $true
    try { if ([Console]::IsInputRedirected) { $interactive = $false } } catch { $interactive = $false }
    try { if (-not [Environment]::UserInteractive) { $interactive = $false } } catch { }
    if (-not $interactive) {
        Write-Log "Keine bedienbare Konsole fuer die Rueckfrage - nichts gestartet." 'WARN'
        return $null
    }

    try {
        Write-Host ''
        Write-Host '  === FLOPPY LAUNCHER - Bestaetigung (Fallback) ===' -ForegroundColor Yellow
        for ($i = 0; $i -lt $Candidates.Count; $i++) {
            Write-Host ("   [{0}] {1}" -f ($i + 1), $Candidates[$i]) -ForegroundColor White
        }
        $a = if ($Candidates.Count -eq 1) { Read-Host '  Ausfuehren? (J/N)' }
             else { Read-Host ('  Nummer 1-{0} oder N' -f $Candidates.Count) }
    }
    catch { return $null }
    $a = ([string]$a).Trim()
    if ($Candidates.Count -eq 1 -and $a -match '^(j|ja|y|yes)$') { return $Candidates[0] }
    if ($a -match '^[1-9]$' -and [int]$a -le $Candidates.Count)   { return $Candidates[[int]$a - 1] }
    return $null
}

function Request-LaunchChoice {
    # Oeffnet das zweite Terminal (Interface) und liefert den gewaehlten Pfad
    # zurueck ($null = abgebrochen). Das Hauptfenster wartet solange.
    param(
        [Parameter(Mandatory)][string[]] $Candidates,
        [AllowNull()][string] $Arguments
    )

    if ($NonInteractive) {
        Write-Log "Bestaetigung noetig, aber -NonInteractive gesetzt. Uebersprungen: $($Candidates -join ' | ')" 'WARN'
        return $null
    }

    $cands = @($Candidates | Select-Object -First 9)   # Exitcode traegt 1..9
    if ($Candidates.Count -gt $cands.Count) {
        Write-Log "Mehr als 9 EXE - nur die ersten 9 werden im Interface angeboten." 'WARN'
    }
    Write-Log ("Interface-Fenster wird geoeffnet ({0} Kandidat[en])." -f $cands.Count) 'ASK'

    # [Floppy Hub] externen Retro-Skin (FloppyInterface.ps1) bevorzugen,
    # sonst die eingebaute Version. Faellt der Skin aus -> Exitcode != 1..9
    # -> Launcher startet nichts (sichere Vorgabe).
    $ifSource =
        if (Test-Path -LiteralPath $SkinScript -PathType Leaf) {
            try { Get-Content -LiteralPath $SkinScript -Raw -ErrorAction Stop } catch { Get-InterfaceScript }
        }
        else { Get-InterfaceScript }
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($ifSource))
    $chosen = $null
    try {
        $env:FLOPPY_IF_LIST    = ($cands -join "`n")
        $env:FLOPPY_IF_ARGS    = $Arguments
        $env:FLOPPY_IF_DRIVE   = $DriveRoot
        $env:FLOPPY_IF_TIMEOUT = [string]$ConfirmTimeout

        # Eigenen .NET-Prozess starten (eigenes Fenster) und mit HARTEM Timeout
        # warten. So haengt der Launcher NICHT ewig, falls das Fenster nicht
        # sauber beendet (VS Code, Rechte, kaputter Skin ...). Das Interface
        # bricht selbst nach ConfirmTimeout ab; wir geben 30 s Puffer.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName        = $SelfExe
        $psi.Arguments       = '-NoProfile -ExecutionPolicy Bypass -EncodedCommand ' + $enc
        $psi.UseShellExecute = $true          # -> eigenes Konsolenfenster, erbt die $env:FLOPPY_IF_*
        $proc = [System.Diagnostics.Process]::Start($psi)

        if (-not $proc) {
            Write-Log "Interface-Prozess konnte nicht gestartet werden - Fallback im Hauptfenster." 'WARN'
            $chosen = Read-ChoiceInline -Candidates $cands
        }
        elseif ($proc.WaitForExit(($ConfirmTimeout + 30) * 1000)) {
            $idx = [int]$proc.ExitCode
            if ($idx -ge 1 -and $idx -le $cands.Count) { $chosen = $cands[$idx - 1] }
        }
        else {
            Write-Log "Interface-Fenster reagiert nicht - wird beendet (gilt als Abbruch)." 'WARN'
            try { $proc.Kill() } catch { }
        }
    }
    catch {
        Write-Log "Zweites Terminal nicht moeglich ($($_.Exception.Message)) - Fallback im Hauptfenster." 'WARN'
        $chosen = Read-ChoiceInline -Candidates $cands
    }
    finally {
        'FLOPPY_IF_LIST', 'FLOPPY_IF_ARGS', 'FLOPPY_IF_DRIVE', 'FLOPPY_IF_TIMEOUT' |
            ForEach-Object { Remove-Item "Env:\$_" -ErrorAction SilentlyContinue }
    }

    if ($chosen) { Write-Log "Interface: bestaetigt -> $chosen" 'OK' }
    else         { Write-Log "Interface: abgebrochen, nichts gewaehlt." 'INFO' }
    return $chosen
}

function Test-PathUnder {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Root)
    try {
        $p = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
        $r = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    } catch { return $false }
    if ($p.Equals($r, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $p.StartsWith($r + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function ConvertFrom-QuotedValue {
    # Entfernt umschliessende Anfuehrungszeichen. Ein aus dem Explorer
    # kopierter Pfad ("Als Pfad kopieren") bringt " mit - und JEDE
    # .NET-Pfadfunktion wirft dann "Illegales Zeichen im Pfad".
    param([string] $Value)
    if ($null -eq $Value) { return $null }
    $v = "$Value".Trim()
    while ($v.Length -ge 2 -and
           (($v[0] -eq '"' -and $v[-1] -eq '"') -or ($v[0] -eq "'" -and $v[-1] -eq "'"))) {
        $v = $v.Substring(1, $v.Length - 2).Trim()
    }
    return ($v -replace '"', '').Trim()
}

function Test-PathRootedSafe {
    # IsPathRooted wirft bei unerlaubten Zeichen - hier nie.
    param([string] $Path)
    try { return [System.IO.Path]::IsPathRooted($Path) } catch { return $false }
}

function Test-ExecutableExtension {
    param([Parameter(Mandatory)][string] $Path)
    $ext = try { [System.IO.Path]::GetExtension($Path) } catch { '' }
    return ($ExecutableExtensions -contains $ext)
}

function Resolve-OnDrive {
    # Loest einen relativen Pfad gegen das Diskettenwurzelverzeichnis auf und
    # stellt sicher, dass das Ergebnis WIRKLICH auf der Diskette liegt.
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $RelativeOrAbsolute
    )
    $RelativeOrAbsolute = ConvertFrom-QuotedValue $RelativeOrAbsolute
    if ([string]::IsNullOrWhiteSpace($RelativeOrAbsolute)) {
        Write-Log "run= ist leer." 'ERROR'
        return $null
    }
    if (Test-PathRootedSafe $RelativeOrAbsolute) {
        Write-Log "run= erwartet einen Pfad relativ zur Diskette. Fuer PC-Pfade bitte pcrun= verwenden: $RelativeOrAbsolute" 'ERROR'
        return $null
    }
    try { $full = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativeOrAbsolute)) } catch { return $null }

    if (-not (Test-PathUnder -Path $full -Root $Root)) {
        Write-Log "run= zeigt aus der Diskette heraus, ignoriert: $RelativeOrAbsolute" 'WARN'
        return $null
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        Write-Log "run=-Datei fehlt auf der Diskette: $RelativeOrAbsolute" 'ERROR'
        return $null
    }
    if (-not (Test-ExecutableExtension $full)) {
        Write-Log "run=-Datei ist nicht ausfuehrbar: $RelativeOrAbsolute" 'ERROR'
        return $null
    }
    return $full
}

function Resolve-PcPath {
    # Prueft einen absoluten PC-Pfad fuer pcrun= gegen Sperr-/Positivliste.
    param([Parameter(Mandatory)][string] $InputPath)

    $InputPath = ConvertFrom-QuotedValue $InputPath
    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        Write-Log "pcrun= ist leer." 'ERROR'
        return $null
    }
    if (-not (Test-PathRootedSafe $InputPath)) {
        Write-Log "pcrun= benoetigt einen absoluten Pfad (z. B. C:\Spiele\x.exe): $InputPath" 'ERROR'
        return $null
    }
    try { $full = [System.IO.Path]::GetFullPath($InputPath) }
    catch {
        Write-Log "pcrun= ist kein gueltiger Pfad ($($_.Exception.Message)): $InputPath" 'ERROR'
        return $null
    }

    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        Write-Log "pcrun=-Datei nicht gefunden: $full" 'ERROR'
        return $null
    }
    if (-not (Test-ExecutableExtension $full)) {
        Write-Log "pcrun=-Datei ist nicht ausfuehrbar: $full" 'ERROR'
        return $null
    }
    foreach ($blocked in $BlockedRootsFull) {
        if (Test-PathUnder -Path $full -Root $blocked) {
            Write-Log "pcrun= liegt in einem gesperrten Systemordner ($blocked) und wird abgelehnt: $full" 'ERROR'
            return $null
        }
    }
    if ($AllowedRootsFull.Count -gt 0) {
        $ok = $false
        foreach ($allowed in $AllowedRootsFull) {
            if (Test-PathUnder -Path $full -Root $allowed) { $ok = $true; break }
        }
        if (-not $ok) {
            Write-Log "pcrun= liegt ausserhalb der erlaubten Ordner (-AllowedRoots) und wird abgelehnt: $full" 'ERROR'
            return $null
        }
    }
    return $full
}

function Read-ReferenceFile {
    param([Parameter(Mandatory)][string] $Path)
    $map = @{}
    foreach ($raw in Get-Content -LiteralPath $Path -ErrorAction Stop) {
        $line = $raw.Trim()
        if (-not $line -or $line.StartsWith('#') -or $line.StartsWith(';')) { continue }
        if ($line -match '^\s*([A-Za-z_]+)\s*[:=]\s*(.+?)\s*$') {
            # Anfuehrungszeichen wegnehmen - sonst scheitert jede Pfadpruefung.
            $map[$Matches[1].ToLowerInvariant()] = (ConvertFrom-QuotedValue $Matches[2])
        }
        elseif ($line -match '^\s*(\d{3,})\s*$') {
            $map['id'] = $Matches[1]
        }
    }
    return $map
}

function Get-DiskSignature {
    # Leichter Fingerabdruck des aktuellen Disketteninhalts (nur das
    # Wurzelverzeichnis - schnell, auch bei einem echten Diskettenlaufwerk,
    # das alle 3 s abgefragt wird). Aendert er sich nicht, wurde dieser
    # Zustand bereits behandelt (kein erneutes Fragen/Starten).
    param([Parameter(Mandatory)][string] $Root)
    $parts = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem -LiteralPath $Root -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $len = if ($_.PSIsContainer) { 'DIR' } else { $_.Length }
        $parts.Add(('{0}|{1}|{2}' -f $_.Name, $len, $_.LastWriteTimeUtc.Ticks))
    }
    if ($parts.Count -eq 0) { return 'empty' }
    return ($parts -join "`n")
}

function Get-LaunchPlan {
    # Ermittelt, WAS von der Diskette gestartet werden soll.
    # Rueckgabe: $null  oder  Objekt mit
    #   Kind=Steam|Process ; Steam ; Candidates[] ; Arguments ; NeedsConfirm
    param([Parameter(Mandatory)][string] $Root)

    $refPath = $null
    foreach ($name in $ReferenceFileNames) {
        $p = Join-Path $Root $name
        if (Test-Path -LiteralPath $p -PathType Leaf) { $refPath = $p; break }
    }

    if ($refPath) {
        $ref = Read-ReferenceFile -Path $refPath

        # --- Steam-ID ---
        $steamId = $null
        foreach ($key in 'id', 'steam', 'steamid', 'gameid') {
            if ($ref.ContainsKey($key)) { $steamId = $ref[$key]; break }
        }
        if ($steamId) {
            if ($steamId -notmatch '^\d+$') {
                Write-Log "Ungueltige Steam-ID in $($refPath): '$steamId'" 'ERROR'
                return $null
            }
            return [pscustomobject]@{
                Kind = 'Steam'; Steam = $steamId; Candidates = @(); Arguments = $null; NeedsConfirm = $false
            }
        }

        # --- [Floppy Hub] Diskette oeffnet das Hub-Menue (hub=1) ---
        foreach ($hk in $HubKeys) {
            if ($ref.ContainsKey($hk)) {
                if (-not (Test-Path -LiteralPath $HubScript -PathType Leaf)) {
                    Write-Log "Hub-Diskette erkannt ($hk=), aber $HubScript fehlt." 'ERROR'
                    return $null
                }
                Write-Log "Hub-Diskette erkannt - oeffne Floppy Hub Menue." 'OK'
                return [pscustomobject]@{
                    Kind = 'Process'; Steam = $null
                    Candidates   = @($SelfExe)
                    Arguments    = ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $HubScript)
                    NeedsConfirm = $false
                }
            }
        }

        $arguments = if ($ref.ContainsKey('args')) { $ref['args'] } else { $null }

        # --- EXE auf dem PC (pcrun=) : immer mit Rueckfrage ---
        $pcRef = $null
        foreach ($key in 'pcrun', 'localrun', 'pcexe') {
            if ($ref.ContainsKey($key)) { $pcRef = $ref[$key]; break }
        }
        if ($pcRef) {
            $pcPath = Resolve-PcPath -InputPath $pcRef
            if (-not $pcPath) { return $null }
            return [pscustomobject]@{
                Kind = 'Process'; Steam = $null; Candidates = @($pcPath); Arguments = $arguments; NeedsConfirm = $true
            }
        }

        # --- EXE auf der Diskette (run=) : startet sofort ---
        $exeRef = $null
        foreach ($key in 'run', 'exe', 'program', 'path') {
            if ($ref.ContainsKey($key)) { $exeRef = $ref[$key]; break }
        }
        if ($exeRef) {
            $exePath = Resolve-OnDrive -Root $Root -RelativeOrAbsolute $exeRef
            if (-not $exePath) { return $null }
            return [pscustomobject]@{
                Kind = 'Process'; Steam = $null; Candidates = @($exePath); Arguments = $arguments; NeedsConfirm = $false
            }
        }

        Write-Log "Referenzdatei $($refPath) enthaelt keine verwertbare Angabe (id= / run= / pcrun=)." 'WARN'
    }

    # --- Keine (brauchbare) Referenzdatei: ausfuehrbare Dateien suchen ---
    $exeFiles = @(
        Get-ChildItem -LiteralPath $Root -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
            Where-Object { Test-ExecutableExtension $_.FullName } |
            Sort-Object FullName |
            Select-Object -First 20
    )
    if ($exeFiles.Count -eq 0) { return $null }
    if ($exeFiles.Count -eq 1) {
        return [pscustomobject]@{
            Kind = 'Process'; Steam = $null; Candidates = @($exeFiles[0].FullName); Arguments = $null; NeedsConfirm = $false
        }
    }

    $names = $exeFiles | Group-Object Name
    if ($names.Name.Count -lt $exeFiles.Count) {
        Write-Log ("Gleicher Dateiname mehrfach auf der Diskette - jede EXE wird einzeln abgefragt.") 'WARN'
    } else {
        Write-Log ("Mehrere ausfuehrbare Dateien auf der Diskette - jede wird einzeln abgefragt.") 'WARN'
    }
    return [pscustomobject]@{
        Kind = 'Process'; Steam = $null; Candidates = @($exeFiles.FullName); Arguments = $null; NeedsConfirm = $true
    }
}

function Start-SteamGame {
    param([Parameter(Mandatory)][string] $SteamId)
    if ($DryRun) { Write-Log "[DRYRUN] wuerde starten: steam://rungameid/$SteamId" 'OK'; return }
    Write-Log "Steam-ID $SteamId gefunden. Starte Steam..." 'OK'
    Start-Process "steam://rungameid/$SteamId"
}

function Start-Target {
    param(
        [Parameter(Mandatory)][string] $Path,
        [AllowNull()][string] $Arguments
    )
    if ($DryRun) { Write-Log "[DRYRUN] wuerde starten: $Path $Arguments" 'OK'; return }
    Write-Log "Starte Programm: $Path $Arguments" 'OK'
    $startParams = @{ FilePath = $Path }
    $workDir = try { [System.IO.Path]::GetDirectoryName($Path) } catch { $null }
    if ($workDir -and (Test-Path -LiteralPath $workDir -PathType Container)) {
        $startParams['WorkingDirectory'] = $workDir
    }
    if ($Arguments) { $startParams['ArgumentList'] = $Arguments }
    Start-Process @startParams
}

function Invoke-LaunchPlan {
    param([Parameter(Mandatory)][pscustomobject] $Plan)

    if ($Plan.Kind -eq 'Steam') { Start-SteamGame -SteamId $Plan.Steam; return }

    if (-not $Plan.NeedsConfirm) {
        Start-Target -Path $Plan.Candidates[0] -Arguments $Plan.Arguments
        return
    }

    if ($DryRun) {
        foreach ($candidate in $Plan.Candidates) {
            Write-Log "[DRYRUN] wuerde Bestaetigung anfordern fuer: $candidate" 'ASK'
        }
        return
    }

    $started = $false
    foreach ($candidate in $Plan.Candidates) {
        if (Confirm-Execution -Target $candidate) {
            Start-Target -Path $candidate -Arguments $Plan.Arguments
            $started = $true
            break
        }
    }
    if (-not $started) { Write-Log "Kein Programm gestartet (nichts bestaetigt)." 'INFO' }
}





# ---------------------------------------------------------------------------
# DEBUGGING / DIAGNOSE
# ---------------------------------------------------------------------------

function Write-DebugState {
    param(
        [string] $Context = 'Allgemein'
    )

    Write-Log "========== DEBUG: $Context ==========" 'INFO'

    try {
        Write-Log "PowerShell: $($PSVersionTable.PSVersion)" 'INFO'
        Write-Log "OS: $([Environment]::OSVersion.VersionString)" 'INFO'
        Write-Log "64 Bit OS: $([Environment]::Is64BitOperatingSystem)" 'INFO'
        Write-Log "64 Bit Prozess: $([Environment]::Is64BitProcess)" 'INFO'
        Write-Log "Skriptpfad: $PSCommandPath" 'INFO'
        Write-Log "Logdatei: $LogFile" 'INFO'
        Write-Log "Laufwerk: $DriveRoot" 'INFO'
        Write-Log "PollSeconds: $PollSeconds" 'INFO'
        Write-Log "ConfirmTimeout: $ConfirmTimeout" 'INFO'
        Write-Log "NonInteractive: $NonInteractive" 'INFO'
        Write-Log "DryRun: $DryRun" 'INFO'
        Write-Log "RunOnce: $RunOnce" 'INFO'
        Write-Log "SelfExe: $SelfExe" 'INFO'

        $drive = [System.IO.DriveInfo]::new($DriveLetter)

        Write-Log "Drive vorhanden: $($drive.Name)" 'INFO'
        Write-Log "Drive bereit: $($drive.IsReady)" 'INFO'

        if ($drive.IsReady) {
            Write-Log "Drive Typ: $($drive.DriveType)" 'INFO'
            Write-Log "Volume Label: $($drive.VolumeLabel)" 'INFO'
            Write-Log "RootDirectory: $($drive.RootDirectory.FullName)" 'INFO'
            Write-Log "FreeSpace: $($drive.AvailableFreeSpace) Bytes" 'INFO'
            Write-Log "TotalSize: $($drive.TotalSize) Bytes" 'INFO'
        }

        Write-Log "ReferenceFiles: $($ReferenceFileNames -join ', ')" 'INFO'
        Write-Log "ExecutableExtensions: $($ExecutableExtensions -join ', ')" 'INFO'
        Write-Log "BlockedRoots: $($BlockedRootsFull -join ' | ')" 'INFO'
        Write-Log "AllowedRoots: $($AllowedRootsFull -join ' | ')" 'INFO'

        if ($drive.IsReady) {
            Write-Log "Inhalt von ${DriveRoot}:" 'INFO'

            Get-ChildItem -LiteralPath $DriveRoot -Recurse -Depth 2 -File `
                -ErrorAction SilentlyContinue |
                ForEach-Object {
                    Write-Log ("  FILE: {0} | {1} Bytes | {2}" -f `
                        $_.FullName,
                        $_.Length,
                        $_.LastWriteTimeUtc.ToString('o')) 'INFO'
                }

            foreach ($name in $ReferenceFileNames) {
                $ref = Join-Path $DriveRoot $name
                if (Test-Path -LiteralPath $ref -PathType Leaf) {
                    Write-Log "Referenzdatei gefunden: $ref" 'OK'

                    try {
                        Get-Content -LiteralPath $ref -ErrorAction Stop |
                            ForEach-Object {
                                Write-Log "  REF: $_" 'INFO'
                            }
                    }
                    catch {
                        Write-Log "Referenzdatei konnte nicht gelesen werden: $($_.Exception.Message)" 'ERROR'
                    }
                }
            }

            try {
                $sig = Get-DiskSignature -Root $DriveRoot
                Write-Log "DiskSignature Laenge: $($sig.Length)" 'INFO'
                Write-Log "DiskSignature SHA256:" 'INFO'

                $bytes = [Text.Encoding]::UTF8.GetBytes($sig)
                $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
                $hex = ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
                Write-Log "  $hex" 'INFO'
            }
            catch {
                Write-Log "DiskSignature konnte nicht ermittelt werden: $($_.Exception.Message)" 'ERROR'
            }

            try {
                $debugPlan = Get-LaunchPlan -Root $DriveRoot

                if ($null -eq $debugPlan) {
                    Write-Log "DEBUG: Kein LaunchPlan gefunden." 'WARN'
                }
                else {
                    Write-Log "DEBUG: LaunchPlan gefunden:" 'OK'
                    Write-Log "  Kind: $($debugPlan.Kind)" 'INFO'
                    Write-Log "  Steam: $($debugPlan.Steam)" 'INFO'
                    Write-Log "  Arguments: $($debugPlan.Arguments)" 'INFO'
                    Write-Log "  NeedsConfirm: $($debugPlan.NeedsConfirm)" 'INFO'
                    Write-Log "  Candidates: $($debugPlan.Candidates -join ' | ')" 'INFO'
                }
            }
            catch {
                Write-Log "DEBUG: Get-LaunchPlan FEHLER: $($_.Exception.Message)" 'ERROR'
                Write-Log "DEBUG: StackTrace: $($_.ScriptStackTrace)" 'ERROR'
            }
        }
    }
    catch {
        Write-Log "DEBUG-Fehler: $($_.Exception.Message)" 'ERROR'
        Write-Log "DEBUG-StackTrace: $($_.ScriptStackTrace)" 'ERROR'
    }

    Write-Log "========== DEBUG ENDE ==========" 'INFO'
}

# ---------------------------------------------------------------------------
# Fehlende Confirm-Execution-Funktion
# ---------------------------------------------------------------------------
function Confirm-Execution {
    param(
        [Parameter(Mandatory)]
        [string] $Target
    )

    Write-Log "Confirm-Execution aufgerufen: $Target" 'ASK'

    try {
        $chosen = Request-LaunchChoice `
            -Candidates @($Target) `
            -Arguments $null

        if ($chosen) {
            Write-Log "Confirm-Execution: JA -> $chosen" 'OK'
            return $true
        }

        Write-Log "Confirm-Execution: NEIN -> $Target" 'WARN'
        return $false
    }
    catch {
        Write-Log "Confirm-Execution FEHLER: $($_.Exception.Message)" 'ERROR'
        Write-Log "Confirm-Execution StackTrace: $($_.ScriptStackTrace)" 'ERROR'
        return $false
    }
}

# Diagnose beim Start nur auf Wunsch - die Vollpruefung macht mehrere
# (auf einer echten Diskette langsame) Scans. Einschalten mit:
#   $env:FLOPPY_DEBUG = '1'      oder      -Debug
if ($DebugPreference -ne 'SilentlyContinue' -or $env:FLOPPY_DEBUG -eq '1') {
    Write-DebugState -Context 'Programmstart'
}




# ---------------------------------------------------------------------------
# Hauptschleife  -  ausschliesslich Laufwerk $DriveRoot, alle $PollSeconds Sek.
# ---------------------------------------------------------------------------

$mode = @()
if ($DryRun)         { $mode += 'DryRun' }
if ($NonInteractive) { $mode += 'NonInteractive' }
$modeText = if ($mode) { ' [' + ($mode -join ', ') + ']' } else { '' }

Write-Log "Floppy Launcher gestartet. Warte auf Diskette in $DriveRoot ...$modeText" 'INFO'
Write-Log "Host: $($Host.Name) | PS $($PSVersionTable.PSVersion) | Interface-EXE: $SelfExe" 'INFO'
foreach ($problem in $script:RootListProblems) { Write-Log $problem 'WARN' }

# Nur EINE Instanz. Sonst pollen nach einer Installation zwei Launcher
# dasselbe Laufwerk (z. B. alter Autostart von Hand + neuer vom Setup) und
# jede Diskette wuerde doppelt starten.  -AllowMultiple hebt das auf.
$script:InstanceMutex = $null
if (-not $AllowMultiple -and -not $RunOnce) {
    try {
        $isNew = $false
        $script:InstanceMutex = New-Object System.Threading.Mutex($true, 'Local\FloppyLauncherSingleInstance', [ref]$isNew)
        if (-not $isNew) {
            Write-Log "Es laeuft bereits ein Floppy Launcher. Dieser Start wird beendet (-AllowMultiple erzwingt Mehrfachstart)." 'WARN'
            return
        }
    }
    catch { Write-Log "Einmalstart-Pruefung nicht moeglich: $($_.Exception.Message)" 'WARN' }
}
if ($Host.Name -notmatch 'ConsoleHost') {
    Write-Log "Hinweis: Kein klassisches Konsolenfenster ($($Host.Name)). Das Bestaetigungsfenster wird trotzdem als eigener powershell-Prozess geoeffnet." 'WARN'
}

$lastSignature = $null

do {
    try {
        $drive = [System.IO.DriveInfo]::new($DriveLetter)

        if (-not $drive.IsReady) {
            $lastSignature = $null   # keine Diskette -> naechste wird neu behandelt
        }
        else {
            $signature = Get-DiskSignature -Root $DriveRoot

            if ($signature -ne $lastSignature) {
                $lastSignature = $signature   # diesen Zustand nur einmal behandeln
                try {
                    $plan = Get-LaunchPlan -Root $DriveRoot
                    if ($plan) { Invoke-LaunchPlan -Plan $plan }
                }
                catch {
                    Write-Log "Fehler beim Auswerten der Diskette: $($_.Exception.Message)" 'ERROR'
                }
            }
        }
    }
    catch {
        Write-Log "Unerwarteter Fehler: $($_.Exception.Message)" 'ERROR'
    }

    if (-not $RunOnce) { Start-Sleep -Seconds $PollSeconds }

} while (-not $RunOnce)
