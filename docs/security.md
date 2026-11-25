---
layout: default
title: セキュリティと監査ログ
nav_url: security.html
lead: "認証とHTTPSでの運用、監査ログの見方と古い記録の削除です。"
prev_url: integration.html
prev_title: 外部連携
next_url: database.html
next_title: データベースとバックアップ
---

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
