using MesApp.Api.Policies;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

/// <summary>
/// 直（シフト）の時間帯判定（Spec.md 5.7）。日跨ぎ（<c>EndTime &lt;= StartTime</c>）の扱いが
/// 実績の直・重なりの拒否・製造日境界の警告のすべての土台になるため、ここで固定する。
/// </summary>
public class ShiftSchedulePolicyTests
{
    /// <summary>境界になりやすい時刻（0時・製造日境界の6時・交代時刻・その前後30分）</summary>
    private static readonly TimeOnly[] BoundaryTimes =
    [
        new(0, 0), new(0, 30), new(5, 0), new(5, 30), new(6, 0),
        new(9, 0), new(17, 30), new(18, 0), new(22, 0), new(23, 30),
    ];

    [Fact]
    public void 重なり判定は1日を分に開いた総当たりと全ての組み合わせで一致する()
    {
        // 0時で切って区間に開く実装が、以前の「1440分を総当たりする」実装と同じ答えを返すことを固定する。
        // 日跨ぎが絡むと直感が効かないため、期待値は総当たり（意味が明らかな側）で作る
        foreach (var (startA, endA) in Pairs())
        {
            foreach (var (startB, endB) in Pairs())
            {
                var expected = OverlapsByBruteForce(startA, endA, startB, endB);
                var actual = ShiftSchedulePolicy.Check(startA, endA, [Shift(startB, endB)]) is not null;
                Assert.True(expected == actual,
                    $"{startA:HH:mm}〜{endA:HH:mm} と {startB:HH:mm}〜{endB:HH:mm} の判定が食い違う"
                    + $"（総当たり={expected} / 実装={actual}）");
            }
        }
    }

    [Fact]
    public void 日跨ぎと日跨ぎでない直の重なりを見落とさない()
    {
        // 開始・終了の大小比較だけで済ませると落ちる組み合わせ（このためだけに区間へ開いている）
        Assert.NotNull(ShiftSchedulePolicy.Check(
            new TimeOnly(5, 0), new TimeOnly(9, 0), [Shift(new(22, 0), new(6, 0))]));
        // 夜勤の終了ちょうどから始まる直は重ならない（終了は含まない）
        Assert.Null(ShiftSchedulePolicy.Check(
            new TimeOnly(6, 0), new TimeOnly(9, 0), [Shift(new(22, 0), new(6, 0))]));
        // 終了が0時ちょうどの直と、0時から始まる直も重ならない
        Assert.Null(ShiftSchedulePolicy.Check(
            new TimeOnly(0, 0), new TimeOnly(6, 0), [Shift(new(18, 0), new(0, 0))]));
    }

    [Theory]
    // 昼勤（日跨ぎなし）：開始は含み、終了は含まない
    [InlineData(6, 0, 18, 0, 6, 0, true)]
    [InlineData(6, 0, 18, 0, 17, 59, true)]
    [InlineData(6, 0, 18, 0, 18, 0, false)]
    [InlineData(6, 0, 18, 0, 5, 59, false)]
    // 夜勤（日跨ぎ）：開始以降と、翌日の終了より前の両方が当たる
    [InlineData(18, 0, 6, 0, 18, 0, true)]
    [InlineData(18, 0, 6, 0, 23, 0, true)]
    [InlineData(18, 0, 6, 0, 0, 0, true)]
    [InlineData(18, 0, 6, 0, 2, 0, true)]
    [InlineData(18, 0, 6, 0, 5, 59, true)]
    [InlineData(18, 0, 6, 0, 6, 0, false)]
    [InlineData(18, 0, 6, 0, 7, 0, false)]
    [InlineData(18, 0, 6, 0, 17, 59, false)]
    public void 時間帯の判定は開始を含み終了を含まない(
        int startHour, int startMinute, int endHour, int endMinute,
        int hour, int minute, bool expected)
    {
        var shift = Shift(new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

        Assert.Equal(expected, ShiftSchedulePolicy.Covers(shift, new TimeOnly(hour, minute)));
    }

    [Fact]
    public void 時刻から直を引ける()
    {
        var day = Shift(new(6, 0), new(18, 0), "D");
        var night = Shift(new(18, 0), new(6, 0), "N");
        List<Shift> shifts = [day, night];

        Assert.Equal("N", ShiftSchedulePolicy.Resolve(shifts, new TimeOnly(23, 30))?.Code);
        Assert.Equal("N", ShiftSchedulePolicy.Resolve(shifts, new TimeOnly(2, 0))?.Code);
        Assert.Equal("D", ShiftSchedulePolicy.Resolve(shifts, new TimeOnly(6, 0))?.Code);

        // 無効な直は当たらない。どの直にも入らない時刻は null
        // （直を登録していない運用・時間帯に穴がある運用では実績の直が付かない）
        night.IsActive = false;
        Assert.Null(ShiftSchedulePolicy.Resolve(shifts, new TimeOnly(23, 30)));
        Assert.Null(ShiftSchedulePolicy.Resolve([], new TimeOnly(10, 0)));
    }

    [Fact]
    public void 開始と終了が同じ直は登録できない()
    {
        var message = ShiftSchedulePolicy.Check(new TimeOnly(6, 0), new TimeOnly(6, 0), []);

        Assert.NotNull(message);
        Assert.Contains("24時間の直は表せない", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 重なりのメッセージは相手の直を名指しする()
    {
        var message = ShiftSchedulePolicy.Check(
            new TimeOnly(5, 0), new TimeOnly(9, 0), [Shift(new(22, 0), new(6, 0), "N")]);

        Assert.NotNull(message);
        Assert.Contains("'N'", message, StringComparison.Ordinal);
        Assert.Contains("22:00〜翌06:00", message, StringComparison.Ordinal);
    }

    /// <summary>境界になりやすい時刻の全組み合わせ（開始＝終了は直として成立しないので除く）</summary>
    private static IEnumerable<(TimeOnly Start, TimeOnly End)> Pairs() =>
        from start in BoundaryTimes
        from end in BoundaryTimes
        where start != end
        select (start, end);

    /// <summary>1日を1分ずつ見て、両方の時間帯に入る瞬間があるかを数える（期待値の計算）</summary>
    private static bool OverlapsByBruteForce(TimeOnly startA, TimeOnly endA, TimeOnly startB, TimeOnly endB)
    {
        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var time = new TimeOnly(minute / 60, minute % 60);
            if (InRange(startA, endA, time) && InRange(startB, endB, time))
            {
                return true;
            }
        }
        return false;

        static bool InRange(TimeOnly start, TimeOnly end, TimeOnly time) =>
            end <= start
                ? time >= start || time < end
                : time >= start && time < end;
    }

    private static Shift Shift(TimeOnly start, TimeOnly end, string code = "X") =>
        new() { Code = code, Name = "テスト", StartTime = start, EndTime = end, IsActive = true };
}
