# MSIX用タイル画像とアプリアイコンを build/icon.svg から生成する。
# 図柄を差し替えるときは build/icon.svg だけを書き換えて再実行する（このスクリプトは配置係）。
#
# 前提: Inkscape（SVGのラスタライズに使う）。既定の場所に無い場合は -InkscapePath で渡す。
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\MesApp.Desktop\Assets'),
    [string]$SvgPath = (Join-Path $PSScriptRoot 'icon.svg'),
    [string]$InkscapePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $InkscapePath) {
    $candidates = @(
        (Get-Command inkscape -ErrorAction SilentlyContinue).Source,
        'C:\Program Files\Inkscape\bin\inkscape.exe',
        'C:\Program Files (x86)\Inkscape\bin\inkscape.exe'
    )
    $InkscapePath = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $InkscapePath) {
    throw 'Inkscape が見つかりません。インストールするか -InkscapePath で場所を指定してください。'
}

$SvgPath = (Resolve-Path $SvgPath).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) ("mes-assets-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null

# SVGを指定ピクセルでラスタライズする（背景は透過のまま）
function Convert-Svg {
    param([int]$Size)

    $path = Join-Path $temp "mark-$Size.png"
    if (-not (Test-Path $path)) {
        & $InkscapePath --export-type=png -w $Size -h $Size --export-filename=$path $SvgPath | Out-Null
        if (-not (Test-Path $path)) { throw "Inkscape がPNGを出力しませんでした（$Size px）。" }
    }
    return $path
}

# 透過キャンバスの中央にマークを置く。$Fill はマークがキャンバスの短辺に占める割合
function New-Tile {
    param([int]$Width, [int]$Height, [string]$Path, [double]$Fill)

    $side = [int][Math]::Round([Math]::Min($Width, $Height) * $Fill)
    $mark = [System.Drawing.Image]::FromFile((Convert-Svg -Size $side))

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($mark, [int](($Width - $side) / 2), [int](($Height - $side) / 2), $side, $side)
    $g.Dispose()
    $mark.Dispose()

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $([IO.Path]::GetFileName($Path)) ($Width x $Height)"
}

function New-Icon {
    param([string]$Path, [int[]]$Sizes)

    $streams = foreach ($size in $Sizes) {
        , [IO.File]::ReadAllBytes((Convert-Svg -Size $size))
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

try {
    # ストア／タイルに必要な画像。
    # 中〜大タイルはマークを小さめに置く（タイルは周囲に余白があるほうが収まりがよい）
    New-Tile -Width 50  -Height 50  -Fill 0.86 -Path (Join-Path $OutputDirectory 'StoreLogo.png')
    New-Tile -Width 44  -Height 44  -Fill 0.86 -Path (Join-Path $OutputDirectory 'Square44x44Logo.png')
    New-Tile -Width 71  -Height 71  -Fill 0.78 -Path (Join-Path $OutputDirectory 'Square71x71Logo.png')
    New-Tile -Width 150 -Height 150 -Fill 0.62 -Path (Join-Path $OutputDirectory 'Square150x150Logo.png')
    New-Tile -Width 310 -Height 310 -Fill 0.62 -Path (Join-Path $OutputDirectory 'Square310x310Logo.png')
    New-Tile -Width 310 -Height 150 -Fill 0.62 -Path (Join-Path $OutputDirectory 'Wide310x150Logo.png')

    # タスクバー・スタートメニューが参照するターゲットサイズ版（小さいので余白は最小限）
    foreach ($size in 16, 24, 32, 48, 256) {
        New-Tile -Width $size -Height $size -Fill 1.0 -Path (Join-Path $OutputDirectory "Square44x44Logo.targetsize-$size.png")
    }

    # 実行ファイルのアイコン
    New-Icon -Path (Join-Path $OutputDirectory 'AppIcon.ico') -Sizes @(16, 32, 48, 256)
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "完了しました。"
