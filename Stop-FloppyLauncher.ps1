<#
.SYNOPSIS
    Beendet laufende Floppy-Launcher-Instanzen.

.DESCRIPTION
    Sucht PowerShell-Prozesse, die FloppyLauncher.ps1 ausfuehren, und beendet
    sie. Wird vom Deinstallationsprogramm aufgerufen (damit die Dateien
    geloescht werden koennen) und liegt als Startmenue-Eintrag bei.

    Ohne Treffer passiert nichts - der Aufruf ist also immer unbedenklich.

.PARAMETER Quiet
    Keine Ausgabe (fuer die Deinstallation).

.EXAMPLE
    .\Stop-FloppyLauncher.ps1
#>

[CmdletBinding()]
param(
    [switch] $Quiet
)

$ErrorActionPreference = 'SilentlyContinue'

function Note { param([string] $Text, [string] $Color = 'Gray')
    if (-not $Quiet) { try { Write-Host $Text -ForegroundColor $Color } catch { } }
}

$found = @(
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" |
        Where-Object { "$($_.CommandLine)" -match 'FloppyLauncher\.ps1' }
)

if ($found.Count -eq 0) {
    Note '  Es laeuft kein Floppy Launcher.' 'DarkGray'
    exit 0
}

foreach ($proc in $found) {
    Note "  beende PID $($proc.ProcessId) ..." 'Yellow'
    Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Milliseconds 400

$left = @(
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" |
        Where-Object { "$($_.CommandLine)" -match 'FloppyLauncher\.ps1' }
)

if ($left.Count -eq 0) {
    Note "  $($found.Count) Instanz(en) beendet." 'Green'
    exit 0
}

Note "  $($left.Count) Instanz(en) liessen sich nicht beenden." 'Red'
exit 1
