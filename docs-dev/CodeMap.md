# CodeMap — 業務機能 → 実装ファイル 対応表

`Orchestration.md` §6.3 の資産。**探索（Glob→Grep→Read）を1回の参照に置換するための表**であり、
「どこを触ればよいか」はまずここで確認する。ここで足りないときだけ grep する。

- MES No は『MES/MOM導入のための標準業務一覧』（ENAA）の業務プロセス番号。詳細は `MES.md`（ローカルのみ）を grep する。
- 仕様の詳細は `Spec.md` の該当節のみを grep する。
- 表にないファイル（`Migrations/` など）は読まない。
- **新規モジュールを追加したら、このファイルへの行追加を DoD に含める。**

パスの略記：`Api/` = `src/MesApp.Api/`、`Core/` = `src/MesApp.Core/`、`Web/` = `src/MesApp.Client.Web/`、
`Infra/` = `src/MesApp.Infrastructure/`、`Tests/` = `tests/MesApp.Api.Tests/`。

---

## A. 生産管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 製造指図（発行・承認・変更・工程展開） | A-20 / B-10-10 | `Api/Controllers/ManufacturingOrdersController.cs`<br>`api/manufacturing-orders` | `Core/Entities/Production.cs`<br>ManufacturingOrder / **ManufacturingOrderMaterial**（予定材料＝展開時のMBOM固定） / WorkOrder（工順スナップショット付き） / Lot | `/manufacturing-orders` `Web/Pages/ManufacturingOrders.razor`<br>`/manufacturing-orders/{id}` `ManufacturingOrderDetail.razor` | `Tests/ProductionTests.cs` |
| 品目マスタ・MBOM・工順/BOP（工順の作業区は**最下段のみ**。展開時に作業指示へスナップショット） | A-40-10 / A-40-20 | `ProductsController.cs` `api/products`<br>`ProcessesController.cs` `api/processes` | `Core/Entities/Masters.cs`<br>Product / BomItem / ProcessMaster / Routing | `/masters` `Web/Pages/Masters/ProductsTab.razor` / `ProcessesTab.razor` | `Tests/MasterTests.cs` |
| 作業手順書（SOP。版数管理） | I-30-40 / I-30-20-12 / B-10-30-03 | `WorkProceduresController.cs` `api/work-procedures`（更新で版数+1。**工順から参照中は無効化できない**） | Masters.cs: WorkProcedure（対象品目・工程は持たない。紐付けは `Routing.WorkProcedureId` 側）<br>本文を置けない手順書は `Reference`（文書番号・URL）だけでよい | `/masters` `Masters/WorkProceduresTab.razor`<br>工順への紐付けは `Masters/ProductsTab.razor`<br>作業指示での閲覧は `ProductionRecordEntry.razor` | `Tests/MasterTests.cs` `Tests/MasterCsvTests.cs` |
| 作業指示の手順書表示 | B-10-30-03 | `WorkOrdersController.cs` `GET api/work-orders/{id}/procedure`（紐付けなしは404） | Production.cs: WorkOrder（`WorkProcedureId` ＋ `WorkProcedureVersion`）<br>**本文は固定せず版数だけスナップショット**。表示はマスタ現在値で、版数が食い違えば `IsRevised`（他のスナップショットとは方針が逆。Spec.md 5.7） | `/work-orders/{id}` `ProductionRecordEntry.razor` | `Tests/ProductionTests.cs` |
| マスタCSV一括入出力 | Spec.md 3.1 | `MasterCsvController.cs` `api/masters/csv`<br>`Api/Services/MasterCsvService.cs` / `.Import.cs` / `MasterCsvKinds.cs` / `CsvTable.cs` / `CsvFile.cs` | （各マスタ） | `Web/Shared/CsvIoPanel.razor`（各Tabに配置） | `Tests/MasterCsvTests.cs` |

## B. 製造実行

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 作業指示・差立（作業員/設備割当・着手順） | B-10-20 / F-20-30-01 | `WorkOrdersController.cs` `api/work-orders`<br>候補設備は `api/work-orders/{id}/equipment-candidates`（**候補があればその中からしか割り当てられない**。候補は工順マスタの現在値） | Production.cs: WorkOrder<br>Masters.cs: **RoutingEquipment**（工順の候補設備） | `/work-orders` `WorkOrders.razor`<br>`/dispatch` `Dispatch.razor` | `Tests/ExecutionTests.cs` |
| 実行系（着手・段取り・チェックリスト・部材投入・実績報告・製造条件データ・状態履歴） | B-30-30-01 / B-20-50 / B-40-40 | `WorkOrderExecutionController.cs`<br>`api/work-orders/{id:int}` | `Core/Entities/Execution.cs`<br>SetupRecord / ChecklistRecord / ChecklistResultItem / MaterialConsumption / ProductionRecord / ProductionDataRecord | `/work-orders/{id}/setup` `WorkOrderSetup.razor`<br>`/work-orders/{id}/record` `ProductionRecordEntry.razor`（部材投入・製造条件データ・訂正・状態履歴もここ）<br>`/process-progress` `ProcessProgress.razor` | `Tests/ExecutionTests.cs` |
| 製造履歴訂正（訂正履歴＋監査ログ） | B-70-30-01 | `ProductionRecordsController.cs` `api/production-records` | Execution.cs: ProductionRecord / **ProductionRecordCorrection**（訂正前の値。Spec.md 5.7） | `ProductionRecordEntry.razor` の実績一覧から訂正<br>`/traceability` の履歴タブに訂正履歴を表示 | `Tests/ExecutionTests.cs` / `QualityTests.cs` |
| 作業時間記録（直接/間接） | B-30-30-02 / F-30-20-02 | `WorkTimeRecordsController.cs` `api/work-time-records` | Execution.cs: WorkTimeRecord | `/work-time` `WorkTime.razor` | `Tests/ExecutionTests.cs` |
| 製造トラブル報告 | B-40-10-06 / B-60-10 | `TroubleReportsController.cs` `api/trouble-reports` | Execution.cs: TroubleReport | `ProcessProgress.razor` 内 | `Tests/ExecutionTests.cs` |
| 工程間搬送・移動指示 | B-50-10 / D-30-10-04 | `TransferOrdersController.cs` `api/transfer-orders`（更新系は在庫権限） | Execution.cs: TransferOrder | `/transfer-orders` `TransferOrders.razor`（`Inventory.razor` の「振替」は別機能の `api/inventory/transfer`） | `Tests/InventoryTests.cs` |
| 設備稼働報告・稼働監視 | B-40-20 / E-20-10 | `EquipmentLogsController.cs` `api/equipment-logs`（`?workOrderId=` で絞り込み） | `Core/Entities/Maintenance.cs`: EquipmentLog（`WorkOrderId` 任意＝**PQC×EQCの交差点**。ロット履歴 H-30-10-04 はこれを辿る。`EquipmentLogStatus` への追加は**末尾のみ**：JSONが数値） | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |

## C. 品質管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 工程管理項目マスタ（指示値・許容範囲。版数付き） | B-30-30-04 | `ControlItemsController.cs` `api/control-items`<br>展開時のスナップショット取得は `WorkOrdersController.ControlItems` `api/work-orders/{id}/control-items` | Masters.cs: ControlItem<br>Production.cs: **WorkOrderControlItem**（展開時点の指示値。判定・表示はこちらを使い、マスタ現在値を参照しない） | `/masters` `Masters/ControlItemsTab.razor` | `Tests/MasterTests.cs` / `MasterCsvTests.cs` |
| 検査項目・基準マスタ | C-10-10 | `InspectionItemsController.cs` `api/inspection-items` | Masters.cs: InspectionItem | `/masters` `Masters/InspectionItemsTab.razor` | `Tests/MasterTests.cs` |
| 検査指示・実績・判定・成績書 | C-20 | `InspectionOrdersController.cs` `api/inspection-orders` | `Core/Entities/Quality.cs`<br>InspectionOrder / InspectionOrderItem（**発行時点の基準スナップショット**。判定・成績書はこちらを使い、マスタ現在値を参照しない：Spec.md 5.7） / InspectionResult / **InspectionResultCorrection**（訂正前の記録。詳細画面・成績書に表示） | `/inspections` `Inspections.razor`<br>`/inspections/{id}` `InspectionDetail.razor`<br>`/print/inspection/{id}` `Print/InspectionCertificate.razor` | `Tests/QualityTests.cs` |
| 不適合・逸脱管理（特採・廃棄・保留） | C-30 / B-40-30 | `NonconformanceController.cs` `api/nonconformances` | Quality.cs: NonconformanceReport | `/nonconformances` `Nonconformances.razor` | `Tests/QualityTests.cs` |
| 品質分析（不良理由別・品目別・工程別・**直別**・期間別） | C-40-10 | `QualityAnalysisController.cs` `api/quality/summary` | Execution.cs: ProductionDefect（不良理由別の内訳）<br>ProductionRecord.`ShiftId`（**記録時に固定**。集計で引き直さない） | `/quality-analysis` `QualityAnalysis.razor`<br>`Web/Shared/BarMeter.razor` | `Tests/QualityTests.cs` |
| 不良理由マスタ | C-40-10-01 | `DefectReasonsController.cs` `api/defect-reasons` | Masters.cs: DefectReason | `/masters` `Masters/DefectReasonsTab.razor` | `Tests/MasterTests.cs` / `MasterCsvTests.cs` |
| チェックリストマスタ（HSE含む） | B-30-10 / G-20-20-02 | `ChecklistsController.cs` `api/checklists` | Masters.cs: Checklist / ChecklistItem | `/masters` `Masters/ChecklistsTab.razor` | `Tests/MasterTests.cs` |

## D. 物流／在庫管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 受入・受入ロット採番 | D-10-10 | `ReceivingController.cs` `api/receiving` | `Core/Entities/Inventory.cs`: InventoryStock / InventoryTransaction<br>Production.cs: Lot | `/receiving` `Receiving.razor` | `Tests/InventoryTests.cs` |
| 在庫オペレーション（照会・移動・調整・分割/統合・廃棄・期限） | D-10-30 / D-30-10 / D-40-40 | `InventoryController.cs` `api/inventory`<br>`Api/Services/InventoryService.cs` | Inventory.cs: InventoryStock / InventoryTransaction | `/inventory` `Inventory.razor` | `Tests/InventoryTests.cs` |
| 出庫・ピッキング・工程払出（FEFO自動引当） | D-20-10 / D-20-20 | `PickingOrdersController.cs` `api/picking-orders` | Inventory.cs: PickingOrder / PickingLine | `/picking` `Picking.razor` | `Tests/InventoryTests.cs` |
| 出荷（出荷判定ゲート付き） | D-40 / H-10-10 | `ShippingOrdersController.cs` `api/shipping-orders` | Inventory.cs: ShippingOrder / ShippingLine | `/shipping` `Shipping.razor`<br>`/print/shipping/{id}` `Print/ShippingSlip.razor` | `Tests/InventoryTests.cs` |
| 棚卸（スナップショット→実棚→差異→確定） | D-50-10 | `StocktakesController.cs` `api/stocktakes` | Inventory.cs: Stocktake / StocktakeLine | `/stocktakes` `Stocktakes.razor`<br>`/print/stocktake/{id}` `Print/StocktakeSheet.razor` | `Tests/InventoryTests.cs` |
| ロケーション・棚番管理 | D-50-20-01 | `LocationsController.cs` `api/locations` | Masters.cs: Location（`WorkCenterId`＝所属する資源。**段は問わない**） | `/masters` `Masters/LocationsTab.razor` | `Tests/MasterTests.cs` |

## E. 設備保全

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 作業区／資源階層（BOR。工場/ライン/エリア/作業区） | I-10-20-02 | `WorkCentersController.cs` `api/work-centers`<br>`Api/Policies/WorkCenterHierarchyPolicy.cs`（段の妥当性・循環。単票APIとCSV取込の**両方**から呼ぶ） | Masters.cs: WorkCenter（自己参照。`Level` は文字列保存のため**DB側で並べると段の順にならない**。取得後に並べ直す） | `/masters` `Masters/WorkCentersTab.razor` | `Tests/MasterTests.cs` / `MasterCsvTests.cs` |
| 設備台帳／BOE・保全部品 | E-10-10 / I-10-20 | `EquipmentsController.cs` `api/equipments`<br>保全部品は `api/equipments/{id}/parts`（**設備ごとの一括置換**） | Masters.cs: Equipment（`WorkCenterId`＝設置場所の正。**作業区（最下段）のみ**。`Site` は移行用の旧項目）<br>Masters.cs: **EquipmentPart**（品目参照。資産管理部品/消耗品の区分。`MaintenanceParts` の自由記述は移行元） | `/masters` `Masters/EquipmentsTab.razor` | `Tests/MasterTests.cs` |
| 保全手順書（版数管理） | E-10-20 / E-20-30 | `MaintenanceProceduresController.cs` `api/maintenance-procedures` | Maintenance.cs: MaintenanceProcedure | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 保全計画（中長期・年次） | E-30-10 | `MaintenancePlansController.cs` `api/maintenance-plans` | Maintenance.cs: MaintenancePlan | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 保全指示・実績・突発依頼 | E-30-20 / E-30-30 / E-40 | `MaintenanceOrdersController.cs` `api/maintenance-orders`<br>消費部材の在庫引落しは `POST {id}/record` の `parts`（**`InventoryService.RemoveAsync` 経由**。区分 `MaintenanceIssue`） | Maintenance.cs: MaintenanceOrder / MaintenanceRecord / **MaintenanceRecordPart**（ロット単位の消費明細。`PartsUsed` の自由記述は補足） | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 消耗材モニタリング | E-20-10-04 | `MaintenanceOrdersController.cs` `GET api/maintenance-orders/parts-consumption`（品目別の消費数量＋現在庫） | Maintenance.cs: MaintenanceRecordPart | `/maintenance` `Maintenance.razor`（消耗材モニタリングタブ） | `Tests/MaintenanceTests.cs` |
| 治工具マスタ・寿命管理・利用実績 | E-60 | `ToolsController.cs` `api/tools`<br>`ToolUsagesController.cs` `api/tool-usages` | Masters.cs: Tool<br>Maintenance.cs: ToolUsage | `/masters` `Masters/ToolsTab.razor`<br>`/tool-management` `ToolManagement.razor` | `Tests/MaintenanceTests.cs` |

## F. 従業員管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 工場従業員（ユーザー）管理・論理削除 | F-10-10 | `UsersController.cs` `api/users` | `Core/Entities/AppUser.cs`（`WorkCenterId`＝作業場所・`Department`＝所属・`ShiftId`＝所属する直） | `/masters` `Masters/UsersTab.razor` | `Tests/MasterTests.cs` |
| 勤務シフト（直） | F-10-10-01 | `ShiftsController.cs` `api/shifts`（**時間帯が重なる直は登録不可**。所属者がいる直は無効化不可） | Masters.cs: Shift（夜勤は `EndTime <= StartTime` で日跨ぎを表す。翌日フラグは持たない） | `/masters` `Masters/ShiftsTab.razor` | `Tests/MasterTests.cs` `Tests/MasterCsvTests.cs` |
| スキル・資格マスタと割当（有効期限） | F-20-10 | `SkillsController.cs` `api/skills` | Masters.cs: SkillMaster / UserSkill | `/masters` `Masters/SkillsTab.razor` | `Tests/MasterTests.cs` |

## H. 出荷判定・トレーサビリティ

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 出荷判定（可／保留／特採・単段階承認） | H-10-10 | `ShipmentJudgmentsController.cs` `api/shipment-judgments` | Quality.cs: ShipmentJudgment | `/shipment-judgments` `ShipmentJudgments.razor`<br>`/print/shipment-judgment/{id}` `Print/ShipmentJudgmentDoc.razor` | `Tests/QualityTests.cs`<br>出荷ゲートは `InventoryTests.cs` |
| ロットトレーサビリティ（前方・後方追跡） | H-30-10 | `TraceabilityController.cs` `api/traceability`（`/history` は製造・検査・在庫・状態・訂正・**設備稼働**の履歴を返す） | Production.cs: Lot<br>Execution.cs: MaterialConsumption<br>Maintenance.cs: EquipmentLog | `/traceability` `Traceability.razor`<br>`Web/Shared/TraceTree.razor` | `Tests/QualityTests.cs` |

---

## 基盤・横断（業務機能ではないが変更頻度が高い）

| 関心事 | 実装 | 備考 |
|:--|:--|:--|
| 認証（JWT＋リフレッシュ） | `Api/Controllers/AuthController.cs` `api/auth`<br>`Api/Services/JwtTokenService.cs` / `RefreshTokenService.cs` / `SigningKeyProvider.cs` / `JwtOptions.cs`<br>`Web/Auth/AuthService.cs` / `AuthMessageHandler.cs` / `TokenStore.cs` / `ApiAuthenticationStateProvider.cs` | Spec.md 7.4。`/login` `Login.razor`、`/change-password`。`Tests/AuthTests.cs` / `TestAuth.cs` |
| エラー応答（競合の変換） | `Api/MesAppExceptionFilter.cs`<br>`Api/MesAppHost.cs`（`AddProblemDetails` / `UseExceptionHandler`） | Spec.md 3.9「競合時の応答」。楽観ロック（`DbUpdateConcurrencyException`）・採番衝突（`DbUpdateException`）を409、`InventoryException`を400の日本語ProblemDetailsへ。**コントローラ側に例外処理を増やさない** |
| 初回パスワード変更の強制 | `Api/MustChangePasswordFilter.cs`（＋`AllowPendingPasswordChange`属性）<br>`Core/Constants/MesClaimTypes.cs`<br>`Api/Services/JwtTokenService.cs`（クレーム付与）<br>`Web/Layout/MainLayout.razor`（画面誘導） | Spec.md 7.4。未変更のトークンは参照系も403。素通しするアクションには`[AllowPendingPasswordChange]`を付ける（現状は`api/auth/me`と`api/auth/change-password`のみ）。`Tests/AuthTests.cs` |
| ロール定義・権限グループ | `Core/Constants/MesRoles.cs`（7ロール）<br>`Api/RoleGroups.cs`（MasterWrite / ProductionManage / UserAdmin / InventoryManage 等） | 新しい組み合わせが要るときだけ RoleGroups に追加 |
| 初期セットアップ（初期管理者） | `Api/Controllers/SetupController.cs` `api/setup`<br>`Api/Services/IdentitySeeder.cs`（`SeedAsync` / `IsInitialPasswordPendingAsync`） | Spec.md 2.2 E。`/setup` `Setup.razor`。パスワードは `MesAdmin:Password` の指定か `MesAdmin:GeneratePassword=true` の自動生成のいずれか。**両方ないときは作らない**。自動生成値は保存しないため、変更が済むまで起動のたびに再生成する（デスクトップの案内表示用）。`Tests/SetupTests.cs` |
| 選択肢（ドロップダウン） | `Core/Contracts/Common/Selection.cs`（`OptionQuery` / `OptionsResult<T>`）<br>`Api/QueryableOptionsExtensions.cs`（`ToOptionsResultAsync`）<br>`Web/Shared/OptionSelect.razor`<br>API: `api/inventory/stocks/options` / `api/work-orders/options` / `api/shipping-orders/options` / `api/products/options` / `api/users/options`（作業者。差立で使う。ユーザー管理の一覧は管理者専用のまま） | Spec.md 7.5。**一覧（ページング）とは別物**。件数が増えたときに要るのは検索であってページ送りではない。上限超過は `truncated` で画面に伝える（黙って切らない）。スキャンしたコードをそのまま `q` に渡せる。`Tests/InventoryTests.cs` / `ProductionTests.cs` / `Client.Web.Tests/OptionSelectTests.cs` |
| 工程別進捗の集計 | `WorkOrdersController.ProcessSummary` `api/work-orders/process-summary`（`?workCenterId=` で作業区絞り込み。**上位を指定すると配下へ展開**する：`WorkCenterHierarchyPolicy.SelfAndDescendantIds`）<br>`Core/Contracts/Production`: `ProcessProgressRow` | Spec.md 7.5。一覧を全件取って画面で数えない。`/process-progress` の「工程別の進捗」が使う。`Tests/ProductionTests.cs` |
| 一覧のページング | `Core/Contracts/Common/Paging.cs`（`PageQuery` / `PagedResult<T>`）<br>`Api/QueryablePagingExtensions.cs`（`ToPagedResultAsync`）<br>`Web/Shared/Pager.razor` | Spec.md 7.5。**アクションの引数名は `paging`**（`page` にするとクエリの `page` とプレフィックスが衝突して `pageSize` が効かない）。エンティティで取得してから組み立てる一覧は `PagedResult.Map(...)`。対象外の一覧（選択肢用・クライアント側絞り込み）はSpec.md 7.5参照。`Tests/InventoryTests.cs` / `Client.Web.Tests/PagerTests.cs` |
| ビルド共通設定 | `Directory.Build.props` | `TreatWarningsAsErrors` でDoD 1（新規の警告を増やさない）をビルドで担保する。`TargetFramework` は Desktop だけ `net10.0-windows` のため各csprojに残す |
| クライアントのエラー処理 | `Web/Layout/MainLayout.razor`（`ErrorBoundary`）<br>`Web/Auth/AuthMessageHandler.cs`（401時のリフレッシュ→失敗ならログイン画面へ）<br>`Web/Auth/AuthService.cs`（`EndSession`） | 画面のGET失敗でアプリ全体が操作不能にならないようにする。書き込み系は各画面の`_error` + `<Notice>` が担当（従来どおり） |
| 監査ログ | `Core/Abstractions/IAuditLogger.cs`<br>`Infra/Services/AuditLogger.cs`<br>`Core/Entities/AuditLog.cs` | `Api/Controllers/AuditLogsController.cs` `api/audit-logs`（参照専用・システム管理者のみ）<br>`/audit-logs` `Web/Pages/AuditLogs.razor`<br>`Tests/AuditLogTests.cs`。Spec.md 7.6。**全ての書き込み系アクションで呼ぶ**。変更前後を追跡する操作は `detail:` に匿名オブジェクト（`{ before, after, reason }`）を渡す＝JSON保存。要約でよい操作は文字列のまま |
| 採番（指図番号・ロット番号等） | `Api/Services/NumberingService.cs`<br>`Core/Entities/NumberSequence.cs` | Spec.md 3.9。新しい採番区分はここに追加。払い出しは採番テーブルの1行を更新してから読む（最大値+1にしない）。**変更追跡を使わない**（呼び出し側の未確定の変更を書き込まないため`ExecuteUpdate`と生SQL）。`Tests/InventoryTests.cs` |
| ロット使用可否（投入・引当・出荷の共通判定） | `Api/Policies/LotUsabilityPolicy.cs` | Spec.md 3.9。ステータス・有効期限の条件は**ここだけ**に置く。呼び先は `WorkOrderExecutionController`（投入）／`InventoryService.AllocateFefoAsync`（FEFO）／`ShippingOrdersController`（出荷） |
| 製造条件の逸脱判定 | `Api/Policies/ControlItemDeviationPolicy.cs` | Spec.md 5.7。基準は**マスタ現在値ではなく作業指示のスナップショット**（`WorkOrderControlItem`）。数値なし・上下限なしは判定せず`null`のまま（`false`にしない）。呼び先は `WorkOrderExecutionController.AddDataRecords` |
| 直（シフト）の時間帯判定 | `Api/Policies/ShiftSchedulePolicy.cs` | Spec.md 5.7。日跨ぎ（`EndTime <= StartTime`）・重なり判定・時刻→直の解決。単票APIとCSV取込の両方から通す。重なり判定は**1日を分に開いて突き合わせる**（開始・終了の大小比較だと 22:00〜06:00 と 05:00〜09:00 の重なりを見落とす） |
| 部材投入の照合（予定材料） | `Api/Policies/MaterialIssuePolicy.cs` | Spec.md 3.9・5.7。基準はMBOMの現在値ではなく**指図の予定材料**。呼び先は `WorkOrderExecutionController.AddConsumption` |
| 作業指示ステータス変更（＋状態履歴） | `Api/Services/WorkOrderStatusService.cs`<br>`Core/Entities/Production.cs`: WorkOrderStatusHistory | Spec.md 5.2。`WorkOrder.Status` を**直接代入しない**。履歴は `GET api/work-orders/{id}/status-history` |
| システム管理者を失わない | `Api/Policies/LastAdminPolicy.cs` | Spec.md 3.6。有効なシステム管理者が0人になる無効化・降格を拒否する。呼び先は `UsersController.Update`（1件ずつ判定）／`MasterCsvService.Import.ImportUsersAsync`（**全行の適用後**に判定。行順で引き継ぎを弾かないため）。`Tests/MasterTests.cs` / `MasterCsvTests.cs` |
| 出荷判定ゲート | `Api/Policies/ShipmentGatePolicy.cs` | Spec.md 3.9。承認済みの「可／特採」判定の条件はここだけに置く |
| ロット在庫ステータス変更（＋状態履歴） | `Api/Services/LotStatusService.cs`<br>`Core/Entities/Production.cs`: LotStatusHistory | Spec.md 5.3。`Lot.StockStatus` を**直接代入しない**。呼び先は `InventoryController`／`InspectionOrdersController`／`NonconformanceController`／`ReceivingController` |
| ロット系譜（分割・統合・振替） | `Core/Entities/Production.cs`: LotGenealogy<br>`InventoryController.AddGenealogy` | Spec.md 5.3・5.7。追跡の正は `Lot.ParentLotId` ではなくこちら。`TraceabilityController` はこの関係を辿る |
| 製造日（業務日付）境界 | `Core/Abstractions/IBusinessDateService.cs`<br>`Api/Services/BusinessDateService.cs` | Spec.md 7章 |
| DB・DbContext・スキーマ | `Infra/MesAppDbContext.cs`（653行）<br>`Infra/DependencyInjection.cs`（起動時 `MigrateAsync`）<br>`Infra/DatabaseOptions.cs` | SQLite。`Infra/Migrations/` は**読まない** |
| 列挙型（全業務共通） | `Core/Entities/Enums.cs`（36種） | ステータス追加はここ。UI表示名は `Web/Shared/Labels.cs` |
| DTO | `Core/Contracts/{Auth,Execution,Inventory,Maintenance,Masters,Production,Quality,Setup,Users}/` | すべて `record`。エンティティを直接返さない |
| 共通UIコンポーネント | `Web/Shared/`<br>Notice / CsvIoPanel / StatusBadge / BarMeter / ScanInput / PrintButton / TraceTree / Labels.cs / Code39.cs | 新規CSSクラスを増やさない |
| 画面導線 | `Web/Layout/NavMenu.razor` / `MainLayout.razor` / `EmptyLayout.razor`（印刷用） | 新規画面は NavMenu 登録を忘れない |
| Webアプリの組み立て（DI・パイプライン） | `Api/MesAppHost.cs`（`Build` / `InitializeAsync`）<br>`Api/Program.cs`（サーバー実行の1行だけ） | サービス登録・ミドルウェアの追加は**ここ**。サーバー実行とデスクトップ実行の共通の起点 |
| 静的配信（WASMをAPIが配信） | `Api/MesAppHost.cs`（`UseStaticWebAssets` / `UseBlazorFrameworkFiles`） | Spec.md 2.1。`Tests/StaticHostingTests.cs` |
| データ保存先（DB・署名鍵） | `Infra/MesAppDataDirectory.cs` | Spec.md 4章。相対パスは `%LOCALAPPDATA%\ParallelFactoryMES` 基準に解決。環境変数 `MESAPP_DATA_DIR` で変更可 |
| デスクトップ配布（MSIX） | `src/MesApp.Desktop/`（`Program.cs` / `MainForm.cs` / `Package.appxmanifest` / `Assets/`）<br>`build/Pack-Msix.ps1` / `New-MsixAssets.ps1` / `msix-identity.json` | Spec.md 7.8。手順は `docs-dev/MsixRelease.md`。業務ロジックは持たない（Kestrel起動＋WebView2表示のみ） |
| テスト基盤 | `Tests/ApiFactory.cs`（一時SQLite）/ `TestAuth.cs` / `Phase3TestData.cs`<br>`tests/MesApp.Client.Web.Tests/`（bUnit。`LayoutTests.cs`） | APIテストの基盤は増やさない。bUnit側は**全画面に効く横断的な振る舞いだけ**（`MainLayout` の初期パスワード誘導・`ErrorBoundary`）。画面ごとのテストは作らない |
