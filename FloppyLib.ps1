<#
.SYNOPSIS
    FloppyLib - gemeinsame Hilfsfunktionen fuer das "Floppy Hub" Oekosystem.

.DESCRIPTION
    Wird von FloppyHub.ps1 und FloppyDisc.ps1 per Dot-Sourcing geladen:

        . "$PSScriptRoot\FloppyLib.ps1"

    WICHTIG: FloppyLauncher.ps1 benutzt diese Datei NICHT. Der Launcher bleibt
    absichtlich eigenstaendig, damit der Autostart nie von einer zweiten Datei
    abhaengt. FloppyLib ist nur fuer die manuell gestarteten Hub-Werkzeuge.

    Enthaelt:
      * INI-Parser + Konfigurations-Zugriff (FloppyLauncher.ini)
      * Retro-Konsole: Farbschemata, Rahmen, Zeilen, Banner
      * Disketten-Helfer: Laufwerk pruefen, game.txt lesen, "was wuerde der
        Launcher tun" zusammenfassen
      * Steam-Helfer: AppID <-> Name, rungameid-URL
      * Easter Eggs (gemeinsam fuer Hub und Interface)

.NOTES
    Teil von: FloppyLauncher.ps1 / FloppyHub.ps1 / FloppyDisc.ps1 / FloppyInterface.ps1
#>

Set-StrictMode -Version Latest

# ===========================================================================
#  Grundlagen
# ===========================================================================

function Get-FloppyHome {
    # Ordner, in dem die Floppy-Skripte liegen.
    if ($PSScriptRoot) { return $PSScriptRoot }
    if ($PSCommandPath) { return (Split-Path -Parent $PSCommandPath) }
    return (Get-Location).Path
}

function ConvertFrom-FloppyQuoted {
    <#
        Entfernt umschliessende Anfuehrungszeichen und Leerraum.
        WICHTIG: Windows-Pfade duerfen kein " enthalten. Kopiert man einen Pfad
        aus dem Explorer ("Als Pfad kopieren"), sind Anfuehrungszeichen dabei -
        die haben frueher .NET-Pfadfunktionen zum Absturz gebracht
        ("Illegales Zeichen im Pfad").
    #>
    param([string] $Value)
    if ($null -eq $Value) { return $null }
    $v = "$Value".Trim()
    while ($v.Length -ge 2 -and
           (($v[0] -eq '"' -and $v[-1] -eq '"') -or ($v[0] -eq "'" -and $v[-1] -eq "'"))) {
        $v = $v.Substring(1, $v.Length - 2).Trim()
    }
    return ($v -replace '"', '').Trim()
}

function Test-FloppyPathRooted {
    # [System.IO.Path]::IsPathRooted wirft bei unerlaubten Zeichen - hier nicht.
    param([string] $Path)
    try { return [System.IO.Path]::IsPathRooted($Path) } catch { return $false }
}

function Get-FloppyExtension {
    # [System.IO.Path]::GetExtension wirft bei unerlaubten Zeichen - hier nicht.
    param([string] $Path)
    try { return [System.IO.Path]::GetExtension($Path) } catch { return '' }
}

function Expand-FloppyPath {
    # Loest %ENV%-Variablen und relative Pfade (relativ zu Get-FloppyHome) auf.
    param([string] $Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $p = [Environment]::ExpandEnvironmentVariables((ConvertFrom-FloppyQuoted $Path))
    if (-not (Test-FloppyPathRooted $p)) {
        $p = Join-Path (Get-FloppyHome) $p
    }
    try { return [System.IO.Path]::GetFullPath($p) } catch { return $p }
}

# ===========================================================================
#  INI / Konfiguration
# ===========================================================================

function Import-FloppyIni {
    <#
        Parst eine INI-Datei zu einer verschachtelten Hashtable:
            @{ section = @{ key = 'wert'; ... }; ... }
        ';' und '#' am Zeilenanfang sind Kommentare. Schluessel ohne Sektion
        landen unter '' (leerer String).
    #>
    param([Parameter(Mandatory)][string] $Path)

    $result = @{ '' = @{} }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $result }

    $section = ''
    foreach ($raw in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $line = $raw.Trim()
        if (-not $line -or $line[0] -eq ';' -or $line[0] -eq '#') { continue }

        if ($line -match '^\[(.+?)\]$') {
            $section = $Matches[1].Trim().ToLowerInvariant()
            if (-not $result.ContainsKey($section)) { $result[$section] = @{} }
            continue
        }
        if ($line -match '^([^=]+?)\s*=\s*(.*)$') {
            $key = $Matches[1].Trim().ToLowerInvariant()
            $val = $Matches[2].Trim()
            if ($val.Length -ge 2 -and $val[0] -eq '"' -and $val[-1] -eq '"') {
                $val = $val.Substring(1, $val.Length - 2)
            }
            $result[$section][$key] = $val
        }
    }
    return $result
}

function Get-FloppyIniValue {
    param(
        [Parameter(Mandatory)] $Ini,
        [Parameter(Mandatory)][string] $Section,
        [Parameter(Mandatory)][string] $Key,
        $Default = $null
    )
    if ($null -eq $Ini -or -not ($Ini -is [System.Collections.IDictionary])) { return $Default }
    $s = $Section.ToLowerInvariant()
    $k = $Key.ToLowerInvariant()
    if ($Ini.ContainsKey($s) -and $Ini[$s].ContainsKey($k) -and $Ini[$s][$k] -ne '') {
        return $Ini[$s][$k]
    }
    return $Default
}

function ConvertTo-FloppyBool {
    param($Value, [bool] $Default = $false)
    if ($null -eq $Value) { return $Default }
    switch -Regex ("$Value".Trim().ToLowerInvariant()) {
        '^(1|true|ja|yes|on|an|y|j)$'   { return $true }
        '^(0|false|nein|no|off|aus|n)$' { return $false }
        default                         { return $Default }
    }
}

function ConvertTo-FloppyList {
    # "a, b ; c" -> @('a','b','c')   (immer ein echtes Array, auch leer)
    param([string] $Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return , @() }
    return , @($Value -split '[;,]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Get-FloppyConfigPath {
    return (Join-Path (Get-FloppyHome) 'FloppyLauncher.ini')
}

# ===========================================================================
#  Retro-Konsole
# ===========================================================================

function Get-FloppyTheme {
    # Farbschema fuer die Retro-Ausgabe. Namen: green | amber | ice | mono
    param([string] $Name = 'green')
    switch ("$Name".ToLowerInvariant()) {
        'amber' { return @{ Frame='DarkYellow'; Text='Yellow';   Dim='DarkYellow'; Accent='White'; Warn='White';  Bad='Red'; Good='Yellow'; Inv='Black' } }
        'ice'   { return @{ Frame='DarkCyan';   Text='Cyan';     Dim='DarkCyan';   Accent='White'; Warn='Yellow'; Bad='Red'; Good='Cyan';   Inv='Black' } }
        'mono'  { return @{ Frame='DarkGray';   Text='Gray';     Dim='DarkGray';   Accent='White'; Warn='White';  Bad='White'; Good='White'; Inv='Black' } }
        default { return @{ Frame='DarkGreen';  Text='Green';    Dim='DarkGreen';  Accent='Cyan';  Warn='Yellow'; Bad='Red'; Good='Green';  Inv='Black' } }
    }
}

function Set-FloppyConsole {
    # Retro-Look: schwarzer Hintergrund, Themenfarbe, Fenstergroesse.
    param([hashtable] $Theme = (Get-FloppyTheme), [int] $Width = 80, [int] $Height = 34, [string] $Title = 'FLOPPY HUB')
    try {
        $ui = $Host.UI.RawUI
        $ui.WindowTitle     = $Title
        $ui.BackgroundColor = 'Black'
        $ui.ForegroundColor = $Theme.Text
        try { $ui.BufferSize = New-Object System.Management.Automation.Host.Size ($Width, 1200) } catch { }
        try { $ui.WindowSize = New-Object System.Management.Automation.Host.Size ($Width, $Height) } catch { }
    } catch { }
    try { Clear-Host } catch { }
}

$script:FloppyBox = @{
    TL = [char]0x2554; TR = [char]0x2557; BL = [char]0x255A; BR = [char]0x255D
    H  = [char]0x2550; V  = [char]0x2551
    LT = [char]0x2560; RT = [char]0x2563       # kraeftige T-Stuecke  |=
    LS = [char]0x255F; RS = [char]0x2562       # duenne T-Stuecke     |-
}

function Write-FloppyRule {
    param([int] $Width = 76, [hashtable] $Theme = (Get-FloppyTheme), [ValidateSet('top', 'mid', 'thin', 'bottom')] [string] $Style = 'mid')
    $b = $script:FloppyBox
    $pair = switch ($Style) {
        'top'    { @($b.TL, $b.TR) }
        'bottom' { @($b.BL, $b.BR) }
        'thin'   { @($b.LS, $b.RS) }
        default  { @($b.LT, $b.RT) }
    }
    Write-Host ('  ' + $pair[0] + ([string]$b.H * ($Width + 2)) + $pair[1]) -ForegroundColor $Theme.Frame
}

function Write-FloppyRow {
    param([string] $Text = '', [hashtable] $Theme = (Get-FloppyTheme), [string] $Color, [int] $Width = 76)
    if (-not $Color) { $Color = $Theme.Text }
    if ($Text.Length -gt $Width) { $Text = $Text.Substring(0, $Width) }
    $b = $script:FloppyBox
    Write-Host ('  ' + $b.V + ' ') -NoNewline -ForegroundColor $Theme.Frame
    Write-Host ($Text.PadRight($Width))  -NoNewline -ForegroundColor $Color
    Write-Host (' ' + $b.V)              -ForegroundColor $Theme.Frame
}

function Format-FloppyShort {
    # Kuerzt lange Pfade in der Mitte:  C:\sehr\langer\...\datei.exe
    param([string] $Path, [int] $Max = 62)
    if ([string]::IsNullOrEmpty($Path) -or $Path.Length -le $Max) { return $Path }
    return '...' + $Path.Substring($Path.Length - ($Max - 3))
}

function Show-FloppyBanner {
    param([hashtable] $Theme = (Get-FloppyTheme), [string] $Subtitle = 'physische steam-library')
    $bb = [char]0x00BB
    $art = @(
        '   __________________ '
        '  |  ______________  |   T H E  F L O P P Y   H U B'
        "  | |              | |   $Subtitle"
        '  | |   //  //     | |'
        "  | |______________| |   $bb insert disk to begin"
        '  |__________________|'
    )
    foreach ($l in $art) { Write-Host ('  ' + $l) -ForegroundColor $Theme.Accent }
}

# ===========================================================================
#  Disketten-Helfer
# ===========================================================================

function Get-FloppyDriveInfo {
    param([string] $Letter = 'A')
    $l = "$Letter".Trim().TrimEnd(':', '\')
    if ([string]::IsNullOrWhiteSpace($l)) { return $null }
    $l = $l.Substring(0, 1).ToUpper()
    if ($l -notmatch '^[A-Z]$') { return $null }
    try   { return [System.IO.DriveInfo]::new($l) }
    catch { return $null }
}

function Test-FloppyPresent {
    param([string] $Letter = 'A')
    $d = Get-FloppyDriveInfo -Letter $Letter
    return ($d -and $d.IsReady)
}

function Read-FloppyReference {
    <#
        Liest eine Referenzdatei (game.txt) und gibt eine Hashtable der
        Schluessel zurueck. Gleiche Logik wie im Launcher.
    #>
    param([Parameter(Mandatory)][string] $Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $map }
    foreach ($raw in Get-Content -LiteralPath $Path -ErrorAction SilentlyContinue) {
        $line = $raw.Trim()
        if (-not $line -or $line[0] -eq '#' -or $line[0] -eq ';') { continue }
        if ($line -match '^\s*([A-Za-z_]+)\s*[:=]\s*(.+?)\s*$') {
            # Anfuehrungszeichen entfernen - sonst scheitert spaeter jede
            # Pfadpruefung mit "Illegales Zeichen im Pfad".
            $map[$Matches[1].ToLowerInvariant()] = (ConvertFrom-FloppyQuoted $Matches[2])
        }
        elseif ($line -match '^\s*(\d{3,})\s*$') {
            $map['id'] = $Matches[1]
        }
    }
    return $map
}

function Find-FloppyReferenceFile {
    param(
        [string] $Root = 'A:\',
        [string[]] $Names = @('game.txt', 'floppy.txt', 'launch.txt')
    )
    foreach ($n in $Names) {
        $p = Join-Path $Root $n
        if (Test-Path -LiteralPath $p -PathType Leaf) { return $p }
    }
    return $null
}

function Get-FloppyPlanSummary {
    <#
        Menschlich lesbare Vorschau: "Was wuerde der Launcher mit dieser
        Diskette tun?" - rein informativ, startet nichts.
    #>
    param([string] $Root = 'A:\', [string[]] $ReferenceNames = @('game.txt', 'floppy.txt', 'launch.txt'), [string[]] $Extensions = @('.exe', '.bat', '.cmd'))

    $ref = Find-FloppyReferenceFile -Root $Root -Names $ReferenceNames
    if ($ref) {
        $r = Read-FloppyReference -Path $ref
        foreach ($k in 'hub', 'hubmenu', 'menu', 'floppyhub') {
            if ($r.ContainsKey($k)) { return [pscustomobject]@{ Kind = 'Hub'; Detail = 'oeffnet das Floppy Hub Menue'; Source = $ref } }
        }
        foreach ($k in 'id', 'steam', 'steamid', 'gameid') {
            if ($r.ContainsKey($k)) {
                $id = $r[$k]
                $name = Get-SteamAppName -AppId $id
                return [pscustomobject]@{ Kind = 'Steam'; Detail = "startet Steam-Spiel $id$(if($name){" ($name)"})"; Source = $ref }
            }
        }
        foreach ($k in 'pcrun', 'localrun', 'pcexe') {
            if ($r.ContainsKey($k)) { return [pscustomobject]@{ Kind = 'PC-EXE'; Detail = "PC-Programm '$($r[$k])' (mit Rueckfrage im Interface)"; Source = $ref } }
        }
        foreach ($k in 'run', 'exe', 'program', 'path') {
            if ($r.ContainsKey($k)) { return [pscustomobject]@{ Kind = 'Disketten-EXE'; Detail = "fuehrt '$($r[$k])' von der Diskette aus"; Source = $ref } }
        }
        return [pscustomobject]@{ Kind = 'Unklar'; Detail = "Referenzdatei ohne verwertbaren Schluessel"; Source = $ref }
    }

    $exes = @(Get-ChildItem -LiteralPath $Root -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
            Where-Object { $Extensions -contains $_.Extension } | Select-Object -First 20)
    if ($exes.Count -eq 0) { return [pscustomobject]@{ Kind = 'Leer'; Detail = 'keine Referenzdatei, keine ausfuehrbare Datei'; Source = $null } }
    if ($exes.Count -eq 1) { return [pscustomobject]@{ Kind = 'Disketten-EXE'; Detail = "startet automatisch: $($exes[0].Name)"; Source = $null } }
    return [pscustomobject]@{ Kind = 'Auswahl'; Detail = "$($exes.Count) EXE gefunden - Interface fragt, welche"; Source = $null }
}

# ===========================================================================
#  Diskette beschreiben  +  Bibliothek
# ===========================================================================

function Write-FloppyReference {
    <#
        Schreibt eine Referenzdatei (game.txt) auf eine Diskette.
        Gibt bei Erfolg den Pfad zurueck, sonst $null.
        Ueberschreibt nur mit -Force oder nach Rueckfrage des Aufrufers.
    #>
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][ValidateSet('steam', 'run', 'pcrun', 'hub')][string] $Kind,
        [string] $Value,
        [string] $Arguments,
        [string] $Title,
        [string] $FileName = 'game.txt',
        [switch] $Force
    )

    if (-not (Test-Path -LiteralPath $Root)) {
        Write-Warning "Ziel nicht erreichbar: $Root  (Diskette eingelegt?)"
        return $null
    }
    $target = Join-Path $Root $FileName
    if ((Test-Path -LiteralPath $target) -and -not $Force) {
        Write-Warning "$target existiert bereits - mit -Force ueberschreiben."
        return $null
    }

    # Eingaben saeubern: Anfuehrungszeichen (z. B. aus "Als Pfad kopieren")
    # wuerden den Launcher sonst mit "Illegales Zeichen im Pfad" abwuergen.
    $Value     = ConvertFrom-FloppyQuoted $Value
    $Arguments = ConvertFrom-FloppyQuoted $Arguments

    # Plausibilitaet pruefen, BEVOR etwas auf die Diskette geschrieben wird.
    switch ($Kind) {
        'steam' {
            if ($Value -notmatch '^\d+$') { Write-Warning "Steam-ID muss aus Ziffern bestehen: '$Value'"; return $null }
        }
        'run' {
            if ([string]::IsNullOrWhiteSpace($Value)) { Write-Warning 'run= braucht einen Pfad.'; return $null }
            if (Test-FloppyPathRooted $Value) {
                Write-Warning "run= erwartet einen Pfad RELATIV zur Diskette. Fuer PC-Pfade -PcRun verwenden: $Value"
                return $null
            }
        }
        'pcrun' {
            if ([string]::IsNullOrWhiteSpace($Value)) { Write-Warning 'pcrun= braucht einen Pfad.'; return $null }
            if (-not (Test-FloppyPathRooted $Value)) {
                Write-Warning "pcrun= braucht einen ABSOLUTEN Pfad (z. B. D:\Spiele\x.exe): $Value"
                return $null
            }
            if (-not (Test-Path -LiteralPath $Value -PathType Leaf)) {
                Write-Warning "Achtung: '$Value' existiert derzeit nicht - der Launcher wird es spaeter ablehnen."
            }
            # Der Launcher sperrt C:\Windows grundsaetzlich - hier schon sagen.
            $blocked = ConvertTo-FloppyList (Get-FloppyIniValue (Import-FloppyIni (Get-FloppyConfigPath)) 'security' 'blocked_roots' '%SystemRoot%')
            foreach ($b in $blocked) {
                $bf = Expand-FloppyPath $b
                if ($bf -and $Value.StartsWith($bf, [StringComparison]::OrdinalIgnoreCase)) {
                    Write-Warning "Achtung: '$Value' liegt in einem gesperrten Systemordner ($bf). Der Launcher wird den Start ABLEHNEN."
                    break
                }
            }
        }
    }

    $lines = @('# von FloppyDisc/FloppyHub geschrieben  ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
    if ($Title) { $lines += "# $Title" }
    switch ($Kind) {
        'steam' { $lines += "id=$Value" }
        'run'   { $lines += "run=$Value" }
        'pcrun' { $lines += "pcrun=$Value" }
        'hub'   { $lines += 'hub=1' }
    }
    if ($Arguments) { $lines += "args=$Arguments" }

    try {
        Set-Content -LiteralPath $target -Value $lines -Encoding UTF8 -ErrorAction Stop
        return $target
    }
    catch {
        Write-Warning "Schreiben fehlgeschlagen: $($_.Exception.Message)"
        return $null
    }
}

function Get-FloppyDataHome {
    <#
        Ordner fuer schreibbare Daten (library.csv, Log).
        Normalerweise der Programmordner - ist der schreibgeschuetzt (weil nach
        C:\Program Files installiert wurde), wird auf %LOCALAPPDATA%\FloppyHub
        ausgewichen.
    #>
    $home1 = Get-FloppyHome
    try {
        $probe = Join-Path $home1 (".floppy-write-test-" + [guid]::NewGuid().ToString('N'))
        [System.IO.File]::WriteAllText($probe, 'x')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        return $home1
    }
    catch {
        $fallback = Join-Path $env:LOCALAPPDATA 'FloppyHub'
        try { if (-not (Test-Path -LiteralPath $fallback)) { New-Item -ItemType Directory -Path $fallback -Force | Out-Null } } catch { }
        return $fallback
    }
}

function Get-FloppyLibraryPath {
    # Vorhandene library.csv im Programmordner gewinnt (Bestandsdaten), sonst
    # der schreibbare Datenordner.
    $inHome = Join-Path (Get-FloppyHome) 'library.csv'
    if (Test-Path -LiteralPath $inHome -PathType Leaf) { return $inHome }
    return (Join-Path (Get-FloppyDataHome) 'library.csv')
}

function Get-FloppyLibrary {
    $p = Get-FloppyLibraryPath
    if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return , @() }
    try { return , @(Import-Csv -LiteralPath $p) } catch { return , @() }
}

function Add-FloppyLibraryEntry {
    <#
        Nimmt die aktuelle Diskette (oder uebergebene Werte) in library.csv auf.
        Doppelte (gleiche Kind+Value) werden aktualisiert statt dupliziert.
    #>
    param(
        [string] $Label,
        [string] $Kind,
        [string] $Value,
        [string] $Notes = ''
    )
    $p = Get-FloppyLibraryPath
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($r in (Get-FloppyLibrary)) {
        if ("$($r.Kind)" -eq $Kind -and "$($r.Value)" -eq $Value) { continue }
        $rows.Add($r)
    }
    $rows.Add([pscustomobject]@{
        Label   = $Label
        Kind    = $Kind
        Value   = $Value
        Added   = (Get-Date -Format 'yyyy-MM-dd')
        Notes   = $Notes
    })
    try {
        $rows | Select-Object Label, Kind, Value, Added, Notes |
            Export-Csv -LiteralPath $p -NoTypeInformation -Encoding UTF8 -ErrorAction Stop
        return $true
    }
    catch {
        Write-Warning "library.csv konnte nicht geschrieben werden: $($_.Exception.Message)"
        return $false
    }
}

# ===========================================================================
#  Logdatei
# ===========================================================================

function Get-FloppyLogPath {
    <#
        Pfad der Launcher-Logdatei laut FloppyLauncher.ini (sonst Standard).
        Relative Angaben werden gegen den SCHREIBBAREN Datenordner aufgeloest -
        genau wie der Launcher es tut, damit Hub und Launcher dieselbe Datei
        meinen, auch wenn nach C:\Program Files installiert wurde.
    #>
    param($Ini)
    if ($null -eq $Ini) { $Ini = Import-FloppyIni (Get-FloppyConfigPath) }
    $raw = Get-FloppyIniValue $Ini 'log' 'file' 'FloppyLauncher.log'
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }   # "" = kein Log

    $clean = ConvertFrom-FloppyQuoted ([Environment]::ExpandEnvironmentVariables("$raw"))
    if (Test-FloppyPathRooted $clean) {
        try { return [System.IO.Path]::GetFullPath($clean) } catch { return $clean }
    }
    # Bestandsdatei im Programmordner gewinnt, sonst Datenordner.
    $inHome = Join-Path (Get-FloppyHome) $clean
    if (Test-Path -LiteralPath $inHome -PathType Leaf) { return $inHome }
    return (Join-Path (Get-FloppyDataHome) $clean)
}

function Clear-FloppyLog {
    <#
        Leert die Launcher-Logdatei. Der bisherige Inhalt wandert einmalig nach
        <name>.old, damit nichts unwiederbringlich weg ist - beim naechsten Mal
        wird diese .old-Datei ueberschrieben. So bleiben es hoechstens zwei
        Dateien, statt endlos zu wachsen.

        Funktioniert auch, waehrend der Launcher laeuft: der schreibt mit
        Add-Content (oeffnet/schliesst pro Zeile), also stoert das Leeren nicht.

        Rueckgabe: Objekt mit Cleared / Bytes / Path / Backup / Message
    #>
    param(
        [string] $Path,
        [switch] $NoBackup
    )
    if (-not $Path) { $Path = Get-FloppyLogPath }
    $res = [pscustomobject]@{ Cleared = $false; Bytes = 0; Path = $Path; Backup = $null; Message = '' }

    if (-not $Path) { $res.Message = 'Logging ist in der INI abgeschaltet.'; return $res }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        $res.Message = 'Keine Logdatei vorhanden.'
        return $res
    }

    try {
        $res.Bytes = (Get-Item -LiteralPath $Path).Length
        if (-not $NoBackup -and $res.Bytes -gt 0) {
            $old = "$Path.old"
            Copy-Item -LiteralPath $Path -Destination $old -Force -ErrorAction Stop
            $res.Backup = $old
        }
        # Leeren statt Loeschen: ein laufender Launcher behaelt seinen Pfad.
        Set-Content -LiteralPath $Path -Value @() -Encoding UTF8 -Force -ErrorAction Stop
        $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
        Add-Content -LiteralPath $Path -Encoding UTF8 -ErrorAction SilentlyContinue `
            -Value "$stamp [INFO ] Log geleert (vorher $([math]::Round($res.Bytes/1KB,1)) KB)."
        $res.Cleared = $true
        $res.Message = 'Log geleert.'
    }
    catch {
        $res.Message = "Log konnte nicht geleert werden: $($_.Exception.Message)"
    }
    return $res
}

# ===========================================================================
#  Steam-Helfer
# ===========================================================================

$script:FloppySteamNames = @{
    '70'     = 'Half-Life'
    '220'    = 'Half-Life 2'
    '400'    = 'Portal'
    '620'    = 'Portal 2'
    '240'    = 'Counter-Strike: Source'
    '730'    = 'Counter-Strike 2'
    '440'    = 'Team Fortress 2'
    '570'    = 'Dota 2'
    '4000'   = "Garry's Mod"
    '105600' = 'Terraria'
    '220200' = 'Kerbal Space Program'
    '292030' = 'The Witcher 3: Wild Hunt'
    '271590' = 'Grand Theft Auto V'
    '346110' = 'ARK: Survival Evolved'
    '236850' = 'Stellaris'
    '294100' = 'RimWorld'
    '413150' = 'Stardew Valley'
    '739630' = 'Phasmophobia'
    '1145360' = 'Hades'
    '1091500' = 'Cyberpunk 2077'
}

function Get-SteamAppName {
    # Kennt ein paar Klassiker eingebaut; optional erweiterbar ueber
    # steam-appids.csv (Spalten: appid,name) neben den Skripten.
    param([Parameter(Mandatory)][string] $AppId)
    $id = "$AppId".Trim()
    if ($script:FloppySteamNames.ContainsKey($id)) { return $script:FloppySteamNames[$id] }

    $csv = Join-Path (Get-FloppyHome) 'steam-appids.csv'
    if (Test-Path -LiteralPath $csv -PathType Leaf) {
        try {
            $row = Import-Csv -LiteralPath $csv | Where-Object { "$($_.appid)".Trim() -eq $id } | Select-Object -First 1
            if ($row) { return $row.name }
        } catch { }
    }
    return $null
}

function ConvertTo-SteamRunUrl {
    param([Parameter(Mandatory)][string] $AppId)
    return "steam://rungameid/$AppId"
}

function Resolve-SteamAppId {
    # Nimmt eine Zahl ODER eine Steam-Store-URL und gibt die reine AppID zurueck.
    param([Parameter(Mandatory)][string] $InputText)
    $t = "$InputText".Trim()
    if ($t -match '^\d+$') { return $t }
    if ($t -match 'store\.steampowered\.com/app/(\d+)') { return $Matches[1] }
    if ($t -match 'rungameid/(\d+)') { return $Matches[1] }
    if ($t -match '/(\d+)(/|$)') { return $Matches[1] }
    return $null
}

# ===========================================================================
#  Easter Eggs   (gemeinsam fuer Hub-Menue und Interface)
# ===========================================================================

function Get-FloppyEasterEgg {
    <#
        Gibt zu einer Eingabe einen versteckten Spruch zurueck ($null = keiner).
        Rein kosmetisch - veraendert nie eine Programmentscheidung.
    #>
    param([string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $t = "$Text".Trim().ToLowerInvariant() -replace '\s+', ''

    switch -Regex ($t) {
        '^(konami|uuddlrlrba)$' {
            return "  UP UP DOWN DOWN LEFT RIGHT LEFT RIGHT B A   ...  30 Extraleben gewaehrt. (nicht wirklich)"
        }
        '^(wopr|joshua)$' {
            return "  `"A STRANGE GAME.`"`n  `"THE ONLY WINNING MOVE IS NOT TO PLAY.`"`n  `"HOW ABOUT A NICE GAME OF CHESS?`"   -  WOPR, 1983"
        }
        '^xyzzy$'                  { return '  Nothing happens.' }
        '^(42|answer|theanswer)$'  { return '  Die Antwort auf die Frage nach dem Leben, dem Universum und dem ganzen Rest.' }
        '^sudo'                    { return '  Schoener Versuch. Auf einer Diskette gibt es kein sudo.' }
        '^(hello|hallo|hi)$'       { return '  HELLO, HUMAN. SCHOEN, DASS DU DA BIST.' }
        '^(format|formatc|del|delete)' { return '  Nein. NEIN. Fass die Diskette nicht an.' }
        '^(fsociety|mrrobot)$'     { return '  control is an illusion.' }
        '^(rosebud|motherlode)$'   { return '  Falsche Aera - das war eine andere Diskette.' }
        '^(starwars|r2d2)$'        { return "  [o_o]   *bloop beep*   die Diskette, die du suchst, ist es nicht." }
        '^(dir|ls)$'               { return '  Track 00 ... Track 79 ... 1.44 MB frei? Traeum weiter.' }
        default                    { return $null }
    }
}

function Get-FloppySeasonalTheme {
    <#
        Datumsabhaengiges Sonder-Thema fuer die Retro-Fenster.
        $null = normaler Tag. Sonst @{ Name; Line; Theme }.
    #>
    param([datetime] $Date = (Get-Date))
    $doy = $Date.DayOfYear

    if ($Date.Month -eq 12 -and $Date.Day -ge 24 -and $Date.Day -le 26) {
        return @{ Name = 'weihnachten'; Line = '*  *  frohe weihnachten  *  *'; Theme = (Get-FloppyTheme 'ice') }
    }
    if (($Date.Month -eq 12 -and $Date.Day -eq 31) -or ($Date.Month -eq 1 -and $Date.Day -eq 1)) {
        return @{ Name = 'silvester'; Line = ".*'  frohes neues jahr  '*."; Theme = (Get-FloppyTheme 'amber') }
    }
    if ($Date.Month -eq 10 -and $Date.Day -eq 31) {
        return @{ Name = 'halloween'; Line = 'spukt es in track 13?'; Theme = (Get-FloppyTheme 'amber') }
    }
    if ($Date.Day -eq 13 -and $Date.DayOfWeek -eq 'Friday') {
        return @{ Name = 'freitag-der-13te'; Line = 'track 13, sektor 13 ... viel glueck.'; Theme = (Get-FloppyTheme 'mono') }
    }
    if ($doy -eq 256) {
        return @{ Name = 'tag-des-programmierers'; Line = '0x100 - tag des programmierers'; Theme = (Get-FloppyTheme 'green') }
    }
    return $null
}

function Get-FloppyDiskArt {
    # ASCII-3.5"-Diskette - ALLE Zeilen exakt 33 Zeichen breit, sonst franst
    # der Rahmen im Hub-Menue aus. Test: Get-FloppyDiskArt | % { $_.Length }
    param([switch] $Wink)
    $eye = if ($Wink) { 'o  -' } else { 'o  o' }
    return @(
        '     ___________________________ '
        '    |  _______________________  |'
        '    | |                       | |'
        '    | |     F L O P P Y       | |'
        '    | |       H U B           | |'
        "    | |   $eye   1.44 MB      | |"
        '    | |_______________________| |'
        '    |   ___________________  [] |'
        '    |__|___________________|____|'
    )
}

# ===========================================================================
#  Selbsttest:   . .\FloppyLib.ps1 ; Test-FloppyLib
# ===========================================================================

function Test-FloppyLib {
    "FloppyLib geladen aus: $(Get-FloppyHome)"
    "Config-Pfad          : $(Get-FloppyConfigPath)  (vorhanden: $(Test-Path (Get-FloppyConfigPath)))"
    "Diskette A: bereit   : $(Test-FloppyPresent -Letter 'A')"
    if (Test-FloppyPresent -Letter 'A') {
        $s = Get-FloppyPlanSummary -Root 'A:\'
        "Diskette wuerde      : [$($s.Kind)] $($s.Detail)"
    }
    "Steam 220200         : $(Get-SteamAppName -AppId '220200')"
    "Easter Egg 'wopr'    : $([bool](Get-FloppyEasterEgg 'wopr'))  '$(Get-FloppyEasterEgg 'xyzzy')'"
    $__season = Get-FloppySeasonalTheme
    "Saison-Thema heute   : $(if ($__season) { $__season.Name } else { '(normaler Tag)' })"
}
