using MesApp.Core.Contracts.Masters;

namespace MesApp.Core.Contracts.Integration;

/// <summary>
/// 連携API で取り込める CSV の種別と列定義（Spec.md 3.8 連携API）。
/// 列キー（<c>Columns[].Name</c>）は約束として固定し、足すだけで消さない（以前の名前は <c>Aliases</c>）
/// </summary>
/// <param name="Masters">マスタCSV（<c>masters/{kind}</c>。キーで上書き）</param>
/// <param name="Actuals">実績CSV（<c>actuals/{kind}</c>。常に新規登録）</param>
public record IntegrationCsvKindsResponse(List<CsvKindInfo> Masters, List<CsvKindInfo> Actuals);
