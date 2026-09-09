<#
.SYNOPSIS
    Floppy Hub - Retro-Menue rund um die Disketten-Steam-Library.

.DESCRIPTION
    Der "Hub" wird normalerweise NICHT von Hand gestartet, sondern indem man
    eine dafuer vorbereitete Diskette einlegt: eine Referenzdatei mit

        hub=1

    Dann startet FloppyLauncher.ps1 dieses Menue (wie jede andere Disketten-
    Art auch). Manuell geht natuerlich auch:

        powershell -ExecutionPolicy Bypass -File .\FloppyHub.ps1

    Kein Autostart, kein Dauerbetrieb - das Fenster laeuft nur, solange du es
    benutzt, und belegt danach kein RAM / keine CPU.

.NOTES
    Braucht FloppyLib.ps1 im selben Ordner.
    Teil von: FloppyLauncher.ps1 / FloppyInterface.ps1 / FloppyDisc.ps1 / FloppyLib.ps1
#>

[CmdletBinding()]
param(
    [string] $FloppyDrive
)

$ErrorActionPreference = 'Stop'
$Home2 = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

. (Join-Path $Home2 'FloppyLib.ps1')

# --- Konfiguration einlesen (nur fuer Anzeige / Standardwerte) --------
$ini      = Import-FloppyIni (Get-FloppyConfigPath)
$drive    = if ($FloppyDrive) { $FloppyDrive } else { Get-FloppyIniValue $ini 'drive' 'letter' 'A:' }
$themeNm  = Get-FloppyIniValue $ini 'ui' 'theme' 'green'
$eggsOn   = ConvertTo-FloppyBool (Get-FloppyIniValue $ini 'ui' 'easter_eggs' 'true') $true
$refNames = ConvertTo-FloppyList (Get-FloppyIniValue $ini 'reference' 'file_names' 'game.txt, floppy.txt, launch.txt')
if ($refNames.Count -eq 0) { $refNames = @('game.txt', 'floppy.txt', 'launch.txt') }

$theme  = Get-FloppyTheme $themeNm
$season = if ($eggsOn) { Get-FloppySeasonalTheme } else { $null }
if ($season) { $theme = $season.Theme }

$W = 76

# --- kleine Zeichen-Helfer (nutzen FloppyLib) ------------------------
function Line   { param([string]$s = '', [string]$c) if ($c) { Write-FloppyRow -Text $s -Theme $theme -Color $c -Width $W } else { Write-FloppyRow -Text $s -Theme $theme -Width $W } }
function RuleT  { Write-FloppyRule -Width $W -Theme $theme -Style top }
function RuleM  { Write-FloppyRule -Width $W -Theme $theme -Style mid }
function RuleTh { Write-FloppyRule -Width $W -Theme $theme -Style thin }
function RuleB  { Write-FloppyRule -Width $W -Theme $theme -Style bottom }
function Pause  { Write-Host ''; Write-Host '   [ Enter druecken ]' -ForegroundColor $theme.Dim -NoNewline; [void](Read-Host) }

function Show-BootSequence {
    if (-not $eggsOn) { return }
    Set-FloppyConsole -Theme $theme -Width ($W + 6) -Height 36 -Title 'FLOPPY HUB'
    $diskState = if (Test-FloppyPresent -Letter $drive) { 'diskette erkannt' } else { 'leer' }
    $libRows   = Get-FloppyLibrary
    $libCount  = $libRows.Count
    $steps = @(
        'FLOPPY HUB  -  kaltstart',
        '  bios . . . . . . . . . . . . . . . . . . . . ok',
        "  laufwerk $drive . . . . . . . . . . . . . . . .  $diskState",
        "  bibliothek . . . . . . . . . . . . . . . . . .  $libCount eintraege",
        '  hub bereit.'
    )
    foreach ($s in $steps) { Write-Host ('  ' + $s) -ForegroundColor $theme.Dim; Start-Sleep -Milliseconds 110 }
    Start-Sleep -Milliseconds 300
}

function Show-Header {
    Set-FloppyConsole -Theme $theme -Width ($W + 6) -Height 36 -Title 'FLOPPY HUB'
    Write-Host ''
    RuleT
    foreach ($l in (Get-FloppyDiskArt)) {
        if ($l.Length -gt $W) { $l = $l.Substring(0, $W) }
        Write-FloppyRow -Text $l -Theme $theme -Color $theme.Accent -Width $W
    }
    RuleM
    Line
    if ($season) { Line ("   " + $season.Line) $theme.Warn ; Line }
    $present = Test-FloppyPresent -Letter $drive
    Line ("   laufwerk $drive : " + $(if ($present) { 'DISKETTE EINGELEGT' } else { 'leer' })) $(if ($present) { $theme.Good } else { $theme.Dim })
    if ($present) {
        $s = Get-FloppyPlanSummary -Root ($drive.TrimEnd('\') + '\') -ReferenceNames $refNames
        Line ("   inhalt        : [$($s.Kind)] " + (Format-FloppyShort $s.Detail ($W - 20)))
    }
    Line
    RuleB
    Write-Host ''
}

function Show-Menu {
    Show-Header
    Write-Host '     [1]  Status         was wuerde der Launcher mit dieser Diskette tun' -ForegroundColor $theme.Text
    Write-Host '     [2]  Bibliothek     bekannte Disketten ansehen / durchsuchen'        -ForegroundColor $theme.Text
    Write-Host '     [3]  Disc bespielen game.txt schreiben (Steam-ID / EXE / Hub)'       -ForegroundColor $theme.Text
    Write-Host '     [4]  Disc merken    aktuelle Diskette in die Bibliothek aufnehmen'   -ForegroundColor $theme.Text
    Write-Host '     [5]  Log            letzte Zeilen von FloppyLauncher.log'            -ForegroundColor $theme.Text
    Write-Host '     [6]  Konfiguration  FloppyLauncher.ini im Editor oeffnen'            -ForegroundColor $theme.Text
    Write-Host '     [7]  Launcher       laeuft er? Autostart-Hinweis'                    -ForegroundColor $theme.Text
    Write-Host '     [Q]  Ende'                                                           -ForegroundColor $theme.Dim
    Write-Host ''
    return (Read-Host '   auswahl')
}

# --- Menuepunkte -----------------------------------------------------

function Do-Status {
    Show-Header
    $root = $drive.TrimEnd('\') + '\'
    if (-not (Test-FloppyPresent -Letter $drive)) {
        Line '   keine Diskette im Laufwerk.' $theme.Warn
        Pause; return
    }
    $s = Get-FloppyPlanSummary -Root $root -ReferenceNames $refNames
    Write-Host ''
    Write-Host "   Diskette in $drive" -ForegroundColor $theme.Accent
    Write-Host "   -> Einordnung : $($s.Kind)" -ForegroundColor $theme.Text
    Write-Host "   -> Aktion     : $($s.Detail)" -ForegroundColor $theme.Text
    if ($s.Source) {
        Write-Host "   -> Referenz   : $($s.Source)" -ForegroundColor $theme.Dim
        Write-Host ''
        Write-Host '   Inhalt der Referenzdatei:' -ForegroundColor $theme.Dim
        Get-Content -LiteralPath $s.Source | ForEach-Object { Write-Host "      $_" -ForegroundColor $theme.Dim }
    }
    Write-Host ''
    Write-Host '   Dateien auf der Diskette:' -ForegroundColor $theme.Dim
    Get-ChildItem -LiteralPath $root -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
        Select-Object -First 30 |
        ForEach-Object { Write-Host ("      {0,10:n0}  {1}" -f $_.Length, $_.FullName.Substring($root.Length)) -ForegroundColor $theme.Dim }
    Pause
}

function Do-Library {
    Show-Header
    $lib = Get-FloppyLibrary
    if ($lib.Count -eq 0) {
        Line '   Die Bibliothek ist leer. Menuepunkt [4] fuellt sie.' $theme.Warn
        Pause; return
    }
    $q = Read-Host '   suchbegriff (leer = alle)'
    $rows = if ($q) { $lib | Where-Object { ($_ | Out-String) -match [regex]::Escape($q) } } else { $lib }
    Write-Host ''
    if (-not $rows) { Write-Host '   nichts gefunden.' -ForegroundColor $theme.Warn; Pause; return }
    Write-Host ('   {0,-26} {1,-14} {2,-22} {3}' -f 'LABEL', 'ART', 'WERT', 'HINZU') -ForegroundColor $theme.Accent
    Write-Host ('   ' + ('-' * ($W - 4))) -ForegroundColor $theme.Dim
    foreach ($r in $rows) {
        Write-Host ('   {0,-26} {1,-14} {2,-22} {3}' -f `
            (Format-FloppyShort "$($r.Label)" 25), "$($r.Kind)", (Format-FloppyShort "$($r.Value)" 21), "$($r.Added)") -ForegroundColor $theme.Text
        if ($r.Notes) { Write-Host "       $($r.Notes)" -ForegroundColor $theme.Dim }
    }
    Pause
}

function Do-Prepare {
    Show-Header
    $root = $drive.TrimEnd('\') + '\'
    Write-Host '   DISC BESPIELEN  -  schreibt eine game.txt' -ForegroundColor $theme.Accent
    Write-Host ''
    if (-not (Test-FloppyPresent -Letter $drive)) {
        Write-Host "   Keine Diskette in $drive. Bitte einlegen und erneut versuchen." -ForegroundColor $theme.Warn
        Pause; return
    }
    Write-Host '     [1]  Steam-Spiel   (AppID oder Store-Link)'  -ForegroundColor $theme.Text
    Write-Host '     [2]  EXE von der Diskette   (run=)'          -ForegroundColor $theme.Text
    Write-Host '     [3]  EXE auf dem PC        (pcrun=, mit Rueckfrage)' -ForegroundColor $theme.Text
    Write-Host '     [4]  Diese Diskette wird eine HUB-Diskette' -ForegroundColor $theme.Text
    Write-Host '     [Q]  zurueck'                               -ForegroundColor $theme.Dim
    Write-Host ''
    $sel = Read-Host '   auswahl'

    $kind = $null; $value = $null; $title = $null; $gameArgs = $null
    switch ($sel) {
        '1' {
            $in = Read-Host '   Steam-AppID oder Link'
            $id = Resolve-SteamAppId $in
            if (-not $id) { Write-Host '   konnte keine AppID erkennen.' -ForegroundColor $theme.Bad; Pause; return }
            $kind = 'steam'; $value = $id; $title = Get-SteamAppName -AppId $id
            $gameArgs = Read-Host '   Startargumente (optional, Enter = keine)'
            Write-Host ''
            Write-Host ("   -> id=$id" + $(if ($title) { "   ($title)" } else { '' })) -ForegroundColor $theme.Good
        }
        '2' {
            $value = Read-Host '   Pfad RELATIV zur Diskette (z. B. spiel\game.exe)'
            $kind = 'run'
            $gameArgs = Read-Host '   Startargumente (optional)'
        }
        '3' {
            $value = Read-Host '   ABSOLUTER Pfad auf dem PC (z. B. D:\Spiele\x.exe)'
            $kind = 'pcrun'
            $gameArgs = Read-Host '   Startargumente (optional)'
            Write-Host ''
            Write-Host '   Hinweis: der Launcher fragt beim Einlegen im Interface-Fenster nach,' -ForegroundColor $theme.Dim
            Write-Host '            und C:\Windows-Pfade werden grundsaetzlich abgelehnt.'        -ForegroundColor $theme.Dim
        }
        '4' { $kind = 'hub'; $title = 'Hub-Diskette' }
        default { return }
    }
    if (-not $kind) { return }

    $exists = Test-Path -LiteralPath (Join-Path $root 'game.txt')
    Write-Host ''
    $ok = Read-Host ("   game.txt auf $drive schreiben" + $(if ($exists) { ' und BESTEHENDE ersetzen' } else { '' }) + '?  (J/N)')
    if ($ok -notmatch '^(j|ja|y)$') { Write-Host '   abgebrochen.' -ForegroundColor $theme.Warn; Pause; return }

    $written = Write-FloppyReference -Root $root -Kind $kind -Value $value -Arguments $gameArgs -Title $title -Force
    if ($written) {
        Write-Host "   geschrieben: $written" -ForegroundColor $theme.Good
        Write-Host ''
        Get-Content -LiteralPath $written | ForEach-Object { Write-Host "      $_" -ForegroundColor $theme.Dim }
        if ((Read-Host '   auch in die Bibliothek aufnehmen? (J/N)') -match '^(j|ja|y)$') {
            $lbl = if ($title) { $title } else { Read-Host '   Label fuer die Bibliothek' }
            [void](Add-FloppyLibraryEntry -Label $lbl -Kind $kind -Value ("$value") -Notes 'via FloppyHub')
            Write-Host '   in library.csv aufgenommen.' -ForegroundColor $theme.Good
        }
    } else {
        Write-Host '   nichts geschrieben.' -ForegroundColor $theme.Bad
    }
    Pause
}

function Do-Remember {
    Show-Header
    $root = $drive.TrimEnd('\') + '\'
    if (-not (Test-FloppyPresent -Letter $drive)) { Write-Host '   keine Diskette.' -ForegroundColor $theme.Warn; Pause; return }
    $s = Get-FloppyPlanSummary -Root $root -ReferenceNames $refNames
    $kind = $s.Kind
    $value = ''
    if ($s.Source) {
        $r = Read-FloppyReference -Path $s.Source
        foreach ($k in 'id', 'steam', 'pcrun', 'run', 'exe') { if ($r.ContainsKey($k)) { $value = $r[$k]; break } }
    }
    Write-Host ''
    Write-Host "   erkannt: [$kind] $($s.Detail)" -ForegroundColor $theme.Text
    $lbl = Read-Host '   Label (z. B. Spielname)'
    $notes = Read-Host '   Notiz (optional)'
    if (Add-FloppyLibraryEntry -Label $lbl -Kind $kind -Value $value -Notes $notes) {
        Write-Host '   gespeichert in library.csv' -ForegroundColor $theme.Good
    }
    Pause
}

function Do-Log {
    Show-Header
    $logPath = Expand-FloppyPath (Get-FloppyIniValue $ini 'log' 'file' 'FloppyLauncher.log')
    if (-not (Test-Path -LiteralPath $logPath)) { Write-Host "   kein Log gefunden ($logPath)" -ForegroundColor $theme.Warn; Pause; return }
    Write-Host "   $logPath   (letzte 25 Zeilen)" -ForegroundColor $theme.Dim
    Write-Host ''
    Get-Content -LiteralPath $logPath -Tail 25 | ForEach-Object {
        $col = switch -Regex ($_) { '\[ERROR' { 'Red' } '\[WARN' { 'Yellow' } '\[OK' { $theme.Good } '\[ASK' { 'Magenta' } default { $theme.Dim } }
        Write-Host "   $_" -ForegroundColor $col
    }
    Pause
}

function Do-Config {
    $cfg = Get-FloppyConfigPath
    if (-not (Test-Path -LiteralPath $cfg)) {
        Write-Host "   $cfg existiert nicht - lege eine Vorlage an? (J/N)" -ForegroundColor $theme.Warn
        if ((Read-Host) -notmatch '^(j|ja|y)$') { return }
        Set-Content -LiteralPath $cfg -Value "[drive]`nletter = A:`n`n[ui]`ntheme = green`neaster_eggs = true" -Encoding UTF8
    }
    Write-Host "   oeffne $cfg ..." -ForegroundColor $theme.Dim
    try { Start-Process notepad.exe $cfg } catch { Write-Host "   konnte Editor nicht oeffnen: $($_.Exception.Message)" -ForegroundColor $theme.Bad }
    Start-Sleep -Milliseconds 700
}

function Do-LauncherInfo {
    Show-Header
    $running = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue |
        Where-Object { "$($_.CommandLine)" -match 'FloppyLauncher\.ps1' })
    Write-Host ''
    if ($running.Count -gt 0) {
        Write-Host "   Der Floppy Launcher laeuft.  (PID $($running.ProcessId -join ', '))" -ForegroundColor $theme.Good
    } else {
        Write-Host '   Der Floppy Launcher laeuft derzeit NICHT.' -ForegroundColor $theme.Warn
        Write-Host "   Start:  powershell -ExecutionPolicy Bypass -File `"$(Join-Path $Home2 'FloppyLauncher.ps1')`"" -ForegroundColor $theme.Dim
    }
    Write-Host ''
    Write-Host '   Autostart bleibt bewusst NUR fuer FloppyLauncher.ps1 eingerichtet.' -ForegroundColor $theme.Dim
    Write-Host '   Dieses Hub-Menue startet nur auf Wunsch (Hub-Diskette oder von Hand)' -ForegroundColor $theme.Dim
    Write-Host '   und belegt danach kein RAM / keine CPU.' -ForegroundColor $theme.Dim
    Pause
}

function Handle-EasterEgg {
    param([string] $Text)
    $egg = Get-FloppyEasterEgg $Text
    if (-not $egg) { return $false }
    Write-Host ''
    foreach ($ln in ($egg -split "`n")) { Write-Host $ln -ForegroundColor $theme.Accent }
    Write-Host ''
    Start-Sleep -Milliseconds 400
    [void](Read-Host '   [ Enter ]')
    return $true
}

# --- Hauptschleife -------------------------------------------------
Show-BootSequence
while ($true) {
    $raw = Show-Menu
    if ($null -eq $raw) { return }          # EOF / keine Eingabe mehr
    $sel = "$raw".Trim()
    switch -Regex ($sel.ToLowerInvariant()) {
        '^1$'        { Do-Status }
        '^2$'        { Do-Library }
        '^3$'        { Do-Prepare }
        '^4$'        { Do-Remember }
        '^5$'        { Do-Log }
        '^6$'        { Do-Config }
        '^7$'        { Do-LauncherInfo }
        '^(q|quit|exit|ende)$' {
            Set-FloppyConsole -Theme $theme -Width ($W + 6) -Height 36 -Title 'FLOPPY HUB'
            Write-Host ''
            Write-Host '   ' -NoNewline
            Write-Host ' bis zur naechsten diskette. ' -ForegroundColor Black -BackgroundColor $theme.Good
            Write-Host ''
            Start-Sleep -Milliseconds 700
            return
        }
        default {
            if (-not (Handle-EasterEgg $sel)) {
                Write-Host '   unbekannte auswahl.' -ForegroundColor $theme.Warn
                Start-Sleep -Milliseconds 700
            }
        }
    }
}
