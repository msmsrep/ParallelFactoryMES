# MES設計基礎ガイド 適合レビュー

`ref/00.MES設計基礎ガイド（k_mt）.pdf`（105頁・ロット製造MESの設計基礎）を読み、本リポジトリの実装と突き合わせた結果。
**提案であり、決定ではない。** 着手する場合は1項目＝1セッション（`Orchestration.md` §4.1）でタスクカード化する。

### 対応状況

| 指摘 | 状態 |
|:--|:--|
| 1. 出荷実行のロットステータス未確認 | **対応済**（Spec.md 改訂11、`Api/Policies/LotUsabilityPolicy.cs`） |
| 2. 有効期限切れロットの投入・引当 | **対応済**（同上） |
| 5. ロット統合の系譜が残らない | **対応済**（Spec.md 改訂12、`LotGenealogy`。統合されたロットも前方・後方追跡できる） |
| 6. ロット状態・作業指示状態の履歴がない | **一部対応**（`LotStatusHistory` と `LotStatusService` でロットの状態遷移を記録。`WorkOrder.Status` の履歴は未着手） |
| 3. 部材投入のMBOM照合がない | **一部対応**（Spec.md 改訂14、`MaterialIssuePolicy`。MBOM外の品目は投入不可。代替材料は代替部品グループで表現し、代替使用時の個別承認・理由記録と予定/実績の対比表示は未着手） |
| 4. 検査規格の版数が値を保存していない | **対応済**（Spec.md 改訂13、`InspectionOrderItem` へ発行時点の基準をスナップショット） |
| 7. 実績訂正が上書き＋ログ文字列 | **対応済**（Spec.md 改訂15・16。`AuditLog.Detail` を構造化JSONへ。さらに訂正前の値を業務履歴（`ProductionRecordCorrection` / `InspectionResultCorrection`）として残し、トレース画面・検査詳細・検査成績書から参照できるようにした。訂正そのものへの承認フローは Spec.md の単段階承認方針に従い将来拡張） |
| 8. 数量区分が良品／不良の2つだけ | **対応済**（Spec.md 改訂17・18。不良数の内訳として廃棄数・再作業待ち数を追加し、さらに不良理由マスタ（`DefectReason`）と不良明細（`ProductionDefect`）で理由別の内訳を記録・集計できるようにした） |
| 10. 業務ルールの置き場 | **一部対応**（`Api/Policies/` を新設しロット使用可否を集約。投入可否のMBOM照合・出荷ゲートの残りは未着手） |
| 9 | 未着手 |

## 総評

業務カバレッジ（マスタ／製造実行／品質／在庫／保全／トレース）はガイドが挙げる「ロット製造MESの代表エンティティ」をほぼ満たしている。
ガイドの視点で不足が見えるのは機能ではなく、次の3系統に集約される。

| 系統 | ガイドの主張 | 本実装の状態 |
|:--|:--|:--|
| **状態と履歴の分離** | 「現在状態は業務を進めるため、履歴は後から説明するため。混ぜると進捗は出せても製造履歴を説明できない」 | 現在状態のみ保持。状態遷移の理由・判断者は `AuditLog.Detail` の文字列に散在 |
| **マスタ版と実績時点値** | 「最新マスタを過去実績へ無条件に適用しない。実績側に当時の値か参照した版を残す」 | 版数カラムはあるが旧版の値を残さず、実績はマスタ現在値を参照 |
| **業務ルール（照合・ゲート）の一貫性** | 「指定外材料は投入不可」「保留中ロットは進めない」を Domain 層に置き、全経路で効かせる | 経路ごとに個別実装のため、抜けている経路がある |

以下、優先度順。**1〜3 は現行仕様の範囲内の欠落（＝バグ寄り）**、4以降は設計上の拡張。

---

## 1. 出荷実行がロットの在庫ステータスを検査していない【最優先】

- 該当: `src/MesApp.Api/Controllers/ShippingOrdersController.cs:105-131`
- 現状: 出荷ゲートは「この出荷指示に対する承認済み判定書（可／特採）の存在」だけを見る。
  出荷明細のループ（:119-147）はロットの品目と数量しか突合せず、`Lot.StockStatus` を見ていない。
  引当を行う `InventoryService.RemoveAsync` も数量しか見ない（`src/MesApp.Api/Services/InventoryService.cs:40-63`）。
- 結果: **出荷判定の承認後に不適合報告で保留（`OnHold`）／不良（`Defective`）／廃棄予定になったロットが、そのまま出荷できる。**
  `NonconformanceController.cs:126-129` はまさにそのステータス変更を行う。
- ガイドの該当箇所: 「検査待ちのロットを誤って進めたくない」「判定済と未判定、保留中と出荷可を分けて扱いたい」（現場要求の章）、
  「物理的には完成品置場にあっても、システム上は出荷不可として扱う」（完成品計上の章）。
- 対応: `Ship` の明細検証で `lot.StockStatus != LotStockStatus.Normal` を `Conflict` で拒否する。特採（`SpecialAcceptance`）判定があるロットの扱いを明示的に決める（特採は保留のまま出荷を許す業務判断なので、判定書の `LotId` と突合して例外扱いにするのが自然）。
- 規模: 実装 約20行＋`Tests/InventoryTests.cs` に2ケース。

## 2. 有効期限切れロットが投入・自動引当できる

- 該当:
  - 手動投入 `src/MesApp.Api/Controllers/WorkOrderExecutionController.cs:214-218`（`StockStatus` のみ確認）
  - FEFO自動引当 `src/MesApp.Api/Services/InventoryService.cs:100-112`（`ExpiresOn` を**並べ替えにしか使っていない**）
- 結果: バックフラッシュ・ピッキング引当・手動投入のいずれでも期限切れロットが選ばれうる。
  期限接近の一覧（`InventoryController.cs:70-78`）はあるので、データは揃っているのに消費側でゲートしていない。
- ガイド: 「使用期限を過ぎた材料を投入したくない」「先入れ先出しの扱いも確認対象」（材料準備・投入の章）。
- 対応: `IBusinessDateService` の業務日付を基準に、`ExpiresOn < 業務日付` を引当対象から除外し、手動投入は拒否する。
  期限延長判定検査（C-20-50-04・`Spec.md` 153 で［将来拡張］）との整合として、「期限切れは投入不可、延長は将来」で線を引くのが素直。
- 規模: 実装 約25行＋`Tests/InventoryTests.cs` / `ExecutionTests.cs` に各1ケース。

## 3. 部材投入に指定材料との照合がない（MBOM照合・代替材料の承認）

- 該当: `WorkOrderExecutionController.cs:199-249`（`AddConsumption`）
- 現状: 任意のロットを投入できる。作業指示の品目のMBOM（`BomItem`）と照合していない。
  `BomItem.AlternativeGroup`（代替部品グループ）は定義済みだが**どこからも参照されていない**。
- ガイドが要求と要件の変換例として繰り返し使う中心例がこれ:
  「材料を間違えたくない」→「投入前に材料ロットを読み取り、指定材料と一致しない場合は投入不可にする」。
  さらに「代替材料を使う場合は、誰が代替を判断し、どの条件なら認め、予定材料と実績材料をどう残すか」。
- 対応（3段階。1段階目だけでも価値がある）:
  1. MBOMに無い品目のロット投入を既定で拒否（`Conflict`）。
  2. `AlternativeGroup` が一致する品目は「代替」として許可し、理由の入力を必須にする。
     `MaterialConsumption` に `IsSubstitute` / `SubstituteReason` / `ApprovedByUserId` を追加。
  3. 予定材料（MBOM×数量）と実績材料の対比を `WorkOrderSetup.razor` / `ProductionRecordEntry.razor` に表示。
- 規模: 1のみなら 約30行。2まで含めるとマイグレーション＋DTO＋画面で中規模。

## 4. 検査規格の「版数」が値を保存していないため、過去の成績書を再現できない

- 該当:
  - `src/MesApp.Api/Controllers/InspectionItemsController.cs:103-113` — 規格値を**上書き**して `Version++`
  - `src/MesApp.Api/Services/MasterCsvService.Import.cs:418-432` — CSV取込も同様
  - `src/MesApp.Core/Entities/Quality.cs` `InspectionOrderItem` — コメントは「指示作成時のスナップショット」だが、実体は `InspectionItemId` の参照のみ
  - 判定 `InspectionOrdersController.cs:401-406`、成績書表示 `:424` — いずれも**マスタの現在値**を参照
- 結果: 規格を改訂すると、**改訂前に検査したロットの成績書を再出力したとき、当時と違う規格値が印字される**。
  版数は増えるが旧版の値はどこにも残らないため、「版で追える」状態になっていない。
  `Print/InspectionCertificate.razor` を品質記録・顧客提出資料として使う前提なら、これは説明責任の問題になる。
- ガイド: 「検査項目マスタの最新値を表示するだけでは、過去ロットの判定根拠を再現できない。実績側に保持している当時の規格値、または参照した検査規格の版を帳票に出せるようにする」（帳票設計の章）、
  「マスタ変更コードでは、過去実績を最新マスタで上書きしないことが重要」（コードベース構成の章）。
- 対応: `InspectionOrderItem` に発行時点の `LowerLimit` / `UpperLimit` / `StandardValue` / `Method` / `SamplingCount` / `ItemVersion` を写す。
  自動判定・画面表示・成績書はスナップショット値を使い、マスタは新規指示の作成時のみ参照する。
- 規模: エンティティ＋マイグレーション＋DTO＋判定処理の差し替え＋`QualityTests.cs`。中規模だが影響範囲は品質モジュール内に閉じる。

## 5. ロット統合の系譜が残らず、前方追跡が切れる

- 該当: `src/MesApp.Api/Controllers/InventoryController.cs:280-313`（`Merge`）
- 現状: 統合は数量の移動のみ。統合先ロットに「どのロットを統合したか」のリンクが残らず、記録は `AuditLog.Detail` の文字列だけ。
  `Lot.ParentLotId` は単一親のため、そもそも統合（多対一）を表現できない。
  `TraceabilityController.cs:100,145` は `ParentLotId` だけを辿るので、**統合された材料ロットから完成品を洗い出せない**。
- ガイド: 「ロット分割・統合が発生しても、完成品から原材料へ、原材料から完成品へ追跡できるようロット系譜を持つ」（トレーサビリティモデルの章）。
- 対応: `LotGenealogy`（`ParentLotId`, `ChildLotId`, `RelationType`＝Split/Merge/Transfer, `Quantity`, `OccurredAt`, `PerformedByUserId`）を追加し、
  分割・振替・統合の3経路すべてを登録する。`TraceabilityController` の探索を `Lot.ParentLotId` からこのテーブルへ切り替える。
  `Lot.ParentLotId` は互換のため残してもよいが、追跡の正は系譜テーブルにする。
- 規模: 中規模（新エンティティ＋3経路の書き込み＋トレース探索の差し替え＋`QualityTests.cs`）。

## 6. ロット状態・作業指示状態の履歴がない

- 該当: `Lot.StockStatus` の書き換えが9箇所（`InspectionOrdersController.cs:152,260,314,385` / `InventoryController.cs:207` / `NonconformanceController.cs:126,129,214` / `ReceivingController.cs:110`。採番時の初期値設定を除く）。`WorkOrder.Status` も同様。
- 現状: 保留・解除の判断根拠は `AuditLog` の文字列 `detail` にしか残らず、ロット単位で時系列に並べられない。
  トレース画面・製造記録から「いつ誰がなぜ止め、何を根拠に解除したか」を出せない。
- ガイド: 「現在状態と履歴を分けて保持する」「保留は単なるメモではなくロットの進行を止める業務判断。誰が、いつ、なぜ止め、どの判断で解除したかを残す」（状態遷移・履歴管理の章）。
- 対応: `LotStatusHistory`（`LotId`, `FromStatus`, `ToStatus`, `Reason`, `RelatedNonconformanceId`, `RelatedInspectionOrderId`, `ChangedByUserId`, `ChangedAt`）を追加し、
  **状態変更を1つのサービスメソッドに集約**して7箇所すべてをそこ経由にする（集約しないと必ず書き漏れる）。
  `Traceability.razor` / `TraceTree.razor` に保留履歴を表示する。
- 規模: 中規模。5と同じセッションでまとめると DbContext とマイグレーションが1回で済む。

## 7. 実績訂正が上書き＋ログ文字列で、構造化された訂正履歴がない

- 該当: `src/MesApp.Api/Controllers/ProductionRecordsController.cs:69-76`（値を上書きし before/after を文字列で `AuditLog` に）、
  `Quality.cs` `InspectionResult.CorrectionNote`（自由記述のみ）
- 補足: `AuditLog.Detail` の XML コメントは「変更前後の値などを **JSON で格納**」だが、実際の呼び出しはすべて `$"before(...) -> after(...)"` 形式の文字列で、JSON ではない。コメントと実装が乖離している。
- ガイド: 訂正履歴の項目として「訂正前値・訂正後値・訂正者・訂正日時・訂正理由」を挙げ、
  「監査証跡を単なるアプリログで代替しない」とする（履歴・監査証跡の章）。
- 対応（軽い順）:
  1. `AuditLog.Detail` を実際に JSON で書く（`IAuditLogger` に `object? detail` オーバーロードを足す）。コメントとの乖離解消だけでも検索性が上がる。
  2. 訂正を業務履歴として扱うなら、訂正前レコードを残す方式（訂正レコードを追加し、元は無効化）へ変更する。
- 規模: 1は小。2は中（`B-70-30-01` の仕様見直しを伴うため `Spec.md` 更新が必要）。

## 8. 生産実績の数量区分が良品／不良の2つしかない

- 該当: `src/MesApp.Core/Entities/Execution.cs` `ProductionRecord.GoodQuantity` / `DefectQuantity`
- ガイド: 「予定1000個に対し、良品970・不適合20・再作業待ち10ということもある。良品・不適合・廃棄・再作業を分けて記録しなければ、出来高と在庫数量が合わなくなる」（完成品計上の章）。
- 現状の帰結: 不良数に廃棄と再作業待ちが混在するため、`QualityAnalysis` の不良率と実際の損失が一致しない。
  リワーク指図（`NonconformanceReport.ReworkOrderId`）との数量突合もできない。
- 対応: `ScrapQuantity` / `ReworkQuantity` を追加するか、不良を理由コード付きの明細（`ProductionDefect`）に分解する。
  後者はガイドが言う「理由コードの粒度は後の分析に影響する。細かすぎると現場が選べない、粗すぎると改善に使えない」に直結し、`QualityAnalysis` の精度も上がる。
- 規模: 中。`Spec.md` の B-40-10 節の更新が必要。

## 9. MBOM・工順に有効期間／版がない

- 該当: `Masters.cs` `BomItem` / `Routing` — 版・有効開始日・有効終了日なし。上書き更新のみ。
- ガイド: 「BOM、工程ルート、検査規格、作業手順は変わる。最新マスタを過去実績へ無条件に適用しない」（論理データモデルの章）。
  例として「5月にBOMを改訂した場合、4月製造ロットの実績を8月に参照するとき、最新BOMだけを参照すると当時の製造条件を誤って説明する」。
- ただしマスタ全体への版管理導入は影響が大きく、費用対効果が悪い。**より安いのは実績側スナップショット**:
  - 指図展開時に `WorkOrder` へ工順の標準時間・管理項目・チェックリストIDを写す（現在は `Routing` を都度参照）。
  - 指図展開時に「予定材料」を `MaterialIssuePlan`（指図×工程×品目×予定数量）として確定する。
    これは 3（MBOM照合）の照合先にもなるため、3とセットで設計すると1回の作業で両方の価値が出る。
- 規模: 中〜大。3の実装方針を決めるときに合わせて判断する。

## 10. 業務ルールの置き場（構造）

- 現状: `MesApp.Core` はエンティティ＋DTO のみ。判定ロジックはすべて Controller に直書き。
  そのため「ロットを使ってよいか」の判定が `WorkOrderExecutionController`（投入時）、`InventoryService.AllocateFefoAsync`（引当時）、`ShippingOrdersController`（出荷時）に**バラバラに実装され、条件も揃っていない**。指摘1・2はこの構造の帰結。
- ガイド: 「業務ルールは Domain 層／Application 層で明示する。SQLや画面Controllerに埋め込まない」「Domain層をDB・HTTP・外部システムから守る」。
- 提案: ポート＆アダプタの全面導入は本プロジェクトの規模に対して過剰。**判定関数の集約だけ行う**のが妥当:
  - `src/MesApp.Api/Policies/`（または既存の `Api/Services/` 配下）に
    `LotUsabilityPolicy`（ステータス・期限・保留の判定）、`MaterialIssuePolicy`（MBOM照合・代替可否）、`ShipmentGatePolicy`（出荷可否）を置く。
  - Controller は「入力検証 → Policy呼び出し → 保存 → 監査ログ」に徹する（現在の記述量とほぼ変わらない）。
  - `CLAUDE.md` の「API 側の規約」に「ロット可否・投入可否・出荷可否の判定は Policy に置き、Controller に書かない」を1行追加。
- 規模: 小〜中。1・2・3を実装するとき、その受け皿として同時に作るのが最も安い。

---

## 意図的にスコープ外と読める項目（対応不要・確認のみ）

ガイドが扱うが、`Spec.md` で明示的に対象外／将来拡張とされているもの。ガイドとの差分＝欠陥ではない。

| ガイドの論点 | 本プロジェクトの扱い |
|:--|:--|
| 前工程完了チェック（工程順序強制） | `Spec.md` 206「初期リリースでは順序強制はしない。着手順は差立で管理」 |
| 多段階承認ワークフロー | `Spec.md` 207「単段階承認＋監査ログ」 |
| 設備自動収集・秤量機連携・OPC UA | `Spec.md` 137/138/140/171 いずれも［将来拡張］。手入力／CSV取込で代替 |
| ERP／WMS／LIMS／QMS 連携、Outbox・再送・重複防止 | 単体完結の構成。ガイドの外部連携章・設備連携章は現時点では N/A |
| シリアル単位のトレース | `Spec.md` 190［将来拡張］ |
| オフライン運用・障害時の紙併用 | `Spec.md` 377 で PWA 化を将来検討としてスコープ外 |

ただし「正とするシステム／データ所有者を分けて決める」というガイドの考え方は、外部連携がなくても
**マスタCSV一括入出力の運用ルール**（誰が更新してよいか、取込が上書きしてよい範囲はどこか）として効く。
`MasterCsvService.Import.cs` は既存レコードを黙って上書きするので、`Readme.md` か `Spec.md` 3.1 に運用上の前提を1段落足す価値はある。

---

## 着手順の提案

| 段階 | 内容 | 目安 |
|:--|:--|:--|
| Step 1 | 指摘1・2（出荷ゲート、期限切れ）＋ 受け皿として `LotUsabilityPolicy` を作る（指摘10の一部） | 1セッション |
| Step 2 | 指摘5・6（`LotGenealogy` と `LotStatusHistory`）。マイグレーション1回で両方 | 1〜2セッション |
| Step 3 | 指摘4（検査規格スナップショット） | 1セッション |
| Step 4 | 指摘3・9（MBOM照合＋予定材料。`MaterialIssuePlan` を挟むか判断） | 2セッション |
| Step 5 | 指摘7・8（訂正履歴、数量区分）。`Spec.md` 改訂を伴う | 2セッション |

各段階の完了条件は `CLAUDE.md` の DoD（ビルド・テスト・`Spec.md` 更新・MES No の説明・変更ファイル数の一致）に従う。
`docs-dev/CodeMap.md` への行追加も忘れないこと。
