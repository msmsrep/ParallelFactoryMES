using MesApp.Api.Localization;
using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 勤務シフト（直）の時間帯判定（Spec.md 5.7 直の定義と実績の直。F-10-10-01）。
/// <para>
/// 直はマスタ登録（単票API・CSV）と実績の直の解決という複数の経路から必要になるため、
/// 判定はコントローラに書かずここへ集約する。
/// </para>
/// <para>
/// 夜勤は終了時刻が開始時刻以下になる（22:00〜06:00）。この形で日跨ぎを表し、
/// 「翌日フラグ」のような別項目は持たない。持たせると 06:00〜06:00 のような
/// 同じ時刻の組み合わせで意味が二通りになる。
/// </para>
/// </summary>
public static class ShiftSchedulePolicy
{
    /// <summary>翌日にまたぐ直か（夜勤）</summary>
    public static bool CrossesMidnight(TimeOnly start, TimeOnly end) => end <= start;

    /// <summary>その時刻が直の時間帯に入るか（開始を含み、終了は含まない）</summary>
    public static bool Covers(Shift shift, TimeOnly time) =>
        CrossesMidnight(shift.StartTime, shift.EndTime)
            // 日跨ぎは「開始以降」または「終了より前」のどちらかで当たる
            ? time >= shift.StartTime || time < shift.EndTime
            : time >= shift.StartTime && time < shift.EndTime;

    /// <summary>
    /// その時刻が属する直を返す（該当なしは null）。
    /// 時間帯が重なる直は登録できないので、当たる直は高々1つになる。
    /// </summary>
    public static Shift? Resolve(IEnumerable<Shift> shifts, TimeOnly time) =>
        shifts.FirstOrDefault(s => s.IsActive && Covers(s, time));

    /// <summary>
    /// 登録・更新しようとしている直が成立するかを確認する。問題があれば日本語の理由を返す。
    /// </summary>
    /// <param name="others">自分以外の有効な直（重なりの確認に使う）</param>
    public static string? Check(TimeOnly start, TimeOnly end, IEnumerable<Shift> others)
    {
        if (start == end)
        {
            return ApiText.T("開始時刻と終了時刻が同じです。24時間の直は表せないため、時間帯を分けて登録してください。");
        }
        // 重なりを許すと、ある時刻がどの直かを一意に決められず実績の直が定まらない
        foreach (var other in others)
        {
            if (Overlaps(start, end, other.StartTime, other.EndTime))
            {
                return ApiText.T("直 '{0}'（{1}）と時間帯が重なっています。", other.Code, Format(other.StartTime, other.EndTime));
            }
        }
        return null;
    }

    /// <summary>
    /// 製造日の境界時刻（Spec.md 3.9）をまたぐ直かを確認する。またぐなら日本語の警告を返す。
    /// <para>
    /// <b>拒否ではなく警告にとどめる。</b>境界をまたぐ直は運用として成立しうる（交代時刻と
    /// 日次締めの時刻が揃わない工場はある）ため、登録自体は通す。ただし、その直の実績は
    /// 2つの製造日へ分かれるので、日報（製造日単位）と直別集計の母数が食い違う。
    /// 気づかずに数字を突き合わせると原因の分からない差になるため、登録時に伝える。
    /// </para>
    /// </summary>
    public static string? CheckBusinessDateBoundary(TimeOnly start, TimeOnly end, int boundaryHour)
    {
        var boundary = new TimeOnly(boundaryHour, 0);
        // 開始が境界ちょうどなら、その直は境界の直後から始まるのでまたがない。
        // 終了が境界ちょうどの場合も InRange が終了を含まないため、ここで拾われない
        if (start == boundary || !InRange(start, end, boundary))
        {
            return null;
        }
        return ApiText.T("この直は製造日の境界時刻（{0:HH:mm}）をまたぎます。同じ直の実績が2つの製造日へ分かれるため、日次集計と直別集計で母数が食い違います。", boundary);
    }

    /// <summary>時間帯の表示（夜勤は翌日であることが分かるようにする）</summary>
    public static string Format(TimeOnly start, TimeOnly end) =>
        CrossesMidnight(start, end)
            ? ApiText.T("{0:HH:mm}〜翌{1:HH:mm}", start, end)
            : $"{start:HH:mm}〜{end:HH:mm}";

    /// <summary>
    /// 2つの時間帯が重なるか。
    /// <para>
    /// 日跨ぎの直（22:00〜06:00）はそのままでは start &gt; end で区間として扱えないため、
    /// <b>0時で切って「開始〜24:00」と「0時〜終了」の2本に分けてから</b>突き合わせる。
    /// こうすると、どの断片も start &lt; end の素直な区間になり、重なりは
    /// 「一方の開始が他方の終了より前」の組み合わせだけで判定できる。
    /// 開始・終了の大小比較だけで済ませると 22:00〜06:00 と 05:00〜09:00 の重なりを見落とす。
    /// </para>
    /// </summary>
    private static bool Overlaps(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB) =>
        Split(startA, endA).Any(a => Split(startB, endB).Any(b => a.Start < b.End && b.Start < a.End));

    /// <summary>
    /// 時間帯を、0時をまたがない1〜2本の区間（分単位・終了は含まない）に開く。
    /// 終端は「24:00」を表す 24*60 で持つ（<see cref="TimeOnly"/> では0時と区別できないため）
    /// </summary>
    private static (int Start, int End)[] Split(TimeOnly start, TimeOnly end)
    {
        const int endOfDay = 24 * 60;
        var from = Minutes(start);
        var to = Minutes(end);
        return CrossesMidnight(start, end)
            // 日跨ぎ：当日ぶんと翌日ぶん。終了が0時ちょうどなら翌日ぶんは空なので持たない
            ? to == 0 ? [(from, endOfDay)] : [(from, endOfDay), (0, to)]
            : [(from, to)];
    }

    private static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    private static bool InRange(TimeOnly start, TimeOnly end, TimeOnly time) =>
        CrossesMidnight(start, end)
            ? time >= start || time < end
            : time >= start && time < end;
}
