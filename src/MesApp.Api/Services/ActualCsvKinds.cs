using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;

namespace MesApp.Api.Services;

/// <summary>実績CSVの種別（列定義と、取込に必要なロール）</summary>
/// <param name="WriteRoles">
/// 取込に必要なロールグループ。<b>単票APIの <c>[Authorize(Roles = ...)]</c> と同じ定数を使う</b>
/// （片方だけ緩いと、画面からは登録できない実績がCSVからは入る。Spec.md 7.4）
/// </param>
public sealed record ActualCsvKind(CsvKindInfo Info, string WriteRoles);

/// <summary>
/// 実績（取引データ）のCSV一括取込に対応する種別（Spec.md 3.8）。
/// マスタCSVと違い<b>取込は常に新規登録</b>で、既存の実績を更新しない（訂正は理由付きの訂正機能で行う）。
/// 他のマスタ・実績はIDではなくコード・番号で参照する。
/// </summary>
public static class ActualCsvKinds
{
    public const string Receiving = "receiving";
    public const string ManufacturingOrders = "manufacturing-orders";
    public const string SetupRecords = "setup-records";
    public const string ChecklistRecords = "checklist-records";
    public const string Consumptions = "consumptions";
    public const string ProductionRecords = "production-records";
    public const string DataRecords = "data-records";
    public const string Inspections = "inspections";
    public const string WorkTimeRecords = "work-time-records";
    public const string TroubleReports = "trouble-reports";
    public const string EquipmentLogs = "equipment-logs";
    public const string ShippingOrders = "shipping-orders";
    public const string ShipmentJudgments = "shipment-judgments";
    public const string Shipments = "shipments";
    public const string MaintenanceOrders = "maintenance-orders";
    public const string MaintenanceRecords = "maintenance-records";
    public const string ToolUsages = "tool-usages";
    public const string ToolIssues = "tool-issues";
    public const string Calibrations = "calibrations";
    public const string InventoryOperations = "inventory-operations";
    public const string TransferOrders = "transfer-orders";
    public const string PickingOrders = "picking-orders";
    public const string Stocktakes = "stocktakes";
    public const string StocktakeCounts = "stocktake-counts";

    private const string OrderNoNote = "登録済みの指図番号（製造指図CSVの OrderNo）";
    private const string SequenceNote = "工順の工程順序。指図番号と合わせて作業指示を指す（展開済みであること）";
    private const string ShippingNoNote = "登録済みの出荷番号（出荷指示CSVの ShippingNo）";
    private const string DateTimeNote = "yyyy-MM-dd HH:mm（工場の時刻）。+09:00 などのオフセット付きも可";

    public static readonly List<ActualCsvKind> All =
    [
        new(new CsvKindInfo(Receiving, "受入", false,
        [
            new("ProductCode", "品目コード", true, "登録済みで有効な品目コード"),
            new("Quantity", "受入数量", true, "0より大きい数値"),
            new("LocationCode", "入庫先ロケーションコード", true, "登録済みで有効なロケーションコード"),
            new("LotNumber", "受入ロット番号", false, "空欄なら自動採番。既存のロット番号と重複できない"),
            new("ExpiresOn", "有効期限", false, "yyyy-MM-dd"),
            new("Note", "備考", false, null),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(ManufacturingOrders, "製造指図", false,
        [
            new("OrderNo", "指図番号", false,
                "空欄なら自動採番。後続の実績CSVから指図を指すので指定を推奨。MOで始まる番号は自動採番用のため使えない"),
            new("ProductCode", "品目コード", true, "登録済みで有効な品目コード（工順が必要）"),
            new("Quantity", "指図数量", true, "0より大きい数値"),
            new("DueDate", "納期", false, "yyyy-MM-dd"),
            new("OrderType", "指図区分", false, "Normal（通常）/ Spot（突発）/ Rework（リワーク）。省略時は通常"),
            new("SourceOrderNo", "元指図番号", false, "リワーク指図のときだけ必須。同じファイルの前の行の指図も指せる"),
            new("Note", "備考", false, null),
            new("Approve", "承認する", false, "true で登録に続けて承認する"),
            new("Expand", "工程展開する", false, "true で承認に続けて作業指示へ展開する（Approve も true が必要）"),
            new("OutputLotNumber", "産出ロット番号", false, "工程展開するときのロット番号。空欄なら自動採番"),
        ]), MesRoleGroups.ProductionManage),
        new(new CsvKindInfo(SetupRecords, "段取り実績", false,
        [
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("Type", "段取り区分", true, "Pre（前段取り）/ Post（後段取り）"),
            new("StartedAt", "開始日時", true, DateTimeNote),
            new("EndedAt", "終了日時", false, DateTimeNote),
            new("AbnormalityNote", "異常内容", false, null),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(ChecklistRecords, "チェックリスト実施", false,
        [
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("ChecklistCode", "チェックリストコード", true,
                "指図番号・工程順序・チェックリストコードが同じ行を1回の実施としてまとめる"),
            new("ItemSequence", "項目の表示順", true, "チェックリストマスタの項目の表示順。書かなかった項目は未チェック扱い"),
            new("IsChecked", "チェック済み", false, "true / false。省略時は true。必須項目が未チェックだと登録できない"),
            new("Note", "メモ", false, null),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(Consumptions, "部材投入", false,
        [
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("LotNumber", "投入ロット番号", true, "登録済みのロット番号"),
            new("LocationCode", "払出元ロケーションコード", true, null),
            new("Quantity", "投入数量", true, "0より大きい数値"),
            new("SubstituteReason", "代替部品の投入理由", false, "予定材料の代替部品を投入するときは必須"),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(ProductionRecords, "生産実績", false,
        [
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("GoodQuantity", "良品数", true, "0以上"),
            new("DefectQuantity", "不良数", false, "0以上。省略時0"),
            new("ScrapQuantity", "廃棄数", false, "不良数の内訳。省略時0"),
            new("ReworkQuantity", "再作業待ち数", false, "不良数の内訳。省略時0"),
            new("StartedAt", "開始日時", true, DateTimeNote + "。この時刻で直を決める"),
            new("EndedAt", "終了日時", false, DateTimeNote),
            new("OutputLocationCode", "入庫先ロケーションコード", false, "最終工程で良品があるときは必須"),
            new("Backflush", "バックフラッシュ", false, "true でこの工程で使う予定材料を 原単位×(良品+不良) だけ先入れ先出しで自動消費（代替部品は引かない）"),
            new("Defects", "不良理由別の内訳", false, "不良理由コード=数量 をセミコロン区切り（DR-02=2;DR-03=1）。合計は不良数以下"),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(DataRecords, "製造条件データ", false,
        [
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("ControlItemCode", "工程管理項目コード", false,
                "展開時に作業指示へ写した工程管理項目のコード。指定すると指示値・許容範囲と照合して逸脱を判定する"),
            new("NumericValue", "数値", false, "ControlItemCode を指定したときは必須"),
            new("Item", "項目", false, "ControlItemCode を省略したときは必須（指定時の既定は項目名）"),
            new("Value", "値（表示用）", false, "省略時は 数値＋単位"),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(Inspections, "検査（指示・実績・判定）", false,
        [
            new("InspectionKey", "検査のまとまり", true,
                "同じ値の行を1件の検査指示にまとめる（このファイルの中だけで使う名前。DBには残らない）"),
            new("Type", "検査種別", true, "Receiving / InProcess / FinalProduct / Sample / Reinspection。まとまりの最初の行の値を使う"),
            new("LotNumber", "対象ロット番号", false, "工程内検査以外で必須"),
            new("OrderNo", "指図番号", false, "工程内検査で必須（工程順序と合わせて作業指示を指す）"),
            new("Sequence", "工程順序", false, "工程内検査で必須"),
            new("ItemCode", "検査項目コード", true, "登録済みの検査項目。まとまりに含まれる項目がこの検査の対象になる"),
            new("SampleNo", "サンプル番号", false, "省略時は1"),
            new("MeasuredValue", "測定値", false, "規格値があれば自動判定する"),
            new("TextValue", "定性の記録", false, null),
            new("Judgment", "判定", false, "Pass（合格）/ Fail（不合格）。測定値で自動判定できない項目は必須"),
            new("DeviceCode", "検査機コード", false, "校正期限切れ・無効の検査機は使えない"),
            new("Judge", "総合判定する", false, "true で実績の登録に続けて総合判定する（まとまりの最初の行の値）"),
            new("Grade", "グレード", false, "総合判定でロットに付けるグレード（まとまりの最初の行の値）"),
            new("Note", "備考", false, "検査指示の備考（まとまりの最初の行の値）"),
        ]), MesRoleGroups.QualityManage),
        new(new CsvKindInfo(WorkTimeRecords, "作業時間", false,
        [
            new("Type", "作業区分", true, "Direct（直接作業）/ Indirect（間接作業）"),
            new("IndirectCategory", "間接作業の分類", false, "段取り・部材準備・設備メンテ など"),
            new("OrderNo", "指図番号", false, "直接作業で必須（工程順序と合わせて作業指示を指す）"),
            new("Sequence", "工程順序", false, "指図番号を書いたときは必須"),
            new("StartedAt", "開始日時", true, DateTimeNote),
            new("EndedAt", "終了日時", false, DateTimeNote),
            new("Note", "備考", false, null),
        ]), MesRoleGroups.ShopFloorRecord),
        new(new CsvKindInfo(TroubleReports, "製造トラブル報告", false,
        [
            new("OccurredAt", "発生日時", true, DateTimeNote),
            new("Category", "区分", true, "Quality（品質）/ Cost（コスト）/ Delivery（納期）/ Safety（安全）"),
            new("OrderNo", "指図番号", false, "作業指示に紐づける場合（工程順序と合わせて指す）"),
            new("Sequence", "工程順序", false, "指図番号を書いたときは必須"),
            new("EquipmentAssetNo", "設備の資産番号", false, null),
            new("Content", "内容", true, null),
        ]), AnyRole),
        new(new CsvKindInfo(EquipmentLogs, "設備稼働記録", false,
        [
            new("EquipmentAssetNo", "設備の資産番号", true, "登録済みで有効な設備"),
            new("Status", "区分", true, "Running（稼働）/ Stopped（停止）/ Setup（段取り）/ Failure（故障）/ Idle（アイドル）"),
            new("StartedAt", "開始日時", true, DateTimeNote + "。稼働率はこの時刻が属する製造日で集計する"),
            new("EndedAt", "終了日時", false, DateTimeNote + "。空欄は継続中（稼働率の集計には入らない）"),
            new("StopCause", "停止原因", false, "停止・故障のときは必須"),
            new("OrderNo", "指図番号", false, "作業指示に紐づける場合（工程順序と合わせて指す）"),
            new("Sequence", "工程順序", false, "指図番号を書いたときは必須"),
            new("Note", "備考", false, null),
        ]), AnyRole),
        new(new CsvKindInfo(ShippingOrders, "出荷指示", false,
        [
            new("ShippingNo", "出荷番号", true,
                "同じ番号の行を1件の出荷指示にまとめる。後続の出荷判定・出荷実行CSVから指示を指す。SHで始まる番号は自動採番用のため使えない"),
            new("Destination", "出荷先", true, "まとまりの最初の行の値を使う"),
            new("PlannedDate", "出荷予定日", false, "yyyy-MM-dd。まとまりの最初の行の値を使う"),
            new("ProductCode", "品目コード", true, "登録済みで有効な品目コード。1件の指示に同じ品目を2行書けない"),
            new("Quantity", "指示数量", true, "0より大きい数値"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(ShipmentJudgments, "出荷判定", false,
        [
            new("ShippingNo", "出荷番号", false, ShippingNoNote + "。出荷実行には出荷指示を対象にした承認済みの判定が必要"),
            new("LotNumber", "対象ロット番号", false, "登録済みのロット番号。ShippingNo と少なくともどちらかは必須"),
            new("Result", "判定", true, "Approved（可）/ Hold（保留）/ SpecialAcceptance（特採）"),
            new("Approve", "承認する", false, "true で判定の登録に続けて承認する"),
            new("Note", "備考", false, null),
        ]), MesRoleGroups.QaManage),
        new(new CsvKindInfo(Shipments, "出荷実行", false,
        [
            new("ShippingNo", "出荷番号", true, ShippingNoNote + "。同じ番号の行を1回の出荷にまとめる"),
            new("LotNumber", "出荷ロット番号", true, "登録済みのロット番号。出荷指示に含まれる品目で、使える状態（正常・期限内）であること"),
            new("LocationCode", "出荷元ロケーションコード", true, null),
            new("Quantity", "出荷数量", true, "0より大きい数値。出荷済みと合わせて指示数量を超えられない"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(MaintenanceOrders, "保全指示・突発依頼", false,
        [
            new("MaintenanceNo", "保全指示番号", true,
                "後続の保全実績CSVから指示を指す。MTで始まる番号は自動採番用のため使えない"),
            new("EquipmentAssetNo", "対象設備の資産番号", false, "対象治工具コードとどちらか一方は必須"),
            new("ToolCode", "対象治工具コード", false, "治工具メンテナンスの指示のとき"),
            new("RequestType", "依頼区分", false,
                "Planned（計画）/ Spot（突発依頼）。省略時は突発依頼。計画は保全担当者だけが登録できる"),
            new("ProcedureNo", "保全手順書番号", false, "登録済みで有効な保全手順書"),
            new("ScheduledDate", "予定日", false, "yyyy-MM-dd"),
            new("Note", "備考", false, null),
        ]), AnyRole),
        new(new CsvKindInfo(MaintenanceRecords, "保全実績", false,
        [
            new("MaintenanceNo", "保全指示番号", true,
                "登録済みの保全指示（保全指示CSVの MaintenanceNo）。同じ番号の行を1件の実績にまとめ、登録で指示は完了する"),
            new("StartedAt", "開始日時", true, DateTimeNote + "。まとまりの最初の行の値を使う"),
            new("EndedAt", "終了日時", false, DateTimeNote),
            new("Result", "実施結果", false, null),
            new("PartsUsed", "使用部品の補足", false, "自由記述（在庫を動かす部材は Part〜 の列に書く）"),
            new("Note", "備考", false, null),
            new("ResetToolLife", "寿命をリセットする", false, "true で治工具の寿命カウンタをリセットする（治工具メンテナンスの指示のみ）"),
            new("PartLotNumber", "消費部材のロット番号", false,
                "1行に1ロット。在庫から引き落とす。設備の資産管理部品・消耗品の区分は単票と同じに判定する"),
            new("PartLocationCode", "払出元ロケーションコード", false, "消費部材を書くときは必須"),
            new("PartQuantity", "消費数量", false, "消費部材を書くときは必須"),
            new("PartNote", "部材の備考", false, null),
        ]), AnyRole),
        new(new CsvKindInfo(ToolUsages, "治工具の利用実績", false,
        [
            new("ToolCode", "治工具コード", true, "登録済みで有効な治工具"),
            new("OrderNo", "指図番号", false, "作業指示に紐づける場合（工程順序と合わせて指す）"),
            new("Sequence", "工程順序", false, "指図番号を書いたときは必須"),
            new("UsageCount", "使用回数", false, "0以上の整数。使用時間とどちらかは0より大きいこと"),
            new("UsageHours", "使用時間", false, "0以上"),
            new("RecordedAt", "記録日時", false,
                DateTimeNote + "。空欄なら取り込んだ時刻。寿命の累計はメンテナンスで寿命をリセットした時刻より後の記録だけを数える"),
        ]), AnyRole),
        new(new CsvKindInfo(ToolIssues, "治工具の引当・払出", false,
        [
            new("ToolCode", "治工具コード", true, "使用中・メンテナンス中・寿命到達の治工具は引き当てられない"),
            new("OrderNo", "指図番号", true, OrderNoNote),
            new("Sequence", "工程順序", true, SequenceNote),
            new("Issue", "払い出す", false, "true で引当に続けて払出・受領確認まで進める（受領者は取り込んだユーザー）"),
            new("Return", "返却する", false, "true で返却まで進める（治工具は再び引当できるようになる）"),
            new("Note", "備考", false, null),
        ]), MesRoleGroups.ProductionManage),
        new(new CsvKindInfo(Calibrations, "検査機の校正", false,
        [
            new("DeviceCode", "検査機コード", true, "登録済みの検査機・測定器"),
            new("CalibratedOn", "校正日", true, "yyyy-MM-dd。検査機の最終校正日になる"),
            new("NextDueOn", "次回校正期限", false, "yyyy-MM-dd。空欄なら校正日＋校正周期（周期も無ければ期限なし）"),
            new("Result", "校正結果", false, "合格・調整後合格 など"),
        ]), MesRoleGroups.QualityManage),
        new(new CsvKindInfo(InventoryOperations, "在庫オペレーション", false,
        [
            new("Operation", "操作", true,
                "Move（移動）/ Adjust（数量調整）/ Status（状態変更）/ Split（分割）/ Merge（統合）/ Transfer（振替）/ Discard（廃棄）/ Return（返品）/ IssueReturn（払出戻し）"),
            new("LotNumber", "ロット番号", true, "操作するロット（統合では統合元）。前の行で分割・振替したロットも指せる"),
            new("LocationCode", "ロケーションコード", false, "操作する在庫の場所（移動元・払出戻しの戻し先）。状態変更以外で必須"),
            new("ToLocationCode", "移動先ロケーションコード", false, "移動で必須"),
            new("Quantity", "数量", false, "状態変更・統合以外で必須。数量調整では調整後の数量（0以上）、ほかは0より大きい数値"),
            new("NewLotNumber", "新ロット番号", false, "分割・振替で作るロットの番号。空欄なら自動採番（後の行から指すなら指定する）"),
            new("TargetLotNumber", "統合先ロット番号", false, "統合で必須。同じ品目・同じロケーションのロット"),
            new("ProductCode", "振替先品目コード", false, "品目振替のとき。振替では振替先品目か新ロット番号のどちらかが必要"),
            new("Status", "在庫状態", false,
                "状態変更で必須。Normal（正常）/ OnHold（保留）/ AwaitingInspection（検査待ち）/ Defective（不良）/ ToBeDiscarded（廃棄予定）"),
            new("OrderNo", "指図番号", false, "払出戻しで作業指示を記録する場合（工程順序と合わせて指す）"),
            new("Sequence", "工程順序", false, "指図番号を書いたときは必須"),
            new("Reason", "理由", false, "数量調整で必須。状態変更・廃棄・返品では状態履歴・在庫履歴に残る"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(TransferOrders, "搬送指示", false,
        [
            new("LotNumber", "ロット番号", true, "搬送するロット"),
            new("FromLocationCode", "移動元ロケーションコード", true, null),
            new("ToLocationCode", "移動先ロケーションコード", true, "登録済みで有効なロケーション"),
            new("Quantity", "数量", true, "0より大きい数値"),
            new("Execute", "移動を実行する", false, "true で指示に続けて在庫を移動し完了にする。空欄なら指示のまま残る"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(PickingOrders, "ピッキング指示", false,
        [
            new("PickingNo", "ピッキング番号", true,
                "同じ番号の行を1件の指示にまとめる。PKで始まる番号は自動採番用のため使えない"),
            new("Type", "払出区分", false,
                "ProcessIssue（工程払出）/ Shipping（出荷）。省略時は工程払出。まとまりの最初の行の値を使う"),
            new("OrderNo", "指図番号", false, "工程払出で必須（工程順序と合わせて払出先の作業指示を指す）"),
            new("Sequence", "工程順序", false, "工程払出で必須"),
            new("ShippingNo", "出荷番号", false, "出荷ピッキングで必須"),
            new("ProductCode", "品目コード", true, "1行＝1明細。ロット・ロケーションは先入れ先出し（有効期限優先）で自動引当する"),
            new("Quantity", "数量", true, "0より大きい数値"),
            new("Execute", "払い出す", false, "true でピッキングを実行し在庫を引き落とす（まとまりの最初の行の値）"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(Stocktakes, "棚卸指示", false,
        [
            new("StocktakeNo", "棚卸番号", true,
                "後続の実棚数CSVから指示を指す。STで始まる番号は自動採番用のため使えない"),
            new("LocationCode", "対象ロケーションコード", false, "空欄なら全ロケーション。作成時点の現在庫が明細になる"),
        ]), MesRoleGroups.InventoryManage),
        new(new CsvKindInfo(StocktakeCounts, "実棚数", false,
        [
            new("StocktakeNo", "棚卸番号", true, "登録済みの棚卸（棚卸指示CSVの StocktakeNo）。同じ番号の行をまとめて登録する"),
            new("LotNumber", "ロット番号", true, "棚卸の明細のロット"),
            new("LocationCode", "ロケーションコード", true, "棚卸の明細のロケーション"),
            new("CountedQuantity", "実棚数量", true, "0以上"),
            new("Finalize", "確定する", false, "true で実棚数の登録に続けて確定し、差異を在庫へ反映する（まとまりの最初の行の値）"),
        ]), MesRoleGroups.InventoryManage),
    ];

    /// <summary>
    /// 認証済みの全ロール。トラブル報告・設備稼働記録は単票APIもロールで絞っていない
    /// （異常は気づいた人がその場で上げられることを優先する。MesRoleGroups の方針）
    /// </summary>
    private static string AnyRole => string.Join(",", MesRoles.All);

    public static ActualCsvKind? Find(string kind) =>
        All.FirstOrDefault(k => string.Equals(k.Info.Kind, kind, StringComparison.OrdinalIgnoreCase));
}
