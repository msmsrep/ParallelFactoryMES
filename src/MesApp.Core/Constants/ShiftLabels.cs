namespace MesApp.Core.Constants;

/// <summary>
/// 直（勤務シフト）の集計で使う表示名（Spec.md 5.7）。
/// APIの集計キーと画面の判定で同じ文字列を使うため、ここへ置く
/// （別々に書くと、片方だけ直したときに案内が出なくなる）。
/// </summary>
public static class ShiftLabels
{
    /// <summary>直が付いていない実績のまとめ先。直を登録する前の実績はここへ入る</summary>
    public const string NoShift = "（直なし）";
}
