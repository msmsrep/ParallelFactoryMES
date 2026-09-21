---
layout: default
title: 管理者向け運用
nav_url: operations.html
lead: サーバーの配置、設定項目、バックアップなど、システム管理者向けの情報です。
prev_url: troubleshooting.html
prev_title: 困ったときは
next_url: privacy.html
next_title: プライバシーポリシー
---

## 構成

Webクライアント（Blazor WebAssembly）は、APIサーバー（ASP.NET Core）が同一オリジンで配信します。
利用者側にインストールするものはありません。

| コンポーネント | 役割 |
|---|---|
| MesApp.Api | 業務ロジックとデータベース。Webクライアントも配信します |
| MesApp.Client.Web | ブラウザで動作するクライアント |
| データベース | SQLite（ファイル1つ）。起動時にマイグレーションが自動適用されます |

## 配布と起動

### 配布ファイルを作る

```bash
dotnet publish src/MesApp.Api -c Release -o publish
```

生成された `publish` フォルダを対象PCにコピーします。

### 起動する

```bash
dotnet publish/MesApp.Api.dll --urls http://0.0.0.0:5000
```

同じLAN内の他のPCからは `http://<サーバーのIP>:5000` でアクセスできます。

<div class="warn">
<p>Windowsファイアウォールで、該当ポートの<strong>受信許可</strong>が必要です。
これを設定しないと、他のPCから接続できません。</p>
</div>

データベースファイル（`mesapp.db`）とJWT署名鍵（`jwt-signing.key`）は、
**起動したディレクトリ** に自動生成されます。

## 設定項目  {#settings}

`appsettings.json` を編集するか、環境変数で上書きします。

| 設定 | 環境変数 | 既定値 |
|---|---|---|
| DBファイルの場所 | `Database__ConnectionString` | `Data Source=mesapp.db` |
| DBプロバイダー | `Database__Provider` | `Sqlite` |
| 業務日付の境界時刻 | `BusinessDay__BoundaryHour` | `6`（午前6時） |
| 監査ログの保持期間（年） | `Audit__RetentionYears` | `5` |
| アクセストークン有効期限（分） | `Jwt__AccessTokenLifetimeMinutes` | `60` |
| リフレッシュトークン有効期限（時間） | `Jwt__RefreshTokenLifetimeHours` | `12` |
| JWT署名鍵ファイル | `Jwt__SigningKeyFile` | `jwt-signing.key`（未存在なら自動生成） |
| 初期管理者の自動作成 | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | 未設定 |
| 初期パスワードの自動生成 | `MesAdmin__GeneratePassword` | `false`（デスクトップ版のみ `true`） |

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
アカウントを残さないため）。パスワードを画面に表示できるデスクトップ版のみ、
`MesAdmin__GeneratePassword=true` による自動生成を使います。

## セキュリティ

<div class="warn">
<p><strong>現時点ではHTTPS強制を実装していません。</strong> LAN運用では、リバースプロキシ
（IIS / nginx）でHTTPSを終端することを推奨します。</p>
<p>あわせて、<strong>カメラによるバーコード読み取りは <code>localhost</code> 以外ではHTTPSが必須</strong>です。
HTTPのままではカメラが使えません（USBリーダーと手入力はHTTPでも動作します）。</p>
</div>

- 認証は ASP.NET Core Identity ＋ JWT です。
- 実績訂正・検査訂正・マスタ変更・在庫調整などは監査ログに記録されます（下記）。
- パスワード要件は8文字以上、英小文字と数字を含むことです。

## 監査ログ  {#audit-log}

「誰が・いつ・何を変更したか」の記録です。**メニューの「監査ログ」**から確認できます
（システム管理者専用。他のロールにはメニュー自体が表示されません）。

### 記録される操作

| 分類 | 主な操作 |
|---|---|
| Setup | 初期管理者の作成 |
| Auth | ログイン、ログイン失敗、アカウントロック、ログアウト、パスワード変更 |
| User | ユーザーの作成・変更、パスワードリセット、スキル資格の付与 |
| Master | マスタの登録・変更・無効化、CSV取込 |
| Production | 製造指図の登録・承認・取消・工程展開、差立 |
| Execution | 着手、実績登録、**実績訂正**、作業時間、製造条件データ、完了承認 |
| Inventory | 受入、移動、**数量調整**、ステータス変更、分割・統合・振替、廃棄、ピッキング、出荷、棚卸、**取消** |
| Quality | 検査実績の登録・**訂正**・判定・承認、不適合の起票・対応指示・承認、出荷判定 |
| Maintenance | 保全計画・保全指示・保全実績、保全手順書の管理 |
| Equipment | 設備稼働の記録、治工具の使用実績 |

数量調整・ステータス変更・訂正・取消など、**変更前後をたどりたい操作は「詳細」列に変更前・変更後・理由**が
入ります。それ以外は要約が入ります。

### 絞り込み

期間・分類/操作・対象（種別とID）で絞り込めます。
たとえば「対象の種別」に `Lot`、「対象のID」にロットの内部IDを入れると、
そのロットに対して行われた操作だけを並べられます。

<div class="note">
<p><strong>期間は記録日（サーバーの日付）で絞り込みます。</strong>
一覧の日時はブラウザの時刻で表示されるため、サーバーとブラウザのタイムゾーンが異なる環境では
日をまたぐ時間帯でずれて見えることがあります。</p>
</div>

### 削除・改ざんについて

- 個々の記録は**画面からもAPIからも変更・削除できません**。記録が増えるだけです。
- **保持期間は5年**です（`Audit__RetentionYears` で変更できます）。**自動削除は行いません**（消してよいかは業務判断のため）。
- 容量が問題になったときだけ、**保持期間を過ぎた分をまとめて削除**できます（下記）。

### 古い記録の一括削除

保持期間を過ぎた記録を整理する操作です。**監査ログ画面の一番下**にあります。
先に下記のバックアップを取得してください。

<div class="steps">
<ol>
<li>「この日まで削除」に日付を、「理由」に削除する理由を入力します（<strong>指定日を含めて</strong>それ以前が対象です）。</li>
<li><strong>削除件数を確認</strong>を押します。この時点ではまだ何も消えません。対象件数が表示されます。</li>
<li>件数を確かめて <strong>「N 件を削除する」</strong> を押すと削除されます。</li>
</ol>
</div>

日付か理由を変更すると件数の確認はリセットされ、もう一度確認し直しになります
（確認した内容と実際に消す内容がずれないようにするためです）。

APIから実行する場合は次のとおりです。`dryRun=true` のあいだは件数を返すだけでDBには触りません。

```
POST /api/audit-logs/purge?dryRun=true
{ "to": "2020-12-31", "reason": "容量削減のため" }
```

<div class="warn">
<p><strong>保持期間の内側は削除できません。</strong> 保持期間より新しい日付を指定すると拒否されます
（直前の操作の記録を消して隠せてしまうと、監査ログが証跡として成り立たないためです）。</p>
<p><strong>理由の入力は必須</strong>で、<strong>削除したこと自体（期間・件数・理由・実行者）が監査ログに残ります</strong>。
この記録は削除の後に書かれるため、同じ操作では消えません。</p>
</div>

## バックアップ

<div class="warn">
<p><strong>稼働中のDBファイルを単純コピーしないでください。</strong>
SQLiteはWALモードで動作するため、コピーしたファイルが壊れる可能性があります。</p>
</div>

安全な方法は次の2つです。

1. サーバーを停止してから `mesapp.db` をコピーする
2. 稼働中なら SQLite の `VACUUM INTO` でバックアップファイルを作る

`mesapp.db*`（`-wal` / `-shm` を含む）をすべて削除して再起動すると、初期状態に戻ります。
検証環境をリセットしたいときに使えます。

## 制限事項

| 項目 | 状況 |
|---|---|
| シート・端末管理（ライセンス） | 未実装。アクティベーション不要で全端末から利用できます |
| WPFライセンスアプリ | 未実装 |
| PostgreSQL / SQL Server | 未対応。`Database__Provider` に指定すると起動時にエラーになります |
| Docker化・Zip配布 | 未対応 |
| HTTPS強制 | 未実装（リバースプロキシで対応） |
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

このガイドは `docs/` フォルダのMarkdownで管理されています。GitHub Pagesで公開するには、
リポジトリの **Settings → Pages** で次のように設定します。

- Source: `Deploy from a branch`
- Branch: `master`（または `main`）／ フォルダ: `/docs`

内容を更新するときは `docs/*.md` を編集してコミットすれば、自動的に再公開されます。
ページを追加した場合は、左メニューに載せるため `docs/_config.yml` の `nav` にも項目を追加してください。
