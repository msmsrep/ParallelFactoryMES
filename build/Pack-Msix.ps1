<#
.SYNOPSIS
    MesApp.Desktop を発行して MSIX パッケージを作る。

.DESCRIPTION
    ストア提出用は署名しない（Microsoft Store が署名するため）。

    手元にインストールして確認するには -SelfSign を使う。フルトラストのデスクトップアプリは
    Add-AppxPackage -AllowUnsigned では入らない（0x80073D2B：未署名パッケージに実行可能
    ファイルのアクティブ化を含められない）ため、開発者モードでも署名が要る。

    -SelfSign は提出用パッケージを書き換えず、署名済みのコピーを別名で作る。

.EXAMPLE
    ./build/Pack-Msix.ps1 -SelfSign          # ローカル検証用（署名済みコピーを作る）
    ./build/Pack-Msix.ps1                    # ストア提出用（未署名）
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [string]$Configuration = 'Release',

    # 省略時は build/msix-identity.json の Version を使う
    [string]$Version,

    # ローカル検証用に自己署名する
    [switch]$SelfSign,

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

foreach ($field in 'IdentityName', 'Publisher', 'PublisherDisplayName') {
    if ($identity.$field -match 'PLACEHOLDER') {
        throw "$IdentityFile の $field が未設定です。Partner Center の製品IDページの値に置き換えてください。"
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

# --- 3. 発行 -------------------------------------------------------------------------
$stage = Join-Path $OutputDirectory "stage-$Architecture"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "発行中: $Architecture / $Configuration"
dotnet publish $project -c $Configuration -r "win-$Architecture" -o $stage --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました。" }

# --- 4. 配布物の整理 -----------------------------------------------------------------
# 開発用設定とシンボルは配布しない。
# .br/.gz は MapStaticAssets を使う構成向けの事前圧縮で、UseStaticFiles では配信されないため削除する。
Remove-Item (Join-Path $stage 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
Get-ChildItem $stage -Recurse -Include *.pdb, *.br, *.gz -File | Remove-Item -Force

# --- 5. マニフェストの生成 -----------------------------------------------------------
$manifest = Get-Content $manifestTemplate -Raw -Encoding UTF8
$manifest = $manifest.
    Replace('$IdentityName$', $identity.IdentityName).
    Replace('$Publisher$', $identity.Publisher).
    Replace('$PublisherDisplayName$', $identity.PublisherDisplayName).
    Replace('$Version$', $Version).
    Replace('$Architecture$', $Architecture)
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

# --- 6. パッケージ化 -----------------------------------------------------------------
$msix = Join-Path $OutputDirectory "ParallelFactoryMES_${Version}_$Architecture.msix"
Write-Host "パッケージ化中: $msix"
& $makeappx pack /d $stage /p $msix /o | Out-String | Write-Verbose
if ($LASTEXITCODE -ne 0) { throw "makeappx pack に失敗しました。" }

# --- 7. ローカル検証用の署名 ---------------------------------------------------------
if ($SelfSign) {
    $signtool = Resolve-SdkTool 'signtool.exe'
    $subject = $identity.Publisher

    # 発行元が一致し、秘密鍵を持ち、コード署名用途で、期限内の証明書を使い回す。
    # 検証のたびに証明書を増やさないため、既存があれば新規作成しない。
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

    # 提出用パッケージは未署名のまま残し、署名はコピーに対して行う
    $signed = [IO.Path]::ChangeExtension($msix, $null).TrimEnd('.') + '_signed.msix'
    Copy-Item $msix $signed -Force
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $signed | Out-String | Write-Verbose
    if ($LASTEXITCODE -ne 0) { throw "signtool sign に失敗しました。" }

    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

    Write-Host ""
    Write-Host "署名しました: $signed"
    Write-Host "  証明書: $($cert.Thumbprint)$(if ($created) { ' (新規作成)' } else { ' (既存を再利用)' })"

    if (-not $trusted) {
        Write-Host ""
        Write-Host "この証明書はまだ信頼されていません。管理者権限のPowerShellで登録してください:"
        Write-Host "  Export-Certificate -Cert Cert:\CurrentUser\My\$($cert.Thumbprint) -FilePath `$env:TEMP\mes-test.cer"
        Write-Host "  Import-Certificate -FilePath `$env:TEMP\mes-test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    }

    Write-Host ""
    Write-Host "インストール:"
    Write-Host "  Add-AppxPackage -Path '$signed'"
    Write-Host "アンインストール:"
    Write-Host "  Get-AppxPackage *ParallelFactoryMES* | Remove-AppxPackage"
}

$size = [Math]::Round((Get-Item $msix).Length / 1MB, 1)
Write-Host ""
Write-Host "完了: $msix ($size MB)"
if (-not $SelfSign) {
    Write-Host "この未署名パッケージを Partner Center にアップロードしてください（署名は Microsoft Store が行います）。"
}
