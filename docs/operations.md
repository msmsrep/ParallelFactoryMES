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
| データベース | SQLite（既定。ファイル1つ）、PostgreSQL、SQL Server のいずれか。起動時にマイグレーションが自動適用されます（[PostgreSQL / SQL Server を使う](#database)） |

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
  `ReverseProxy__KnownNetworks__0`（例 `172.16.0.0/12`）で指定します（[HTTPSで運用する](#https)）
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
| 初期管理者の自動作成 | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | 未設定 |
| 初期パスワードの自動生成 | `MesAdmin__GeneratePassword` | `false`（デスクトップ版のみ `true`） |
| 信頼するリバースプロキシのIP | `ReverseProxy__KnownProxies__0`（複数は `__1`, `__2` …） | 未設定（転送ヘッダを読まない） |
| 信頼するリバースプロキシのネットワーク | `ReverseProxy__KnownNetworks__0`（CIDR。例 `192.168.10.0/24`） | 未設定 |
| フォルダ連携の連携フォルダ | `FolderLink__Root` | 未設定（連携しない） |
| フォルダ連携の取込を行う利用者 | `FolderLink__UserName` | 未設定 |
| フォルダ連携の走査の間隔（秒） | `FolderLink__PollSeconds` | `30` |
| フォルダ連携の結果の言語 | `FolderLink__Language` | `ja`（`en` も可） |

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

## フォルダ連携  {#folder-link}

決めたフォルダに置かれたCSVを自動で取り込みます。販売管理・生産管理などのソフトが出力したCSVを、
プログラムを書かずに取り込めます（たとえば、毎朝出力される製造指図のCSVを共有フォルダに置かせる）。

### 設定する

1. 取込を行う利用者を作ります（例 `folder-link`）。**取り込みたい種別に必要なロールだけ**を与えます
   （製造指図なら生産管理担当者、受入なら物流・倉庫担当者。ロールごとの権限は[ロールと権限](roles.html)）。
   取込の可否・実績の記録者・監査ログは、この利用者で判定・記録されます。
2. 連携フォルダと利用者を設定して、再起動します。

```powershell
$env:FolderLink__Root="D:\mes-link"; $env:FolderLink__UserName="folder-link"; ./start.cmd
```

Docker では、連携フォルダをホストのフォルダにマウントします（`compose.yaml` の例）。

```yaml
    volumes:
      - mesapp-data:/data
      - ./mes-link:/link
    environment:
      FolderLink__Root: /link
      FolderLink__UserName: folder-link
```

設定を変えたときは再起動が要ります。起動すると、連携フォルダの下に `inbox`・`processed`・`error` が作られます。

### ファイルの置き方

| 置く場所 | 種別の決まり方 | 例 |
|---|---|---|
| `inbox` の直下 | ファイル名（最初の「_」より後ろ） | `inbox/manufacturing-orders.csv`、`inbox/20261122_manufacturing-orders.csv` |
| `inbox/<種別>/` | フォルダ名（ファイル名は問わない） | `inbox/manufacturing-orders/製造指図.csv` |
| `inbox` の直下のZIP | 中のファイル名（ZIPの一括取込と同じ） | `inbox/masters.zip` |

- 種別の名前とCSVの列は、マスタ管理・実績CSV取込の画面のテンプレートと同じです。出力するファイル名を変えられないソフトは、種別のフォルダに出力させます。
- CSV・ZIP 以外のファイルは無視します。書き込み中のファイルは、書き終わってから取り込みます。
- 置かれた順（更新日時の古い順）に1ファイルずつ取り込みます。後のファイルが前のファイルを参照するとき（製造指図のあとに受入など）は、順に置くか1つのZIPにまとめます。

### 結果を確かめる

- **取り込めたファイルは `processed`、取り込めなかったファイルは `error`** の `日付/時刻/` の下へ移り、横に結果ファイル（`.result.txt`）が付きます。エラーの行番号と理由はここに書かれます。
- 取込は画面から取り込むときと同じチェックを通り、**1行でもエラーがあればそのファイルは何も反映しません**。直したファイルを `inbox` の同じ場所へ戻せば、取り込み直せます。
- システム管理者は、メニューの「管理」→「フォルダ連携」で、設定の誤りと最近処理したファイルを確かめられます。「今すぐ取り込む」で、次の間隔を待たずに取り込めます。
- 利用者がいない・無効・ロック中のときは、何も取り込まずファイルを `inbox` に残します（画面に理由が出ます）。

MESから外へ書き出す連携（実績の自動出力）は、まだありません。

## APIキー  {#api-keys}

販売管理・生産管理などのシステムや社内のスクリプトが、ログインせずに MES の**連携API**（`/api/integration/v1/...`）を呼ぶためのキーです。

### 発行する

1. キーで操作させる利用者を作ります（例 `erp-link`）。**キーで行える操作は、この利用者のロールで決まります**。用途ごとに専用の利用者を作り、必要なロールだけを与えてください（[ロールと権限](roles.html)）。
2. システム管理者が、メニューの「管理」→「APIキー」で、用途・利用者・有効期限（任意）を指定して **キーを発行** を押します。
3. 表示されたキー（`pfm_` で始まる文字列）を、連携するシステムの設定に写します。**キーはこのときしか表示されません**。なくしたときは、失効させて発行し直します。

### 呼んでみる

要求のヘッダ `X-Api-Key` にキーを付けます。次の口で、誰として扱われるか（利用者とロール）を確かめられます。

```powershell
Invoke-RestMethod http://localhost:5000/api/integration/v1/whoami -Headers @{ "X-Api-Key" = "pfm_..." }
```

```bash
curl -H "X-Api-Key: pfm_..." http://localhost:5000/api/integration/v1/whoami
```

### 注意

- キーで呼べるのは連携API だけです。画面で使っている API（`/api/products` など）はキーでは呼べません（401）。
- キーで行った操作は、紐づけた利用者が行ったものとして監査ログに残ります。
- **失効させたキーは元に戻せません**。利用者を無効にしたとき、キーの有効期限が過ぎたときも、すぐに使えなくなります。ロールを変えると、次の呼び出しから新しいロールで判定されます。
- キーはパスワードと同じように扱ってください。メールやチャットに貼らず、連携するシステムの設定の中だけに置きます。

## セキュリティ

<div class="warn">
<p><strong>アプリ自身はHTTPSを強制しません。</strong> LAN運用では、リバースプロキシ
（IIS / nginx）でHTTPSを終端してください（<a href="#https">HTTPSで運用する</a>）。</p>
<p>あわせて、<strong>カメラによるバーコード読み取りは <code>localhost</code> 以外ではHTTPSが必須</strong>です。
HTTPのままではカメラが使えません（USBリーダーと手入力はHTTPでも動作します）。</p>
</div>

- 認証は ASP.NET Core Identity ＋ JWT です。
- 実績訂正・検査訂正・マスタ変更・在庫調整などは監査ログに記録されます（下記）。
- パスワード要件は8文字以上、英小文字と数字を含むことです。

### HTTPSで運用する  {#https}

ブラウザからのHTTPSはリバースプロキシ（IIS / nginx）が受け、アプリへはHTTPで中継します。
証明書を持つのはプロキシだけです。

```
端末のブラウザ ──HTTPS──▶ IIS / nginx（証明書）──HTTP──▶ MesApp（127.0.0.1:5000）
```

**1. 証明書を用意する。** 社内LANでは公的な証明書を取れないので、社内CAか
[mkcert](https://github.com/FiloSottile/mkcert) で発行します。mkcert の場合、サーバーで次を実行します。

```bash
mkcert -install
mkcert mes.example.local 192.168.10.5
```

端末から使う名前（ホスト名・IP）をすべて並べます。できた `mes.example.local+1.pem`（証明書）と
`mes.example.local+1-key.pem`（秘密鍵）をプロキシに設定します。

**2. CA証明書を端末に配る。** `mkcert -CAROOT` で表示されるフォルダの `rootCA.pem` を
`rootCA.crt` に名前を変えて各端末へ配り、「信頼されたルート証明機関」に入れます
（端末で `certutil -addstore -f Root rootCA.crt`、台数が多ければグループポリシーで配布）。
同じフォルダの `rootCA-key.pem` は**配らないでください**（持っている人は任意の証明書を発行できます）。

**3. アプリをプロキシからだけ届くように起動する。** HTTPで直接つながれないよう、
ループバックでだけ待ち受けます（アプリはHTTPからHTTPSへのリダイレクトをしません）。

```bash
dotnet publish/MesApp.Api.dll --urls http://127.0.0.1:5000
```

**4. プロキシを設定する。** 接続元IPとスキームを `X-Forwarded-For` / `X-Forwarded-Proto` で渡します。

nginx の例:

```nginx
server {
    listen 443 ssl;
    server_name mes.example.local;
    ssl_certificate     mes.example.local+1.pem;
    ssl_certificate_key mes.example.local+1-key.pem;
    client_max_body_size 50m;   # CSV・ZIPの一括取込のため

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

IIS の場合は URL Rewrite と Application Request Routing（ARR）を入れ、ARRのプロキシを有効にします。
`X-Forwarded-For` はARRが付けます。`X-Forwarded-Proto` はサーバー変数 `HTTP_X_FORWARDED_PROTO` を
「許可されたサーバー変数」に追加したうえで、書き換え規則で付けます（`web.config` の例）。

```xml
<rewrite>
  <rules>
    <rule name="MesApp" stopProcessing="true">
      <match url="(.*)" />
      <serverVariables>
        <set name="HTTP_X_FORWARDED_PROTO" value="https" />
      </serverVariables>
      <action type="Rewrite" url="http://127.0.0.1:5000/{R:1}" />
    </rule>
  </rules>
</rewrite>
```

**5. プロキシを信頼する設定を入れる。** アプリは、ここで指定した接続元から来た転送ヘッダだけを読みます。
プロキシが同じPCなら次のとおりです（別のPCならそのIP）。

```powershell
$env:ReverseProxy__KnownProxies__0="127.0.0.1"
```

<div class="warn">
<p><strong>この設定が無いと、転送ヘッダは読まれません。</strong> HTTPSでつながっていても、
リフレッシュトークンのCookieに <code>Secure</code> が付かず、監査ログの接続元IPがすべてプロキシのIPになります。
未設定のときに読まないのは、HTTPで直接つないだ端末がヘッダを偽装して接続元を詐称できないようにするためです。</p>
</div>

**確認:** 端末からHTTPSでログインし、監査ログの「ログイン」の接続元IPが端末のIPになっていれば完了です。
`127.0.0.1` のままなら手順5の設定が効いていません。

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

## PostgreSQL / SQL Server を使う {#database}

既定の SQLite は追加インストールが要らず、数十端末規模までならそのまま運用できます。
同時に書き込む端末が多い、データ量が大きい、既存のDBサーバーでまとめて管理したい、といった場合は
PostgreSQL または SQL Server に切り替えます。

1. DBサーバーに**空のデータベース**と接続用のログインを作ります。
   ログインには、そのデータベースでテーブルを作成・変更できる権限を与えます（起動時にスキーマを作成・更新するため）。
2. `Database__Provider` と `Database__ConnectionString` を設定して起動します。

```powershell
# PostgreSQL
$env:Database__Provider="PostgreSql"; $env:Database__ConnectionString="Host=db;Database=mesapp;Username=mesapp;Password=..."
# SQL Server
$env:Database__Provider="SqlServer"; $env:Database__ConnectionString="Server=db;Database=mesapp;User Id=mesapp;Password=...;TrustServerCertificate=True"
```

3. 起動時にテーブルが作られます。ブラウザで開くと、SQLiteのときと同じく初期セットアップの案内が表示されます。
   以降のバージョンアップでも、起動時に差分が自動適用されます。

<div class="warn">
<p><strong>既存のSQLiteのデータは移りません。</strong> 切り替えると空のデータベースから始まります。
運用を始める前にどのデータベースを使うかを決めてください。設定をSQLiteに戻すと、元の <code>mesapp.db</code> がそのまま使われます（両者のデータは別々です）。</p>
</div>

### SQLite との違い

| 項目 | 違い |
|---|---|
| コードの大文字・小文字 | SQL Server の既定の照合順序では区別しません（`ABC` と `abc` は同じコードとして重複扱いになり、検索も区別しません）。SQLite・PostgreSQL は区別します |
| 日時 | PostgreSQL ではUTCで保存されます。同じ時点を指すため、画面表示・集計・CSVの結果は変わりません |
| 小数 | 小数点以下6桁まで保存します（SQLite と同じ値の見え方になるよう末尾のゼロは落とします） |
| デスクトップ版 | SQLite で使う前提です（1台のPCで完結させる構成のため） |

### 起動しないとき

| 症状 | 対処 |
|---|---|
| 「不明なDBプロバイダー」で止まる | `Database__Provider` は `Sqlite` / `PostgreSql` / `SqlServer` のいずれかを、大文字・小文字も含めてそのとおりに書いてください |
| 接続のタイムアウト・認証エラーで止まる | 接続文字列のホスト名・ポート・ユーザー名・パスワード、DBサーバー側のファイアウォール、SQL Server では TCP/IP 接続が有効かを確認してください |
| SQL Server で証明書のエラーになる | 社内の自己署名証明書のサーバーなら接続文字列に `TrustServerCertificate=True` を付けるか、信頼された証明書をサーバーに設定してください |
| 権限エラー（テーブルを作成できない）で止まる | 接続ログインに、対象データベースでのテーブル作成・変更の権限を与えてください |

## バックアップ

### SQLite

<div class="warn">
<p><strong>稼働中のDBファイルを単純コピーしないでください。</strong>
SQLiteはWALモードで動作するため、コピーしたファイルが壊れる可能性があります。</p>
</div>

安全な方法は次の2つです。

1. サーバーを停止してから `mesapp.db` をコピーする
2. 稼働中なら SQLite の `VACUUM INTO` でバックアップファイルを作る

Docker の場合は、コンテナを止めてからボリュームごと退避します（署名鍵も一緒に残ります）。
ボリューム名は `docker volume ls` で確認してください（compose では `<フォルダ名>_mesapp-data`）。

```bash
docker compose stop
docker run --rm -v <ボリューム名>:/data -v "$PWD":/backup alpine tar czf /backup/mesapp-data.tgz -C /data .
docker compose start
```

`mesapp.db*`（`-wal` / `-shm` を含む）をすべて削除して再起動すると、初期状態に戻ります。
検証環境をリセットしたいときに使えます。

### PostgreSQL / SQL Server

各データベースの標準の手段でバックアップします（稼働中でも取得できます）。

- PostgreSQL: `pg_dump -Fc -d mesapp -f mesapp.dump`（復元は `pg_restore`）
- SQL Server: `BACKUP DATABASE mesapp TO DISK = N'...\mesapp.bak'`（または SQL Server Management Studio のバックアップ）

初期状態に戻すには、データベースを削除して空のデータベースを作り直し、再起動します。

## 制限事項

| 項目 | 状況 |
|---|---|
| PostgreSQL / SQL Server | 対応（[PostgreSQL / SQL Server を使う](#database)）。既存のSQLiteデータを移す機能は無し |
| Docker化・Zip配布 | 対応（[配布と起動](#配布と起動)） |
| HTTPS | アプリ側では強制しない（リバースプロキシで終端。[HTTPSで運用する](#https)） |
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
