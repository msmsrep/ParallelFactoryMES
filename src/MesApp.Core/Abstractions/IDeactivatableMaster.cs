namespace MesApp.Core.Abstractions;

/// <summary>
/// 無効化（論理削除）できるマスタ。
/// <para>
/// マスタは物理削除しない。既に参照している業務データから辿れなくなるため、
/// 「これから使わせない」を表す <see cref="IsActive"/> を false にするだけにする。
/// </para>
/// この形を共通化しておくと、無効化の手順（存在確認・保存・監査ログ）をマスタごとに
/// 書き分けずに済む（Api 側の <c>DeactivateMasterAsync</c>）。
/// </summary>
public interface IDeactivatableMaster
{
    int Id { get; }

    /// <summary>有効フラグ。false にすると新しい業務データからは選べなくなる</summary>
    bool IsActive { get; set; }
}
