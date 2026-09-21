<#
.SYNOPSIS
    Baut die App-Variante fuer das Setup: FloppyLauncher.exe (Motor) + FloppyHub.exe (Godot).

.DESCRIPTION
    1. Unit-Tests (dotnet test)
    2. Motor als native EXE (NativeAOT) - braucht die C++-Werkzeuge von Visual Studio
    3. Godot-App exportieren ("Windows Desktop", .NET) - braucht die Godot-Exportvorlagen
    Ergebnis: build\app-out\  (wird vom Setup eingepackt)

    Die Version steht in app\src\FloppyHub.App\project.godot (config/version). Mit -Version
    wird sie nur fuer diesen Build ersetzt (z. B. aus einem Release-Tag) und danach wieder
    zurueckgestellt.

.PARAMETER Version
    z. B. 2.0.0-beta.1 (Vorgabe: aus project.godot)

.PARAMETER Godot
    Pfad zur Godot-.NET-EXE (Vorgabe: PATH, sonst winget-Installation)

.EXAMPLE
    .\build\Build-App.ps1
.EXAMPLE
    .\build\Build-App.ps1 -Version 2.0.0-beta.2 -SkipTests
#>

[CmdletBinding()]
param(
    [string] $Version,
    [string] $Godot,
    [string] $OutDir,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$app     = Join-Path $root 'app'
$project = Join-Path $app 'src\FloppyHub.App'
$motor   = Join-Path $app 'src\FloppyLauncher\FloppyLauncher.csproj'
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'app-out' }

function Say { param([string]$t, [string]$c = 'Gray') Write-Host $t -ForegroundColor $c }

Say ''
Say '  FLOPPY HUB  ::  App bauen' 'Cyan'
Say '  ---------------------------------------------------------' 'DarkGray'

# --- Version ------------------------------------------------------------------
$projectFile = Join-Path $project 'project.godot'
$presetFile  = Join-Path $project 'export_presets.cfg'
$projectText = [IO.File]::ReadAllText($projectFile)
$presetText  = [IO.File]::ReadAllText($presetFile)
if (-not $Version) {
    if ($projectText -match 'config/version="([^"]+)"') { $Version = $Matches[1] } else { $Version = '3.2.0' }
}
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') { throw "Ungueltige Version: $Version (erwartet z. B. 2.0.0 oder 2.0.0-beta.1)" }
$numeric = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
Say "  Version: $Version   (Datei-Version $numeric.0)" 'DarkGray'

# --- Godot + Vorlagen ---------------------------------------------------------------
if (-not $Godot) {
    $Godot = Get-Command 'godot*mono*console*.exe', 'godot.exe' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty Source
}
if (-not $Godot) {
    $Godot = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter 'Godot_v4*_mono_win64_console.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Godot -or -not (Test-Path -LiteralPath $Godot)) {
    throw 'Godot 4 (.NET) nicht gefunden. Installieren:  winget install GodotEngine.GodotEngine.Mono   (oder -Godot <Pfad>)'
}
$godotVersion = (& $Godot --version 2>$null | Select-Object -First 1).Trim()   # z. B. 4.7.2.stable.mono.official.xxxx
$templateName = ($godotVersion -split '\.')[0..3] -join '.'                   # 4.7.2.stable
$templates = Join-Path $env:APPDATA "Godot\export_templates\$templateName.mono"
if (-not (Test-Path -LiteralPath (Join-Path $templates 'windows_release_x86_64.exe'))) {
    throw "Godot-Exportvorlagen fehlen: $templates`nIm Godot-Editor: Editor -> Exportvorlagen verwalten -> Herunterladen (Mono/.NET)."
}
Say "  Godot:   $godotVersion" 'DarkGray'

# --- 1) Tests -----------------------------------------------------------------------
Say ''
if ($SkipTests) { Say '  [1/3] Tests uebersprungen' 'DarkYellow' }
else {
    Say '  [1/3] Tests' 'White'
    dotnet test (Join-Path $app 'Floppy.sln') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen.' }
}

if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# --- 2) Motor (native EXE) --------------------------------------------------------
Say ''
Say '  [2/3] Motor: FloppyLauncher.exe (NativeAOT)' 'White'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path -LiteralPath $vswhere) -and ($env:PATH -notlike "*$vswhere*")) { $env:PATH = "$vswhere;$env:PATH" }
$motorOut = Join-Path $PSScriptRoot 'motor-publish'
dotnet publish $motor -c Release -r win-x64 -o $motorOut -nologo -v q "-p:Version=$numeric" "-p:InformationalVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Motor-Build fehlgeschlagen (C++-Werkzeuge von Visual Studio installiert?).' }
Copy-Item -LiteralPath (Join-Path $motorOut 'FloppyLauncher.exe') -Destination $OutDir
Remove-Item -LiteralPath $motorOut -Recurse -Force
Say ("        FloppyLauncher.exe  {0:n1} MB" -f ((Get-Item (Join-Path $OutDir 'FloppyLauncher.exe')).Length / 1MB)) 'DarkGray'

# --- 3) App (Godot-Export) ------------------------------------------------------------
Say ''
Say '  [3/3] App: FloppyHub.exe (Godot-Export)' 'White'
try {
    # Version nur fuer diesen Build eintragen
    [IO.File]::WriteAllText($projectFile, ($projectText -replace 'config/version="[^"]*"', "config/version=`"$Version`""))
    [IO.File]::WriteAllText($presetFile, ($presetText -replace '(application/(file|product)_version=)"[^"]*"', "`$1`"$numeric.0`""))

    if (-not (Test-Path -LiteralPath (Join-Path $project '.godot'))) {
        Say '        Grafiken importieren ...' 'DarkGray'
        & $Godot --headless --path $project --import *> $null
    }
    $log = Join-Path $PSScriptRoot 'app-export.log'
    & $Godot --headless --path $project --export-release 'Windows Desktop' (Join-Path $OutDir 'FloppyHub.exe') *> $log
    $problems = Select-String -LiteralPath $log -Pattern '^\s*ERROR:' | Select-Object -First 5
    if ($LASTEXITCODE -ne 0 -or $problems -or -not (Test-Path -LiteralPath (Join-Path $OutDir 'FloppyHub.pck'))) {
        $problems | ForEach-Object { Say "        $($_.Line)" 'Red' }
        throw "Godot-Export fehlgeschlagen - siehe $log"
    }
}
finally {
    [IO.File]::WriteAllText($projectFile, $projectText)
    [IO.File]::WriteAllText($presetFile, $presetText)
}

$size = (Get-ChildItem -LiteralPath $OutDir -Recurse -File | Measure-Object Length -Sum).Sum
Say ''
Say '  FERTIG' 'Green'
Say "      $OutDir" 'White'
Say ("      {0:n0} Dateien, {1:n1} MB" -f (Get-ChildItem -LiteralPath $OutDir -Recurse -File).Count, ($size / 1MB)) 'DarkGray'
Say ''
