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
| WHK-01 送信先の登録と Outbox の土台 | 完了（2026-11-23。Spec.md 改訂159） |
| WHK-02 送信の常駐処理・署名・再送 | 完了（2026-11-23。Spec.md 改訂160） |
| WHK-03 v1 のイベントの発行と約束の固定 | 完了（2026-11-23。Spec.md 改訂161） |
| WHK-04 受け側の見本と利用者ガイド | 完了（2026-11-23。Spec.md 改訂162） |

**バックログは全件完了**（2026-11-23）。続きの候補は「対象外」の節（運用ログの期限切れ削除、英語の要約、製造指図の途中の状態、種類の中の絞り込み）。

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

**実施メモ（2026-11-23）**：変更は24ファイル＋マイグレーション9（想定約20。想定外は `appsettings.json`（Data Protection の鍵の置き場所）・`ApiFactory.cs`（テストの鍵を一時ディレクトリへ）・`_Imports.razor`（タブのフォルダの名前空間）・種類の一覧 `Core/Constants/WebhookEventTypes.cs`。`MesRoleGroups.cs` は既存の `UserAdmin` で足りたので変えていない）。
- **再送の時刻・取り出しの時刻は Unix 秒（`NextAttemptAtUnixSeconds`・`LockedAtUnixSeconds`）で持つ**。SQLite は DateTimeOffset を SQL で比べられないため（`IntegrationRequest.ExpiresAtUnixSeconds` と同じ手）。WHK-02 の取り出しは（`State`, `NextAttemptAtUnixSeconds`）の索引で引ける
- **Data Protection の鍵をデータディレクトリの `dataprotection-keys` に永続化した**（`DataProtection:KeysDirectory`）。既定の置き場所は Docker では再起動で消え、秘密が復号できなくなる。DB と一緒に退避が要ることを Spec.md 7.7 に書いた。鍵を失ったときの扱い（復号できない送信先を失敗にして再発行を促す）は WHK-02 で決める
- 送信待ちには本文の `data`（`Payload`）と `Summary` だけを置き、共通の包み（`id`・`sequence`・`type`・`version`・`occurredAt`）は送るときに付ける（`sequence`＝送信待ちの ID は保存するまで決まらないため）。`data` は camelCase・列挙は名前
- 種類の一覧は `Core/Constants/WebhookEventTypes.cs` に置き、API の入力検査と画面の選択肢で同じものを使う。購読の保存はカンマ区切りで、**知らない種類も読み捨てない**（版を戻したときに購読を失わないため。画面では名前のまま出す）
- 送信先の削除は送信待ち・送信の記録を連鎖して消す（運用ログ。止めるだけなら無効）。送信待ち（`WebhookOutbox`）の行自体は残る（他の送信先の分があり得る）。保持期間による削除は運用ログの削除をまとめて作るときに入れる
- データモデルは 5章ではなく 3.8節に書いた（APIキー・`IntegrationRequest` と同じく、共通基盤のエンティティは 5章に載せていない）。テストは管理系の `AuthTests`（APIキーと同じ場所）に置いた
- 画面は独立画面 `/webhooks`（ナビはシステム管理者の欄。APIキーの次）をタブの形で作り、今は「送信先」だけ。WHK-02 で「送信の記録」タブを足す
- SQLite で全件、SQL Server・PostgreSQL で `AuthTests` の Webhook と `DatabaseProviderTests` を確認した

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

**実施メモ（2026-11-23）**：変更は21ファイル（想定約14。想定外は、WHK-01 で作った `WebhookSubscriptionsController.cs`・`SubscriptionsTab.razor`・`WebhooksPage.razor`（テスト送信・送れていない件数・タブの追加）、`ApiFactory.cs`（常駐処理を止める）、`Pages/Home.razor`（知らせを置く）、`WebhookOptions.cs` を分けた1件。書き漏れで、設計のずれではない。ダッシュボードの部品は `Pages/Webhooks/WebhookFailureNotice.razor` にした）。
- **取り出しは行ロックではなく条件付きの更新**（`ExecuteUpdate` で「送信待ち・未取り出し（または古い）」の行に取り出し時刻を書き、更新できた1件だけを送る）。EQE-02 の行ロックより単純で、3プロバイダーで同じに動く。4つの処理で6件を同時に取り合うテストが3プロバイダーで通る
- 送信中に落ちたときは、取り出しから `LockTimeoutSeconds`（300秒）後に送り直す。受け側にとっては同じ通知がもう一度届くことがある（少なくとも1回の範囲。`X-Mes-Delivery` と `id` は同じ）
- **テスト送信は再送しない**ことにした（カードに無かった判断）。結果はその場で画面に返しているので、失敗したテスト送信が裏で最大8回送り直され、送信先の一覧で「失敗中」と出続けるほうが紛らわしい。失敗したテスト送信は「停止」として記録に残る
- **3xx は再送しない失敗**にした（リダイレクトを追わないため。本文と署名を知らない場所へ転送させない）。HttpClient の待ち時間は無限にし、送信ごとに `TimeoutSeconds` で切る（設定を変えたときに HttpClient を作り直さなくてよい）
- 秘密を復号できない送信（WHK-01 の実施メモの宿題）は、再送せずに止めて記録に「再発行してから再送」と残す。エラーの文は保存される値なので訳さない（画面でも送ったときのまま出す）
- 送信の状態はダッシュボードの「失敗中」の判定に、再送待ち（送信待ちで試行回数が1以上）と停止の両方を使う。まだ一度も送っていないだけの送信待ちは数えない（常駐処理の間隔の間は必ず出るため）
- 業務の API が送信先の影響を受けないことは、作りで担保している（業務の処理は Outbox に積むだけで、HTTP は常駐処理だけが呼ぶ）。専用のテストは書いていない
- SQLite で全件、SQL Server・PostgreSQL で `AuthTests` の Webhook と `DatabaseProviderTests` を確認した

### WHK-03 v1 のイベントの発行と約束の固定

**目的**：「v1 のイベント」の表の出来事が起きると通知が積まれ、その形が約束として固定される。

- 本文の共通の形：`{ id, sequence, type, version, occurredAt, summary, data: {…} }`。`summary` は人が読める1文（**サーバーの既定の言語**で作る。受け側の言語は分からないため。着手時に `ApiText` で作るか日本語固定かを決める）
- 表の各サービスで `Enqueue` を呼ぶ（**状態を変える唯一の経路**で呼ぶので、画面・CSV・連携API・設備イベントのどこから来ても出る）
- `data` の型は `Core/Contracts/Integration/WebhookPayloads.cs` の record。種類と項目の一覧を `tests/MesApp.Api.Tests/WebhookContract.v1.json` に固定するテスト（`MESAPP_UPDATE_WEBHOOK_CONTRACT=1` で更新。消した・型を変えたら落ちる）
- OpenAPI の `webhooks` 節に載せる（難しければガイドの表だけにして、このファイルに理由を書く）

**変更ファイルの想定（約16）**：`WebhookPayloads.cs`、発行を足すサービス（`ShopFloorReportService`・実績訂正のサービス・`WorkOrderStatusService`・`ManufacturingOrderService`・`LotStatusService`・`NonconformanceService`・`EquipmentEventService`。約7）、OpenAPI の変換処理、`WebhookContract.v1.json`、テスト（`ProductionTests`・`QualityTests`・`MaintenanceTests` に発行の確認）、`Spec.md`、`CodeMap.md`、このファイル
**受入基準**：表の出来事ごとに1件積まれる（画面・CSV・連携API の経路の違いで件数が変わらない）／業務が失敗・巻き戻れば積まれない／約束のテストが通り、項目を消すと落ちる／SQL Server・PostgreSQL で発行のテストが通る

**実施メモ（2026-11-23）**：変更は26ファイル（想定約16。多かったのは、ロット・作業指示の状態変更を非同期にしたことで呼び出し側7ファイル（`ReceivingController`・`InspectionService`・`LotOperationService`・`ManufacturingOrderService`・`NonconformanceService`・`WorkOrderDispatchService`・`WorkOrderExecutionService`）に手が入ったこと、約束の書き出しを `WebhookContract.cs` に分けたこと、WHK-01・02 のテスト（匿名型の data を約束の型に直した）・`TestAuth.cs`（補助）・`CLAUDE.md`（約束の規則）。設計のずれではないが、非同期化は着手前に読めていなかった）。業務No は MES.md で確かめた（B-40-10-01 出来高報告・B-70-30-01 製造履歴情報変更・D-10-30-08 在庫ステータス変更・C-30-10-01 逸脱/品質不具合管理・B-40-20-02 設備停止時間・原因報告・E-20-10-03 設備稼働モニタリング）。
- **`LotStatusService.ChangeStatus`・`WorkOrderStatusService.ChangeStatus` を `ChangeStatusAsync` にした**（14か所の呼び出し）。本文にロット・作業指示のコードを引く問い合わせが要り、発行は購読の有無を DB に聞く非同期の処理のため。同期の問い合わせや、購読の一覧を常駐の記憶に置く案は採らなかった（後者は、送信先を消した直後に古い一覧で送信を作ると外部キーで業務の保存が失敗する）
- **生産実績の記録の通知に実績の ID を入れなかった**（カードの表では「実績の id」）。ID は保存するまで決まらず、入れるには保存を2回に分けて明示のトランザクションで包む必要がある。実績CSV は全体を1つのトランザクションで取り込むので入れ子になる。通知は「読みに行くきっかけ」なので、受け側はフィードを `afterId` の続きから読めば足りる。訂正の通知は、訂正した実績の ID（既に保存済み）を `productionResultId` で送る
- 本文の data を作る問い合わせは、購読している送信先があるときだけ行う（`EnqueueAsync` に本文を作る関数を渡す形を足した）。Webhook を使っていない工場で業務の処理を重くしないため
- data は `WebhookPayloads.DataTypes` の型でなければ発行が例外にする（約束の型以外を送らない）。購読できる種類の一覧とこの表がずれていないこともテストで確かめる
- **要約は日本語で固定**にした（カードの「着手時に決める」）。保存して送る値で、`ApiText` で包むと操作した人の表示言語で変わってしまう。区分は `EnumLabels.OriginalOf`（原文）。英語の要約が要るなら、送信先ごとの言語の設定として後から足す（対象外に追記）
- **製造指図は完了・取消だけ**を送る（表のとおり）。種類の名前は `status-changed` なので、途中の変化（承認・発行）を後から足せる。ただし足すと購読している受け側に届く件数が増えるので、足すときは v1 の約束の変更として扱う
- OpenAPI の `webhooks` 節は、画面用の JSON の設定（列挙は数値）から型を作らず、基準ファイルと同じ書き出し（`WebhookContract`）から自前で組んだ。Webhook の本文は列挙を名前で送るため、既定の生成では形がずれる
- 検査の不合格からの不適合の自動起票は、`NonconformanceService.CreatedNotice` を共用して発行する（専用のテストは書いていない。手での起票のテストで本文の形を確かめている）
- 実績CSV からの記録・設備イベントの停止も、単票と同じサービスを通るので同じに出る（設備イベントはテストで確かめた。実績CSV は経路の共有で担保）
- SQLite で全件、SQL Server・PostgreSQL で `AuthTests` の Webhook と `ExecutionTests`・`QualityTests`・`MaintenanceTests`・`InventoryTests`・`ProductionTests`・`DatabaseProviderTests` を確認した

### WHK-04 受け側の見本と利用者ガイド

**目的**：利用者が自分で受け側を作れ、チャットへの転送をすぐ試せる。

- 同梱の Node-RED（EQE-05 の compose プロファイル）に「署名を確かめて Teams／Slack の Incoming Webhook へ転送する」フローの見本を足す
- 受け側の最小の見本（署名の検算）を PowerShell と Python で `samples/webhook/` に置く
- 利用者ガイド（日英。`docs/` と `docs/en/`）：送信先の登録・秘密・署名の確かめ方・再送の規則・重複の捨て方・フィードとの併用（「通知はきっかけ、正しい値はフィード」）・イベントの表
- 実際に Docker で受けて、ガイドの例を全部動かして確かめる（**Docker Desktop は普段止まっているので、確認の前に利用者に起動を頼む**）

**変更ファイルの想定（約10）**：Node-RED のフロー・compose、`samples/webhook/`（2〜3）、ガイド日英（各1〜2）、`assets` の画面写真（要れば）、`Spec.md`（改訂履歴）、このファイル
**受入基準**：見本のフローで Teams／Slack 形式の要求が出る（実際のチャットには送らず、受け側の模擬で確かめる）／ガイドの例が新しい DB で動く／`./scripts/GuideEnCheck.ps1 -Strict` が通る

**実施メモ（2026-11-23）**：変更は15ファイル（想定約10。想定外は `samples/equipment-gateway/settings.js`（下の理由）と同じフォルダの README、WHK-02 の直し（`WebhookSubscriptionsController.cs`・`AuthTests.cs`）。`Readme.md` は Docker の起動の行に一言足した）。
- **Docker で確かめた**。今のソースから作ったイメージを別のプロジェクト名・別のポート（18080・11880・11883）で起動し、API で初期化→送信先を2つ登録（正しい秘密と、わざと違う秘密）→ `MES_WEBHOOK_SECRET` を渡して Node-RED を起動し直した。テスト送信は正しい秘密で 200・転送まで届き、違う秘密は 401 で止まった。設備の停止を手入力すると、常駐処理が数秒で送り、Node-RED のデバッグ欄に本文と Slack 形式の転送が出た。`CHAT_KIND=teams` で Adaptive Card の形になった。設備イベントのゲートウェイ（`EQ-06`）も動き続け、イベントの停止がそのまま Webhook で戻ってくる一周を確かめた。Python の受け側も Docker の MES（`host.docker.internal`）から受けられた。PowerShell の受け側は MES と同じ PC でしか受けられない（`localhost` だけで待つ）ので、Python の `hmac` で独立に署名した要求で確かめた（正しい→200、同じ id→200 で捨てる、書き換え・古い時刻・署名なし→401。Python の受け側も同じ）
- **Node-RED は既定のままでは署名を確かめられなかった**。画面（エディタ）が `/` にあり、その側の JSON の読み替えがすべての要求に先に掛かって、元のバイト列が残らない。HTTP 受信ノードの口ごとの `httpNodeMiddleware` は読み替えの後に呼ばれる。画面の側の `httpAdminMiddleware` は読み替えの前に呼ばれるので、そこで `/mes-webhook` の本文を文字列のまま残し、読み終えた印（`req._body`）を立てた（画面を別の場所に移したときのために `httpNodeMiddleware` にも同じものを掛け、2回目は何もしない）。最初は口ごとの側だけに掛けて、読み終えた流れの終わりを待ち続けて10秒で切れた
- 読み替えた後の JSON から本文を作り直す案は採らなかった。数量は DB の桁で `10.0000` のように送られ、JavaScript で作り直すと `10` になって署名が合わない
- **失敗したテスト送信を「送れていない送信」に数えないようにした**（WHK-02 の直し）。確かめている途中、受け側の準備ができる前に押したテスト送信の失敗が「停止」として残り、ダッシュボードの「失敗中」が消えなくなった。テスト送信は再送せず、手で再送する意味も無いため
- サンプルの Node-RED に自前の `settings.js` を渡すようにした（`command: ["--settings", "/gateway/settings.js"]`）。書いていない項目は Node-RED の既定のままで、設備イベントのゲートウェイの動きは変わらない（確かめた）
- 英語版のガイドの本文の例は、要約（日本語で固定）を JSON の外に出して「訳さない」の印を付けた（コードの中には印を書けない）
- Jekyll でガイドを作り、`#webhooks`・`#backup` の見出しの印ができることを確かめた。確認用のコンテナ・ボリューム・イメージと、秘密の値を置いたファイルは消した

## アプリでの確認（2026-11-23。WHK-01〜04 を通して）

新しい DB でアプリを起動し、Python の受け側（`samples/webhook`）を `localhost:9000` で動かして、画面で確かめた（日本語・英語、1280px とスマホ幅）。

- 確かめたこと：空の登録のエラー／http の注意／登録後の秘密の値の表示と「写したので閉じる」／一覧（イベント・秘密の値の先頭・送れていない件数）／テスト送信の成功（受け側に届く）と失敗（再発行で古い値になった受け側が 401）／変更（現在の値が入り、保存で一覧が変わる）／秘密の再発行と削除の二段階の確認／送信の記録の絞り込み（送信先・状態）と、止まった送信の再送／繋がらない送信先が「失敗中」になり、ダッシュボードの上部に知らせが出る／失敗したテスト送信が件数に入らない
- 直したこと
  - 送信の記録の「結果」とテスト送信の失敗の知らせが「HTTP 401 HTTP 401 Unauthorized」と状態コードを2回出していた → エラーの文の先頭に入っているので重ねない
  - ダッシュボードの知らせのリンクが「送信の記録を見る」なのに送信先のタブが開いた → 「送信先を見る」にした（URL でタブを選ぶ仕組みはこのアプリに無いので足さない。送信先のタブに失敗中と件数が出る）
  - 英語で送信の記録の列見出しと絞り込みが「Destinations」（タブ名と同じキー）だった → 別のキー「送信先の用途」（Destination）にした
- 直さなかったこと
  - 表の行のボタン（テスト送信など）の結果の知らせはタブの上に出るので、一覧を下まで送っていると見えない。APIキーなど他の画面と同じ作りなので揃えたままにした
  - スマホ幅で左のメニューが常に出て本文が狭くなるのは、全画面に共通の既存の作り（APIキーの画面と同じ幅の数値だった）
  - 繋がらない送信先（接続を拒否される）への送信は Windows では1件に2秒ほどかかり、常駐処理は1件ずつ送るので、他の送信先への通知もその分遅れる。待ち時間の上限（10秒）の範囲で、要望が出たら送信先ごとに並べて送る

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
- 要約（`summary`）の英語版（WHK-03 で日本語固定にした。要るなら送信先ごとの言語の設定として足す）
- 製造指図の途中の状態の変化（承認・発行）の通知（WHK-03 で完了・取消だけにした）
- 受け側の見本の重複の記憶をメモリの外に置くこと（Node-RED の `contextStorage`・スクリプトのファイル保存。README に書くだけにした）
