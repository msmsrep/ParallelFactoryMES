using System.Globalization;
using MesApp.Core.Contracts.Masters;

namespace MesApp.Api.Services;

/// <summary>ヘッダー行付きのCSV（1行目＝ヘッダー、2行目以降＝データ行）</summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns = new(StringComparer.OrdinalIgnoreCase);

    private CsvTable(CsvRecord header, List<CsvRecord> rows)
    {
        Header = header;
        Rows = rows;
        for (var i = 0; i < header.Fields.Length; i++)
        {
            var name = header.Fields[i].Trim();
            if (name.Length > 0)
            {
                _columns.TryAdd(name, i);
            }
        }
    }

    public CsvRecord Header { get; }

    public IReadOnlyList<CsvRecord> Rows { get; }

    /// <summary>1行目をヘッダーとして読み込む。データが1行も無い場合はnull。</summary>
    public static CsvTable? Create(List<CsvRecord> records) =>
        records.Count == 0 ? null : new CsvTable(records[0], records.Skip(1).ToList());

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    /// <summary>列の値（前後の空白を除去し、空文字はnull）。列が無い場合もnull。</summary>
    public string? Value(CsvRecord row, string name)
    {
        if (!_columns.TryGetValue(name, out var index) || index >= row.Fields.Length)
        {
            return null;
        }
        var value = row.Fields[index].Trim();
        return value.Length == 0 ? null : value;
    }
}

/// <summary>
/// データ行を型付きで読むためのカーソル。
/// 「列が無い＝現状維持」「列があって空欄＝クリア（必須列はエラー）」という規則で値を解決する。
/// </summary>
public sealed class CsvRowReader(CsvTable table, CsvRecord row, List<CsvImportError> errors)
{
    public int Line => row.Line;

    /// <summary>この行で1件でも解析エラーがあったか</summary>
    public bool Failed { get; private set; }

    public void Fail(string message)
    {
        Failed = true;
        errors.Add(new CsvImportError(row.Line, message));
    }

    /// <summary>必須列（ヘッダー存在は事前検証済み）。空欄はエラー。</summary>
    public string RequiredText(string column, int maxLength = 0)
    {
        var value = table.Value(row, column);
        if (value is null)
        {
            Fail($"{column} は必須です。");
            return string.Empty;
        }
        return CheckLength(column, value, maxLength);
    }

    /// <summary>任意列。列が無ければ現在値を保つ。</summary>
    public string? Text(string column, string? current, int maxLength = 0)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        return value is null ? null : CheckLength(column, value, maxLength);
    }

    public decimal Number(string column, decimal current, decimal? min = null, decimal? max = null)
        => NumberOrNull(column, current, min, max) ?? current;

    public decimal? NumberOrNull(string column, decimal? current, decimal? min = null, decimal? max = null)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return null;
        }
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            Fail($"{column} は数値で指定してください（'{value}'）。");
            return current;
        }
        if (min is not null && parsed < min)
        {
            Fail($"{column} は {min} 以上で指定してください（'{value}'）。");
            return current;
        }
        if (max is not null && parsed > max)
        {
            Fail($"{column} は {max} 以下で指定してください（'{value}'）。");
            return current;
        }
        return parsed;
    }

    public int IntValue(string column, int current, int? min = null)
        => IntOrNull(column, current, min) ?? current;

    public int? IntOrNull(string column, int? current, int? min = null)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return null;
        }
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            Fail($"{column} は整数で指定してください（'{value}'）。");
            return current;
        }
        if (min is not null && parsed < min)
        {
            Fail($"{column} は {min} 以上で指定してください（'{value}'）。");
            return current;
        }
        return parsed;
    }

    public bool Bool(string column, bool current)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return current;
        }
        return value.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" or "はい" or "有効" or "在籍" or "○" or "必須" => true,
            "false" or "0" or "no" or "n" or "いいえ" or "無効" or "退職" or "×" or "任意" => false,
            _ => FailBool(column, value, current),
        };
    }

    public TEnum Enum<TEnum>(string column, TEnum current, IReadOnlyDictionary<string, TEnum> labels)
        where TEnum : struct, System.Enum
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return current;
        }
        if (labels.TryGetValue(value, out var parsed))
        {
            return parsed;
        }
        Fail($"{column} の値 '{value}' は不正です（指定可能：{string.Join(" / ", System.Enum.GetNames<TEnum>())}）。");
        return current;
    }

    public DateOnly? DateOrNull(string column, DateOnly? current)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return null;
        }
        string[] formats = ["yyyy-MM-dd", "yyyy/M/d", "yyyy/MM/dd", "yyyyMMdd"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }
        Fail($"{column} は日付（yyyy-MM-dd）で指定してください（'{value}'）。");
        return current;
    }

    /// <summary>
    /// 日時列。オフセット付き（2026-09-17T08:30:00+09:00）はそのまま、オフセットの無い値
    /// （2026-09-17 08:30）は工場の時刻として localOffset を付けて読む
    /// </summary>
    public DateTimeOffset? DateTimeOrNull(string column, TimeSpan localOffset)
    {
        var value = table.Value(row, column);
        if (value is null)
        {
            return null;
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"(Z|[+-]\d{2}:?\d{2})$")
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            return withOffset;
        }
        string[] formats =
        [
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
            "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss",
        ];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return new DateTimeOffset(local, localOffset);
        }
        Fail($"{column} は日時（yyyy-MM-dd HH:mm）で指定してください（'{value}'）。");
        return null;
    }

    /// <summary>コード列から関連マスタのIDを解決する（空欄はnull、未登録はエラー）</summary>
    public int? Reference(string column, int? current, IReadOnlyDictionary<string, int> byCode, string label)
    {
        if (!table.HasColumn(column))
        {
            return current;
        }
        var value = table.Value(row, column);
        if (value is null)
        {
            return null;
        }
        if (byCode.TryGetValue(value, out var id))
        {
            return id;
        }
        Fail($"{label} '{value}' は登録されていません（{column}）。");
        return current;
    }

    private bool FailBool(string column, string value, bool current)
    {
        Fail($"{column} は true / false で指定してください（'{value}'）。");
        return current;
    }

    private string CheckLength(string column, string value, int maxLength)
    {
        if (maxLength > 0 && value.Length > maxLength)
        {
            Fail($"{column} は{maxLength}文字以内で指定してください（{value.Length}文字）。");
        }
        return value;
    }
}
