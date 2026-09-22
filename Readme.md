# ParallelFactoryMES

製造実績管理システム（MES）。ASP.NET Core Web API（バックエンド）＋ Blazor WebAssembly（Webクライアント）で構成し、
APIが同一オリジンでWebクライアントも配信します。

- **紹介ページ（画面写真でひととおり見る）**: [Parallel Factory MES の紹介](https://msmsrep.github.io/ParallelFactoryMES/intro.html)
- **ユーザーガイド（操作マニュアル）**: [ユーザーガイド](https://msmsrep.github.io/ParallelFactoryMES/)
- **入手**: [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=ja-JP&gl=JP)（1台で試す）／[ZIP](https://github.com/msmsrep/ParallelFactoryMES/releases/latest)（Windows / Linux）／Docker `ghcr.io/msmsrep/parallelfactorymes`
- **開発のサポート**: [GitHub Sponsors](https://github.com/sponsors/msmsrep)／[Ko-fi](https://ko-fi.com/msmsrep)（開発を続けるための寄付です。寄付による機能の追加や制限の解除はありません）

## ライセンス

本リポジトリのソースコードおよびドキュメントは **[GNU Affero General Public License v3.0](LICENSE)（AGPL-3.0）** で提供します。

Copyright (C) 2026 msmsrep

> このプログラムはフリーソフトウェアです。フリーソフトウェア財団が公開する GNU Affero General Public License
> バージョン3、または（任意で）それ以降のバージョンの条件に従って、再頒布および改変ができます。
> このプログラムは有用であることを願って頒布されますが、**一切の保証はありません**。
> 商品性や特定目的への適合性の黙示的保証もありません。詳細は [LICENSE](LICENSE) を参照してください。

AGPL-3.0 では、**改変版をネットワーク経由で第三者に利用させる場合、その利用者に対して改変版のソースコードを
提供する義務**があります（第13条）。本システムはWebアプリケーションのため、この条項が適用されます。

### 第三者の著作物について

本リポジトリは、機能要件の定義にあたり一般財団法人エンジニアリング協会（ENAA）の
**『MES/MOM導入のための標準業務一覧』**（2025年10月）を参照しています。
同一覧の**著作権はENAAに帰属**するため、一覧そのものは本リポジトリに含めていません。

Spec.md 中の業務プロセスNo（`A-20-10-01` 等）は、同一覧の項目を参照するための識別子です。

## 実装済みの範囲

| Phase | 内容 | 状態 |
|---|---|---|
| 1 | バックエンド基盤（EF Core + SQLite、JWT認証、初期セットアップ、監査ログ） | 実装済 |
| 2 | マスタ管理API＋生産管理API（製造指図・工程展開・差立） | 実装済 |
| 3 | 製造実行API＋物流/在庫管理API | 実装済 |
| 4 | 品質管理API＋品質保証API（検査・不適合・出荷判定・トレーサビリティ） | 実装済 |
| 5 | 設備保全API（保全計画・指示・実績、治工具寿命管理） | 実装済 |
| 6〜7 | Blazor WebAssembly クライアント（全画面）、帳票・ラベル出力、バーコード/QRスキャン | 実装済 |
| 8〜9 | （欠番。シート・端末管理とWPFライセンスアプリは実装しないことにした） | — |
| 10 | DBプロバイダー切替（PostgreSQL / SQL Server）、Docker化、Zip配布 | 実装済 |

本システムは AGPL-3.0 のみで提供し、シート・端末単位のライセンス管理は持ちません。**アクティベーション不要で全端末から利用できます**。

## 前提

- **.NET 10 SDK**（実行のみなら ASP.NET Core 10 Runtime）
  - 入手先: https://dotnet.microsoft.com/download
- データベースは既定で SQLite（追加インストール不要。ファイルは自動生成されます）。
  PostgreSQL / SQL Server にも切り替えられます（[運用上の注意](#運用上の注意)）

## 起動方法（開発）

リポジトリのルートで実行します。

```bash
dotnet run --project src/MesApp.Api
```

`http://localhost:5288` で起動します（`src/MesApp.Api/Properties/launchSettings.json` の設定）。
ポートを変えたい場合は次のようにします。

```bash
dotnet run --project src/MesApp.Api --urls http://localhost:5210
```

ブラウザで上記URLを開くと、DBが空の場合は**初期セットアップの案内**が表示されます。
ユーザー名・氏名・パスワード（8文字以上、英小文字と数字を含む。例: `Passw0rd123`）を入力して
初期管理者を作成し、ログインします。

DBファイル（`mesapp.db`）とJWT署名鍵（`jwt-signing.key`）は**データ保存先（既定 `%LOCALAPPDATA%\ParallelFactoryMES`、環境変数 `MESAPP_DATA_DIR` で変更可）**に
自動生成されます。マイグレーションも起動時に自動適用されるため、DB作成作業は不要です。

## 初回の操作手順

1. **マスタ管理**で以下を登録します（この順番が必要です）
   - 工程 → 品目 → 品目行の「MBOM・工順」から**工順（BOP）を登録**
     （工順が未登録だと製造指図を工程展開できません）
   - ロケーション（部材倉庫・製品倉庫など）
   - 必要に応じて 設備／治工具／検査項目・基準／チェックリスト／スキル・資格／ユーザー
2. **受入**で部材在庫を計上します
3. **製造指図**を登録 → 承認 → 工程展開（産出ロットが自動採番されます）
4. **差立** → **段取り・チェックリスト** → **実績入力**
   （最終工程は入庫先ロケーションの指定が必要。バックフラッシュで部材を自動消費できます）
   → **完了承認**
5. 必要に応じて **検査管理** → **出荷判定** → **出荷管理**
   （出荷実行には、対象出荷指示に対する承認済みの出荷判定（可／特採）が必要です）

マスタは1件ずつの入力のほか、マスタ管理の各タブにある **CSV入出力** から一括登録・出力ができます
（テンプレートCSVの出力、UTF-8/Shift_JISの自動判別、エラー行の一覧表示、検証のみの実行に対応。
1行でもエラーがあれば全件ロールバックします）。詳細は
[ユーザーガイドのマスタ管理](https://msmsrep.github.io/ParallelFactoryMES/masters.html#csv)を参照してください。

書き方の見本として、そのまま取り込めるサンプルデータ一式を [`samples/master-csv/`](samples/master-csv/) に置いています
（ファイル名の番号順に取り込むと、製造指図から出荷まで試せる状態になります）。
3か月分の実績サンプルと同じ期間の生産計画は [`samples/production-plans/`](samples/production-plans/) にあり、生産計画・予実画面の計画登録タブから取り込みます。
ZIPにしたものを Releases に添付しています（[マスタ](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-master-csv.zip)・[実績](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv.zip)・[ダッシュボード確認用の3か月分の実績](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv-bulk.zip)・[同じ期間の生産計画](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-production-plans.csv)）。

## 配布・本番相当の実行

ビルド済みのものを使う場合（手順の詳細は [管理者向け運用](docs/operations.md)）:

- **Microsoft Store 版（1台で試す）**: [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=ja-JP&gl=JP) からインストールします。サーバー不要で、ほかの端末からは接続できません
- **ZIP（自己完結版・.NET不要）**: [Releases](https://github.com/msmsrep/ParallelFactoryMES/releases/latest) から
  `ParallelFactoryMES-<版>-win-x64.zip` / `-linux-x64.zip` を取得し、展開して `start.cmd`（Linux は `sh start.sh`）で起動します（ポート 5000）
- **Docker（amd64 / arm64）**: `docker compose up -d` で起動します（ポート 8080。PostgreSQL 付きは `compose.postgres.yaml`）

  ```bash
  docker run -d -p 8080:8080 -v mesapp-data:/data ghcr.io/msmsrep/parallelfactorymes
  ```

リリースは `v1.2.3` 形式のタグを push すると GitHub Actions が作ります（ZIP を Release に添付し、イメージを GHCR に登録）。
手元で ZIP を作るときは `./build/Pack-Zip.ps1`、イメージは `docker build -t parallelfactorymes .` です。

ソースから発行する場合:

```bash
dotnet publish src/MesApp.Api -c Release -o publish
```

生成された `publish` フォルダを対象PCにコピーして実行します。

```bash
dotnet publish/MesApp.Api.dll --urls http://0.0.0.0:5000
```

同じLAN内の他PCからは `http://<サーバーのIP>:5000` でアクセスできます
（Windowsファイアウォールで該当ポートの受信許可が必要です）。

### 設定

`publish/appsettings.json` を編集するか、環境変数で上書きできます。

| 設定 | 環境変数 | 既定値 |
|---|---|---|
| DB接続文字列（SQLiteはファイルの場所） | `Database__ConnectionString` | `Data Source=mesapp.db` |
| DBプロバイダー | `Database__Provider` | `Sqlite`（`PostgreSql` / `SqlServer` も可） |
| 業務日付の境界時刻 | `BusinessDay__BoundaryHour` | `6`（午前6時） |
| アクセストークン有効期限（分） | `Jwt__AccessTokenLifetimeMinutes` | `60` |
| リフレッシュトークン有効期限（時間） | `Jwt__RefreshTokenLifetimeHours` | `12` |
| JWT署名鍵ファイル | `Jwt__SigningKeyFile` | `jwt-signing.key`（未存在なら自動生成） |
| 初期管理者の自動作成 | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | 未設定 |
| データの保存先（DB・署名鍵） | `MESAPP_DATA_DIR` | `%LOCALAPPDATA%\ParallelFactoryMES`（Linux は `~/.local/share/ParallelFactoryMES`、Docker は `/data`） |

無人セットアップの例（ユーザーが0件のときのみ適用され、初回ログイン時にパスワード変更を強制します）。

```powershell
$env:MesAdmin__UserName="admin"; $env:MesAdmin__Password="Passw0rd123"; dotnet publish/MesApp.Api.dll
```

## 運用上の注意

- **HTTPS**: アプリ自身はHTTPSを強制しません。LAN運用ではリバースプロキシ（IIS / nginx）で
  HTTPSを終端し、プロキシのIPを `ReverseProxy__KnownProxies__0` に設定してください（設定しないと
  リフレッシュCookieに `Secure` が付かず、監査ログの接続元IPがプロキシのものになります）。
  手順は [管理者向け運用](docs/operations.md) の「HTTPSで運用する」。なお**カメラによるバーコード読み取りは、ブラウザの制約により
  `localhost` 以外ではHTTPSが必須**です（USB HIDリーダーと手入力はHTTPでも動作します）。
- **バックアップ**: SQLiteはWALモードで動作するため、**稼働中のDBファイルの単純コピーは行わないでください**。
  停止中にコピーするか、`VACUUM INTO` を使用します。
- **DBのリセット**: `mesapp.db*` を削除して再起動すると初期状態に戻ります。
- **PostgreSQL / SQL Server**: `Database__Provider` と `Database__ConnectionString` を指定すると、
  起動時にそのDBへスキーマを作成します（DB自体とログインは事前に作成しておく）。
  `DateTimeOffset` はPostgreSQLではUTCで保存され、SQL Serverの既定照合順序ではコードの大文字・小文字を
  区別しません（Spec.md 4章）。**既存のSQLiteデータを移す機能はありません**。手順・バックアップ・起動しないときの確認点は
  [管理者向け運用](docs/operations.md#database)を参照してください。

  ```powershell
  $env:Database__Provider="PostgreSql"; $env:Database__ConnectionString="Host=db;Database=mesapp;Username=mesapp;Password=..."
  $env:Database__Provider="SqlServer"; $env:Database__ConnectionString="Server=db;Database=mesapp;User Id=mesapp;Password=...;TrustServerCertificate=True"
  ```

## テスト

```bash
dotnet test ParallelFactoryMES.slnx
```

既定は一時SQLiteで流れます。環境変数 `MESAPP_TEST_PROVIDER`（`PostgreSql` / `SqlServer`）と
`MESAPP_TEST_CONNECTION`（データベース名を除いた接続文字列）を設定すると、実DBに対して全テストを流せます
（テストごとに `mesapp_test_<guid>` を作って消すため、ログインにはDBの作成・削除権限が要ります）。

```powershell
$env:MESAPP_TEST_PROVIDER="SqlServer"; $env:MESAPP_TEST_CONNECTION="Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True"
dotnet test tests/MesApp.Api.Tests
```

## プロジェクト構成

```
src/
  MesApp.Api             ASP.NET Core Web API（業務ロジック、Webクライアントの配信）
  MesApp.Client.Web      Blazor WebAssembly（MES機能のクライアント）
  MesApp.Core            ドメインモデル・DTO・APIコントラクト（API/クライアント共有）
  MesApp.Infrastructure  EF Core（DbContext、SQLite用マイグレーション、DBプロバイダー切替）
  MesApp.Migrations.PostgreSql  PostgreSQL用マイグレーション
  MesApp.Migrations.SqlServer   SQL Server用マイグレーション
tests/
  MesApp.Api.Tests       APIの統合テスト
samples/
  master-csv             マスタ一括登録用のサンプルCSV（取込順にファイル名を採番）
  actual-csv             実績一括登録用のサンプルCSV（master-csv の取込後に番号順で取り込む）
  actual-csv-bulk        ダッシュボード・予実確認用の約3か月分の実績（scripts/New-SampleBulkActuals.ps1 で生成）
  production-plans       actual-csv-bulk と同じ期間の生産計画（生産計画・予実画面の計画登録タブから取り込む）
```
