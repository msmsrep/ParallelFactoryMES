using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MesApp.Infrastructure;

/// <summary>
/// DBプロバイダーごとのモデル調整（Spec.md 4章）。SQLiteのスキーマは変えない。
/// </summary>
/// <remarks>
/// どのプロバイダーでも同じエンティティ設定（Configurations/）を使い、プロバイダーの制約で
/// そのままでは保存・適用できないところだけをここで直す。
/// </remarks>
internal static class ProviderModelConventions
{
    /// <summary>型ごとの規約（<see cref="DbContext.ConfigureConventions"/> から呼ぶ）</summary>
    public static void ApplyConventions(ModelConfigurationBuilder builder, DatabaseFacade database)
    {
        // PostgreSQLの timestamptz はオフセット0（UTC）の値しか書き込めない（Npgsqlの仕様）。
        // 工場のタイムゾーン付きで作った時刻をそのまま保存できるよう、書き込み時にUTCへ直す。
        // 読み出した値はUTCになるが、同じ時点を指すので比較・表示（画面側で LocalDateTime）には影響しない
        if (database.IsNpgsql())
        {
            builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        }

        // 精度を指定していない decimal は、SQL Serverでは decimal(18,2) になり測定値・数量の小数3桁目以降が
        // 黙って切り捨てられる。SQLite以外では小数6桁まで持たせる（個別に HasPrecision した列はそちらが優先）。
        // SQLiteは精度を持たない（TEXTで保存）ため、スキーマを変えないよう対象外にする
        // 小数桁を固定した列は読み出すと 1.500000 のように末尾ゼロ付きで返り、そのままCSV・APIに出てしまうため、
        // 読み出し時に末尾ゼロを落としてSQLiteと同じ見え方にする
        if (!database.IsSqlite())
        {
            builder.Properties<decimal>().HavePrecision(18, 6).HaveConversion<TrimmedDecimalConverter>();
        }
    }

    /// <summary>エンティティ設定の後に掛ける調整（<see cref="DbContext.OnModelCreating"/> から呼ぶ）</summary>
    public static void ApplyModel(ModelBuilder builder, DatabaseFacade database)
    {
        if (database.IsSqlServer())
        {
            AvoidMultipleCascadePaths(builder);
        }
    }

    /// <summary>
    /// SQL Serverは、削除の連鎖（CASCADE / SET NULL）が同じ表へ複数の経路で届く形や循環を許さない。
    /// SET NULL はDB側の動作をやめ、読み込み済みの子だけEFが null にする（ClientSetNull）。
    /// </summary>
    /// <remarks>
    /// SET NULL の親（利用者・設備・作業指示・ロット等）はアプリから物理削除しない（無効化で扱う）ため、
    /// 実運用での差は出ない。SQLite・PostgreSQLのスキーマは変えない。
    /// 残っていないことは DatabaseProviderTests が確かめる。
    /// </remarks>
    private static void AvoidMultipleCascadePaths(ModelBuilder builder)
    {
        foreach (var fk in builder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            if (fk.DeleteBehavior == DeleteBehavior.SetNull)
            {
                fk.DeleteBehavior = DeleteBehavior.ClientSetNull;
            }
        }
    }

    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
        v => v.ToUniversalTime(), v => v);

    private sealed class TrimmedDecimalConverter() : ValueConverter<decimal, decimal>(
        v => v, v => TrimTrailingZeros(v));

    /// <summary>値を変えずに末尾ゼロ（スケール）だけを落とす。1.500000 → 1.5</summary>
    private static decimal TrimTrailingZeros(decimal value) => value / 1.0000000000000000000000000000m;
}
