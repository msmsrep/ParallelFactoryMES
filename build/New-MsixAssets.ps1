# MSIX用タイル画像とアプリアイコンを生成する。
# ロゴを差し替えるときはこのスクリプトの Draw-Mark を書き換えて再実行する。
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\MesApp.Desktop\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$background = [System.Drawing.Color]::FromArgb(255, 31, 42, 68)   # 濃紺（マニフェストのBackgroundColorと合わせる）
$accent = [System.Drawing.Color]::FromArgb(255, 92, 200, 168)     # 稼働中を表す緑
$light = [System.Drawing.Color]::White

# 「並列に流れる3本の工程」を表す3本のバー。16px でも潰れない太さにする
function Draw-Mark {
    param([System.Drawing.Graphics]$G, [int]$Size)

    $G.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $G.Clear($background)

    $unit = $Size / 16.0
    $barWidth = $unit * 2.4
    $gap = $unit * 1.6
    $totalWidth = ($barWidth * 3) + ($gap * 2)
    $left = ($Size - $totalWidth) / 2.0
    $bottom = $Size - ($unit * 3.4)
    # PowerShellはカンマが乗算より強く結合するため、要素ごとに括弧が必要
    $heights = @(($unit * 5.0), ($unit * 9.2), ($unit * 7.0))
    $colors = @($light, $accent, $light)

    for ($i = 0; $i -lt 3; $i++) {
        $brush = New-Object System.Drawing.SolidBrush($colors[$i])
        $x = $left + ($i * ($barWidth + $gap))
        $y = $bottom - $heights[$i]
        $G.FillRectangle($brush, [float]$x, [float]$y, [float]$barWidth, [float]($heights[$i]))
        $brush.Dispose()
    }

    # 底辺のライン（生産ラインを表す）
    $lineBrush = New-Object System.Drawing.SolidBrush($accent)
    $G.FillRectangle($lineBrush, [float]$left, [float]($bottom + $unit * 0.8), [float]$totalWidth, [float]($unit * 1.0))
    $lineBrush.Dispose()
}

function New-Tile {
    param([int]$Width, [int]$Height, [string]$Path)

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    # 横長タイルは正方形のマークを中央に置く
    $side = [Math]::Min($Width, $Height)
    $g.Clear($background)
    $inner = New-Object System.Drawing.Bitmap($side, $side)
    $ig = [System.Drawing.Graphics]::FromImage($inner)
    Draw-Mark -G $ig -Size $side
    $ig.Dispose()
    $g.DrawImage($inner, [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)
    $inner.Dispose()

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    Write-Host "  $([IO.Path]::GetFileName($Path)) ($Width x $Height)"
}

function New-Icon {
    param([string]$Path, [int[]]$Sizes)

    $streams = foreach ($size in $Sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        Draw-Mark -G $g -Size $size
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }

    # ICO（Vista以降が対応するPNG格納形式）を手で組み立てる
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($out)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$Sizes.Count)
    $offset = 6 + (16 * $Sizes.Count)
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $s = $Sizes[$i]
        $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
        $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$streams[$i].Length)
        $w.Write([uint32]$offset)
        $offset += $streams[$i].Length
    }
    foreach ($s in $streams) { $w.Write($s) }
    $w.Flush()
    [IO.File]::WriteAllBytes($Path, $out.ToArray())
    $w.Dispose()
    Write-Host "  $([IO.Path]::GetFileName($Path)) (ICO: $($Sizes -join ', '))"
}

$OutputDirectory = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
Write-Host "生成先: $OutputDirectory"

# ストア／タイルに必要な画像
New-Tile -Width 50  -Height 50  -Path (Join-Path $OutputDirectory 'StoreLogo.png')
New-Tile -Width 44  -Height 44  -Path (Join-Path $OutputDirectory 'Square44x44Logo.png')
New-Tile -Width 71  -Height 71  -Path (Join-Path $OutputDirectory 'Square71x71Logo.png')
New-Tile -Width 150 -Height 150 -Path (Join-Path $OutputDirectory 'Square150x150Logo.png')
New-Tile -Width 310 -Height 310 -Path (Join-Path $OutputDirectory 'Square310x310Logo.png')
New-Tile -Width 310 -Height 150 -Path (Join-Path $OutputDirectory 'Wide310x150Logo.png')

# タスクバー・スタートメニューが参照するターゲットサイズ版
foreach ($size in 16, 24, 32, 48, 256) {
    New-Tile -Width $size -Height $size -Path (Join-Path $OutputDirectory "Square44x44Logo.targetsize-$size.png")
}

# 実行ファイルのアイコン
New-Icon -Path (Join-Path $OutputDirectory 'AppIcon.ico') -Sizes @(16, 32, 48, 256)

Write-Host "完了しました。"
