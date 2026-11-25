---
layout: default
title: 外部連携
nav_url: integration.html
lead: "CSVのフォルダ連携、APIキーと連携API、設備イベントの受信、Webhook による通知など、ほかのシステムや設備とつなぐ方法です。"
prev_url: operations.html
prev_title: サーバーの配置と設定
next_url: security.html
next_title: セキュリティと監査ログ
---

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

## 連携API  {#integration-api}

販売管理・生産管理（ERP）や社内のスクリプトから、MES とデータをやり取りするための API です。呼び出しには [APIキー](#api-keys) を使います。

### 仕様書（OpenAPI）

連携API の仕様書を `/openapi/integration-v1.json` で公開しています（例 `http://localhost:5000/openapi/integration-v1.json`）。
Postman や Visual Studio などに読み込むと、口の一覧・引数・応答の形が分かります。仕様書の取得にはキーは要りません。

### 使える口

| 口 | 内容 |
|---|---|
| `GET /api/integration/v1/whoami` | キーが通るか、誰として・どのロールで扱われるかを確かめる |
| `GET /api/integration/v1/csv-kinds` | 取り込める CSV の種別と列（マスタ管理・実績CSV取込の画面と同じ） |
| `POST /api/integration/v1/masters/{種別}` | マスタCSVの取込（コードで上書き） |
| `POST /api/integration/v1/actuals/{種別}` | 実績CSVの取込（常に新規登録） |
| `POST /api/integration/v1/masters/bundle`・`actuals/bundle` | CSV をまとめた ZIP の取込 |
| `GET /api/integration/v1/masters/{種別}` | マスタのCSV出力 |
| `GET /api/integration/v1/production-results` | 生産実績を前回の続きから読む |
| `GET /api/integration/v1/production-result-corrections` | 生産実績の訂正を前回の続きから読む |
| `POST /api/integration/v1/equipment-events` | 設備の状態の変化を送る（[設備イベントの受信](#equipment-events)） |

### CSV を取り込む

本文に CSV をそのまま送ります（文字コードは UTF-8 か Shift_JIS）。`?dryRun=true` を付けると、検証だけして反映しません。
このページの PowerShell の例は PowerShell 7（`pwsh`）向けです。

```powershell
$headers = @{ "X-Api-Key" = "pfm_..."; "Idempotency-Key" = "orders-20261123" }
Invoke-RestMethod -Method Post -Headers $headers -ContentType "text/csv; charset=utf-8" -InFile .\manufacturing-orders.csv -Uri http://localhost:5000/api/integration/v1/actuals/manufacturing-orders
```

```bash
curl -H "X-Api-Key: pfm_..." -H "Idempotency-Key: orders-20261123" -H "Content-Type: text/csv" --data-binary @manufacturing-orders.csv http://localhost:5000/api/integration/v1/actuals/manufacturing-orders
```

- 取り込めたら **200**、1行でもエラーがあれば **422** で、何も反映しません。どちらも応答の本文に、件数と行番号付きのエラーが入ります（PowerShell 7 では `-SkipHttpErrorCheck` を付けると 422 の本文も受け取れます）。
- 取込の権限は、キーに紐づく利用者のロールで画面と同じように判定します（権限が無ければ 403）。

### 送り直しても二重に登録しない（Idempotency-Key）

実績CSVは**常に新規登録**なので、通信が切れたあとに同じ CSV を送り直すと、二重に登録されることがあります。
これを防ぐには、送る内容ごとに決めた値（例：指図の出力日時と連番、UUID）を `Idempotency-Key` ヘッダに付けます。

- 同じキー・同じ内容で送り直すと、**取り込み直さずに前回の応答を返します**（応答のヘッダに `Idempotent-Replayed: true` が付きます）。
- 同じキーで内容が違う要求は 409 になります。新しい内容には新しいキーを付けてください。
- キーは24時間覚えています。キーは APIキーごとに別に扱います。

### 生産実績を読む（前回の続きから）

`afterId` に前回の `nextAfterId` を渡すと、その後に記録された実績だけが返ります。`hasMore` が true の間は続けて読みます。
最後の `nextAfterId` を保存しておけば、次の実行は続きから始まります。

```powershell
$headers = @{ "X-Api-Key" = "pfm_..." }
$after = if (Test-Path .\last-id.txt) { Get-Content .\last-id.txt } else { 0 }
do {
  $page = Invoke-RestMethod -Headers $headers -Uri "http://localhost:5000/api/integration/v1/production-results?afterId=$after"
  $page.items | Export-Csv .\production-results.csv -Append -Encoding utf8
  $after = $page.nextAfterId
} while ($page.hasMore)
Set-Content .\last-id.txt $after
```

- 実績の数量は、訂正された後の今の値です。既に読んだ実績が後から訂正されたことは、`production-result-corrections` を同じように続きから読むと分かります。
- 承認前の実績も返します（`approvedAt` が空）。取り消した指図の実績も、現物のロットが残るので返します（`workOrderStatus` で分かります）。

### 版を上げても連携が動き続けるための約束

- v1（`/api/integration/v1`）の中では、**項目や口を足すだけ**で、消したり名前や意味を変えたりしません。変えるときは v2 を作ります。
- CSV の列も同じです。列は消さず、必須の列も増やしません（新しい列は、省けば今までどおりに動きます）。列の名前を変えるときは、以前の名前でも読めるようにします。
- 応答に知らない項目が増えても、読み飛ばすように連携を作ってください。

## 設備イベントの受信  {#equipment-events}

設備の稼働状態（稼働・停止・段取り・故障・アイドル）を、設備から自動で記録する仕組みです。
**MES は PLC と直接は通信しません。** PLC の状態はゲートウェイ（Node-RED など）が読み、状態が変わったときに MES の連携API へ送ります。
MES は届いた状態の変化を、設備ごとに時刻の順に並べて**稼働履歴の区間**（開始〜終了）に組み立てます。

```
設備・PLC ──(OPC UA・MC プロトコル・Modbus など)──▶ ゲートウェイ ──(HTTP・APIキー)──▶ MES
                                                     （Node-RED など）     POST /api/integration/v1/equipment-events
```

### 準備する

1. 設備マスタ（マスタ管理の **設備（BOE）** タブ）で、対象の設備に **設備イベントで稼働状態を収集する** を付けます。
   付けた設備には、稼働実績を手入力や実績CSVで登録できなくなります（手と自動で同じ時間を二重に数えないため）。停止原因の追記はできます。
2. ゲートウェイ用の利用者を作り、[APIキー](#api-keys) を発行します。受信はロールを問いません。
3. 必要なら、アラーム番号を停止原因に置き換える **停止原因の対応表**（マスタ管理）を登録します。

### 送る形

状態が変わるたびに1件を作り、ためておいてまとめて送ります（1回に1,000件まで）。

```json
{
  "events": [
    { "equipmentCode": "EQ-06", "eventId": "EQ-06-1732323600000-1", "occurredAt": "2026-11-23T09:00:00+09:00", "state": "Running" },
    { "equipmentCode": "EQ-06", "eventId": "EQ-06-1732324200000-2", "occurredAt": "2026-11-23T09:10:00+09:00", "state": "Stopped", "alarmCode": "A-101" }
  ]
}
```

| 項目 | 内容 |
|---|---|
| `equipmentCode` | 設備の資産番号 |
| `eventId` | ゲートウェイが振る一意のID（設備ごとに一意。100文字まで）。**送り直しても同じ値**にする |
| `occurredAt` | 設備で状態が変わった時刻（`+09:00` などのオフセット付き） |
| `state` | `Running`（稼働）／`Stopped`（停止）／`Setup`（段取り）／`Failure`（故障）／`Idle`（アイドル）／`Off`（電源断・通信断） |
| `alarmCode` | 任意。停止・故障のときのアラーム番号（停止原因の対応表で原因を引く） |
| `workOrderNo` ＋ `operationSeq` | 任意。処理中の作業指示（指図番号と工程順序の組） |
| `note` | 任意。備考 |

```powershell
$headers = @{ "X-Api-Key" = "pfm_..." }
$body = @{ events = @(@{ equipmentCode = "EQ-06"; eventId = "test-1"; occurredAt = (Get-Date).ToString("o"); state = "Running" }) } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" -Body $body -Uri http://localhost:5000/api/integration/v1/equipment-events
```

### 届いた後の扱い

- 新しい状態が届くと、開いている区間を閉じて次の区間を開きます。同じ状態が続けて届いても区間は変わりません。`Off` は区間を閉じるだけです。
- 1回に送った中で時刻が前後していても、時刻の順に並べ直して反映します。
- **同じ `eventId` を送り直すと「重複」になり、二重に記録されません。** 通信が切れたら、ためた分をそのまま送り直してください。
- **最後に反映した時刻より前のイベントは「遅着」として拒否します。** 過去の区間は組み直しません。設備とゲートウェイの時計は NTP で揃えてください。サーバーの時刻より先すぎる時刻（既定300秒を超える）も拒否します。
- 応答は 200 で、1件ごとの結果（`Accepted` 採用／`Duplicate` 重複／`Rejected` 拒否と理由）が入ります。全部が拒否でも 200 です。拒否した `eventId` は、直して送り直すと判定し直します。
- 停止・故障の区間は、アラーム番号から停止原因の対応表で原因を引きます。引けなければ**原因未入力**のまま記録され、保全管理の稼働監視タブで後から入れられます。
- 作業指示は、指定があればそれに紐づけます。指定が無ければ、**その設備を割り当てた着手中の作業指示がちょうど1件のときだけ**紐づけます。

受信の様子は、保全管理の稼働監視タブの **設備の今の状態** で見られます。受信が一定時間（既定10分）途絶えると、その設備に「受信が途絶えています」と出ます（ゲートウェイが止まったことに気づけます）。

### サンプルで試す（Docker）

`compose.yaml` には、MQTT ブローカー（Eclipse Mosquitto）と Node-RED のゲートウェイを一緒に起動する `equipment` プロファイルがあります。
Node-RED には、数秒ごとに状態を変える**模擬設備**と、MQTT を受けて MES へまとめて送るフローが入っています（`samples/equipment-gateway`）。

1. リポジトリの直下で `docker compose --profile equipment up -d` を実行します。
2. MES（http://localhost:8080）にサンプルのマスタを取り込みます。`EQ-06` が設備イベントで収集する設備になっています。
3. APIキーを発行し、リポジトリの直下の `.env` に `MES_API_KEY=pfm_...` と書いて、もう一度 `docker compose --profile equipment up -d` を実行します。
4. 稼働監視タブで、`EQ-06` の今の状態と稼働履歴が増えていくのを確かめます。Node-RED（http://localhost:1880）の「結果を見る」ノードに、採用・重複・拒否の件数が出ます。

自分の設備に合わせるときは、Node-RED の「状態を変える」ノードを、PLC から状態を読むノード（OPC UA・MC プロトコル・Modbus など）に置き換えます。
サンプルの MQTT ブローカーは認証も暗号化もしない最小構成です。本番ではパスワードと TLS を設定してください。

## Webhook（MES からの通知）  {#webhooks}

不適合の起票・設備の停止・ロットの保留のように**すぐ知りたい出来事**が起きたとき、MES から登録した URL へ HTTP で知らせる仕組みです。
班長のチャットへの通知や、完成をきっかけにした次の処理に使えます。
定期に取りに来れば足りる相手（ERP の在庫・原価など）は、[連携API](#integration-api) の生産実績の読み出しで足ります。

通知は**薄く**作っています。種類・発生日時・対象の番号と、人が読める1文の要約だけを送ります。
通知は「読みに行くきっかけ」です。正しい今の値は連携API で取りに行ってください（通知が届かなかった・順が入れ替わったときも、それで追いつけます）。

```
MES ──(POST・署名付き)──▶ 受け側（Node-RED・社内のスクリプトなど）──▶ チャット・表示盤・ERP
```

### 送信先を登録する

1. システム管理者が、メニューの「管理」→「Webhook」の **送信先** タブで、用途・送信先の URL・通知するイベントを選んで **登録** を押します。
2. 表示された **署名の秘密の値**（`whsec_` で始まる文字列）を、受け側の設定に写します。**この値は今しか表示できません。** 失くしたら **秘密の値を再発行** を押します（古い値はすぐに使えなくなるので、受け側の設定も直します）。
3. **テスト送信** を押して、受け側に届くか確かめます。結果はその場で出ます（テスト送信は失敗しても送り直しません）。

- URL は `http://` も使えます（工場の中のネットワーク向け）。`http://` の送信先には本文が暗号化されずに流れるので、社外へ送るときは `https://` にしてください。
- 利用者名・パスワードを含む URL は登録できません（一覧と監査ログに URL がそのまま出るため）。受け側は署名で送り手を確かめます。
- 送るのを止めるだけなら、**有効** を外します。削除すると、まだ送っていない通知と送信の記録も消えます。

### 通知するイベント

| 種類 | いつ | 主な項目 |
|---|---|---|
| `production-result.created` | 生産実績を記録した（画面・実績CSV・連携API のどれでも） | 指図番号・作業指示番号・工程順序・品目コード・良品／不良数・産出ロット |
| `production-result.corrected` | 生産実績を訂正した | 訂正した実績の `productionResultId`・訂正前後の数・理由 |
| `work-order.status-changed` | 作業指示の状態が変わった（差立・着手・完了・承認・取消） | 作業指示番号・変更前／後の状態 |
| `manufacturing-order.status-changed` | 製造指図が完了・取消になった | 指図番号・指図区分・変更前／後の状態 |
| `lot.status-changed` | ロットの在庫ステータスが変わった（保留・解除・検査待ちなど） | ロット番号・品目コード・変更前／後・契機・理由 |
| `nonconformance.created` | 不適合を起票した（検査の不合格からの自動起票を含む） | 不適合番号・発生源・ロット番号・品目コード |
| `equipment.stopped` | 設備が停止・故障の区間に入った（手入力・実績CSV・[設備イベント](#equipment-events) のどれでも） | 資産番号・状態・停止原因・アラーム番号 |

区分・状態は英語の名前（`OnHold` など）で送ります（表示言語で変わらないように）。内部のIDは送らず、番号・コードで指します。
要約（`summary`）は日本語です。

### 送る形

```json
{
  "id": "c548bec2-5e89-4210-ba22-2110c0325f09",
  "sequence": 7,
  "type": "equipment.stopped",
  "version": 1,
  "occurredAt": "2026-11-23T11:02:01+00:00",
  "summary": "設備 EQ-01 が「停止」になりました（材料切れ）。",
  "data": { "equipmentAssetNo": "EQ-01", "status": "Stopped", "startedAt": "2026-11-23T10:32:00+00:00", "endedAt": null,
            "stopCause": "材料切れ", "alarmCode": null, "source": "Manual", "workOrderNo": null }
}
```

| 項目・ヘッダ | 内容 |
|---|---|
| `id` | 通知の一意のID。**送り直しても同じ値**（受け側はこれで重複を捨てる） |
| `sequence` | 種類をまたいで増える番号（連番とは限らない） |
| `data` | 種類ごとの中身（上の表）。項目の一覧は [仕様書（OpenAPI）](#integration-api) の `webhooks` 節にある |
| `X-Mes-Event` | 種類（本文の `type` と同じ） |
| `X-Mes-Delivery` | 送信のID（送り直しても同じ） |
| `X-Mes-Timestamp` | 送った時刻（Unix 秒） |
| `X-Mes-Signature` | 署名。`sha256=` に続けて、秘密の値を鍵にした「`X-Mes-Timestamp` の値 `.` 本文」の HMAC-SHA256 を小文字の16進で書いたもの |

### 受け側で確かめること

1. **署名を確かめます。** 受け取った本文のバイト列そのもので計算します。JSON に読み替えてから文字列に戻すと、数値の書き方などが変わって合いません。
2. **時刻のずれを確かめます。** `X-Mes-Timestamp` が5分以上ずれていたら受け付けません（古い要求の送り直しを弾くため）。MES と受け側の時計は NTP で揃えてください。
3. **すぐに 2xx を返します。** MES は応答を既定10秒まで待ち、待ち切れなければ送り直します。重い処理は応答の後に回してください。
4. **同じ `id` の2回目以降は捨てます。** 通知は少なくとも1回届きますが、同じ通知が2回届くことがあります。順序も保証しません。

### 届かなかったとき

- 送信先に繋がらない・待ち切れない・`5xx`・`408`・`429` のときは、間隔を空けて送り直します（1分・5分・30分・2時間・6時間・12時間・24時間。既定で8回まで）。
- それ以外の `4xx` と `3xx` は、送り直しても同じなので1回で止めます（リダイレクトは追いません）。
- 止まった通知は、**送信の記録** タブで受け側を直してから **再送** を押します。通知の `id` は変わらないので、届いていた受け側は重複として捨てられます。
- 送り直しを待っている・止まった通知がある送信先は、**送信先** タブの「送れていない送信」と、システム管理者の **ダッシュボード** の上部に出ます。送信先は自動では止めません。
- 送信の記録は既定30日残します。
- データ保存先の `dataprotection-keys`（秘密の値を暗号化する鍵）を失うと、秘密の値を復号できず、その送信先の通知は止まります。秘密の値を再発行し、受け側の設定を直してから再送してください（[バックアップ](database.html#backup) で一緒に退避します）。

### サンプルで試す

- **Node-RED（Docker）**：`equipment` プロファイルの Node-RED（[設備イベントのサンプル](#equipment-events)）に、通知を受けて署名を確かめ、チャット（Slack・Teams）の形にして転送する「Webhook の受け側」タブがあります。
  1. `docker compose --profile equipment up -d` で起動し、送信先に `http://node-red:1880/mes-webhook` を登録します。
  2. 秘密の値をリポジトリの直下の `.env` に `MES_WEBHOOK_SECRET=whsec_...` と書き、もう一度 `docker compose --profile equipment up -d` を実行します。
  3. **テスト送信** を押すと、Node-RED（http://localhost:1880）のデバッグ欄に「受けた通知」と「チャットに届いた形」が出ます。
     転送先の既定はフローの中の模擬で、実際のチャットには送りません。送るときは `.env` に `CHAT_WEBHOOK_URL`（Slack の Incoming Webhook、または Teams のワークフローの「Webhook 要求を受信したとき」の URL）と、Teams なら `CHAT_KIND=teams` を書きます。
- **スクリプト**：`samples/webhook` に Python（`receive_mes_webhook.py`）と PowerShell（`Receive-MesWebhook.ps1`）の最小の受け側があります。処理を書き足す場所は README を見てください。

### 版を上げても受け側が動き続けるための約束

- 本文の項目は消しません。名前と型も変えません。値が必ずあった項目を、値の無いことがある項目にはしません。
- 項目・種類・区分の値は増えることがあります。受け側は知らない項目を読み飛ばすようにしてください。
- 形を変える必要があるときは、別の種類（v2）として足します。
