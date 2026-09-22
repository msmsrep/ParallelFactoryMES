# 多言語対応（日本語・英語）バックログ

画面と API のメッセージを日本語・英語で切り替えられるようにする。
1項目＝1セッションで進める（`Orchestration.md` §4.1）。MES.md の業務プロセスには当たらない横断的な非機能要件として Spec.md に書く。

着手時点の規模（2026-09-22 計測）：画面 90ファイル（約14,000行）のうち日本語を含む行が約1,160、
API の日本語リテラルが約850（うち CSV 関連が約420）、enum を日本語に変える `switch` が画面・API に計37か所。

## 決定事項

| 論点 | 決定 | 理由 |
|:--|:--|:--|
| 仕組み | `IStringLocalizer` ＋ `.resx` | .NET 標準。API・画面の両方で同じ仕組みを使える |
| キー | **日本語の原文をそのままキーにする**（`L["登録に失敗しました。"]`）。`en.resx` だけを持つ | 訳が無いときは日本語のまま出るので段階的に移行できる。文言を変えるとキーも変わるため、`I18nCheck` で拾う |
| 言語の保存先 | **ブラウザごと**（localStorage）。切替時に再読込 | スキーマ変更が要らない |
| API のエラー文言 | API が `Accept-Language` で訳す（`ApiText.T(原文, 値…)`）。クライアントがヘッダを付ける | `ProblemResultExtensions` / `ApiErrors.ReadErrorAsync` の約束（Title に文言）を変えない。Controller・Policy・Service のどこで作る文言も同じ1つの規則で包める |
| enum の表示名 | `Core/Localization/` に集約し、API・画面で共用 | 37か所の重複を解消する |
| 訳さないもの | マスタの値、監査ログの detail、保存済みの理由、Spec.md、コードコメント、コミットメッセージ | データや記録は訳さず、表示だけを訳す |
| 日付・数値 | `yyyy-MM-dd` のまま（カルチャ依存にしない） | 両言語で通じる |
| 既定言語 | 日本語 | 既存テスト（日本語の Title を検証）が緑のまま |

## バックログ

| ID | 項目 | 状態 |
|:--|:--|:--|
| I18N-01 | 基盤：Localization 登録、言語切替（`MainLayout`）、`<html lang>`、`Accept-Language` の伝搬、API の RequestLocalization、bUnit テスト、Spec.md の節 | **対応済**（Spec.md 改訂100。レイアウト・ログイン画面・認証APIの文言も訳した） |
| I18N-02 | enum の表示名を Core に集約（`Labels.cs` と各 `XxxLabel`） | **対応済**（Spec.md 改訂101。`Core/Localization/EnumLabels.cs`。補足付きの選択肢は画面の段で訳す） |
| I18N-03 | API のエラー文言（Controller / Policy / Service）。`Accept-Language: en` のテストを領域ごとに数件 | **対応済**（Spec.md 改訂102。`ApiText.T` で包む。保存される文字列は包まない。CSV 取込のエラーは I18N-09） |
| I18N-04 | 画面：Layout・NavMenu・Shared | **対応済**（Spec.md 改訂103。部品の既定の文言は `string?` の引数にして部品側で訳す。呼び出し側が渡す文言・`ReadErrorAsync` の既定文言は各画面の段で訳す） |
| I18N-05 | 画面：マスタ | **対応済**（Spec.md 改訂104。`scripts/I18nWrap.py` で一括して包み、残りを手で直した。英訳の `{0}` の数は `CultureTests` で検査する） |
| I18N-06 | 画面：製造・実行 | **対応済**（Spec.md 改訂105。作業指示書・現品票の印刷も含む。文中を `<strong>` で区切った文は、語順が変わるものは1文にまとめて訳した） |
| I18N-07 | 画面：在庫・品質 | **対応済**（Spec.md 改訂106。出荷伝票・棚卸表・検査成績書・出荷判定書の印刷も含む。状態の絞り込みの「取消」は `EnumLabels.Of` にし、`L["取消"]` はボタン（Cancel）だけに使う。API が集計のキーとして返す「（直なし）」は表示するときだけ訳す） |
| I18N-08 | 画面：保全・ダッシュボード | **対応済**（Spec.md 改訂107。治工具管理も含む。日次の区切りの曜日は API が `CurrentUICulture` の `ddd` で返す。グラフの凡例・系列名も訳す。太字で区切った文は太字をやめて1文にした（`MarkupString` は使わない）。About・監査ログ・パスワード変更・セットアップ・実績CSV取込は I18N-10 で拾う） |
| I18N-09 | CSV：列の表示名・説明・取込エラー（列キーは英語のまま変えない） | 未着手 |
| I18N-10 | 仕上げ：`scripts/I18nCheck.ps1`（`en.resx` に無いキー・`L[]` を通っていない日本語リテラルの一覧）、CLAUDE.md の規約改訂 | 未着手 |
