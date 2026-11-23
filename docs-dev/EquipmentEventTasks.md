# 設備イベントの受信（PLC・ゲートウェイ） バックログ

設備の状態変化（稼働・停止・段取り・故障・アイドル）を、ゲートウェイから連携API（`api/integration/v1`）で受け、
MES の設備稼働履歴（`EquipmentLog`）の区間に組み立てる（B-40-20-01・B-40-20-02 の「自動」、E-20-10-01 設備稼働情報収集、E-20-10-03 設備稼働モニタリング）。
連携API（docs-dev/IntegrationApiTasks.md、改訂150〜153）の続き。

1カード＝1セッションで進める（`Orchestration.md` §4.1）。カードの間はセッションをクリアし、次へ進む前に前のカードをコミットする。
新しいセッションでは「`docs-dev/EquipmentEventTasks.md` の EQE-0x を実行して」とだけ頼めば着手できるように書いてある。
**着手時にカードの「変更ファイルの想定」を確かめ、完了時に実際の件数と突き合わせる**（DoD 5）。

## 背景

- 設備稼働履歴は**区間**（`StartedAt`〜`EndedAt`）で持ち、今は手入力（`/maintenance` の稼働記録）と実績CSV（`equipment-logs`）でしか入らない。
  どちらも `ShopFloorReportService.AddEquipmentLogAsync` を通る。チョコ停の記録は手入力では漏れる。
- PLC が出すのは区間ではなく**状態の変化点**。変化点から区間を組み立てる処理が MES 側に無い。
- 停止・故障の記録には停止原因が必須（B-40-20-02。`ShopFloorReportService.cs` の判定）。PLC は原因の文章を持たず、持っていてもアラームの番号だけ。
- 稼働サマリ・OEE・ダッシュボードは**終了済みの区間だけ**を集計している（`EndedAt != null`）。開いた区間があっても集計は乱れない。
- 連携の方向（2026-11-22 に利用者と相談）：**本体に PLC 通信（OPC UA・MC プロトコル・Modbus 等）は入れない**。
  MQTT やメーカーごとのプロトコルはゲートウェイ（同梱のサンプルは Node-RED）が吸収し、MES は HTTP で状態変化だけを受ける。
  配布は工場 LAN 内の自前設置で、利用者が版を上げるペースを決めるので後方互換が最優先（連携API と同じ）。

## 進み具合

| カード | 状態 |
|:--|:--|
| EQE-01 土台（生イベント・収集フラグ・手入力との住み分け） | 完了（2026-11-23。Spec.md 改訂154） |
| EQE-02 受信APIと区間の組み立て | 未着手 |
| EQE-03 停止原因の対応表と原因の追記 | 未着手 |
| EQE-04 稼働監視に今の状態を出す | 未着手 |
| EQE-05 ゲートウェイのサンプル（Mosquitto＋Node-RED）と利用者ガイド | 未着手 |

## 決定事項（2026-11-23 確定）

| 論点 | 案 | 理由 |
|:--|:--|:--|
| 本体の範囲 | **状態の変化だけを受ける**。PLC 通信は持たない。ショット数（治工具の寿命 E-60-20）・センサー値（製造条件データ・予兆保全 E-20-20）・生産数は対象外 | 時系列の大量データは MES の DB に向かない。状態の区間だけで稼働監視・OEE・ロット履歴（H-30-10-04）が埋まる |
| 口 | `POST api/integration/v1/equipment-events`。APIキー認証・OpenAPI に載せる。**複数件をまとめて**送れる（上限は設定値。既定1,000件） | ゲートウェイは通信が切れている間ためておき、戻ったらまとめて送る |
| 設備の指し方 | 設備の**資産番号（`Equipment.AssetNo`）**で指す。内部IDは受けない | 連携API v1 の方針（コード・番号で指す）と同じ |
| 1件の形 | `equipmentCode`・`eventId`（ゲートウェイが振る一意のID。最大100文字）・`occurredAt`（設備側の時刻。オフセット必須）・`state`（列挙子の名前：`Running`/`Stopped`/`Setup`/`Failure`/`Idle`/`Off`）・`alarmCode`（任意）・`workOrderNo`＋`operationSeq`（任意）・`note`（任意） | 状態は既存の `EquipmentLogStatus` の名前をそのまま使う。`Off`（電源断・通信断）だけはイベント専用で、開いている区間を閉じて新しい区間を開かない |
| 再送・重複 | **`eventId` で弾く**（設備ごとに一意）。`Idempotency-Key` は使わない | 再送は要求の単位ではなくイベントの単位で起きる（ためたイベントの一部だけが届いていることがある） |
| 応答 | 200 で**1件ごとの結果**（`accepted`／`duplicate`／`rejected`＋理由）を返す。全件不正でも 200。認証 401・権限 403・本文の形が壊れているときだけ 400 | 1件の不正で他の件を捨てると、ゲートウェイが残りを送り直し続けることになる。CSV 取込（全体を1トランザクションで422）と意図的に扱いを変える |
| 区間の組み立て | 設備ごとに、新しい状態のイベントで**開いている区間を `occurredAt` で閉じ、新しい区間を開く**。同じ状態が続けて来たら何もしない（結果は `accepted`。生イベントは残す） | 区間の形を変えずに、既存の集計・OEE・ロット履歴をそのまま使える |
| 時刻の順序 | **最後に採用したイベントより古いもの（遅着）は拒否**する。同じ時刻は受けた順に並べる。サーバー時刻より先の時刻は許容幅（設定値。既定300秒）を超えたら拒否 | 間に差し込むと過去の区間が割れ、OEE やロット履歴がさかのぼって変わる。設備時計のずれは NTP で揃える運用とし、ガイドに書く |
| 生イベントの保存 | 受けたイベントを `EquipmentEvent` に残す（採用・重複・拒否のどれでも。拒否の理由も）。7.6節の**運用ログ（C 層）**に入れ、保持期間（`Retention:EquipmentEventDays`。既定90日）を過ぎたら手動削除できる | 区間がおかしいときに、届いたイベントまで遡って調べられる。量が多いので区間（既定5年）より短く持つ |
| 作業指示との紐付け | イベントに `workOrderNo`＋`operationSeq` があればそれを使う。無ければ、**その設備を割り当てた作業指示のうち着手（`Started`）中のものがちょうど1件**のときだけ紐付ける。0件・2件以上なら紐付けない | 改訂65で「差立の割当からは補完しない」と決めたが、着手中は割当より強い根拠（作業者が現に着手している）。複数あるときは推測しない（まとめ処理は手入力かゲートウェイの指定で） |
| 停止原因 | 自動の区間は**停止原因が空でも受ける**。`alarmCode` を停止原因の対応表（EQE-03）で文章に置き換え、引けないものは「原因未入力」として画面から後で入れる。**手入力・CSV の必須はそのまま** | PLC は原因の文章を持たない。原因が無いからと区間を拒否すると稼働時間そのものが欠ける |
| 手入力との住み分け | 設備マスタに**「イベントで収集する」フラグ**（`CollectsEvents`。既定 false）を足す。立てた設備では**状態区間の手入力・CSV取込を拒否**する。停止原因の追記だけは許す。フラグが立っていない設備のイベントは拒否する | 同じ時間を手と自動で二重に数えない。フラグは設備マスタCSVの**任意の列**で足す（省けば今までどおり。CSVの列の約束） |
| 権限 | 受信はキーに紐づく利用者の**ロールを問わない**（認証済みなら可） | 設備稼働報告は手入力でもロールで絞っていない（改訂30）。誰でも記録でき、紐付けた利用者が監査ログに残る |
| 監査ログ | 区間を開いた・閉じたことは記録しない。**受信の要求ごとに件数の要約**（採用・重複・拒否）を1行残す | 1イベント1行の監査ログは量が多すぎる。中身は生イベントに残っている |

## カード

### EQE-01 土台（生イベント・収集フラグ・手入力との住み分け）

**目的**：イベントを受ける前に、受け皿と「手入力と自動のどちらで記録する設備か」を決められるようにする。

- エンティティ `EquipmentEvent`（`Core/Entities/Maintenance.cs`）：`Id`・`EquipmentId`・`EventId`・`OccurredAt`・`State`（文字列。`Off` を含むため `EquipmentLogStatus` にしない）・`AlarmCode`・`WorkOrderNo`・`OperationSeq`・`Note`・`Result`（採用／重複／拒否）・`RejectReason`・`EquipmentLogId`（採用して開いた・閉じた区間。任意）・`ReceivedAt`・`ReceivedByUserId`。一意索引は（`EquipmentId`, `EventId`）、検索用に（`EquipmentId`, `OccurredAt`）
- `Equipment.CollectsEvents`（bool。既定 false）。設備タブで切り替え、設備マスタCSVに任意の列 `CollectsEvents` を足して `CsvContract.v1.json` を更新する
- `EquipmentLog` に記録元 `Source`（手入力／CSV／イベント。列挙子は末尾に追加。既存の行は手入力）を足す。画面の稼働記録一覧に出す
- `AddEquipmentLogAsync` で、`CollectsEvents` の設備への登録を拒否する（手入力と実績CSVの両方に効く）
- 7.6節の保持の表に `EquipmentEvent` を足す（運用ログ。既定90日）

**変更ファイルの想定（約16＋マイグレーション）**：`Core/Entities/Maintenance.cs`、`Core/Entities/Masters.cs`、`Core/Entities/Enums.cs`、`Infra/MesAppDbContext.cs`、`Infra/Configurations/MaintenanceConfigurations.cs`・`MasterConfigurations.cs`（実際の名前は着手時に確認）、マイグレーション3プロバイダー（`scripts/Migrations.ps1 -Add AddEquipmentEvents`）、設備の DTO（`Core/Contracts/Masters/`）、`EquipmentsController.cs`、`Api/Services/ShopFloorReportService.cs`、設備マスタCSV（`MasterCsvService` の設備の取込・出力）、`CsvContract.v1.json`、保持・削除のサービス（7.6節の運用ログ削除。着手時に CodeMap で確認）、`Web/Pages/Masters/EquipmentsTab.razor`、`Web/Pages/Maintenance/EquipmentLogsTab.razor`、`UiText.en.resx`・`ApiText.en.resx`、`Tests/MaintenanceTests.cs`・`MasterCsvTests.cs`、`Spec.md`（5.5・5.1・7.6・3.8・改訂履歴）、`CodeMap.md`
**受入基準**：フラグを立てた設備は手入力・実績CSVの稼働記録が拒否され、立てていない設備は今までどおり／設備CSVは `CollectsEvents` 列を省いても今までどおり取り込める／CSVの列の約束のテストが通る（足しただけ）／既存の稼働記録の記録元は手入力になる

**実施メモ（2026-11-23）**：変更は26ファイル＋マイグレーション9（想定約16。多かったのは CSV の種別定義・出力・取込が3ファイルに分かれていたこと、実績CSVの呼び出し側（`ActualCsvService.Quality.cs`）、区分の表示名（`EnumLabels.cs`・`CoreText.en.resx`）、`EquipmentLogsController.cs` の書き漏れ。設計のずれではない）。
- **運用ログ（C層）の期限切れ削除はまだ実装されていなかった**（7.6節は方針だけ）。カードの「既存の削除の対象に入れる」は外し、7.6節の表に保持期間を書くだけにした。削除を作るときに `EquipmentEvent` も対象に入れる
- 既存の行の記録元は、列の既定値（`HasDefaultValue(Manual)`）で `'Manual'` になる。SQLite の移行SQLを `dotnet ef migrations script` で生成して確かめた（マイグレーションのファイルは開いていない）
- `EquipmentEvent.EquipmentLogId` の外部キーは連鎖させない（`Restrict`）。設備からの削除の連鎖が稼働履歴経由と直接の2経路になり、SQL Server が拒否するため
- SQLite で全件、SQL Server・PostgreSQL で `MaintenanceTests`・`DatabaseProviderTests` と設備CSVのテストを確認した

### EQE-02 受信APIと区間の組み立て

**目的**：ゲートウェイが状態変化を送ると、稼働記録の区間ができる。

- `POST api/integration/v1/equipment-events`（`Api/Controllers/Integration/EquipmentEventsController.cs`）。`[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]` と `[ApiExplorerSettings(GroupName = IntegrationOpenApi.DocumentName)]` を付ける。DTO は `Core/Contracts/Integration/EquipmentEventDtos.cs`
- 組み立ては `Api/Services/EquipmentEventService.cs`。**設備ごとに `occurredAt` の順に並べてから**1件ずつ判定する（1つの要求の中で順序が前後していても受ける）
  1. 設備が無い・無効・`CollectsEvents` でない → 拒否
  2. （設備, `eventId`）が既にある → 重複
  3. 最後に採用したイベントより古い・未来の許容幅を超える → 拒否（遅着／未来）
  4. 開いている区間と同じ状態 → 採用（区間はそのまま）
  5. それ以外 → 開いている区間を閉じ、`Off` 以外なら新しい区間を開く（記録元はイベント、記録者はキーの利用者）
- 閉じた区間の長さが0（同じ時刻の変化）になるものは作らずに捨てる
- 作業指示の紐付けは決定事項のとおり（指定があれば番号で引き、見つからなければ紐付けずに採用し、生イベントに理由を残す）
- 停止・故障の区間を開くとき、`alarmCode` があれば対応表で原因を引く（表は EQE-03。このカードでは常に「原因未入力」）
- トランザクションは要求ごとに1つ。同じ設備への同時の要求は（設備, `eventId`）の一意索引と、設備の行の更新で直列にする（着手時に3プロバイダーで同時実行のテストを書く）
- 設定 `EquipmentEvents:MaxBatchSize`（1,000）・`EquipmentEvents:MaxFutureSkewSeconds`（300）

**変更ファイルの想定（約10）**：`Core/Contracts/Integration/EquipmentEventDtos.cs`、`Api/Controllers/Integration/EquipmentEventsController.cs`、`Api/Services/EquipmentEventService.cs`、`Api/MesAppHost.cs`（DI・設定）、`appsettings.json`、`ApiText.en.resx`、`Tests/MaintenanceTests.cs`、`Spec.md`（3.5・3.8・改訂履歴）、`CodeMap.md`、`docs-dev/EquipmentEventTasks.md`
**受入基準**：稼働→停止→稼働で区間が2つ閉じて1つ開く／同じ `eventId` を送り直しても区間は増えず `duplicate`／遅着は `rejected` で区間は変わらない／1つの要求の中で順序が前後していても正しい区間になる／`Off` で区間が閉じて次が開かない／着手中の作業指示が1件なら紐付き、2件なら紐付かない／閉じた区間が稼働サマリ・OEE に出る／キーなしは401／OpenAPI 文書に載る／SQL Server・PostgreSQL で `MaintenanceTests` が通る

### EQE-03 停止原因の対応表と原因の追記

**目的**：アラーム番号から停止原因を引き、引けなかった停止に後から原因を入れられるようにする（B-40-20-02）。

- マスタ `EquipmentAlarmCode`：`EquipmentId`（任意。空なら全設備共通）・`AlarmCode`・`StopCause`・`IsActive`。設備ごとの定義が共通の定義に勝つ。一意は（`EquipmentId`, `AlarmCode`）。`/add-master` の手順で、マスタ画面のタブと CSV 種別 `equipment-alarm-codes` を足す
- EQE-02 の組み立てで対応表を引く。引けなければ原因を空にする（「原因未入力」）
- 原因の追記 `PUT api/equipment-logs/{id}/stop-cause`：停止・故障の区間の原因だけを書き換える。**手入力・CSVの区間にも使える**。監査ログに `before`/`after` を残す。権限は稼働記録と同じ（ロールで絞らない）
- 稼働記録タブに「原因未入力」の絞り込みと、行からの追記を足す

**変更ファイルの想定（約16＋マイグレーション）**：`/add-master` の定型一式（エンティティ・`DbSet`・設定・マイグレーション3プロバイダー・DTO・Controller・`Masters/EquipmentAlarmCodesTab.razor`・`MastersPage.razor`・`MasterCsvKinds`・CSVの取込と出力・`CsvContract.v1.json`）、`Api/Services/EquipmentEventService.cs`、`EquipmentLogsController.cs`、`Web/Pages/Maintenance/EquipmentLogsTab.razor`、`UiText.en.resx`・`ApiText.en.resx`、`Tests/MasterTests.cs`・`MaintenanceTests.cs`・`MasterCsvTests.cs`、`Spec.md`（5.1・3.5・3.8・6.1・改訂履歴）、`CodeMap.md`
**受入基準**：設備ごとの定義が共通の定義に勝つ／引けないアラームの停止は原因未入力で区間ができる／追記で原因が入り、監査ログに変更前後が残る／稼働の区間には追記できない

### EQE-04 稼働監視に今の状態を出す

**目的**：設備ごとの「今どうなっているか」を一覧で見られるようにする（E-20-10-03）。

- `GET api/equipment-logs/current`：有効な設備ごとに、開いている区間の状態・開始時刻・経過時間・作業指示・最後にイベントを受けた時刻（イベントで収集する設備だけ）を返す。開いている区間が無ければ「記録なし」
- 稼働監視タブに一覧を足す（作業区で絞り込み。状態は `EnumLabels.Of`）。自動更新は入れない（「更新」ボタン。必要になってから考える）
- 最後のイベントから一定時間（設定値。既定10分）届いていない収集設備は「受信が途絶えています」と出す（ゲートウェイの停止に気づくため）

**変更ファイルの想定（約7）**：`Core/Contracts/` の稼働監視の DTO、`EquipmentLogsController.cs`、`Web/Pages/Maintenance/EquipmentLogsTab.razor`（大きくなるなら同じフォルダに部品を分ける）、`UiText.en.resx`、`Tests/MaintenanceTests.cs`、`Spec.md`（3.5・6.1・改訂履歴）、`CodeMap.md`
**受入基準**：開いている区間の状態が出る／手入力の設備も出る／途絶の判定が設定値で変わる

### EQE-05 ゲートウェイのサンプル（Mosquitto＋Node-RED）と利用者ガイド

**目的**：利用者が「MQTT で状態を出す設備（または模擬設備）→ MES」を手元で動かし、自分の設備向けに書き換えられるようにする。

- `compose.yaml` に**任意のプロファイル `equipment`** で `mosquitto` と `node-red` を足す（`docker compose --profile equipment up -d`。プロファイルを付けなければ今までどおり MES だけが起動する）。設定は `samples/equipment-gateway/` に置く（`mosquitto.conf`、Node-RED の `flows.json`）
- Node-RED のフロー：①模擬設備（数秒ごとに状態を変えて `mes/equipment/<資産番号>/state` に出す）②MQTT を購読して `equipment-events` へ送る（`eventId` を振る・送れなかったらためて再送する）③応答の `rejected` をデバッグ欄に出す。APIキーは環境変数から読む
- サンプルのマスタ（`samples/master-csv`）の設備のうち1台に `CollectsEvents` を立て、停止原因の対応表の例を足す
- 運用ガイドに「設備イベントの受信」の節（日英）：仕組みの図、収集フラグ、時刻の揃え方（NTP）、遅着・重複の扱い、原因の追記、PLC の接続はゲートウェイ側で行うこと（Node-RED のノードの例は名前を挙げるだけにする）

**変更ファイルの想定（約10）**：`compose.yaml`、`samples/equipment-gateway/mosquitto.conf`・`flows.json`・`README`（短く）、`samples/master-csv/` の設備と対応表、`docs/operations.md`・`docs/en/operations.md`、`Spec.md`（3.8・改訂履歴）、`CodeMap.md`、`Readme.md`（起動の一行）
**受入基準**：新しい DB で `--profile equipment` で起動し、キーを発行して環境変数に入れると、模擬設備の区間が稼働記録に増える／Node-RED を止めて戻すと、ためたイベントが重複なく入る／プロファイルなしの起動が変わらない／`GuideEnCheck.ps1 -Strict` が通る

## 順序と依存

EQE-01 → EQE-02 → EQE-03 → EQE-04 → EQE-05。
EQE-03 と EQE-04 は EQE-02 だけに依存するので入れ替えてよい。EQE-05 は全部が揃ってから（ガイドの例を一度で確かめるため）。

## 対象外（このバックログでは作らない）

- PLC・OPC UA・MQTT の通信を MES 本体に持つこと
- ショット数・カウンタ（治工具の寿命の自動加算 E-60-20、生産数の自動計上）→ 状態の受信が運用に乗ってから別のバックログ
- センサー値・波形（製造条件データの自動記録、予兆保全 E-20-20）
- 遅着イベントの差し込み（過去の区間の組み直し）
- 稼働監視の自動更新（SignalR 等）・アンドン表示
- MES から設備・ゲートウェイへの指示（レシピの送信など）
