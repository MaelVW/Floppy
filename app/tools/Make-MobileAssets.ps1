<#
.SYNOPSIS
    Erzeugt die Bilder der Handy-App (app/src/FloppyChat.Mobile/Resources) aus den Pixel-Icons der Desktop-App.

.DESCRIPTION
    - Die kleinen Icons (16/32 px) werden ganzzahlig (Nearest-Neighbor) auf 96 px hochskaliert, damit sie
      auf dem Handy scharf bleiben und nicht verschwimmen.
    - App-Symbol: der Vordergrund bekommt Rand (Android schneidet runde/eckige Formen aus), der Hintergrund
      ist eine Farbe (in der .csproj).
    - Vorhandene Dateien werden ueberschrieben; die Quellen (FloppyHub.App/assets) werden nur gelesen.

    Aufruf:  pwsh app\tools\Make-MobileAssets.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root   = Split-Path -Parent $PSScriptRoot                       # ...\app
$icons  = Join-Path $root 'src\FloppyHub.App\assets\icons'
$appPng = Join-Path $root 'src\FloppyHub.App\assets\app\icon.png'
$res    = Join-Path $root 'src\FloppyChat.Mobile\Resources'

foreach ($dir in 'Images', 'AppIcon', 'Splash') { New-Item -ItemType Directory -Force (Join-Path $res $dir) | Out-Null }

function Save-Scaled {
    param([string]$Source, [string]$Target, [int]$Canvas, [int]$Factor = 0)

    $src = [System.Drawing.Image]::FromFile($Source)
    try {
        if ($Factor -le 0) { $Factor = [math]::Max(1, [math]::Floor($Canvas / [math]::Max($src.Width, $src.Height))) }
        $w = $src.Width * $Factor
        $h = $src.Height * $Factor

        $bmp = New-Object System.Drawing.Bitmap $Canvas, $Canvas, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.CompositingMode    = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::None
                $x = [int](($Canvas - $w) / 2)
                $y = [int](($Canvas - $h) / 2)
                $g.DrawImage($src, (New-Object System.Drawing.Rectangle $x, $y, $w, $h), 0, 0, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel)
            }
            finally { $g.Dispose() }
            $bmp.Save($Target, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bmp.Dispose() }
    }
    finally { $src.Dispose() }
}

# --- Icons in der Oberflaeche: name in der Desktop-App -> Bildname in der Handy-App ---
$map = [ordered]@{
    chat     = 'ico_chat'
    key      = 'ico_key'
    floppy   = 'ico_floppy'
    contact  = 'ico_contact'
    door     = 'ico_door'
    lan      = 'ico_lan'
    send     = 'ico_send'
    settings = 'ico_settings'
    led_on   = 'ico_led_on'
    led_off  = 'ico_led_off'
    led_warn = 'ico_led_warn'
    warn     = 'ico_warn'
    info     = 'ico_info'
}
foreach ($entry in $map.GetEnumerator()) {
    $from = Join-Path $icons ($entry.Key + '.png')
    if (-not (Test-Path $from)) { throw "Icon fehlt: $from" }
    Save-Scaled -Source $from -Target (Join-Path $res "Images\$($entry.Value).png") -Canvas 96
}

# --- App-Symbol (Vordergrund 432 px mit Rand) und Startbild ---
Save-Scaled -Source $appPng -Target (Join-Path $res 'AppIcon\appiconfg.png') -Canvas 432 -Factor 1
Save-Scaled -Source $appPng -Target (Join-Path $res 'AppIcon\appicon.png')   -Canvas 432 -Factor 1
Copy-Item $appPng (Join-Path $res 'Splash\splash.png') -Force

Write-Host "Fertig: $($map.Count) Icons + App-Symbol + Startbild in $res"
