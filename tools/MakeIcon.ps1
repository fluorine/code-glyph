# Generates assets/icon.svg and assets/icon.png from one set of shapes.
# Motif: a simple Taino sun-face (zemi) with spiral eyes.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets'))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null
$ink = '#B5532F'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function Get-Spiral([double]$cx, [double]$cy, [double]$rMax, [double]$turns, [int]$dir) {
    $pts = @()
    $steps = 60
    for ($i = 0; $i -le $steps; $i++) {
        $t = $i / $steps
        $a = $dir * 2 * [math]::PI * $turns * $t
        $r = $rMax * $t
        $pts += [pscustomobject]@{ X = $cx + $r * [math]::Cos($a); Y = $cy + $r * [math]::Sin($a) }
    }
    return ,$pts
}

$face = @{ cx = 64; cy = 64; r = 40 }
$rays = 0..7 | ForEach-Object {
    $a = $_ * [math]::PI / 4
    [pscustomobject]@{
        X1 = 64 + 49 * [math]::Cos($a); Y1 = 64 + 49 * [math]::Sin($a)
        X2 = 64 + 58 * [math]::Cos($a); Y2 = 64 + 58 * [math]::Sin($a)
    }
}
$eyes = @((Get-Spiral 46 56 13 2.25 1), (Get-Spiral 82 56 13 2.25 -1))
$mouth = [pscustomobject]@{ X1 = 50; Y1 = 88; X2 = 78; Y2 = 88 }

# SVG
$svg = New-Object System.Text.StringBuilder
[void]$svg.AppendLine('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" fill="none" stroke="' + $ink + '" stroke-width="4" stroke-linecap="round" stroke-linejoin="round">')
[void]$svg.AppendLine("  <circle cx=`"$($face.cx)`" cy=`"$($face.cy)`" r=`"$($face.r)`"/>")
foreach ($r in $rays) {
    [void]$svg.AppendLine(('  <line x1="{0}" y1="{1}" x2="{2}" y2="{3}"/>' -f $r.X1.ToString('0.##', $culture), $r.Y1.ToString('0.##', $culture), $r.X2.ToString('0.##', $culture), $r.Y2.ToString('0.##', $culture)))
}
foreach ($e in $eyes) {
    $d = ($e | ForEach-Object { '{0},{1}' -f $_.X.ToString('0.##', $culture), $_.Y.ToString('0.##', $culture) }) -join ' '
    [void]$svg.AppendLine("  <polyline points=`"$d`"/>")
}
[void]$svg.AppendLine(('  <line x1="{0}" y1="{1}" x2="{2}" y2="{3}"/>' -f $mouth.X1, $mouth.Y1, $mouth.X2, $mouth.Y2))
[void]$svg.AppendLine('</svg>')
[System.IO.File]::WriteAllText((Join-Path $OutDir 'icon.svg'), $svg.ToString())

# PNG (128 px, drawn at 4x then reduced for smooth edges)
$scale = 4
$big = New-Object System.Drawing.Bitmap (128 * $scale), (128 * $scale)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($ink)), (4 * $scale)
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$g.DrawEllipse($pen, ($face.cx - $face.r) * $scale, ($face.cy - $face.r) * $scale, 2 * $face.r * $scale, 2 * $face.r * $scale)
foreach ($r in $rays) { $g.DrawLine($pen, $r.X1 * $scale, $r.Y1 * $scale, $r.X2 * $scale, $r.Y2 * $scale) }
foreach ($e in $eyes) {
    $p = $e | ForEach-Object { New-Object System.Drawing.PointF ([single]($_.X * $scale)), ([single]($_.Y * $scale)) }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$p)
}
$g.DrawLine($pen, $mouth.X1 * $scale, $mouth.Y1 * $scale, $mouth.X2 * $scale, $mouth.Y2 * $scale)
$g.Dispose()
$small = New-Object System.Drawing.Bitmap 128, 128
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.InterpolationMode = 'HighQualityBicubic'
$g2.DrawImage($big, 0, 0, 128, 128)
$g2.Dispose()
$small.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose(); $small.Dispose()
Write-Host "Wrote icon.svg and icon.png to $OutDir"
