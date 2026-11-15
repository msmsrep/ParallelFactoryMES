<#
.SYNOPSIS
    MesApp.Desktop を発行して MSIX パッケージを作る。

.DESCRIPTION
    既定で x64 と arm64 の2つを作り、どちらも自己署名する。手元の動作確認にもストア提出にも同じものを使う
    （ストアにアップロードすると Microsoft Store の署名に置き換わる）。

    フルトラストのデスクトップアプリは Add-AppxPackage -AllowUnsigned では入らない
    （0x80073D2B：未署名パッケージに実行可能ファイルのアクティブ化を含められない）ため、
    開発者モードでも署名が要る。

.EXAMPLE
    ./build/Pack-Msix.ps1                       # x64 と arm64 を作る
    ./build/Pack-Msix.ps1 -Architecture x64     # 片方だけ作る
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string[]]$Architecture = @('x64', 'arm64'),

    [string]$Configuration = 'Release',

    # 省略時は build/msix-identity.json の Version を使う
    [string]$Version,

    [string]$OutputDirectory,

    # パッケージIDの定義ファイル。リポジトリ外で管理したいときに指定する
    [string]$IdentityFile
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/MesApp.Desktop/MesApp.Desktop.csproj'
$manifestTemplate = Join-Path $repoRoot 'src/MesApp.Desktop/Package.appxmanifest'
if (-not $IdentityFile) { $IdentityFile = Join-Path $PSScriptRoot 'msix-identity.json' }

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/msix' }

# --- 1. パッケージIDの読み込みと検証 -------------------------------------------------
$identity = Get-Content $IdentityFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Version) { $Version = $identity.Version }

foreach ($field in 'IdentityName', 'Publisher', 'PublisherDisplayName', 'DisplayName') {
    if (-not $identity.$field -or $identity.$field -match 'PLACEHOLDER') {
        throw "$IdentityFile の $field が未設定です。Partner Center の製品IDページ（DisplayName は「アプリ名の管理」で予約した名前）の値に置き換えてください。"
    }
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "バージョンは x.y.z.0 形式（第4桁は0）である必要があります。指定値: $Version"
}

# --- 2. Windows SDK ツールの解決 -----------------------------------------------------
function Resolve-SdkTool {
    param([string]$Name)

    # Windows Kits\10\bin\<SDKバージョン>\x64\<ツール> のうち、最も新しいSDKのものを使う
    $binRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $tool = Get-ChildItem $binRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -as [version] } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$Name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $tool) { throw "$Name が見つかりません。Windows 10/11 SDK をインストールしてください。" }
    return $tool
}

$makeappx = Resolve-SdkTool 'makeappx.exe'
$signtool = Resolve-SdkTool 'signtool.exe'

# --- 3. 署名用の証明書 ---------------------------------------------------------------
$subject = $identity.Publisher

# 発行元が一致し、秘密鍵を持ち、コード署名用途で、期限内の証明書を使い回す。
# 作成のたびに証明書を増やさないため、既存があれば新規作成しない（全アーキテクチャで同じ証明書を使う）。
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) -and
        ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')
    } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

$created = $false
if (-not $cert) {
    Write-Host "自己署名証明書を作成します: $subject"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
        -KeyUsage DigitalSignature -FriendlyName 'Parallel Factory MES (ローカル検証用)' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $created = $true
}

# --- 4. アーキテクチャごとに 発行→整理→マニフェスト→パッケージ化→署名 ---------------
$packages = foreach ($arch in $Architecture) {
    $stage = Join-Path $OutputDirectory "stage-$arch"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    Write-Host "発行中: $arch / $Configuration"
    dotnet publish $project -c $Configuration -r "win-$arch" -o $stage --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました（$arch）。" }

    # 開発用設定とシンボルは配布しない。
    # .br/.gz は MapStaticAssets を使う構成向けの事前圧縮で、UseStaticFiles では配信されないため削除する。
    Remove-Item (Join-Path $stage 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
    Get-ChildItem $stage -Recurse -Include *.pdb, *.br, *.gz -File | Remove-Item -Force

    $manifest = Get-Content $manifestTemplate -Raw -Encoding UTF8
    $manifest = $manifest.
        Replace('$IdentityName$', $identity.IdentityName).
        Replace('$Publisher$', $identity.Publisher).
        Replace('$PublisherDisplayName$', $identity.PublisherDisplayName).
        Replace('$DisplayName$', [Security.SecurityElement]::Escape($identity.DisplayName)).
        Replace('$Version$', $Version).
        Replace('$Architecture$', $arch)
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

    $msix = Join-Path $OutputDirectory "ParallelFactoryMES_${Version}_$arch.msix"
    Write-Host "パッケージ化中: $msix"
    & $makeappx pack /d $stage /p $msix /o | Out-String | Write-Verbose
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack に失敗しました（$arch）。" }

    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-String | Write-Verbose
    if ($LASTEXITCODE -ne 0) { throw "signtool sign に失敗しました（$arch）。" }

    $msix
}

# --- 5. 結果の表示 -------------------------------------------------------------------
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

Write-Host ""
Write-Host "署名の証明書: $($cert.Thumbprint)$(if ($created) { ' (新規作成)' } else { ' (既存を再利用)' })"

if (-not $trusted) {
    Write-Host ""
    Write-Host "この証明書はまだ信頼されていません。手元にインストールするときは管理者権限のPowerShellで登録してください:"
    Write-Host "  Export-Certificate -Cert Cert:\CurrentUser\My\$($cert.Thumbprint) -FilePath `$env:TEMP\mes-test.cer"
    Write-Host "  Import-Certificate -FilePath `$env:TEMP\mes-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
}

Write-Host ""
Write-Host "インストール（使うPCのアーキテクチャに合うもの）:"
foreach ($msix in $packages) { Write-Host "  Add-AppxPackage -Path '$msix'" }
Write-Host "アンインストール:"
Write-Host "  Get-AppxPackage *ParallelFactoryMES* | Remove-AppxPackage"

Write-Host ""
foreach ($msix in $packages) {
    $size = [Math]::Round((Get-Item $msix).Length / 1MB, 1)
    Write-Host "完了: $msix ($size MB)"
}
Write-Host "ストアへはこれらのパッケージを同じ申請で Partner Center にアップロードしてください（署名は Microsoft Store のものに置き換わります）。"
