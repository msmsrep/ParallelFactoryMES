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
| 製造指図（発行・承認・変更・工程展開） | A-20 / B-10-10 | `Api/Controllers/ManufacturingOrdersController.cs`<br>`api/manufacturing-orders` | `Core/Entities/Production.cs`<br>ManufacturingOrder / WorkOrder / Lot | `/manufacturing-orders` `Web/Pages/ManufacturingOrders.razor`<br>`/manufacturing-orders/{id}` `ManufacturingOrderDetail.razor` | `Tests/ProductionTests.cs` |
| 品目マスタ・MBOM・工順/BOP | A-40-10 / A-40-20 | `ProductsController.cs` `api/products`<br>`ProcessesController.cs` `api/processes` | `Core/Entities/Masters.cs`<br>Product / BomItem / ProcessMaster / Routing | `/masters` `Web/Pages/Masters/ProductsTab.razor` / `ProcessesTab.razor` | `Tests/MasterTests.cs` |
| マスタCSV一括入出力 | Spec.md 3.1 | `MasterCsvController.cs` `api/masters/csv`<br>`Api/Services/MasterCsvService.cs` / `.Import.cs` / `MasterCsvKinds.cs` / `CsvTable.cs` / `CsvFile.cs` | （各マスタ） | `Web/Shared/CsvIoPanel.razor`（各Tabに配置） | `Tests/MasterCsvTests.cs` |

## B. 製造実行

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 作業指示・差立（作業員/設備割当・着手順） | B-10-20 / F-20-30-01 | `WorkOrdersController.cs` `api/work-orders` | Production.cs: WorkOrder | `/work-orders` `WorkOrders.razor`<br>`/dispatch` `Dispatch.razor` | `Tests/ExecutionTests.cs` |
| 実行系（着手・段取り・チェックリスト・部材投入・実績報告） | B-30-30-01 / B-20-50 / B-40-40 | `WorkOrderExecutionController.cs`<br>`api/work-orders/{id:int}` | `Core/Entities/Execution.cs`<br>SetupRecord / ChecklistRecord / ChecklistResultItem / MaterialConsumption / ProductionRecord / ProductionDataRecord | `/work-orders/{id}/setup` `WorkOrderSetup.razor`<br>`/work-orders/{id}/record` `ProductionRecordEntry.razor`<br>`/process-progress` `ProcessProgress.razor` | `Tests/ExecutionTests.cs` |
| 製造履歴訂正（監査ログ付き） | B-70-30-01 | `ProductionRecordsController.cs` `api/production-records` | Execution.cs: ProductionRecord | — | `Tests/ExecutionTests.cs` / `QualityTests.cs` |
| 作業時間記録（直接/間接） | B-30-30-02 / F-30-20-02 | `WorkTimeRecordsController.cs` `api/work-time-records` | Execution.cs: WorkTimeRecord | `ProductionRecordEntry.razor` 内 | **テストなし**（触るなら追加する） |
| 製造トラブル報告 | B-40-10-06 / B-60-10 | `TroubleReportsController.cs` `api/trouble-reports` | Execution.cs: TroubleReport | `ProcessProgress.razor` 内 | `Tests/ExecutionTests.cs` |
| 工程間搬送・移動指示 | B-50-10 / D-30-10-04 | `TransferOrdersController.cs` `api/transfer-orders` | Execution.cs: TransferOrder | `/inventory` `Inventory.razor` 内 | `Tests/InventoryTests.cs` |
| 設備稼働報告・稼働監視 | B-40-20 / E-20-10 | `EquipmentLogsController.cs` `api/equipment-logs` | `Core/Entities/Maintenance.cs`: EquipmentLog | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |

## C. 品質管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 検査項目・基準マスタ | C-10-10 | `InspectionItemsController.cs` `api/inspection-items` | Masters.cs: InspectionItem | `/masters` `Masters/InspectionItemsTab.razor` | `Tests/MasterTests.cs` |
| 検査指示・実績・判定・成績書 | C-20 | `InspectionOrdersController.cs` `api/inspection-orders` | `Core/Entities/Quality.cs`<br>InspectionOrder / InspectionOrderItem（**発行時点の基準スナップショット**。判定・成績書はこちらを使い、マスタ現在値を参照しない：Spec.md 5.7） / InspectionResult | `/inspections` `Inspections.razor`<br>`/inspections/{id}` `InspectionDetail.razor`<br>`/print/inspection/{id}` `Print/InspectionCertificate.razor` | `Tests/QualityTests.cs` |
| 不適合・逸脱管理（特採・廃棄・保留） | C-30 / B-40-30 | `NonconformanceController.cs` `api/nonconformances` | Quality.cs: NonconformanceReport | `/nonconformances` `Nonconformances.razor` | `Tests/QualityTests.cs` |
| 品質分析（不良項目別・工程別・期間別） | C-40-10 | `QualityAnalysisController.cs` `api/quality/summary` | （集計のみ） | `/quality-analysis` `QualityAnalysis.razor`<br>`Web/Shared/BarMeter.razor` | `Tests/QualityTests.cs` |
| チェックリストマスタ（HSE含む） | B-30-10 / G-20-20-02 | `ChecklistsController.cs` `api/checklists` | Masters.cs: Checklist / ChecklistItem | `/masters` `Masters/ChecklistsTab.razor` | `Tests/MasterTests.cs` |

## D. 物流／在庫管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 受入・受入ロット採番 | D-10-10 | `ReceivingController.cs` `api/receiving` | `Core/Entities/Inventory.cs`: InventoryStock / InventoryTransaction<br>Production.cs: Lot | `/receiving` `Receiving.razor` | `Tests/InventoryTests.cs` |
| 在庫オペレーション（照会・移動・調整・分割/統合・廃棄・期限） | D-10-30 / D-30-10 / D-40-40 | `InventoryController.cs` `api/inventory`<br>`Api/Services/InventoryService.cs` | Inventory.cs: InventoryStock / InventoryTransaction | `/inventory` `Inventory.razor` | `Tests/InventoryTests.cs` |
| 出庫・ピッキング・工程払出（FEFO自動引当） | D-20-10 / D-20-20 | `PickingOrdersController.cs` `api/picking-orders` | Inventory.cs: PickingOrder / PickingLine | `/picking` `Picking.razor` | `Tests/InventoryTests.cs` |
| 出荷（出荷判定ゲート付き） | D-40 / H-10-10 | `ShippingOrdersController.cs` `api/shipping-orders` | Inventory.cs: ShippingOrder / ShippingLine | `/shipping` `Shipping.razor`<br>`/print/shipping/{id}` `Print/ShippingSlip.razor` | `Tests/InventoryTests.cs` |
| 棚卸（スナップショット→実棚→差異→確定） | D-50-10 | `StocktakesController.cs` `api/stocktakes` | Inventory.cs: Stocktake / StocktakeLine | `/stocktakes` `Stocktakes.razor`<br>`/print/stocktake/{id}` `Print/StocktakeSheet.razor` | `Tests/InventoryTests.cs` |
| ロケーション・棚番管理 | D-50-20-01 | `LocationsController.cs` `api/locations` | Masters.cs: Location | `/masters` `Masters/LocationsTab.razor` | `Tests/MasterTests.cs` |

## E. 設備保全

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 設備台帳／BOE | E-10-10 / I-10-20 | `EquipmentsController.cs` `api/equipments` | Masters.cs: Equipment | `/masters` `Masters/EquipmentsTab.razor` | `Tests/MasterTests.cs` |
| 保全手順書（版数管理） | E-10-20 / E-20-30 | `MaintenanceProceduresController.cs` `api/maintenance-procedures` | Maintenance.cs: MaintenanceProcedure | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 保全計画（中長期・年次） | E-30-10 | `MaintenancePlansController.cs` `api/maintenance-plans` | Maintenance.cs: MaintenancePlan | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 保全指示・実績・突発依頼 | E-30-20 / E-30-30 / E-40 | `MaintenanceOrdersController.cs` `api/maintenance-orders` | Maintenance.cs: MaintenanceOrder / MaintenanceRecord | `/maintenance` `Maintenance.razor` | `Tests/MaintenanceTests.cs` |
| 治工具マスタ・寿命管理・利用実績 | E-60 | `ToolsController.cs` `api/tools`<br>`ToolUsagesController.cs` `api/tool-usages` | Masters.cs: Tool<br>Maintenance.cs: ToolUsage | `/masters` `Masters/ToolsTab.razor`<br>`/tool-management` `ToolManagement.razor` | `Tests/MaintenanceTests.cs` |

## F. 従業員管理

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 工場従業員（ユーザー）管理・論理削除 | F-10-10 | `UsersController.cs` `api/users` | `Core/Entities/AppUser.cs` | `/masters` `Masters/UsersTab.razor` | `Tests/MasterTests.cs` |
| スキル・資格マスタと割当（有効期限） | F-20-10 | `SkillsController.cs` `api/skills` | Masters.cs: SkillMaster / UserSkill | `/masters` `Masters/SkillsTab.razor` | `Tests/MasterTests.cs` |

## H. 出荷判定・トレーサビリティ

| 業務 | MES No | API | エンティティ | 画面 | テスト |
|:--|:--|:--|:--|:--|:--|
| 出荷判定（可／保留／特採・単段階承認） | H-10-10 | `ShipmentJudgmentsController.cs` `api/shipment-judgments` | Quality.cs: ShipmentJudgment | `/shipment-judgments` `ShipmentJudgments.razor`<br>`/print/shipment-judgment/{id}` `Print/ShipmentJudgmentDoc.razor` | `Tests/QualityTests.cs`<br>出荷ゲートは `InventoryTests.cs` |
| ロットトレーサビリティ（前方・後方追跡） | H-30-10 | `TraceabilityController.cs` `api/traceability` | Production.cs: Lot<br>Execution.cs: MaterialConsumption | `/traceability` `Traceability.razor`<br>`Web/Shared/TraceTree.razor` | `Tests/QualityTests.cs` |

---

## 基盤・横断（業務機能ではないが変更頻度が高い）

| 関心事 | 実装 | 備考 |
|:--|:--|:--|
| 認証（JWT＋リフレッシュ） | `Api/Controllers/AuthController.cs` `api/auth`<br>`Api/Services/JwtTokenService.cs` / `RefreshTokenService.cs` / `SigningKeyProvider.cs` / `JwtOptions.cs`<br>`Web/Auth/AuthService.cs` / `AuthMessageHandler.cs` / `TokenStore.cs` / `ApiAuthenticationStateProvider.cs` | Spec.md 7.4。`/login` `Login.razor`、`/change-password`。`Tests/AuthTests.cs` / `TestAuth.cs` |
| ロール定義・権限グループ | `Core/Constants/MesRoles.cs`（7ロール）<br>`Api/RoleGroups.cs`（MasterWrite / ProductionManage / UserAdmin / InventoryManage 等） | 新しい組み合わせが要るときだけ RoleGroups に追加 |
| 初期セットアップ（初期管理者） | `Api/Controllers/SetupController.cs` `api/setup`<br>`Api/Services/IdentitySeeder.cs` | Spec.md 2.2 E。`/setup` `Setup.razor`。`Tests/SetupTests.cs` |
| 監査ログ | `Core/Abstractions/IAuditLogger.cs`<br>`Infra/Services/AuditLogger.cs`<br>`Core/Entities/AuditLog.cs` | Spec.md 7.6。**全ての書き込み系アクションで呼ぶ** |
| 採番（指図番号・ロット番号等） | `Api/Services/NumberingService.cs` | 新しい採番区分はここに追加 |
| ロット使用可否（投入・引当・出荷の共通判定） | `Api/Policies/LotUsabilityPolicy.cs` | Spec.md 3.9。ステータス・有効期限の条件は**ここだけ**に置く。呼び先は `WorkOrderExecutionController`（投入）／`InventoryService.AllocateFefoAsync`（FEFO）／`ShippingOrdersController`（出荷） |
| 部材投入のMBOM照合 | `Api/Policies/MaterialIssuePolicy.cs` | Spec.md 3.9。作業指示の品目のMBOMにない品目は投入不可。呼び先は `WorkOrderExecutionController.AddConsumption` |
| ロット在庫ステータス変更（＋状態履歴） | `Api/Services/LotStatusService.cs`<br>`Core/Entities/Production.cs`: LotStatusHistory | Spec.md 5.3。`Lot.StockStatus` を**直接代入しない**。呼び先は `InventoryController`／`InspectionOrdersController`／`NonconformanceController`／`ReceivingController` |
| ロット系譜（分割・統合・振替） | `Core/Entities/Production.cs`: LotGenealogy<br>`InventoryController.AddGenealogy` | Spec.md 5.3・5.7。追跡の正は `Lot.ParentLotId` ではなくこちら。`TraceabilityController` はこの関係を辿る |
| 製造日（業務日付）境界 | `Core/Abstractions/IBusinessDateService.cs`<br>`Api/Services/BusinessDateService.cs` | Spec.md 7章 |
| DB・DbContext・スキーマ | `Infra/MesAppDbContext.cs`（653行）<br>`Infra/DependencyInjection.cs`（起動時 `MigrateAsync`）<br>`Infra/DatabaseOptions.cs` | SQLite。`Infra/Migrations/` は**読まない** |
| 列挙型（全業務共通） | `Core/Entities/Enums.cs`（36種） | ステータス追加はここ。UI表示名は `Web/Shared/Labels.cs` |
| DTO | `Core/Contracts/{Auth,Execution,Inventory,Maintenance,Masters,Production,Quality,Setup,Users}/` | すべて `record`。エンティティを直接返さない |
| 共通UIコンポーネント | `Web/Shared/`<br>Notice / CsvIoPanel / StatusBadge / BarMeter / ScanInput / PrintButton / TraceTree / Labels.cs / Code39.cs | 新規CSSクラスを増やさない |
| 画面導線 | `Web/Layout/NavMenu.razor` / `MainLayout.razor` / `EmptyLayout.razor`（印刷用） | 新規画面は NavMenu 登録を忘れない |
| 静的配信（WASMをAPIが配信） | `Api/Program.cs`（`UseStaticWebAssets`） | Spec.md 2.1。`Tests/StaticHostingTests.cs` |
| テスト基盤 | `Tests/ApiFactory.cs`（一時SQLite）/ `TestAuth.cs` / `Phase3TestData.cs` | 新しいテスト基盤は作らない |
