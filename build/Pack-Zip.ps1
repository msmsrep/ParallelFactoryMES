<#
.SYNOPSIS
    MesApp.Api を自己完結版で発行し、ランタイムごとの ZIP を作る。

.DESCRIPTION
    自己完結版なので配布先に .NET / ASP.NET Core Runtime は要らない。
    ZIP には起動スクリプト（start.cmd / start.sh）・LICENSE・ソースの所在（AGPL-3.0 の対応ソース）を同梱する。
    あわせて、取込画面に渡せるサンプルCSVのZIP（samples-master-csv.zip / samples-actual-csv.zip / samples-actual-csv-bulk.zip）を作る。

    ZIP は Unix の実行権限を保持しないため、start.sh が起動前に MesApp.Api へ実行権限を付ける
    （利用者は `sh start.sh` で起動する）。

    GitHub Actions（.github/workflows/release.yml）からも同じスクリプトを呼ぶ。

.EXAMPLE
    ./build/Pack-Zip.ps1                              # Directory.Build.props の版で win-x64 / linux-x64 を作る
    ./build/Pack-Zip.ps1 -Version 1.2.3 -Runtime win-x64
#>
[CmdletBinding()]
param(
    # 省略時は Directory.Build.props の Version を使う
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64'),

    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    # ソースの所在として ZIP に記載するコミット。省略時は git から取る
    [string]$Commit
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/MesApp.Api/MesApp.Api.csproj'
$sourceUrl = 'https://github.com/msmsrep/ParallelFactoryMES'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/zip' }

if (-not $Version) {
    $Version = (dotnet msbuild $project -getProperty:Version -nologo).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "バージョンは x.y.z（プレリリースは x.y.z-rc.1 など）である必要があります。指定値: $Version"
}
if (-not $Commit) {
    $Commit = (git -C $repoRoot rev-parse HEAD 2>$null)
    if (-not $Commit) { $Commit = '不明' }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($rid in $Runtime) {
    $name = "ParallelFactoryMES-$Version-$rid"
    $stage = Join-Path $OutputDirectory $name
    $zip = "$stage.zip"
    foreach ($old in $stage, $zip) { if (Test-Path $old) { Remove-Item $old -Recurse -Force } }

    # --- 1. 発行 ----------------------------------------------------------------------
    Write-Host "発行中: $rid ($Version)"
    dotnet publish $project -c $Configuration -r $rid --self-contained true -o $stage --nologo -v q `
        "-p:Version=$Version" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish が失敗しました（$rid）。" }

    # --- 2. 同梱ファイル ------------------------------------------------------------------
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stage 'LICENSE.txt')

    # 起動スクリプトは文字コードの影響を受けないよう ASCII だけで書く。
    # exe はフルパスで呼ぶ（NoDefaultCurrentDirectoryInExePath が設定された環境ではカレントの exe が見つからない）
    if ($rid -like 'win-*') {
        $script = @(
            '@echo off'
            'rem Parallel Factory MES - start (http://localhost:5000)'
            'rem Change the port: set MESAPP_URLS=http://0.0.0.0:8080 before running.'
            'cd /d "%~dp0"'
            'if "%MESAPP_URLS%"=="" set MESAPP_URLS=http://0.0.0.0:5000'
            '"%~dp0MesApp.Api.exe" --urls %MESAPP_URLS%'
            'pause'
        ) -join "`r`n"
        [IO.File]::WriteAllText((Join-Path $stage 'start.cmd'), "$script`r`n", [Text.Encoding]::ASCII)
    }
    else {
        $script = @(
            '#!/bin/sh'
            '# Parallel Factory MES - start (http://localhost:5000)'
            '# Change the port: MESAPP_URLS=http://0.0.0.0:8080 sh start.sh'
            'cd "$(dirname "$0")" || exit 1'
            'chmod +x ./MesApp.Api'
            'exec ./MesApp.Api --urls "${MESAPP_URLS:-http://0.0.0.0:5000}"'
        ) -join "`n"
        [IO.File]::WriteAllText((Join-Path $stage 'start.sh'), "$script`n", [Text.Encoding]::ASCII)
    }

    $start = if ($rid -like 'win-*') { 'start.cmd をダブルクリック' } else { 'sh start.sh' }
    $readme = @"
Parallel Factory MES $Version（$rid・自己完結版）

■ 起動
  $start
  ブラウザで http://localhost:5000 を開き、初期セットアップで管理者を作成してください。
  他のPCからは http://<このPCのIP>:5000 で開けます（ファイアウォールで受信許可が必要）。

■ データの保存先
  既定: Windows は %LOCALAPPDATA%\ParallelFactoryMES、Linux は ~/.local/share/ParallelFactoryMES
  環境変数 MESAPP_DATA_DIR で変更できます。バージョンアップ時はこのフォルダを残したまま
  アプリのフォルダだけを差し替えてください（DB は起動時に自動で更新されます）。

■ 設定・運用
  $sourceUrl#readme
  https://msmsrep.github.io/ParallelFactoryMES/operations.html

■ ライセンスとソースコード
  GNU Affero General Public License v3.0 only（LICENSE.txt）
  対応するソースコード: $sourceUrl/tree/$Commit
"@
    [IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, (New-Object Text.UTF8Encoding $true))

    # --- 3. 圧縮（フォルダごと入れて、展開先が散らからないようにする） ---------------------------
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
    Remove-Item $stage -Recurse -Force
    Write-Host "作成: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
}

# --- 4. サンプルCSV -------------------------------------------------------------------------
# ZIP版・ストア版の利用者はリポジトリを持たないため、取込画面にそのまま渡せる形で添付する。
# 版数を名前に入れず、releases/latest/download/<名前> の固定リンクで案内できるようにする
foreach ($kind in 'master-csv', 'actual-csv', 'actual-csv-bulk') {
    $zip = Join-Path $OutputDirectory "samples-$kind.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $repoRoot "samples/$kind"), $zip)
    Write-Host "作成: $zip"
}
