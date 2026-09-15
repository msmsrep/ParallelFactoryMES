namespace MesApp.Core.Abstractions;

/// <summary>
/// 製造日（業務日付）の算出（Spec.md 3.9：日付境界は既定で午前6時、設定で変更可能。
/// 夜勤の日跨ぎ実績を同一製造日に集計するため、日次集計はすべてこの境界を用いる）
/// </summary>
public interface IBusinessDateService
{
    /// <summary>指定時刻（ローカル時刻）が属する製造日を返す</summary>
    DateOnly GetBusinessDate(DateTimeOffset moment);

    /// <summary>現在時刻が属する製造日</summary>
    DateOnly Today { get; }

    /// <summary>指定製造日の開始・終了時刻（ローカル）を返す</summary>
    (DateTimeOffset Start, DateTimeOffset End) GetRange(DateOnly businessDate);

    /// <summary>
    /// 製造日の境界時刻（既定6時。`BusinessDay:BoundaryHour`）。
    /// 直の時間帯がこの時刻をまたぐと、同じ直の実績が2つの製造日へ分かれるため、
    /// 直の登録時にそれを警告するのに使う（Spec.md 5.7）
    /// </summary>
    int BoundaryHour { get; }

    /// <summary>
    /// 工場のタイムゾーンへ変換する。サーバー側で時刻を文字列にする場面（履歴の表示など）で使う。
    /// UTCのまま文字列にすると、画面がローカル時刻へ直して表示する箇所と食い違う
    /// </summary>
    DateTimeOffset ToFactoryTime(DateTimeOffset moment);
}
