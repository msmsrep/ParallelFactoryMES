namespace MesApp.Core.Entities;

/// <summary>
/// 採番の連番（Spec.md 3.9）。番号のプレフィックス（例 <c>MO20260903-</c>、<c>RM-01-20260903-</c>）ごとに、
/// 最後に払い出した値を持つ。
/// </summary>
/// <remarks>
/// 既存番号の最大値を読んで+1すると、同時に採番したリクエストが同じ番号を得てしまう。
/// 「1行を更新してから読む」形にすることで、払い出しそのものを不可分にする。
/// </remarks>
public class NumberSequence
{
    public int Id { get; set; }

    /// <summary>番号のプレフィックス（日付を含む。日が変われば別の行になる）</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>最後に払い出した連番</summary>
    public int LastValue { get; set; }
}
