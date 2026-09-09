<#
.SYNOPSIS
    FloppyDisc - eine Diskette bespielen oder inspizieren (Kommandozeile).

.DESCRIPTION
    Kleines Werkzeug fuers Vorbereiten von Disketten fuer den Floppy Launcher.
    Das gemuetliche Menue dazu ist FloppyHub.ps1 - dieses Skript ist die
    schnelle Variante fuer die Kommandozeile / fuer Skripte.

.PARAMETER Inspect
    Zeigt, was auf der Diskette steht und was der Launcher damit tun wuerde.

.PARAMETER Steam
    AppID oder Steam-Store-Link -> schreibt  id=<appid>  in die game.txt.

.PARAMETER Run
    Pfad RELATIV zur Diskette -> schreibt  run=<pfad>.

.PARAMETER PcRun
    ABSOLUTER Pfad auf dem PC -> schreibt  pcrun=<pfad>  (Launcher fragt nach).

.PARAMETER Hub
    Macht die Diskette zu einer Hub-Diskette (schreibt  hub=1).

.PARAMETER Arguments
    Optionale Startargumente (args=).

.PARAMETER Drive
    Laufwerksbuchstabe. Standard: aus FloppyLauncher.ini, sonst A:.

.PARAMETER Force
    Bestehende game.txt ohne Rueckfrage ueberschreiben.

.PARAMETER AddToLibrary
    Nach dem Schreiben zusaetzlich in library.csv aufnehmen.

.EXAMPLE
    .\FloppyDisc.ps1 -Inspect

.EXAMPLE
    .\FloppyDisc.ps1 -Steam 220 -Arguments '-console' -AddToLibrary

.EXAMPLE
    .\FloppyDisc.ps1 -PcRun 'D:\Emu\dosbox\DOSBox.exe'

.EXAMPLE
    .\FloppyDisc.ps1 -Hub

.NOTES
    Braucht FloppyLib.ps1 im selben Ordner.
    Teil von: FloppyLauncher.ps1 / FloppyInterface.ps1 / FloppyHub.ps1 / FloppyLib.ps1
#>

[CmdletBinding(DefaultParameterSetName = 'Inspect')]
param(
    [Parameter(ParameterSetName = 'Inspect')]                       [switch] $Inspect,
    [Parameter(ParameterSetName = 'Steam', Mandatory)]              [string] $Steam,
    [Parameter(ParameterSetName = 'Run',   Mandatory)]              [string] $Run,
    [Parameter(ParameterSetName = 'PcRun', Mandatory)]              [string] $PcRun,
    [Parameter(ParameterSetName = 'Hub',   Mandatory)]              [switch] $Hub,
    [Parameter(ParameterSetName = 'Steam')]
    [Parameter(ParameterSetName = 'Run')]
    [Parameter(ParameterSetName = 'PcRun')]                         [string] $Arguments,
    [string] $Drive,
    [switch] $Force,
    [switch] $AddToLibrary
)

$ErrorActionPreference = 'Stop'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
. (Join-Path $here 'FloppyLib.ps1')

$ini   = Import-FloppyIni (Get-FloppyConfigPath)
if (-not $Drive) { $Drive = Get-FloppyIniValue $ini 'drive' 'letter' 'A:' }
$root  = $Drive.TrimEnd('\') + '\'
$refNm = ConvertTo-FloppyList (Get-FloppyIniValue $ini 'reference' 'file_names' 'game.txt, floppy.txt, launch.txt')
if ($refNm.Count -eq 0) { $refNm = @('game.txt', 'floppy.txt', 'launch.txt') }

function Say { param([string]$t, [string]$c = 'Gray') Write-Host $t -ForegroundColor $c }

# --- Inspect --------------------------------------------------------
if ($PSCmdlet.ParameterSetName -eq 'Inspect') {
    Say ""
    Say "  FLOPPY DISC  ::  $Drive" 'Cyan'
    Say "  --------------------------------------------------" 'DarkGray'
    if (-not (Test-FloppyPresent -Letter $Drive)) {
        Say "  Keine Diskette im Laufwerk $Drive." 'Yellow'
        return
    }
    $s = Get-FloppyPlanSummary -Root $root -ReferenceNames $refNm
    Say "  Einordnung : $($s.Kind)" 'White'
    Say "  Aktion     : $($s.Detail)" 'White'
    if ($s.Source) {
        Say "  Referenz   : $($s.Source)" 'DarkGray'
        Say ""
        Say "  Inhalt:" 'DarkGray'
        Get-Content -LiteralPath $s.Source | ForEach-Object { Say "     $_" 'DarkGray' }
    }
    Say ""
    Say "  Dateien:" 'DarkGray'
    Get-ChildItem -LiteralPath $root -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
        ForEach-Object { Say ("     {0,10:n0}  {1}" -f $_.Length, $_.FullName.Substring($root.Length)) 'DarkGray' }
    return
}

# --- Schreiben -----------------------------------------------------
if (-not (Test-FloppyPresent -Letter $Drive)) {
    Say "  Keine Diskette in $Drive - bitte einlegen." 'Red'
    exit 1
}

$kind = $null; $value = $null; $title = $null
switch ($PSCmdlet.ParameterSetName) {
    'Steam' {
        $id = Resolve-SteamAppId $Steam
        if (-not $id) { Say "  Keine AppID aus '$Steam' erkennbar." 'Red'; exit 1 }
        $kind = 'steam'; $value = $id; $title = Get-SteamAppName -AppId $id
        Say ("  Steam-Spiel: id=$id" + $(if ($title) { "  ($title)" } else { '' })) 'Green'
    }
    'Run'   { $kind = 'run';   $value = $Run;   Say "  Disketten-EXE: run=$Run" 'Green' }
    'PcRun' {
        $kind = 'pcrun'; $value = $PcRun
        Say "  PC-Programm: pcrun=$PcRun" 'Green'
        Say "  (Der Launcher fragt beim Einlegen nach; C:\Windows ist gesperrt.)" 'DarkGray'
    }
    'Hub'   { $kind = 'hub'; $title = 'Hub-Diskette'; Say "  Hub-Diskette (hub=1)" 'Green' }
}

$target = Join-Path $root 'game.txt'
if ((Test-Path -LiteralPath $target) -and -not $Force) {
    $a = Read-Host "  $target existiert. ueberschreiben? (J/N)"
    if ($a -notmatch '^(j|ja|y)$') { Say "  abgebrochen." 'Yellow'; exit 0 }
}

$written = Write-FloppyReference -Root $root -Kind $kind -Value $value -Arguments $Arguments -Title $title -Force
if (-not $written) { Say "  Schreiben fehlgeschlagen." 'Red'; exit 1 }

Say ""
Say "  geschrieben: $written" 'Green'
Get-Content -LiteralPath $written | ForEach-Object { Say "     $_" 'DarkGray' }

if ($AddToLibrary) {
    $lbl = if ($title) { $title } else { "$kind $value" }
    if (Add-FloppyLibraryEntry -Label $lbl -Kind $kind -Value "$value" -Notes 'via FloppyDisc') {
        Say "  in library.csv aufgenommen." 'Green'
    }
}
