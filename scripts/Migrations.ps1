<#
.SYNOPSIS
    3プロバイダー（SQLite / PostgreSQL / SQL Server）のマイグレーションを同じ名前でまとめて追加・削除する（Spec.md 4章）

.DESCRIPTION
    マイグレーションはプロバイダーごとに別物になる（EF Coreの制約）ため、スキーマを変えるたびに3つ作る。
    手で3回打つと作り忘れ・名前の食い違いが起きるので、このスクリプトでまとめて行い、
    最後に DatabaseProviderTests で3つともモデルに追いついていることを確かめる。

    追加ではDBに接続しない（PostgreSQL・SQL Server の接続文字列は形式だけのダミー）。
    取り消しでは EF が適用済みかを確かめに行く（下の $providers のコメント参照）。

.EXAMPLE
    ./scripts/Migrations.ps1 -Add AddShiftCalendar
    3プロバイダーに AddShiftCalendar を追加して検査する

.EXAMPLE
    ./scripts/Migrations.ps1 -RemoveLast
    3プロバイダーの最新のマイグレーションを取り消す（名前が3つとも同じときだけ）

.NOTES
    取り消すと EF が各 MesAppDbContextModelSnapshot.cs を書き直し、ToTable("X") が ToTable("X", (string)null)
    になることがある（意味は同じ）。直前に追加したものを取り消しただけなら git checkout で戻してよい。
#>
[CmdletBinding(DefaultParameterSetName = 'Add')]
param(
    # 追加するマイグレーション名（C#の識別子。例: AddShiftCalendar）
    [Parameter(Mandatory, ParameterSetName = 'Add', Position = 0)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string]$Add,

    # 最新のマイグレーションを3プロバイダーとも取り消す
    [Parameter(Mandatory, ParameterSetName = 'RemoveLast')]
    [switch]$RemoveLast,

    # 追加後の DatabaseProviderTests を省く
    [Parameter(ParameterSetName = 'Add')]
    [switch]$SkipTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$startup = 'src/MesApp.Api'

# 1つ目（SQLite）でビルドし、残りは --no-build で同じビルドを使う。
# migrations remove は適用済みかをDBに問い合わせる。SQLiteは開発用DB（mesapp.db）で確かめ、適用済みなら止める。
# PostgreSQL・SQL Server の接続文字列はダミーで問い合わせが必ず失敗するため、--force で確認を飛ばして消す
$providers = @(
    @{ Name = 'SQLite';     Project = 'src/MesApp.Infrastructure';        MigrationsDir = 'src/MesApp.Infrastructure/Migrations';        RemoveOptions = @();          Args = @() }
    @{ Name = 'PostgreSQL'; Project = 'src/MesApp.Migrations.PostgreSql'; MigrationsDir = 'src/MesApp.Migrations.PostgreSql/Migrations'; RemoveOptions = @('--force'); Args = @('--', '--Database:Provider=PostgreSql', '--Database:ConnectionString=Host=localhost;Database=mesapp') }
    @{ Name = 'SQL Server'; Project = 'src/MesApp.Migrations.SqlServer';  MigrationsDir = 'src/MesApp.Migrations.SqlServer/Migrations';  RemoveOptions = @('--force'); Args = @('--', '--Database:Provider=SqlServer', '--Database:ConnectionString=Server=localhost;Database=mesapp') }
)

function Invoke-Ef([hashtable]$provider, [string[]]$command, [bool]$build) {
    $efArgs = @('ef') + $command + @('--project', $provider.Project, '--startup-project', $startup)
    if (-not $build) { $efArgs += '--no-build' }
    $efArgs += $provider.Args
    $output = & dotnet @efArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        # 失敗時だけ末尾を見せる（成功時の生ログは流さない）
        $output | Select-Object -Last 30 | ForEach-Object { Write-Host "    $_" }
        return $false
    }
    return $true
}

# 最新のマイグレーション名（タイムスタンプを除く）。ファイル名だけを見て中身は開かない
function Get-LastMigrationName([hashtable]$provider) {
    $last = Get-ChildItem (Join-Path $root $provider.MigrationsDir) -Filter '*_*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } |
        Sort-Object Name | Select-Object -Last 1
    if ($null -eq $last) { return $null }
    return ($last.BaseName -split '_', 2)[1]
}

Push-Location $root
try {
    & dotnet tool restore *> $null

    if ($RemoveLast) {
        $names = $providers | ForEach-Object { Get-LastMigrationName $_ }
        if (@($names | Select-Object -Unique).Count -ne 1) {
            Write-Host "最新のマイグレーション名がプロバイダー間で食い違っているため中止します:" -ForegroundColor Red
            for ($i = 0; $i -lt $providers.Count; $i++) { Write-Host "  $($providers[$i].Name): $($names[$i])" }
            exit 1
        }
        Write-Host "最新のマイグレーション '$($names[0])' を3プロバイダーから取り消します"
        $first = $true
        foreach ($p in $providers) {
            if (-not (Invoke-Ef $p (@('migrations', 'remove') + $p.RemoveOptions) $first)) {
                Write-Host "  $($p.Name): 失敗（SQLiteの開発用DBに適用済みだと取り消せません。上の出力を確認してください）" -ForegroundColor Red
                exit 1
            }
            Write-Host "  $($p.Name): 取り消し"
            $first = $false
        }
        exit 0
    }

    Write-Host "マイグレーション '$Add' を3プロバイダーに追加します"
    $created = @()
    $first = $true
    foreach ($p in $providers) {
        if (-not (Invoke-Ef $p @('migrations', 'add', $Add) $first)) {
            Write-Host "  $($p.Name): 失敗" -ForegroundColor Red
            # 途中で止まると一部のプロバイダーだけにマイグレーションが残るので、作った分を戻す
            foreach ($c in $created) {
                if (Invoke-Ef $c (@('migrations', 'remove') + $c.RemoveOptions) $false) {
                    Write-Host "  $($c.Name): 取り消し（途中で失敗したため）"
                } else {
                    Write-Host "  $($c.Name): 取り消しに失敗。手で削除してください" -ForegroundColor Red
                }
            }
            exit 1
        }
        Write-Host "  $($p.Name): 追加"
        $created += $p
        $first = $false
    }

    if ($SkipTest) { exit 0 }

    Write-Host "DatabaseProviderTests で3プロバイダーの同期を確認します"
    $result = & dotnet test tests/MesApp.Api.Tests --filter 'FullyQualifiedName~DatabaseProviderTests' -v q --nologo 2>&1
    $result | Select-String -Pattern 'error|Failed|成功!|失敗' | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
