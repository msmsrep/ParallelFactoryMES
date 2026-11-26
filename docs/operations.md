---
layout: default
title: サーバーの配置と設定
nav_url: operations.html
lead: "サーバーの構成、配布物ごとの起動方法、設定項目など、管理者が最初に押さえる情報です。"
prev_url: roles.html
prev_title: ロールと権限
next_url: integration.html
next_title: 外部連携
---

<!-- 以前は1ページだった。移した節への古いリンク（operations.html#webhooks など）を移し先へ送る -->
<script>
(function () {
  var moved = {"folder-link": "integration.html", "設定する": "integration.html", "ファイルの置き方": "integration.html", "結果を確かめる": "integration.html", "api-keys": "integration.html", "発行する": "integration.html", "呼んでみる": "integration.html", "注意": "integration.html", "integration-api": "integration.html", "仕様書openapi": "integration.html", "使える口": "integration.html", "csv-を取り込む": "integration.html", "送り直しても二重に登録しないidempotency-key": "integration.html", "生産実績を読む前回の続きから": "integration.html", "版を上げても連携が動き続けるための約束": "integration.html", "equipment-events": "integration.html", "準備する": "integration.html", "送る形": "integration.html", "届いた後の扱い": "integration.html", "サンプルで試すdocker": "integration.html", "webhooks": "integration.html", "送信先を登録する": "integration.html", "通知するイベント": "integration.html", "送る形-1": "integration.html", "受け側で確かめること": "integration.html", "届かなかったとき": "integration.html", "サンプルで試す": "integration.html", "版を上げても受け側が動き続けるための約束": "integration.html", "セキュリティ": "security.html", "https": "security.html", "audit-log": "security.html", "記録される操作": "security.html", "絞り込み": "security.html", "削除改ざんについて": "security.html", "古い記録の一括削除": "security.html", "database": "database.html", "sqlite-との違い": "database.html", "起動しないとき": "database.html", "backup": "database.html", "sqlite": "database.html", "postgresql--sql-server": "database.html", "データ保存先の鍵": "database.html"};
  var id = decodeURIComponent(location.hash.slice(1));
  if (moved[id]) location.replace(moved[id] + location.hash);
})();
</script>

このほかの管理者向けの情報：

- [外部連携](integration.html)
- [セキュリティと監査ログ](security.html)
- [データベースとバックアップ](database.html)

## 構成

Webクライアント（Blazor WebAssembly）は、APIサーバー（ASP.NET Core）が同一オリジンで配信します。
利用者側にインストールするものはありません。

| コンポーネント | 役割 |
|---|---|
| MesApp.Api | 業務ロジックとデータベース。Webクライアントも配信します |
| MesApp.Client.Web | ブラウザで動作するクライアント |
| データベース | SQLite（既定。ファイル1つ）、PostgreSQL、SQL Server のいずれか。起動時にマイグレーションが自動適用されます（[PostgreSQL / SQL Server を使う](database.html#database)） |

## 配布と起動

サーバーに置く方法は3通りあります。1台のPCで試すだけなら、[Microsoft Store のデスクトップ版](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=ja-JP&gl=JP)が手軽です
（ほかの端末からは接続できないため、現場での運用には下のいずれかを使います）。

| 方法 | 向いている場面 | 入手先 |
|---|---|---|
| ZIP（自己完結版） | Windows / Linux のサーバーに直接置く。.NET のインストールは不要 | [GitHub Releases](https://github.com/msmsrep/ParallelFactoryMES/releases/latest) の `ParallelFactoryMES-<版>-win-x64.zip` / `-linux-x64.zip` |
| Docker | コンテナで動かす（amd64 / arm64） | `ghcr.io/msmsrep/parallelfactorymes` |
| ソースから発行 | 手元で改造したものを配る | `dotnet publish src/MesApp.Api -c Release -o publish` |

### ZIP で起動する

展開したフォルダで、Windows は `start.cmd` をダブルクリック、Linux は `sh start.sh` を実行します。
既定のポートは 5000 です。変えるときは環境変数 `MESAPP_URLS`（例 `http://0.0.0.0:8080`）を設定してから起動します。

バージョンアップは、サーバーを停止し、展開したフォルダを新しい版に差し替えて起動します。
データはデータ保存先（下記）にあるため、フォルダを差し替えても消えません。データベースは起動時に自動で更新されます。

### Docker で起動する  {#docker}

```bash
docker compose up -d
```

リポジトリの `compose.yaml` を使うと SQLite で、`compose.postgres.yaml` を使うと PostgreSQL 付きで起動します
（`.env` に `POSTGRES_PASSWORD=...` を書いてから `docker compose -f compose.postgres.yaml up -d`）。
ブラウザで `http://<サーバーのIP>:8080` を開きます。

- データベースと署名鍵はボリューム（コンテナ内の `/data`）に置かれます。コンテナを作り直しても消えません
- タイムゾーンの既定は `TZ=Asia/Tokyo` です。業務日付の境界時刻はこの時刻で判定されます
- HTTPS はコンテナでは扱いません。前段のリバースプロキシで終端し、プロキシのいるネットワークを
  `ReverseProxy__KnownNetworks__0`（例 `172.16.0.0/12`）で指定します（[HTTPSで運用する](security.html#https)）
- バージョンアップは `docker compose pull` → `docker compose up -d` です

### ソースから発行して起動する

```bash
dotnet publish/MesApp.Api.dll --urls http://0.0.0.0:5000
```

ASP.NET Core 10 Runtime が必要です。同じLAN内の他のPCからは `http://<サーバーのIP>:5000` でアクセスできます。

<div class="warn">
<p>Windowsファイアウォールで、該当ポートの<strong>受信許可</strong>が必要です。
これを設定しないと、他のPCから接続できません。</p>
</div>

データベースファイル（`mesapp.db`）とJWT署名鍵（`jwt-signing.key`）は、
**データ保存先（既定 `%LOCALAPPDATA%\ParallelFactoryMES`、Linux は `~/.local/share/ParallelFactoryMES`、Docker は `/data`。環境変数 `MESAPP_DATA_DIR` で変更可）** に自動生成されます。
設定値に絶対パスを書いた場合はそのパスを使います。

## 設定項目  {#settings}

`appsettings.json` を編集するか、環境変数で上書きします。

| 設定 | 環境変数 | 既定値 |
|---|---|---|
| DB接続文字列（SQLiteはファイルの場所） | `Database__ConnectionString` | `Data Source=mesapp.db` |
| DBプロバイダー | `Database__Provider` | `Sqlite`（`PostgreSql` / `SqlServer` も可） |
| データの保存先（DB・署名鍵） | `MESAPP_DATA_DIR` | `%LOCALAPPDATA%\ParallelFactoryMES`（Linux は `~/.local/share/ParallelFactoryMES`、Docker は `/data`） |
| 業務日付の境界時刻 | `BusinessDay__BoundaryHour` | `6`（午前6時） |
| 監査ログの保持期間（年） | `Audit__RetentionYears` | `5` |
| アクセストークン有効期限（分） | `Jwt__AccessTokenLifetimeMinutes` | `60` |
| リフレッシュトークン有効期限（時間） | `Jwt__RefreshTokenLifetimeHours` | `12` |
| JWT署名鍵ファイル | `Jwt__SigningKeyFile` | `jwt-signing.key`（未存在なら自動生成） |
| 初期管理者の自動作成 | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | 未設定 || 信頼するリバースプロキシのIP | `ReverseProxy__KnownProxies__0`（複数は `__1`, `__2` …） | 未設定（転送ヘッダを読まない） |
| 信頼するリバースプロキシのネットワーク | `ReverseProxy__KnownNetworks__0`（CIDR。例 `192.168.10.0/24`） | 未設定 |
| フォルダ連携の連携フォルダ | `FolderLink__Root` | 未設定（連携しない） |
| フォルダ連携の取込を行う利用者 | `FolderLink__UserName` | 未設定 |
| フォルダ連携の走査の間隔（秒） | `FolderLink__PollSeconds` | `30` |
| フォルダ連携の結果の言語 | `FolderLink__Language` | `ja`（`en` も可） |
| 設備イベントの1回の件数の上限 | `EquipmentEvents__MaxBatchSize` | `1000` |
| 設備イベントの時刻の先行の許容（秒） | `EquipmentEvents__MaxFutureSkewSeconds` | `300` |
| 設備イベントの受信が途絶えたとみなす時間（分） | `EquipmentEvents__SilenceMinutes` | `10` |

### 業務日付の境界時刻

`BusinessDay__BoundaryHour` を変更すると、集計と採番の日付境界が変わります。
たとえば `8` にすると、午前8時が日付の切り替わりになります。
シフトの開始時刻に合わせて設定してください。

### 無人セットアップ

初期管理者を画面操作なしで作成する場合の例です。**ユーザーが0件のときのみ適用され**、
初回ログイン時にパスワード変更が強制されます（変更するまで、パスワード変更以外のAPIは使えません）。

```powershell
$env:MesAdmin__UserName="admin"; $env:MesAdmin__Password="Passw0rd123"; dotnet publish/MesApp.Api.dll
```

`MesAdmin__Password` を指定しない場合、初期管理者は作成されません（値を誰も知らない
アカウントを残さないため）。その場合は[初期セットアップ画面](getting-started.html)で作成します。

## 制限事項

| 項目 | 状況 |
|---|---|
| PostgreSQL / SQL Server | 対応（[PostgreSQL / SQL Server を使う](database.html#database)）。既存のSQLiteデータを移す機能は無し |
| Docker化・Zip配布 | 対応（[配布と起動](#配布と起動)） |
| HTTPS | アプリ側では強制しない（リバースプロキシで終端。[HTTPSで運用する](security.html#https)） |
| 設備・秤量機からの自動データ収集 | 未対応（手入力） |
| EDI / ASN連携 | 未対応 |
| シリアル番号（個体）単位の追跡 | 未対応（ロット単位のみ） |
| 多段階承認ワークフロー | 未対応（単段階承認） |

## 動作確認

```bash
dotnet test ParallelFactoryMES.slnx
```

APIの統合テストが実行されます。

## このガイドの公開について

このガイドは `docs/` フォルダのMarkdownで管理されています。英語版は `docs/en/` に日本語版と同じファイル名で置いています。GitHub Pagesで公開するには、
リポジトリの **Settings → Pages** で次のように設定します。

- Source: `Deploy from a branch`
- Branch: `master`（または `main`）／ フォルダ: `/docs`

内容を更新するときは `docs/*.md`（英語版は `docs/en/*.md`）を編集してコミットすれば、自動的に再公開されます。
ページを追加した場合は、左メニューに載せるため `docs/_data/nav.yml` の `ja` と `en` の両方に項目を追加してください。
