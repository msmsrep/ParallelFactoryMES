# 画面からしか登録できない機能のCSV対応 バックログ

サンプルCSVだけでアプリ全体を一通り試せるように、また試験・移行で記録を一括投入できるように、
今は画面（単票API）からしか登録できない業務記録をCSVで取り込めるようにする（Spec.md 3.8）。
1カード＝1セッションで進める（`Orchestration.md` §4.1）。カードの間はセッションをクリアし、次へ進む前に前のカードをコミットする。
新しいセッションでは「`docs-dev/CsvCoverageTasks.md` の CSV-0x を実行して」とだけ頼めば着手できるように書いてある。

## 背景

2026-09-23 のサンプル点検で、次の機能はCSVの種別が無く、サンプル取込後に画面で一から入力するしかないと分かった
（サンプルの不足 A1〜A6 は PR #28 で対応済み。こちらはその残り「B」の根本対応）。

保全手順書・保全計画・保全指示／実績・突発依頼、治工具の利用実績・引当、校正記録、在庫オペレーション（移動・調整・状態変更・分割・統合・振替・廃棄・返品・工程戻し）、
工程間搬送、ピッキング、棚卸、不適合、サンプル品保管。

## 決定事項（2026-09-23）

| 論点 | 決定 | 理由 |
|:--|:--|:--|
| 取込の型 | 既存の実績CSV（常に新規登録・1行でもエラーなら全件取消・ZIP一括可）に乗せる。保全手順書だけはマスタCSV（キーで上書き・出力あり） | 保全手順書は作業手順書（`work-procedures`）と同じ版数付きのマスタ。ほかは日々増える業務記録 |
| 業務判定 | **行ごとに単票APIと同じ業務サービスを呼ぶ**（既存の実績CSVと同じ）。登録処理が Controller に直接書かれている箇所は、先にサービスへ移す | CSVと画面で判定がずれないようにする |
| 番号のある記録 | 番号を**手入力**にして、後のCSVから指せるようにする。自動採番の接頭辞で始まる番号は拒否する（出荷指示の `ShippingNo`・製造指図の `OrderNo` と同じ） | 自動採番の番号は取り込むまで分からない |
| 番号の無い記録 | 搬送・治工具の引当などは、1行の中の列（`Execute`・`Issue`・`Return` など）で実行・返却まで進め、ほかのファイルからは指さない。**番号の列を足すマイグレーションは作らない** | データモデルを変えずに済む |
| 在庫オペレーション | **1種別 `inventory-operations`** に `Operation` 列を持たせる。操作ごとの必須列はコードで検査する | 操作ごとに分けると9種別になり、実績CSV取込の画面にパネルが9枚増える |
| 自動起票された不適合の指し方 | **ロット番号で指し、そのロットの未完了の不適合が1件だけのときに限る**（0件・2件以上は行エラー）。新規起票の行は `ReportNo` を手入力 | 検査の不合格で自動起票された不適合は番号が取り込むまで分からない |
| 保全計画 | **実績CSV（常に新規登録）で行う方針。ただし実装は保留**（下の「保留」） | 自然なキーが無く、生産計画のようなキーで上書きの形にできない |
| 取消 | 取消（キャンセル）の操作はCSVの対象にしない | 取込は記録を作るためのもので、取り消しは画面で1件ずつ確かめて行う |
| 設計変更 | 対象外。工順の改訂は既存の工順CSV（`routing`）で入る | ― |
| 権限 | 種別ごとの取込ロールは単票APIと同じ定数（`ActualCsvKinds` の `WriteRoles`） | 既存の方針 |

## バックログ

| ID | 内容 | 想定層 | 状態 |
|:--|:--|:--|:--|
| CSV-01 | 下準備：実績CSVサービスの分割と、手入力番号の受け口 | L1 | 完了 |
| CSV-02 | 保全手順書のマスタCSV | L1 | 未着手 |
| CSV-03 | 保全指示・突発依頼・保全実績 | L1 | 未着手 |
| CSV-04 | 治工具の利用実績・引当、校正記録 | L1 | 未着手 |
| CSV-05 | 在庫オペレーション | L1 | 未着手 |
| CSV-06 | 搬送・ピッキング・棚卸 | L1 | 未着手 |
| CSV-07 | 不適合・サンプル品保管 | L1 | 未着手 |
| CSV-08 | サンプルの通し取込とユーザーガイドの見直し | L2 | 未着手 |
| （保留） | 保全計画 `maintenance-plans` | L1 | 保留 |

### 各カード共通のDoD

- ビルド成功・新規警告なし、`dotnet test` 全緑
- 新しい種別は `GET api/actuals/csv/kinds`（マスタは `api/masters/csv/kinds`）に出て、実績CSV取込画面にパネルが自動で並ぶ（画面のコードは変えない）
- 列の説明・エラー文言の英訳を `Api/Localization/ApiText.en.resx` に足し、`./scripts/I18nCheck.ps1` が終了コード0
- `Spec.md` 3.8節の種別一覧と改訂行、`docs-dev/CodeMap.md` の該当行、`docs/masters.md` の実績CSVの節を同じコミットで更新
- サンプルCSV（`samples/actual-csv/` の次の番号）とREADMEを足し、`MasterCsvTests` の通し取込（ファイル名順・ZIP一括）が通る。ZIPテストのファイル数も直す
- 領域の既存テストクラスに「取り込める」「1行エラーで全件取消」「単票APIと同じ判定で拒否される」を足す
- 状態表とメモ（`csv-coverage-backlog`）を更新

---

## タスク: CSV-01 下準備：実績CSVサービスの分割と手入力番号の受け口

- 目的 / 背景: `ActualCsvService.cs`（755行）にこの先12種別を足すと読めなくなる。領域ごとの partial に分けてから足す。あわせて、番号を自動採番しているサービスに手入力の番号を渡せる引数を足す
- 変更対象:
  - `src/MesApp.Api/Services/ActualCsvService.cs` → `ActualCsvService.Production.cs`・`.Execution.cs`・`.Quality.cs` などに分割（`.Shipping.cs` と同じ形。**挙動は変えない**）
  - `PickingService.CreateAsync`・`StocktakeService.CreateAsync`・`NonconformanceService.CreateAsync`・`MaintenanceOrderService.CreateAsync` に `string? no` 引数（空なら今までどおり自動採番。自動採番の接頭辞で始まる番号・既存と重複する番号は拒否）
  - 接頭辞の判定を `NumberingService` に集める（今は `ManufacturingOrderService.AutoOrderNoPrefix` と出荷で別々）
  - 呼び出し側の Controller（引数 `null` を渡すだけ）
- 変更禁止: 既存のCSVの列・結果・エラー文言、API契約、Migrations
- 受入条件: 既存テストが無変更で全緑。`ActualCsvService` のどのファイルも500行以下。手入力番号の拒否（接頭辞・重複）をサービス単位でテスト
- 参照: Spec.md 3.8・3.9（採番）/ CodeMap「実績CSV一括取込」「採番」
- 想定層: L1 / effort: xhigh（CSV取込・採番を触る）
- 想定変更ファイル数: 約10

## タスク: CSV-02 保全手順書のマスタCSV

- 目的 / 背景: 保全手順書（E-10-20）だけがマスタなのにCSVで入出力できない
- 変更対象: `MasterCsvKinds.cs`（種別 `maintenance-procedures`。`ImportOrder` では設備・治工具・スキルの後）、`MasterCsvService.cs`（出力）、`MasterCsvService.Import.cs` またはその partial（取込。版数の扱いは `work-procedures` と同じ）、`Pages/Maintenance/MaintenanceProceduresTab.razor`（`<CsvIoPanel>` を置く）、`samples/master-csv/20_maintenance-procedures.csv`・README、`MasterCsvTests`
- 列: `ProcedureNo`（キー）/ `Title` / `TargetEquipmentAssetNo` / `TargetToolCode` / `RequiredSkillCode` / `Steps` / `IsActive`（`Version` は出力専用）
- 変更禁止: 保全手順書のAPI契約、版数の付け方
- 受入条件: 取り込み直しても件数が増えない。本文が変わったときだけ版数が上がる（作業手順書と同じ）。参照中の手順書を `IsActive=false` にすると `MasterDeactivationPolicy` と同じ理由で拒否。マスタZIPの一括出力に入り、別DBへそのまま取り込める（既存テストの種別網羅チェックが通る）
- 参照: Spec.md 3.1・3.8 / MES.md E-10-20 / CodeMap「保全手順書」「マスタCSV一括入出力」
- 想定層: L1 / effort: xhigh
- 想定変更ファイル数: 約8

## タスク: CSV-03 保全指示・突発依頼・保全実績

- 目的 / 背景: 保全指示・実績（E-30-20 / E-30-30 / E-40）が画面からしか入らず、消耗材モニタリング・保全の履歴を試せない
- 変更対象: `ActualCsvKinds.cs`、`ActualCsvService.Maintenance.cs`（新規）、`MaintenanceOrderService`（CSV-01 の番号引数を使う）、サンプル・README、`MaintenanceTests`
- 種別:
  - `maintenance-orders`：`MaintenanceNo`（手入力・必須）/ `EquipmentAssetNo` か `ToolCode` / `RequestType`（計画・突発など）/ `ProcedureNo` / `ScheduledDate` / `Note`
  - `maintenance-records`：`MaintenanceNo` / `StartedAt` / `EndedAt` / `Result` / `Note` / 部品 `PartProductCode`・`PartLotNumber`・`PartLocationCode`・`PartQuantity`（**同じ番号・同じ開始日時の行を1件の実績にまとめる**。部品の在庫引落しは `AddRecordAsync` のまま）
- 変更禁止: 保全の在庫引落しの経路（新設しない。メモ [[maintenance-parts-design]]）、必要スキルの照合（実施者＝取り込んだユーザー）
- 受入条件: 資産管理部品と消耗品の書き分けが単票と同じに通る。必要スキルを持たないユーザーの取込は拒否。部品の在庫不足は行エラーで全件取消
- 参照: Spec.md 3.8・5.x（保全）/ MES.md E-30-20・E-30-30・E-40 / CodeMap「保全指示・実績・突発依頼」「消耗材モニタリング」
- 想定層: L1 / effort: xhigh（在庫更新を含む）
- 想定変更ファイル数: 約8

## タスク: CSV-04 治工具の利用実績・引当、校正記録

- 目的 / 背景: 治工具の寿命（E-60）・引当（B-20-30）・校正（C-20-50-03）がサンプルで試せない
- 変更対象:
  - 利用実績の登録を `ToolUsagesController` からサービスへ移す（`ToolIssueService` に足すか `ToolUsageService` を新設。**調査で確定**）
  - 校正記録の登録を `InspectionDevicesController`（`{id}/calibrations`）からサービスへ移す
  - `ActualCsvKinds.cs`、`ActualCsvService.Tools.cs`（新規）、サンプル・README、`MaintenanceTests`・`QualityTests`
- 種別:
  - `tool-usages`：`ToolCode` / `OrderNo`＋`Sequence`（任意）/ `UsageCount` / `UsageHours` / `RecordedAt`
  - `tool-issues`：`ToolCode` / `OrderNo`＋`Sequence` / `Issue`（true で払出まで）/ `Return`（true で返却まで）/ `Note`。判定は `ToolIssuePolicy`（引当時・払出時の両方）
  - `calibrations`：`DeviceCode` / `CalibratedOn` / `Result` / `NextDueOn`（省略時は周期から）/ `Note`
- 変更禁止: 治工具の状態を手で付け外しする規則（`ToolIssuePolicy.CheckManualStatus`）、API契約
- 受入条件: 使用中の治工具を引き当てる行は拒否。校正の記録で検査機の次回期限が更新され、DV-03 に合格の校正を入れると検査に使えるようになる
- 参照: Spec.md 3.8 / MES.md E-60・B-20-30・C-20-50-03 / CodeMap「治工具の引当・払出・受領確認」「治工具マスタ・寿命管理・利用実績」「検査機・測定器マスタ／校正管理」
- 想定層: L1 / effort: xhigh
- 想定変更ファイル数: 約10（3ファイル超。重ければ「治工具」と「校正」の2つに分ける）

## タスク: CSV-05 在庫オペレーション

- 目的 / 背景: 在庫の移動・調整・分割・統合などが画面からしか行えない
- 変更対象: `ActualCsvKinds.cs`、`ActualCsvService.Inventory.cs`（新規）、サンプル・README、`InventoryTests`
- 種別 `inventory-operations`：`Operation`（`Move` / `Adjust` / `Status` / `Split` / `Merge` / `Transfer` / `Discard` / `Return` / `IssueReturn`）/ `LotNumber` / `LocationCode` / `ToLocationCode` / `Quantity` / `TargetLotNumber`（統合先）/ `ProductCode`（振替先）/ `Status` / `Reason`。操作ごとの必須列はコードで検査し、足りなければ「操作 '{0}' には {1} が必要です。」の行エラー。各行は `LotOperationService` の同名メソッドを呼ぶ
- 変更禁止: `LotOperationService` の判定、ロット系譜・状態履歴の残し方、ステータスの直接代入の禁止
- 受入条件: 分割で作られたロットを同じファイルの後の行から指せる（分割先のロット番号を列で指定。**指定できない場合は調査で確定**）。調整・状態変更の理由が状態履歴・監査ログに残る。不良ロットの移動など単票で拒否されるものはCSVでも拒否
- 参照: Spec.md 3.8・5.2 / MES.md D-10-30・D-30-10・D-40-40 / CodeMap「在庫オペレーション」「ロット系譜」
- 想定層: L1 / effort: xhigh（在庫更新）
- 想定変更ファイル数: 約6

## タスク: CSV-06 搬送・ピッキング・棚卸

- 目的 / 背景: 物流の指示系（B-50-10 / D-20 / D-50-10）と倉庫業務進捗（D-50-30-07）がサンプルで空のまま
- 変更対象:
  - 搬送の作成・実行を `TransferOrdersController` からサービスへ移す（`InventoryService` に足すか `TransferOrderService` を新設。**調査で確定**）
  - `ActualCsvKinds.cs`、`ActualCsvService.Logistics.cs`（新規）、`PickingService`・`StocktakeService`（CSV-01 の番号引数）、サンプル・README、`InventoryTests`
- 種別:
  - `transfer-orders`：`LotNumber` / `FromLocationCode` / `ToLocationCode` / `Quantity` / `Execute`
  - `picking-orders`：`PickingNo`（手入力・必須）/ `OrderNo`＋`Sequence` / `Execute`（FEFO の自動引当は単票と同じ）
  - `stocktakes`：`StocktakeNo`（手入力・必須）/ 範囲（`LocationCode` など。**単票の作成要求に合わせて調査で確定**）
  - `stocktake-counts`：`StocktakeNo` / `LotNumber` / `LocationCode` / `CountedQuantity` / `Finalize`（まとまりの最初の行）
- 変更禁止: FEFO 引当の規則、棚卸の差異計算
- 受入条件: 期限切れロット（サンプルの R3006-250801）はピッキングの引当に入らない。棚卸の確定で差異が在庫調整として残る。倉庫業務進捗に指示が出る
- 参照: Spec.md 3.8 / MES.md B-50-10・D-20-10・D-20-20・D-50-10・D-50-30-07 / CodeMap「工程間搬送・移動指示」「出庫・ピッキング・工程払出」「棚卸」「倉庫業務進捗管理」
- 想定層: L1 / effort: xhigh（在庫更新）
- 想定変更ファイル数: 約10（3ファイル超。重ければ「搬送・ピッキング」と「棚卸」の2つに分ける）

## タスク: CSV-07 不適合・サンプル品保管

- 目的 / 背景: 不適合の対応（C-30）と保管サンプル（D-40-50-01）が画面からしか進められない
- 変更対象:
  - サンプルの採取・払出／廃棄を `SampleStoragesController` からサービスへ移す（`SampleStorageService` を新設）
  - `ActualCsvKinds.cs`、`ActualCsvService.Quality.cs`、`NonconformanceService`（CSV-01 の番号引数）、サンプル・README、`QualityTests`・`InventoryTests`
- 種別:
  - `nonconformances`：`ReportNo`（新規起票のとき手入力）**または** `LotNumber`（既存の不適合を指す。**そのロットの未完了の不適合が1件だけのときに限る**。0件・2件以上は行エラー）/ 新規時の `Content`・`OrderNo`＋`Sequence` / `CauseCategory`・`CauseDetail` / `Action`・`ActionInstruction`（対応指示）/ `ActionRecord`（対応記録）/ `Approve`
  - `sample-storages`：`SampleNo`（手入力・必須）/ `LotNumber` / `Quantity` / `StorageLocationCode` / `RetentionUntil` / `Close`（払出・廃棄の区分）
- 変更禁止: 不適合の連鎖（リワーク指図の起票など `NonconformanceService` の判定）、保管棚を引当の対象にしない規則
- 受入条件: サンプルの R3001-260902（検査不合格で自動起票）をロット番号で指して対応指示→承認まで進められる。同じロットに未完了の不適合が2件あると行エラー。サンプル採取で在庫から抜かれる
- 参照: Spec.md 3.8 / MES.md C-30・B-40-30・D-40-50-01 / CodeMap「不適合・逸脱管理」「サンプル品保管管理」
- 想定層: L1 / effort: xhigh
- 想定変更ファイル数: 約9（3ファイル超。重ければ「不適合」と「サンプル保管」の2つに分ける）

## タスク: CSV-08 サンプルの通し取込とユーザーガイドの見直し

- 目的 / 背景: CSV-02〜07 で足したサンプルを、マスタ→実績のZIP一括取込で最後まで通し、ユーザーガイドの「画面から操作すること」を実態に合わせる
- 変更対象: `samples/actual-csv/README.md`（全体の流れ）、`docs/walkthrough.md`（10章の表）・`docs/getting-started.md`、`MasterCsvTests`（通し取込の確認項目）
- 変更禁止: アプリのコード
- 受入条件: マスタZIP→実績ZIPの順で1回ずつ取り込むだけで、保全・治工具・校正・在庫・物流・不適合・サンプル保管の各画面に記録が出る（テストで各APIの件数>0を確認）。`jekyll build` が通る
- 参照: CodeMap「実績CSV一括取込」
- 想定層: L2 / effort: medium
- 想定変更ファイル数: 約5

---

## 保留

### 保全計画 `maintenance-plans`（2026-09-23 利用者の判断で実装保留）

- 方針だけ決めてある：**実績CSV（常に新規登録）**。列は `EquipmentAssetNo` / `Category` / `PlanYear` / `ScheduledDate` / `CycleDays` / `Note`
- マスタCSV（キーで上書き）にしない理由：設備×年×区分でも同じ設備に同じ区分の計画が複数ありうるので、自然なキーが無い
- 下準備：登録処理が `MaintenancePlansController.Create` に直接書かれているので、実装するときは先に `MaintenanceOrderService` へ移す
- 計画から保全指示を作る流れ（CSV-03 の `maintenance-orders` から計画を指す）は、計画に番号が無いため指せない。着手するときに、計画の指し方（設備＋年＋区分＋予定日の一致など）を決める
