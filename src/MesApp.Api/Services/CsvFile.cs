using System.Text;

namespace MesApp.Api.Services;

/// <summary>CSVの1レコード（Lineは元CSVの物理行番号。1始まり）</summary>
public sealed record CsvRecord(int Line, string[] Fields);

/// <summary>
/// RFC 4180準拠の簡易CSV読み書き（外部ライブラリ非依存）。
/// 出力はExcel互換のためCRLF改行＋UTF-8 BOM、取込はBOM判定とShift_JISフォールバックに対応する。
/// </summary>
public static class CsvFile
{
    static CsvFile()
    {
        // Excel（日本語版）が既定で保存するShift_JIS(CP932)を読めるようにする
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>ヘッダー＋データ行をCSV文字列に整形する</summary>
    public static string Format(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }
        return builder.ToString();
    }

    /// <summary>CSV文字列（UTF-8 BOM付き）をバイト列にする（Excelでの文字化け回避）</summary>
    public static byte[] ToUtf8Bom(string csv) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

    /// <summary>
    /// アップロードされたバイト列を文字列にする。BOMがあればそれに従い、
    /// 無い場合はUTF-8として解釈し、不正なバイト列ならShift_JIS(CP932)として読み直す。
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding(932).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // CP932が利用できない環境では、置換文字付きのUTF-8として読む
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    /// <summary>
    /// CSV文字列をレコードに分解する。引用符内のカンマ・改行・二重引用符（""）に対応。
    /// 空行は読み飛ばす。
    /// </summary>
    public static List<CsvRecord> Parse(string text)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        void EndRecord()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Count > 1 || fields[0].Trim().Length > 0)
            {
                records.Add(new CsvRecord(recordLine, [.. fields]));
            }
            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRecord();
                    line++;
                    recordLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }
        return records;
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            builder.Append(Escape(value));
        }
        builder.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        var needsQuotes = value.AsSpan().IndexOfAny(',', '"', '\n') >= 0
            || value.Contains('\r')
            || value[0] == ' ' || value[^1] == ' ';
        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
