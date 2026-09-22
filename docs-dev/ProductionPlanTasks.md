# 生産進捗の予実管理 バックログ

生産管理が工場・ラインの進捗を計画と突き合わせて見られるようにする（MES.md A-30-10-01 生産進捗管理モニタリング）。
1カード＝1セッションで進める（`Orchestration.md` §4.1）。カードの間はセッションをクリアし、次へ進む前に前のカードをコミットする。
新しいセッションでは「`docs-dev/ProductionPlanTasks.md` の PLAN-0x を実行して」とだけ頼めば着手できるように書いてある。

## 背景

「予実」の「実」は揃っているが「予」（時間軸を持つ計画）が無い。

| 既存 | 中身 |
|:--|:--|
| `api/manufacturing-orders/progress` | 指図単位の進捗（計画数量と実績数量） |
| `api/work-orders/delays` | 納期超過・標準時間超過 |
| `api/productivity` | 標準時間の予実（工数） |
| `ManufacturingOrder` | 計画として持つのは `Quantity` と `DueDate` だけ。**いつ・どれだけ作る予定かの日付を持たない** |

計画立案（A-10）は MES の対象外（×）なので、**MES で計画を立てるのではなく、外から来た計画を受け取って実績と突き合わせる**位置づけにする。

## 決定事項（2026-09-23）

| 論点 | 決定 | 理由 |
|:--|:--|:--|
| 既存機能との分け方 | 別モジュール（`Planning`）にするが、**別プロジェクト・別DBにはしない**。DbContext・3プロバイダーのマイグレーション・製造日判定・ロール・多言語・グラフ部品・CSV基盤・監査ログは共有する | 別DBにするとマイグレーションが3方言×2系統になる。この規模では分離の効果より登録の仕組みを作る手間が勝つ |
| 依存の向き | **計画 → 既存実績の一方向**。計画側は `ProductionRecord` / `WorkOrder` を読むだけ。既存エンティティ・画面は計画を参照せず、既存テーブルに計画への外部キーを張らない | 既存の指図・実行の流れに変更を入れない |
| 計画の粒度 | 日別の計画数量。**キーは 製造日×品目×工程、作業区は任意の列**（一意キーには含める） | 実績を工程ごとの出来高にするため。同じ品目が同じ作業区で2工程を通ると、作業区だけでは計画と実績を1対1に突き合わせられない。作業区は作業指示に展開時点で固定されているので絞り込みに使える |
| 指図との関係 | 計画を指図に紐付けない。品目・工程・作業区・製造日で突き合わせる | 指図・作業指示の画面・CSV・実績CSVに手を入れずに済む（指図に予定着手日・完了日を持たせる案は結合が強いので採らなかった） |
| 実績 | **工程ごとの出来高**＝`ProductionRecord` の良品数。記録の開始時刻が属する製造日で振り分け（ダッシュボードと同じ）。**リワーク指図の産出は数えない**（`productivity` と同じ規則） | 同じ画面群で日付と数え方の意味をそろえる |
| 計画の入力 | 画面入力とCSV取込の両方。CSVは**マスタCSVの仕組み（キーで上書き）**に乗せる | 計画は改訂されるので、常に新規登録の実績CSVは合わない |

## バックログ

| ID | 内容 | 想定層 | 状態 |
|:--|:--|:--|:--|
| PLAN-01 | 生産計画のデータモデルと登録API | L1 | 完了（2026-09-23。Spec.md 改訂110） |
| PLAN-02 | 工程別の予実集計API | L1 | 完了（2026-09-23。Spec.md 改訂111） |
| PLAN-03 | 生産計画の画面（計画登録タブ・予実タブ） | L2 | 完了 |
| PLAN-04 | 生産計画のCSV取込・出力 | L1 | 完了（2026-09-23。Spec.md 改訂113） |

---

## タスク: PLAN-01 生産計画（日別・品目別・工程別の計画数量）のデータモデルと登録API
- 目的 / 背景: 予実管理（A-30-10-01）の「予」が無い。今ある計画は `ManufacturingOrder.Quantity` と `DueDate` だけで、日付の軸を持たない。MESは計画を立てない（A-10 は対象外）ので、外から来た計画を受け取る口として作る。**既存のエンティティや画面からは計画を参照しない（一方向の依存）**
- 変更対象:
  - `src/MesApp.Core/Entities/Planning.cs`（新規。`ProductionPlan`：`BusinessDate` / `ProductId` / `ProcessId` / `WorkCenterId?` / `PlannedQuantity` / `Note?`）
  - `src/MesApp.Infrastructure/MesAppDbContext.cs`（`DbSet` を追加）
  - `src/MesApp.Infrastructure/Configurations/PlanningConfigurations.cs`（新規。一意キーは 製造日・品目・工程・作業区）
  - マイグレーション：`./scripts/Migrations.ps1 -Add AddProductionPlan`（3プロバイダー分を自動生成。中身は開かない）
  - `src/MesApp.Core/Contracts/Planning/ProductionPlanDtos.cs`（新規）
  - `src/MesApp.Api/Controllers/ProductionPlansController.cs`（新規。`api/production-plans`。一覧は期間・品目・工程・作業区で絞り込み。登録・更新・削除は監査ログを残す）
  - `src/MesApp.Api/Localization/ApiText.en.resx`
  - `tests/MesApp.Api.Tests/ProductionTests.cs`
  - `Spec.md`（§3.1、§5.2、改訂履歴）と `docs-dev/CodeMap.md`（業務行を追加）
  - 調査で確定：書き込みに使うロールの組み合わせ（`Core/Constants/MesRoleGroups.cs` に生産管理担当者向けの既存定数があるか）
- 変更禁止: 既存エンティティ（`ManufacturingOrder`・`WorkOrder`・`ProductionRecord`）に列や外部キーを足さない。既存APIの契約を変えない。マイグレーションに生SQLを書かない。`Migrations/**` を開かない
- 受入条件(DoD):
  - 同じキー（製造日・品目・工程・作業区）の二重登録は 409（`ConflictProblem`）。存在しない品目・工程・作業区を指定したら 400
  - 計画数量が負の値なら 400。0 は許す（計画上の休止を表す）
  - 作業区の絞り込みで上位の段を指定したら、配下へ展開して返す（`WorkCenterHierarchyPolicy.SelfAndDescendantIds`）
  - 参照は認証済みなら誰でもできる。書き込みはロールが無いと 403
  - `DatabaseProviderTests` が緑（3プロバイダーのマイグレーションがそろっている）
  - `./scripts/I18nCheck.ps1` の終了コードが 0
  - `git diff --stat` に既存エンティティのファイル（`Production.cs`・`Execution.cs`）が出てこない
- 参照: Spec.md §3.1・§3.9（製造日）・§5.2・§5.7 / MES.md No.A-30-10-01 / CodeMap「製造指図」「作業区／資源階層」の行
- 想定層: L1。データモデルの変更（新しいテーブルと一意キーの決定）を含むため
- 想定変更ファイル数: 9＋マイグレーション3組（自動生成）

## タスク: PLAN-02 工程別の予実集計API
- 目的 / 背景: 計画と「工程ごとの出来高」を、製造日×品目×工程で突き合わせる。集計は画面でせず、APIが返す（§7.5）
- 変更対象:
  - `src/MesApp.Api/Services/ProductionPlanService.cs`（新規。予実の集計はここだけで行う）
  - `src/MesApp.Api/Controllers/ProductionPlansController.cs`（`GET api/production-plans/plan-actual?from=&to=&productId=&processId=&workCenterId=` を追加）
  - `src/MesApp.Core/Contracts/Planning/ProductionPlanDtos.cs`（予実の行）
  - 調査で確定：サービスをDIに登録する場所（`src/MesApp.Api` の他のサービスの登録箇所）
  - `tests/MesApp.Api.Tests/ProductionTests.cs`、`Spec.md`（§3.1）、`docs-dev/CodeMap.md`
- 変更禁止: `ProductivityController` と `DashboardService` の集計ロジックを書き換えない（リワークの判定は同じ規則にそろえるが、呼び出し元は変えない）。`ProductionRecord` を書き換えない
- 受入条件(DoD):
  - 実績は `ProductionRecord` の良品数を、**記録の開始時刻が属する製造日**で振り分ける（ダッシュボードと同じ基準）。期間は製造日の半開区間
  - **リワーク指図（`ManufacturingOrderType.Rework`）の産出は実績に数えない**（テストで確かめる）
  - 計画だけの日、実績だけの日も1行として返す（計画0、または実績0として並ぶ）。達成率は計画が0なら `null`
  - 製造日の境界（既定6時）をまたいだ実績が前日側に数えられることをテストで確かめる
  - 行は製造日×品目×工程で1行。同じキーに作業区違いの計画が複数あれば計画数量を合算する（作業区ごとに分けて並べない）
  - 作業区の絞り込みは、実績側では `WorkOrder.WorkCenterId`（展開時点の値）で行い、上位の段を指定したら配下へ展開する。**計画・実績とも「作業区が配下にあるもの」だけに揃える**（作業区なしの計画も、作業区なしの作業指示の実績もどちらも外す。片側だけが減って達成率がずれないようにする。テストで確かめる）
- 参照: Spec.md §3.1・§3.2（B-60-10-05 の規則）・§3.9・§7.5 / MES.md No.A-30-10-01 / CodeMap「生産性モニタリング」「ダッシュボード」の行
- 想定層: L1。実績の集計（数え方の規則を決める）を含むため
- 想定変更ファイル数: 6〜7

## タスク: PLAN-03 生産計画の画面（計画登録タブと予実タブ）
- 目的 / 背景: 計画を入力して、予実を表とグラフで見られるようにする
- 変更対象:
  - `src/MesApp.Client.Web/Pages/ProductionPlanPage.razor`（新規。タブバーと `@switch` だけ）
  - `src/MesApp.Client.Web/Pages/ProductionPlan/PlanEntryTab.razor`（新規）
  - `src/MesApp.Client.Web/Pages/ProductionPlan/PlanActualTab.razor`（新規。`Shared/Charts/ComboChart` で計画を棒、実績を折れ線に。期間の選択は `Dashboard/PeriodPicker` を使えるか調査で確定）
  - `src/MesApp.Client.Web/Layout/NavMenu.razor`、`src/MesApp.Client.Web/Shared/UiText.en.resx`
  - `Spec.md`（§6.1）、`docs-dev/CodeMap.md`
- 変更禁止: 新しいCSSクラスを作らない。既存の画面（`ProcessProgress` や `Dashboard`）に計画を差し込まない。画面では数えない（集計は PLAN-02 のAPIに任せる）
- 受入条件(DoD):
  - 書き込みの操作要素だけを `<AuthorizeView Roles="@MesRoleGroups.…">` で隠す（PLAN-01 と同じ定数を使う）
  - 失敗応答は `ReadErrorAsync` で読み、表示は `<Notice>` で行う
  - `./scripts/I18nCheck.ps1` の終了コードが 0
  - `preview_start mesapp` で、計画の登録→予実タブに表示→英語に切り替えても表示される、を目視で確かめる
- 参照: Spec.md §6.1・§7.9 / CodeMap「ダッシュボード」の行（グラフ部品）/ `MastersPage`・`MaintenancePage`（タブ構成の見本）
- 想定層: L2
- 想定変更ファイル数: 7

## タスク: PLAN-04 生産計画のCSV取込・出力
- 目的 / 背景: 上位システムやExcelからまとめて流し込めるようにする。計画は改訂されるので、常に新規登録の実績CSVではなく、**キーで上書き（upsert）するマスタCSVの仕組み**に乗せる
- 変更対象:
  - `src/MesApp.Api/Services/MasterCsvKinds.cs`
  - `src/MesApp.Api/Services/MasterCsvService.Import.cs`（該当箇所の±40行だけ読む）と `MasterCsvService.cs`（出力）
  - `src/MesApp.Api/Policies/ProductionPlanPolicy.cs`（新規。PLAN-01 で `ProductionPlansController` の private メソッドにある参照先の存在確認とキー重複の判定をここへ移し、単票APIとCSV取込の両方から呼ぶ）
  - `src/MesApp.Api/Controllers/ProductionPlansController.cs`（判定をポリシー呼び出しに置き換える）
  - `src/MesApp.Client.Web/Pages/ProductionPlan/PlanEntryTab.razor`（`<CsvIoPanel>` を置く）
  - `src/MesApp.Api/Localization/ApiText.en.resx`、`tests/MesApp.Api.Tests/MasterCsvTests.cs`
  - `Spec.md`（§3.8）、`docs-dev/CodeMap.md`
- 変更禁止: CSV取込の基盤（`CsvImport.cs`・`CsvBundle.cs`）の共通の振る舞いを変えない。単票APIと別の判定を書かない（PLAN-01 の検証を共通化して両方から呼ぶ）
- 受入条件(DoD):
  - キーは `BusinessDate, ProductCode, ProcessCode, WorkCenterCode(任意)`。同じキーなら計画数量を上書きし、新しいキーなら追加する
  - `WorkCenterCode` が空の行も1つのキーとして扱い、同じ製造日・品目・工程の作業区なしの計画があれば上書きする（二重に追加しない。単票APIと同じ扱い。テストで確かめる）
  - 存在しないコードや負の数量がある行はエラーになり、取込全体をロールバックする（既存のマスタCSVと同じ）
  - 出力したCSVをそのまま取り込み直しても差分が出ない（往復テスト）
  - ZIPの一括入出力（`api/masters/csv/bundle`）にも含まれる
  - `./scripts/I18nCheck.ps1` の終了コードが 0
- 参照: Spec.md §3.8 / CodeMap「マスタCSV一括入出力」の行
- 想定層: L1。CSV取込を含むため
- 想定変更ファイル数: 9〜10

自己チェック：どのカードも変更対象は3ファイルを超えるが、エンティティから画面まで1つの縦の流れで、ファイルを減らすとタスクが成り立たないためこの粒度にしている。「調査で確定」は各カード1項目以内。
