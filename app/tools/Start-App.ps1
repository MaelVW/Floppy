<#
.SYNOPSIS
    Startet die Floppy Hub App (Godot) zum Ausprobieren - ohne Export.

.DESCRIPTION
    Baut den C#-Code, importiert beim ersten Mal die Grafiken und startet die App
    mit dem Repo-Ordner als Programmordner (FloppyLauncher.ini, library.csv, Log).

.EXAMPLE
    .\app\tools\Start-App.ps1
.EXAMPLE
    .\app\tools\Start-App.ps1 -Theme dark -FirstRun
.EXAMPLE
    .\app\tools\Start-App.ps1 -Editor      # Projekt im Godot-Editor oeffnen
#>
[CmdletBinding()]
param(
    [ValidateSet('system', 'light', 'dark')]
    [string] $Theme,

    # Willkommensdialog erneut zeigen
    [switch] $FirstRun,

    # Nur zum Testen: Ordner statt Laufwerk A: auswerten
    [string] $Drive,

    # Godot-Editor statt App oeffnen
    [switch] $Editor
)

$ErrorActionPreference = 'Stop'
$project = Resolve-Path (Join-Path $PSScriptRoot '..\src\FloppyHub.App')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# Godot finden: PATH, sonst winget-Installation
$godot = Get-Command 'godot*mono*.exe', 'godot.exe' -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty Source
if (-not $godot) {
    $godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter 'Godot_v4*_mono_win64.exe' -ErrorAction SilentlyContinue |
        Where-Object Name -notlike '*console*' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $godot) {
    throw 'Godot 4 (.NET) nicht gefunden. Installieren mit:  winget install GodotEngine.GodotEngine.Mono'
}

Write-Host "[1/3] C# bauen ..." -ForegroundColor Cyan
dotnet build (Join-Path $project 'FloppyHub.App.csproj') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

if (-not (Test-Path (Join-Path $project '.godot'))) {
    Write-Host "[2/3] Erster Start: Grafiken importieren ..." -ForegroundColor Cyan
    & $godot --headless --path $project --import | Out-Null
}
else {
    Write-Host "[2/3] Grafiken bereits importiert." -ForegroundColor DarkGray
}

if ($Editor) {
    Write-Host "[3/3] Godot-Editor oeffnen ..." -ForegroundColor Cyan
    & $godot --editor --path $project
    return
}

$appArgs = @('--path', "`"$project`"", '++', '--home', "`"$repo`"")
if ($Theme) { $appArgs += @('--theme', $Theme) }
if ($FirstRun) { $appArgs += '--first-run' }
if ($Drive) { $appArgs += @('--drive', "`"$Drive`"") }

Write-Host "[3/3] Floppy Hub starten ..." -ForegroundColor Cyan
Start-Process -FilePath $godot -ArgumentList $appArgs
