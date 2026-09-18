namespace MesApp.Core.Contracts.Common;

/// <summary>
/// 製造日（業務日付。Spec.md 3.9）の現在値。
/// </summary>
/// <remarks>
/// 画面が「当日」の集計を出すために要る。日付境界は既定6時で設定により変わるため、
/// クライアントが暦日（<c>DateTime.Today</c>）で代用すると、境界時刻をまたぐ時間帯に
/// 夜勤の実績が前日／当日へずれて数字が食い違う。境界の判断はサーバーに持たせ、
/// 画面はその結果を受け取るだけにする。
/// </remarks>
public record BusinessDateResponse(
    /// <summary>現在時刻が属する製造日</summary>
    DateOnly Today,
    /// <summary>製造日の境界時刻（既定6時。画面に「6時境界」と注記するために返す）</summary>
    int BoundaryHour);
