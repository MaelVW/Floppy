<#
.SYNOPSIS
    Baut die Handy-App "Floppy Chat" als Android-APK und installiert sie auf Wunsch aufs angesteckte Handy.

.DESCRIPTION
    1. Tests der Chat-Logik (Floppy.Chat.Client.Tests) - schnell, am PC.
    2. dotnet publish der MAUI-App (Release) -> APK.
    3. APK nach dist\ kopieren (FloppyChat-<Version>.apk).
    4. Mit -Install: per adb aufs Handy (USB-Debugging an, Handy per Kabel dran); mit -Launch startet die App gleich.

    Voraussetzung (einmalig, siehe docs/HANDY-PORT.md): .NET-Workload "maui-android" und das Android-SDK.
    Die APK ist mit dem Debug-Schluessel dieses PCs signiert - zum Testen gedacht, nicht fuer den Play Store.

    Beispiele:
        build\Build-Mobile.ps1
        build\Build-Mobile.ps1 -Install -Launch
        build\Build-Mobile.ps1 -SkipTests -Configuration Debug
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Launch,
    [switch]$SkipTests,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$Sdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' })
)

$ErrorActionPreference = 'Stop'
$repo    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'app\src\FloppyChat.Mobile\FloppyChat.Mobile.csproj'
$tests   = Join-Path $repo 'app\tests\Floppy.Chat.Client.Tests'
$dist    = Join-Path $repo 'dist'
$appId   = 'io.github.maelvw.floppychat'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Check($what) { if ($LASTEXITCODE -ne 0) { throw "$what fehlgeschlagen (Exitcode $LASTEXITCODE)." } }

if (-not $SkipTests) {
    Step 'Tests der Chat-Logik'
    dotnet test $tests -nologo -v q
    Check 'Tests'
}

Step "APK bauen ($Configuration)"
$props = @("-p:AndroidSdkDirectory=$Sdk")
if ($Configuration -eq 'Debug') { $props += '-p:EmbedAssembliesIntoApk=true' }   # sonst laeuft eine Debug-APK nur mit "Schnell-Deployment"
dotnet publish $project -f net10.0-android -c $Configuration -nologo @props
Check 'Build'

$publish = Join-Path $repo "app\src\FloppyChat.Mobile\bin\$Configuration\net10.0-android\publish"
$apk = Get-ChildItem $publish -Filter '*.apk' -ErrorAction SilentlyContinue |
       Sort-Object { $_.Name -like '*-Signed.apk' } -Descending | Select-Object -First 1
if (-not $apk) { throw "Keine APK unter $publish gefunden." }

$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.ApplicationDisplayVersion | Where-Object { $_ } | Select-Object -First 1
New-Item -ItemType Directory -Force $dist | Out-Null
$target = Join-Path $dist "FloppyChat-$version.apk"
Copy-Item $apk.FullName $target -Force
Write-Host ("APK: {0} ({1:N1} MB)" -f $target, ($apk.Length / 1MB)) -ForegroundColor Green

if ($Install -or $Launch) {
    $adb = Join-Path $Sdk 'platform-tools\adb.exe'
    if (-not (Test-Path $adb)) { throw "adb nicht gefunden: $adb" }

    Step 'Handy suchen'
    & $adb devices
    $devices = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' })
    if ($devices.Count -eq 0) { throw 'Kein Handy gefunden: USB-Debugging an? Kabel dran? Rueckfrage am Handy bestaetigt?' }

    Step 'Installieren'
    & $adb install -r $target
    Check 'Installation'

    if ($Launch) {
        Step 'App starten'
        & $adb shell monkey -p $appId -c android.intent.category.LAUNCHER 1 | Out-Null
    }
}
