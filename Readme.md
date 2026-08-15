# ParallelFactoryMES

製造実績管理システム（MES）。ASP.NET Core Web API（バックエンド）＋ Blazor WebAssembly（Webクライアント）で構成し、
APIが同一オリジンでWebクライアントも配信します。

- 仕様: [Spec.md](Spec.md)
- 機能スコープの根拠: [MES.md](MES.md)（MES業務プロセス定義表）

## 実装済みの範囲

| Phase | 内容 | 状態 |
|---|---|---|
| 1 | バックエンド基盤（EF Core + SQLite、JWT認証、初期セットアップ、監査ログ） | 実装済 |
| 2 | マスタ管理API＋生産管理API（製造指図・工程展開・差立） | 実装済 |
| 3 | 製造実行API＋物流/在庫管理API | 実装済 |
| 4 | 品質管理API＋品質保証API（検査・不適合・出荷判定・トレーサビリティ） | 実装済 |
| 5 | 設備保全API（保全計画・指示・実績、治工具寿命管理） | 実装済 |
| 6〜7 | Blazor WebAssembly クライアント（全画面）、帳票・ラベル出力、バーコード/QRスキャン | 実装済 |
| 8〜9 | シート・端末管理、WPFライセンスアプリ | **未実装（今回は対象外）** |
| 10 | DBプロバイダー切替（PostgreSQL / SQL Server）、Docker化、Zip配布 | 未実装 |

ライセンス（シート・端末）機能は未実装のため、**アクティベーション不要で全端末から利用できます**。

## 前提

- **.NET 10 SDK**（実行のみなら ASP.NET Core 10 Runtime）
  - 入手先: https://dotnet.microsoft.com/download
- データベースは SQLite（追加インストール不要。ファイルは自動生成されます）

## 起動方法（開発）

リポジトリのルートで実行します。

```bash
dotnet run --project src/MesApp.Api
```

admin Passw0rd123  
`http://localhost:5288` で起動します（`src/MesApp.Api/Properties/launchSettings.json` の設定）。
ポートを変えたい場合は次のようにします。

```bash
dotnet run --project src/MesApp.Api --urls http://localhost:5210
```

ブラウザで上記URLを開くと、DBが空の場合は**初期セットアップの案内**が表示されます。
ユーザー名・氏名・パスワード（8文字以上、英小文字と数字を含む。例: `Passw0rd123`）を入力して
初期管理者を作成し、ログインします。

DBファイル（`mesapp.db`）とJWT署名鍵（`jwt-signing.key`）は**起動したディレクトリ**（＝`src/MesApp.Api/`）に
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

## 配布・本番相当の実行

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
| DBファイルの場所 | `Database__ConnectionString` | `Data Source=mesapp.db` |
| DBプロバイダー | `Database__Provider` | `Sqlite`（他はPhase 10で対応） |
| 業務日付の境界時刻 | `BusinessDay__BoundaryHour` | `6`（午前6時） |
| アクセストークン有効期限（分） | `Jwt__AccessTokenLifetimeMinutes` | `60` |
| リフレッシュトークン有効期限（時間） | `Jwt__RefreshTokenLifetimeHours` | `12` |
| JWT署名鍵ファイル | `Jwt__SigningKeyFile` | `jwt-signing.key`（未存在なら自動生成） |
| 初期管理者の自動作成 | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | 未設定 |

無人セットアップの例（ユーザーが0件のときのみ適用され、初回ログイン時にパスワード変更を強制します）。

```powershell
$env:MesAdmin__UserName="admin"; $env:MesAdmin__Password="Passw0rd123"; dotnet publish/MesApp.Api.dll
```

## 運用上の注意

- **HTTPS**: 現時点ではHTTPS強制を実装していません。LAN運用ではリバースプロキシ（IIS / nginx）で
  HTTPS終端してください。なお**カメラによるバーコード読み取りは、ブラウザの制約により
  `localhost` 以外ではHTTPSが必須**です（USB HIDリーダーと手入力はHTTPでも動作します）。
- **バックアップ**: SQLiteはWALモードで動作するため、**稼働中のDBファイルの単純コピーは行わないでください**。
  停止中にコピーするか、`VACUUM INTO` を使用します。
- **DBのリセット**: `mesapp.db*` を削除して再起動すると初期状態に戻ります。
- **PostgreSQL / SQL Server**: Phase 10で対応予定です。現在 `Database__Provider` に指定すると
  起動時にエラーになります。

## テスト

```bash
dotnet test ParallelFactoryMES.slnx
```

## プロジェクト構成

```
src/
  MesApp.Api             ASP.NET Core Web API（業務ロジック、Webクライアントの配信）
  MesApp.Client.Web      Blazor WebAssembly（MES機能のクライアント）
  MesApp.Core            ドメインモデル・DTO・APIコントラクト（API/クライアント共有）
  MesApp.Infrastructure  EF Core（DbContext、マイグレーション、DBプロバイダー切替）
tests/
  MesApp.Api.Tests       APIの統合テスト
```
