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
    public static string? Check(string code, TimeOnly start, TimeOnly end, IEnumerable<Shift> others)
    {
        if (start == end)
        {
            return "開始時刻と終了時刻が同じです。24時間の直は表せないため、時間帯を分けて登録してください。";
        }
        // 重なりを許すと、ある時刻がどの直かを一意に決められず実績の直が定まらない
        foreach (var other in others)
        {
            if (Overlaps(start, end, other.StartTime, other.EndTime))
            {
                return $"直 '{other.Code}'（{Format(other.StartTime, other.EndTime)}）と時間帯が重なっています。";
            }
        }
        return null;
    }

    /// <summary>時間帯の表示（夜勤は翌日であることが分かるようにする）</summary>
    public static string Format(TimeOnly start, TimeOnly end) =>
        CrossesMidnight(start, end)
            ? $"{start:HH:mm}〜翌{end:HH:mm}"
            : $"{start:HH:mm}〜{end:HH:mm}";

    /// <summary>
    /// 2つの時間帯が重なるか。日跨ぎがあるので、1日を分に開いて突き合わせる
    /// （開始・終了の大小比較だけでは 22:00〜06:00 と 05:00〜09:00 の重なりを見落とす）
    /// </summary>
    private static bool Overlaps(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB)
    {
        for (var minute = 0; minute < 24 * 60; minute += 1)
        {
            var time = new TimeOnly(minute / 60, minute % 60);
            if (InRange(startA, endA, time) && InRange(startB, endB, time))
            {
                return true;
            }
        }
        return false;
    }

    private static bool InRange(TimeOnly start, TimeOnly end, TimeOnly time) =>
        CrossesMidnight(start, end)
            ? time >= start || time < end
            : time >= start && time < end;
}
