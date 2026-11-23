# Webhook（MES から外への通知） バックログ

MES で業務の出来事（実績の記録・訂正、作業指示・ロットの状態変化、不適合の発生、設備の停止など）が起きたとき、
登録した外部の URL へ HTTP で知らせる。連携の方向④（①フォルダ連携 → ②連携API → ③設備イベントの受信 → ④Webhook）。
連携API（docs-dev/IntegrationApiTasks.md、改訂150〜153）・設備イベント（docs-dev/EquipmentEventTasks.md、改訂154〜158）の続き。

1カード＝1セッションで進める（`Orchestration.md` §4.1）。カードの間はセッションをクリアし、次へ進む前に前のカードをコミットする。
新しいセッションでは「`docs-dev/WebhookTasks.md` の WHK-0x を実行して」とだけ頼めば着手できるように書いてある。
**着手時にカードの「変更ファイルの想定」を確かめ、完了時に実際の件数と突き合わせる**（DoD 5）。
EQE・INT では想定が毎回少なすぎた（CSV が3ファイルに分かれる・訳の resx・CodeMap・サンプル）。今回の想定はそれを見込んで書いてある。

## 背景

- 外へ渡す口は今、**取りに来てもらう**ものだけ：フォルダ連携の `outbox`、連携API の生産実績フィード（`production-results`・`production-result-corrections`。ID の昇順で `afterId` から読む。改訂152）。
  ERP の在庫・原価のように定期に取りに来れば足りる相手はこれで済む（INT-03 で Webhook を持たずに済むと書いた理由）。
- 足りないのは**すぐ知りたい**相手：不適合・設備故障・ロット保留を班長のチャットやアンドン盤に出す、完成をきっかけに次の工場・倉庫の処理を始める、など。
  定期の取得では「間隔を縮めるほど無駄な問い合わせが増える」。
- 状態変化はサービスに集まっている（`LotStatusService`・`WorkOrderStatusService`・`ManufacturingOrderService`・`InspectionService`・`NonconformanceService`・`ShopFloorReportService`・`EquipmentEventService`）。発行の差し込み口はここ。
- 常駐処理の見本は `Api/Services/FolderLink/FolderLinkWorker.cs`（`BackgroundService` ＋ `PeriodicTimer`）。
- 配布は工場 LAN 内の自前設置。利用者が版を上げるペースを決めるので**後方互換が最優先**。Webhook の中身（イベントの種類と項目）は CSV の列と同じく**外部との約束**になる。

## 進み具合

| カード | 状態 |
|:--|:--|
| WHK-01 送信先の登録と Outbox の土台 | 未着手 |
| WHK-02 送信の常駐処理・署名・再送 | 未着手 |
| WHK-03 v1 のイベントの発行と約束の固定 | 未着手 |
| WHK-04 受け側の見本と利用者ガイド | 未着手 |

## 決定事項（2026-11-23 確定。推奨案のまま）

| 論点 | 案 | 理由 |
|:--|:--|:--|
| 中身の厚さ | **薄い通知**：種類・発生日時・対象の番号（作業指示番号・ロット番号など）と、表示に要る最小限の項目だけ。実績の全項目は送らず、必要な側は連携API のフィードで取りに行く | 約束にする項目が少ないほど後方互換を守りやすい。届かなかった・順が入れ替わった通知も、フィードを読めば正しい今の値に追いつける（通知は「読みに行くきっかけ」） |
| 届け方の保証 | **少なくとも1回**（at-least-once）。順序は保証しない。各通知に一意の `id` と、種類をまたいで増える `sequence` を付け、受け側が重複を捨てる | 「ちょうど1回」は HTTP では作れない。受け側の重複排除を前提にするのが一般的 |
| 取りこぼし防止 | **Outbox**：業務の変更と**同じトランザクション**で送信待ちの行（`WebhookOutbox`）を足し、常駐処理が後から送る。業務の処理の中で HTTP を呼ばない | 送信先が落ちていても業務は止まらない。業務だけ保存されて通知が消える・通知だけ出て業務が巻き戻る、のどちらも起きない |
| 発行の場所 | 状態変化を集めたサービスで**明示的に** `IWebhookPublisher.Enqueue(...)` を呼ぶ。EF の `SaveChanges` の傍受（エンティティの変更から自動で作る）はしない | 傍受だとスキーマの変更がそのまま外部の約束を変えてしまう。CLAUDE.md の「状態は Service 経由で変える」と同じ場所に置ける |
| 再送 | 失敗（接続不可・タイムアウト・2xx 以外）は指数的に間隔を空けて再送（例：1分・5分・30分・2時間・6時間・12時間…最大24時間・既定8回）。尽きたら「失敗」で止め、画面から手で再送できる。**4xx（408・429 以外）は再送しない** | 受け側の不具合で同じ要求を送り続けない。429 は混雑なので待って再送 |
| 送信先の停止 | 失敗が続いても**自動で無効にしない**。稼働監視（ダッシュボード）と送信先の一覧に「失敗中」を出す | 自動停止は気付かれないまま通知が止まる。工場 LAN の送信先は相手の保守で一時的に落ちることが多い |
| 改ざん・なりすまし防止 | 送信先ごとに**秘密の値**を発行し（作成時に1回だけ表示。保存はハッシュでなく復号可能な形——署名に要るため。Data Protection で暗号化）、本文の **HMAC-SHA256** を `X-Mes-Signature`、時刻を `X-Mes-Timestamp` に付ける。受け側は時刻のずれ（例：5分）で再生攻撃を弾く | 受け側が「本当にこの MES からか」を確かめられる。方式は Stripe・GitHub と同じ形なので受け側の実装例が多い |
| 送信先の URL | http・https のどちらも許す（工場 LAN 内は http が多い）。http のときは画面で注意を出す。私設アドレスも許す（LAN 内に送るのが目的） | クラウド SaaS と違い、送信先は利用者自身の LAN。SSRF の心配は「送信先を登録できる人」を絞って抑える |
| 登録できる人 | **システム管理者だけ**（`MesRoleGroups` に既存の管理者の組があればそれを使う） | 送信先は業務データの持ち出し口になる |
| 購読の単位 | 送信先ごとに**イベントの種類を選ぶ**（複数可）。種類の中の絞り込み（工程・設備ごと等）は v1 では持たない | 絞り込みは受け側か中継（Node-RED）でできる。足すときは任意の条件として後から追加できる |
| 種類の名前と版 | `production-result.created` のように「対象.出来事」。本文に `version: 1`。項目は**消さない・意味を変えない・足すのは任意の項目だけ**（CSV の列の約束と同じ）。項目の一覧を基準ファイル `WebhookContract.v1.json` に固定するテストを置く | CSV で効いた仕組みをそのまま使う |
| v1 の種類 | 下の「v1 のイベント」の表 | 「すぐ知りたい」ものに絞る |
| チャット（Teams・Slack・LINE WORKS 等） | 本体は**汎用の JSON だけ**を送る。チャット向けの形への変換は同梱の Node-RED（EQE-05 の compose プロファイル）の見本で行う。本文に人が読める1文 `summary` は含める | チャットごとの形は頻繁に変わり、本体に入れると版上げを強いる。EQE で「メーカーごとの差はゲートウェイが吸収する」と決めたのと同じ考え |
| 送信の記録 | 送信の試行ごとに結果（状態コード・所要時間・エラーの先頭）を残す。**7.6節の運用ログ（C 層）**で保持は既定30日（`Retention:WebhookDeliveryDays`）。本文は送り終えた後も再送用に保持期間まで残す | 「届いていない」と言われたときに調べられる |
| 監査ログ | 送信先の作成・変更・削除・秘密の再発行・手での再送を記録する。送信そのものは記録しない（送信の記録に残る） | 量が多すぎる |
| 複数台で動かすとき | 送信待ちの行に「取り出した時刻」を持たせ、取り出しを行の更新で直列にする（EQE-02 の行ロックと同じ手） | 今は1台構成だが、Docker で2台にしたときに二重送信が増えないように |
| OpenAPI | 連携API の文書（`integration-v1`）の **`webhooks` 節**に種類ごとの本文の形を載せる（OpenAPI 3.1） | 受け側の開発者が型を自動生成できる。載せるのが難しければガイドの表だけにする（WHK-03 で判断） |

### v1 のイベント

| 種類 | いつ | 主な項目 | 業務No（着手時に MES.md を grep して確定） |
|:--|:--|:--|:--|
| `production-result.created` | 生産実績を記録した（画面・CSV・連携API） | 実績の `id`（フィードの位置）・作業指示番号・工程順序・品目コード・良品／不良数・産出ロット番号 | B-40-10 |
| `production-result.corrected` | 生産実績を訂正した | 訂正の `id`・元の実績の `id`・理由 | B-70-30-01 |
| `work-order.status-changed` | 作業指示の状態が変わった | 作業指示番号・工程順序・変更前／後の状態 | B-20 系 |
| `manufacturing-order.status-changed` | 製造指図が完了・取消になった | 指図番号・変更前／後の状態 | A-30 系 |
| `lot.status-changed` | ロットの状態が変わった（保留・解除・廃棄など） | ロット番号・品目コード・変更前／後の状態・理由 | C・H 系 |
| `nonconformance.created` | 不適合を起票した | 不適合番号・品目コード・ロット番号・区分 | C-30 系 |
| `equipment.stopped` | 設備が停止・故障の区間に入った（手入力・CSV・設備イベント） | 設備の資産番号・状態・停止原因（未入力なら空）・アラーム番号 | E-20-10-03 |

状態・区分は連携API と同じく**列挙の名前（英語）**で送る（表示言語で変わらないように）。内部ID はフィードの位置（`id`）にしか使わない。

## カード

### WHK-01 送信先の登録と Outbox の土台

**目的**：送信先を登録でき、業務の変更と同じトランザクションで送信待ちを積めるようにする（まだ送らない）。

- エンティティ（`Core/Entities/Integration.cs`。無ければ新設）：
  - `WebhookSubscription`：`Name`・`Url`・`EventTypes`（文字列の一覧）・`SecretProtected`・`IsActive`・`CreatedAt` 等
  - `WebhookOutbox`：`Id`（＝`sequence`）・`EventId`（GUID）・`EventType`・`OccurredAt`・`Payload`（JSON）
  - `WebhookDelivery`：`OutboxId`・`SubscriptionId`・`State`（待ち／送信済み／失敗）・`Attempts`・`NextAttemptAt`・`LockedAt`・`LastStatusCode`・`LastError`・`DeliveredAt`
- `IWebhookPublisher.Enqueue(eventType, payload)`：`DbContext` に行を足すだけ（`SaveChanges` は呼び出し側）。購読中の送信先ぶん `WebhookDelivery` を作る
- 送信先の管理 API `api/webhook-subscriptions`（システム管理者）と、秘密の再発行。画面はシステム設定（またはマスタ）にタブを足す（置き場所は着手時に CodeMap で決める）
- 7.6節の保持の表に `WebhookOutbox`・`WebhookDelivery` を足す（運用ログ。既定30日）

**変更ファイルの想定（約20＋マイグレーション9）**：エンティティ、`MesAppDbContext.cs`、`Configurations/`（新設か既存に追記）、マイグレーション3プロバイダー（`scripts/Migrations.ps1 -Add AddWebhooks`）、DTO（`Core/Contracts/Integration/WebhookDtos.cs`）、`WebhookSubscriptionsController.cs`、`Api/Services/Webhooks/WebhookPublisher.cs`、秘密の暗号化（Data Protection の登録）、`MesAppHost.cs`（DI）、`MesRoleGroups.cs`（要れば）、画面のタブと親ページの登録、`NavMenu.razor`（要れば）、`UiText.en.resx`・`ApiText.en.resx`、`EnumLabels.cs`・`CoreText.en.resx`（送信の状態）、テスト（連携の既存クラス。無ければ `MasterTests` に）、`Spec.md`（5章・7.6・6.1・改訂履歴）、`CodeMap.md`、このファイル
**受入基準**：管理者だけが送信先を登録・変更・削除できる／秘密は作成・再発行の応答でだけ見え、一覧・詳細では見えない／`Enqueue` した業務が巻き戻ると送信待ちも残らない／購読していない種類では送信待ちが作られない／SQL Server・PostgreSQL で同じテストが通る

### WHK-02 送信の常駐処理・署名・再送

**目的**：送信待ちが実際に届き、落ちていた送信先にも後から届く。

- `Api/Services/Webhooks/WebhookDispatchWorker.cs`（`FolderLinkWorker` と同じ形）と `WebhookDispatcher.cs`。`IHttpClientFactory` の名前付きクライアント（タイムアウトは設定値。既定10秒）
- 取り出しは `NextAttemptAt <= 今` かつ（未ロック または ロックが古い）を、行の更新で直列にして取る
- ヘッダ：`X-Mes-Event`・`X-Mes-Delivery`（`WebhookDelivery.Id`）・`X-Mes-Timestamp`・`X-Mes-Signature: sha256=<hex>`（`timestamp + "." + body` の HMAC）
- 再送の間隔・回数・再送しない状態コードは決定事項のとおり。設定 `Webhooks:MaxAttempts`・`Webhooks:TimeoutSeconds`・`Webhooks:PollIntervalSeconds`
- 送信の記録タブ（送信先ごと・状態で絞り込み、行から手で再送）、送信先の「テスト送信」（`webhook.ping`）
- ダッシュボード（稼働監視）に「失敗中の送信先」を出す（置き場所は着手時に決める）
- テストは受け側をテストの中に立てる（`WebApplicationFactory` の中で `HttpMessageHandler` を差し替える。実ネットワークに出ない）

**変更ファイルの想定（約14）**：Worker・Dispatcher・署名の計算（`WebhookSignature.cs`）、`MesAppHost.cs`、`appsettings.json`、送信の記録の Controller（または WHK-01 の Controller に追記）・DTO、画面のタブ、ダッシュボードの部品、`UiText.en.resx`・`ApiText.en.resx`、テスト、`Spec.md`（3.8・7.6・改訂履歴）、`CodeMap.md`、このファイル
**受入基準**：届いた本文の署名を秘密で検算できる／500 を返す送信先には間隔を空けて再送し、戻れば届く／400 では再送しない／回数が尽きたら失敗になり、手で再送すると届く／同じ送信待ちを2つの処理が同時に取り出しても送るのは1回／テスト送信が届く／業務の API は送信先が落ちていても遅くならない

### WHK-03 v1 のイベントの発行と約束の固定

**目的**：「v1 のイベント」の表の出来事が起きると通知が積まれ、その形が約束として固定される。

- 本文の共通の形：`{ id, sequence, type, version, occurredAt, summary, data: {…} }`。`summary` は人が読める1文（**サーバーの既定の言語**で作る。受け側の言語は分からないため。着手時に `ApiText` で作るか日本語固定かを決める）
- 表の各サービスで `Enqueue` を呼ぶ（**状態を変える唯一の経路**で呼ぶので、画面・CSV・連携API・設備イベントのどこから来ても出る）
- `data` の型は `Core/Contracts/Integration/WebhookPayloads.cs` の record。種類と項目の一覧を `tests/MesApp.Api.Tests/WebhookContract.v1.json` に固定するテスト（`MESAPP_UPDATE_WEBHOOK_CONTRACT=1` で更新。消した・型を変えたら落ちる）
- OpenAPI の `webhooks` 節に載せる（難しければガイドの表だけにして、このファイルに理由を書く）

**変更ファイルの想定（約16）**：`WebhookPayloads.cs`、発行を足すサービス（`ShopFloorReportService`・実績訂正のサービス・`WorkOrderStatusService`・`ManufacturingOrderService`・`LotStatusService`・`NonconformanceService`・`EquipmentEventService`。約7）、OpenAPI の変換処理、`WebhookContract.v1.json`、テスト（`ProductionTests`・`QualityTests`・`MaintenanceTests` に発行の確認）、`Spec.md`、`CodeMap.md`、このファイル
**受入基準**：表の出来事ごとに1件積まれる（画面・CSV・連携API の経路の違いで件数が変わらない）／業務が失敗・巻き戻れば積まれない／約束のテストが通り、項目を消すと落ちる／SQL Server・PostgreSQL で発行のテストが通る

### WHK-04 受け側の見本と利用者ガイド

**目的**：利用者が自分で受け側を作れ、チャットへの転送をすぐ試せる。

- 同梱の Node-RED（EQE-05 の compose プロファイル）に「署名を確かめて Teams／Slack の Incoming Webhook へ転送する」フローの見本を足す
- 受け側の最小の見本（署名の検算）を PowerShell と Python で `samples/webhook/` に置く
- 利用者ガイド（日英。`docs/` と `docs/en/`）：送信先の登録・秘密・署名の確かめ方・再送の規則・重複の捨て方・フィードとの併用（「通知はきっかけ、正しい値はフィード」）・イベントの表
- 実際に Docker で受けて、ガイドの例を全部動かして確かめる（**Docker Desktop は普段止まっているので、確認の前に利用者に起動を頼む**）

**変更ファイルの想定（約10）**：Node-RED のフロー・compose、`samples/webhook/`（2〜3）、ガイド日英（各1〜2）、`assets` の画面写真（要れば）、`Spec.md`（改訂履歴）、このファイル
**受入基準**：見本のフローで Teams／Slack 形式の要求が出る（実際のチャットには送らず、受け側の模擬で確かめる）／ガイドの例が新しい DB で動く／`./scripts/GuideEnCheck.ps1 -Strict` が通る

## 順序と依存

WHK-01 → WHK-02 → WHK-03 → WHK-04。
WHK-02 と WHK-03 は WHK-01 だけに依存するので入れ替えてよい（WHK-03 を先にすると、送れないまま積まれる行を確かめやすい）。
WHK-04 は全部が揃ってから（ガイドの例を一度で確かめるため）。

## 対象外（このバックログでは作らない）

- メール送信（SMTP）。要るなら Node-RED で受けて送る
- 種類の中の絞り込み（工程・設備・品目ごと）→ 要望が出たら購読の任意の条件として足す
- 送信先ごとの本文の形の変更（テンプレート）→ 中継で変換する
- 受け側から MES への応答による処理（返事で業務を進める）
- 運用ログ（C 層）の期限切れ削除そのもの（EQE-01 の実施メモのとおり未実装。作るときに Webhook の2表も対象に入れる）
