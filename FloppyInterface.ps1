<#
.SYNOPSIS
    FloppyInterface - das Retro-Bestaetigungsfenster ("Floppy-Interface").

.DESCRIPTION
    OPTIONALER "Skin" fuer FloppyLauncher.ps1. Liegt diese Datei neben dem
    Launcher, benutzt er sie als Inhalt des zweiten Terminals statt seiner
    eingebauten Minimal-Variante. Fehlt oder bricht sie, nimmt der Launcher
    automatisch wieder seine eingebaute Version (sicher, nur schlichter).

    Eigenstaendig: laeuft als eigener powershell-Prozess, haengt von KEINER
    anderen Datei ab (auch nicht von FloppyLib.ps1), damit die
    Sicherheitsabfrage immer funktioniert.

    EINGABE  (Umgebungsvariablen, vom Launcher gesetzt):
        FLOPPY_IF_LIST      Kandidaten, per Zeilenumbruch getrennt (1..9)
        FLOPPY_IF_ARGS      optionale Argumentzeile (nur Anzeige)
        FLOPPY_IF_DRIVE     Laufwerk, z. B. "A:\"
        FLOPPY_IF_TIMEOUT   Sekunden bis Auto-Abbruch
        FLOPPY_IF_THEME     green | amber | ice | mono   (optional)
        FLOPPY_IF_EGGS      1/0 - versteckte Spielereien an/aus (Standard 1)

    AUSGABE  (Exitcode - Vertrag identisch zur eingebauten Version):
        0        abgebrochen / nichts waehlen / Zeitablauf
        1..9     Kandidat Nr. N soll gestartet werden

.NOTES
    Teil von: FloppyLauncher.ps1 / FloppyHub.ps1 / FloppyDisc.ps1 / FloppyLib.ps1
    Aendern erlaubt - nur den Exitcode-Vertrag bitte NICHT anfassen.
#>

$ErrorActionPreference = 'SilentlyContinue'
$WIDTH = 76

# --- Eingaben -------------------------------------------------------------
$list = @()
if ($env:FLOPPY_IF_LIST) {
    $list = $env:FLOPPY_IF_LIST -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
}
if ($list.Count -eq 0) { exit 0 }
if ($list.Count -gt 9) { $list = $list[0..8] }

$argline = $env:FLOPPY_IF_ARGS
$drive   = if ($env:FLOPPY_IF_DRIVE) { $env:FLOPPY_IF_DRIVE } else { '?' }
$timeout = 90 ; [void][int]::TryParse($env:FLOPPY_IF_TIMEOUT, [ref]$timeout)

# theme / eggs: bevorzugt aus den Umgebungsvariablen des Launchers,
# sonst direkt aus FloppyLauncher.ini (Abschnitt [ui]), sonst Standard.
$themeName = $env:FLOPPY_IF_THEME
$eggsRaw   = $env:FLOPPY_IF_EGGS
try {
    $ifHome  = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $iniPath = Join-Path $ifHome 'FloppyLauncher.ini'
    if ((-not $themeName -or -not $eggsRaw) -and (Test-Path -LiteralPath $iniPath)) {
        $inUi = $false
        foreach ($ln in (Get-Content -LiteralPath $iniPath -Encoding UTF8)) {
            $t = $ln.Trim()
            if ($t -match '^\[(.+?)\]$') { $inUi = ($Matches[1].Trim().ToLowerInvariant() -eq 'ui'); continue }
            if ($inUi -and $t -match '^([^=;#]+?)\s*=\s*(.*)$') {
                $k = $Matches[1].Trim().ToLowerInvariant(); $v = $Matches[2].Trim().Trim('"')
                if ($k -eq 'theme' -and -not $themeName) { $themeName = $v }
                if ($k -eq 'easter_eggs' -and -not $eggsRaw) { $eggsRaw = $v }
            }
        }
    }
} catch { }
$eggs = ("$eggsRaw".ToLowerInvariant() -notmatch '^(0|false|nein|no|off|aus)$')

# --- Farbschema ---------------------------------------------------------
function Get-Theme {
    param([string] $Name)
    switch ("$Name".ToLowerInvariant()) {
        'amber' { @{ Frame='DarkYellow'; Text='Yellow'; Dim='DarkYellow'; Accent='White'; Warn='White';  Bad='Red'; Good='Yellow' } }
        'ice'   { @{ Frame='DarkCyan';   Text='Cyan';   Dim='DarkCyan';   Accent='White'; Warn='Yellow'; Bad='Red'; Good='Cyan'   } }
        'mono'  { @{ Frame='DarkGray';   Text='Gray';   Dim='DarkGray';   Accent='White'; Warn='White';  Bad='White'; Good='White' } }
        default { @{ Frame='DarkGreen';  Text='Green';  Dim='DarkGreen';  Accent='Cyan';  Warn='Yellow'; Bad='Red'; Good='Green'  } }
    }
}
$theme = Get-Theme $themeName

# --- Saison-Thema (Easter Egg) ----------------------------------------
$season = $null
if ($eggs) {
    $now = Get-Date
    if     ($now.Month -eq 12 -and $now.Day -ge 24 -and $now.Day -le 26)                   { $season = @{ Line = '*  *  frohe weihnachten  *  *';    Theme = (Get-Theme 'ice')   } }
    elseif (($now.Month -eq 12 -and $now.Day -eq 31) -or ($now.Month -eq 1 -and $now.Day -eq 1)) { $season = @{ Line = ".*'  frohes neues jahr  '*.";      Theme = (Get-Theme 'amber') } }
    elseif ($now.Month -eq 10 -and $now.Day -eq 31)                                        { $season = @{ Line = 'spukt es in track 13?';           Theme = (Get-Theme 'amber') } }
    elseif ($now.Day -eq 13 -and $now.DayOfWeek -eq 'Friday')                              { $season = @{ Line = 'track 13, sektor 13 ... viel glueck.'; Theme = (Get-Theme 'mono') } }
    elseif ($now.DayOfYear -eq 256)                                                        { $season = @{ Line = '0x100 - tag des programmierers';   Theme = $theme } }
    if ($season) { $theme = $season.Theme }
}

# --- bekannte EXE -> Spruch (Easter Egg) ------------------------------
function Get-ExeFlavor {
    param([string] $Path)
    if (-not $eggs) { return $null }
    switch -Regex ([System.IO.Path]::GetFileNameWithoutExtension("$Path").ToLowerInvariant()) {
        '^(hl2|hl)$'        { '  "Rise and shine, Mr. Freeman."' ; break }
        '^portal2?$'        { '  "The cake is a lie."'           ; break }
        '^(csgo|cs2)$'      { '  "Rush B, no stop."'             ; break }
        '^hl$'              { '  "Unforeseen consequences."'     ; break }
        '^dosbox'           { '  C:\> _   willkommen zurueck in 1994.' ; break }
        '^(qflipper|flipper)' { '  *dolphin noises*  der Flipper freut sich.' ; break }
        default            { $null }
    }
}

# --- Retro-Konsole ---------------------------------------------------
try {
    $ui = $Host.UI.RawUI
    $ui.WindowTitle     = 'FLOPPY LAUNCHER  ::  disketten-interface'
    $ui.BackgroundColor = 'Black'
    $ui.ForegroundColor = $theme.Text
    try { $ui.BufferSize = New-Object System.Management.Automation.Host.Size (($WIDTH + 6), 900) } catch { }
    try { $ui.WindowSize = New-Object System.Management.Automation.Host.Size (($WIDTH + 6), 36) } catch { }
} catch { }
Clear-Host

$H  = [char]0x2550 ; $V  = [char]0x2551
$TL = [char]0x2554 ; $TR = [char]0x2557 ; $BL = [char]0x255A ; $BR = [char]0x255D
$LT = [char]0x2560 ; $RT = [char]0x2563 ; $LS = [char]0x255F ; $RS = [char]0x2562

function Rule { param([char]$l, [char]$r, [string]$col = $theme.Frame)
    Write-Host ('  ' + $l + ([string]$H * ($WIDTH + 2)) + $r) -ForegroundColor $col
}
function Row { param([string]$s = '', [string]$c = $theme.Text)
    if ($s.Length -gt $WIDTH) { $s = $s.Substring(0, $WIDTH) }
    Write-Host ('  ' + $V + ' ') -NoNewline -ForegroundColor $theme.Frame
    Write-Host ($s.PadRight($WIDTH)) -NoNewline -ForegroundColor $c
    Write-Host (' ' + $V) -ForegroundColor $theme.Frame
}
function Short { param([string]$p, [int]$max = 64)
    if ([string]::IsNullOrEmpty($p) -or $p.Length -le $max) { return $p }
    return '...' + $p.Substring($p.Length - ($max - 3))
}

# --- Boot-Flimmern (Easter Egg) -------------------------------------
if ($eggs) {
    $boot = @(
        'FLOPPY-BIOS  v1.44   (c) hausgemacht',
        'detecting drives . . . . . . . . . . . . . . .  A: OK',
        'reading track 00  head 0  sector 1 . . . . . .  OK',
        'loading interface . . . . . . . . . . . . . . .  OK'
    )
    foreach ($b in $boot) { Write-Host ('  ' + $b) -ForegroundColor $theme.Dim; Start-Sleep -Milliseconds 90 }
    Start-Sleep -Milliseconds 180
    Clear-Host
}

# --- Kopf ----------------------------------------------------------
$wink = ($eggs -and ((Get-Random -Minimum 0 -Maximum 6) -eq 0))
Write-Host ''
Rule $TL $TR
Row '   ___________     F L O P P Y   L A U N C H E R' $theme.Accent
Row ('  |####|      |    disketten-interface  ::  sicherheitsabfrage') $theme.Dim
Row ('  |####|  ' + $(if ($wink) { ';)' } else { '..' }) + '  |    ' + $(if ($season) { $season.Line } else { 'pruefe, bevor du bestaetigst' })) $theme.Dim
Row '  |####|______|' $theme.Dim
Rule $LT $RT
Row
Row ("  datentraeger : {0}" -f $drive)
if ($list.Count -eq 1) {
    Row "  anforderung  : ein programm auf dem PC soll gestartet werden" $theme.Warn
    $flavor = Get-ExeFlavor $list[0]
    if ($flavor) { Row $flavor $theme.Dim }
} else {
    Row ("  anforderung  : {0} ausfuehrbare dateien - GENAU EINE waehlen" -f $list.Count) $theme.Warn
}
if ($argline) { Row ("  argumente    : {0}" -f (Short $argline 60)) $theme.Dim }

# seltener "Bit-Rot"-Glitch (Easter Egg, rein optisch)
if ($eggs -and ((Get-Random -Minimum 0 -Maximum 12) -eq 0)) {
    Row ("  {0}  bit-rot in sektor {1:00} erkannt - trotzdem lesbar" -f ([char]0x2593), (Get-Random -Max 79)) $theme.Bad
}
Row
Rule $LS $RS
Row
for ($i = 0; $i -lt $list.Count; $i++) {
    Row ("    [ {0} ]   {1}" -f ($i + 1), (Short $list[$i])) $theme.Accent
}
Row
Rule $BL $BR
Write-Host ''
Write-Host '   WARNUNG: nur bestaetigen, wenn du dieser diskette vertraust.' -ForegroundColor $theme.Bad
Write-Host ''
if ($list.Count -eq 1) {
    Write-Host '     ' -NoNewline
    Write-Host ' J ' -NoNewline -ForegroundColor Black -BackgroundColor $theme.Good
    Write-Host ' = starten        ' -NoNewline -ForegroundColor $theme.Text
    Write-Host ' N ' -NoNewline -ForegroundColor Black -BackgroundColor Red
    Write-Host ' = abbrechen        ' -NoNewline -ForegroundColor $theme.Bad
    Write-Host '(? = hilfe)' -ForegroundColor $theme.Dim
} else {
    Write-Host ("     Zahl 1-{0} = starten     " -f $list.Count) -NoNewline -ForegroundColor $theme.Text
    Write-Host ' N ' -NoNewline -ForegroundColor Black -BackgroundColor Red
    Write-Host ' = abbrechen     ' -NoNewline -ForegroundColor $theme.Bad
    Write-Host '(? = hilfe)' -ForegroundColor $theme.Dim
}
Write-Host ''

# --- Entscheidung ------------------------------------------------
$deadline = (Get-Date).AddSeconds($timeout)
$choice = 0
$helpShown = $false
try {
    while ((Get-Date) -lt $deadline) {
        $rem = [int][math]::Ceiling(($deadline - (Get-Date)).TotalSeconds)
        Write-Host ("`r   [ auto-abbruch in {0,3} s ]   >>> " -f $rem) -NoNewline -ForegroundColor $theme.Dim
        if ([Console]::KeyAvailable) {
            $k = [Console]::ReadKey($true)
            $c = ([string]$k.KeyChar).ToUpper()
            if ($k.Key -eq 'Escape' -or $c -eq 'N') { $choice = 0; break }
            if ($list.Count -eq 1) {
                if ($c -eq 'J' -or $c -eq 'Y') { $choice = 1; break }
            } elseif ($c -match '^[1-9]$' -and [int]$c -le $list.Count) {
                $choice = [int]$c; break
            }
            if ($c -eq '?' -and -not $helpShown) {
                $helpShown = $true
                Write-Host ''
                Write-Host '   hilfe: J/Zahl bestaetigt den start. N oder Esc bricht ab.' -ForegroundColor $theme.Dim
                Write-Host '          keine eingabe -> nach ablauf der zeit automatisch abbruch.' -ForegroundColor $theme.Dim
                Write-Host '          pcrun= aus C:\Windows wird vom launcher grundsaetzlich blockiert.' -ForegroundColor $theme.Dim
            }
        }
        Start-Sleep -Milliseconds 120
    }
} catch {
    # Kein Tastatur-Zugriff (Eingabe umgeleitet o. Ae.). NICHT blockierend
    # nachfragen - sicherste Vorgabe ist Abbruch.
    $choice = 0
}

Write-Host ''
Write-Host ''
if ($choice -ge 1) {
    Write-Host ("   >> STARTE:  {0}" -f $list[$choice - 1]) -ForegroundColor Black -BackgroundColor $theme.Good
} else {
    Write-Host '   >> ABGEBROCHEN - es wird nichts ausgefuehrt.' -ForegroundColor White -BackgroundColor Red
}
Start-Sleep -Milliseconds 1100
exit $choice
