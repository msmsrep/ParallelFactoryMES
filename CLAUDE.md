# CLAUDE.md — Parallel Factory MES

.NET 10 / ASP.NET Core API + Blazor WebAssembly の製造実行システム（MES）。
**応答・コード内コメント・UI文言・コミットメッセージはすべて日本語。**

作業の進め方（セッション分割・探索規律・受入基準）は `Orchestration.md` に従うこと。要点は末尾に再掲。

## コマンド

ビルド（生ログを流さない。必ずフィルタを通す）:
```powershell
dotnet build ParallelFactoryMES.slnx -v q --nologo 2>&1 | Select-String -Pattern "error|warning CS|Build succeeded|ビルドに成功" | Select-Object -First 40
```

テスト:
```powershell
dotnet test ParallelFactoryMES.slnx -v q --nologo 2>&1 | Select-String -Pattern "error|Failed|Passed!|成功!|失敗|合計" | Select-Object -First 40
```

失敗時のみ該当クラスを単体で再実行して詳細を取る:
```powershell
dotnet test tests/MesApp.Api.Tests --filter FullyQualifiedName~MasterCsvTests -v n
```

マイグレーション（`dotnet tool restore` が前提。EF ツールは `dotnet-tools.json` で固定）:
```powershell
dotnet ef migrations add <Name> --project src/MesApp.Infrastructure --startup-project src/MesApp.Api
```

アプリ起動は `.claude/launch.json` の `mesapp`（preview_start）を使う。シェルから `dotnet run` を常駐させない。

## 構成（依存は Core ← Infrastructure ← Api、Client.Web → Core）

| プロジェクト | 責務 |
|:--|:--|
| `src/MesApp.Core` | エンティティ（`Entities/`）、DTO（`Contracts/<領域>/`、record）、`Constants/MesRoles.cs`、抽象（`Abstractions/`）。外部依存なし |
| `src/MesApp.Infrastructure` | `MesAppDbContext`、EF Core (SQLite)、`Migrations/`、`Services/AuditLogger.cs`、DI 拡張 |
| `src/MesApp.Api` | Controllers、業務サービス（`Services/`）、`RoleGroups.cs`、JWT 認証、Blazor WASM の静的配信 |
| `src/MesApp.Client.Web` | Blazor WASM。`Pages/`、`Pages/Masters/*Tab.razor`、`Shared/` 共通コンポーネント、`Auth/` |
| `tests/MesApp.Api.Tests` | xUnit + `WebApplicationFactory`。テストごとに一時 SQLite |
| `tests/MesApp.Client.Web.Tests` | xUnit + bUnit。全画面に効く横断的な振る舞い（`MainLayout` 等）だけを対象にする |

DB は SQLite（`mesapp.db`）。起動時に `MigrateAsync()` で自動適用される。

## 探索の起点

**まず `docs-dev/CodeMap.md`**（業務機能 → Controller / エンティティ / 画面 / テストの対応表）を見る。
ここで対象ファイルが分かれば grep しない。足りないときだけ Grep（`-n`・パス限定）で補う。

補助コマンド: `/task-card`（タスクカード生成）、`/verify`（ビルド＋テスト＋DoD判定）、`/add-master`（マスタ追加の定型手順）。

## 読み込み禁止・注意

- `src/MesApp.Infrastructure/Migrations/**`（`*.Designer.cs`、`MesAppDbContextModelSnapshot.cs` 含む、約13,700行）— **開かない**。スキーマは `MesAppDbContext.cs` とエンティティで確認する。
- `Spec.md`（496行）/ `MES.md`（518行）— **全文を読まない。必ず grep で該当節・該当業務Noだけ**を読む。
- `MES.md` は ENAA 著作物のためリポジトリに含めない（`.gitignore` 済み・ローカルのみ）。内容を他ファイルに転記しない。
- 500行超のファイル（`Maintenance.razor`、`MasterCsvService.Import.cs` 等）は該当行 ±40行のみ読む。

## API 側の規約

- コントローラは**プライマリコンストラクタで DI**：`public class XController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase`
- 属性は `[ApiController]` / `[Route("api/xxx")]`（小文字複数形）/ クラスに `[Authorize]`
- クラスの XML コメントに**根拠を書く**：`/// <summary>ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01）</summary>`
- 書き込み系アクションに `[Authorize(Roles = RoleGroups.Xxx)]`。ロール定数は `MesRoles`、組み合わせは `RoleGroups`（新しい組み合わせが要るときだけ `RoleGroups.cs` に追加）
- 参照系は `AsNoTracking()`、全アクションに `CancellationToken ct`
- DTO は `MesApp.Core/Contracts/<領域>/` の `record`。エンティティを直接返さない
- エラーは `ProblemDetails` + **日本語のメッセージ**（例: `$"ロケーションコード '{request.Code}' は既に存在します。"`）。重複は `Conflict`、未存在は `NotFound`
- 作成・更新・削除の後に `auditLogger.LogAsync(...)` を呼ぶ。**変更前後を追跡する操作（訂正・調整・ステータス変更）は `detail:` に匿名オブジェクト `new { before, after, reason }` を渡す**（JSONで保存される）。要約で足りる操作は文字列でよい
- **複数の経路で必要になる業務判定は Controller に書かない**。`Api/Policies/` に置き、Controller はそれを呼んで結果を `ProblemDetails` に変換するだけにする（`LotUsabilityPolicy` / `MaterialIssuePolicy` / `ShipmentGatePolicy`）
- **ロット・作業指示のステータスを直接代入しない**。`LotStatusService` / `WorkOrderStatusService` 経由で変更し、遷移を状態履歴に残す（Spec.md 5.2・5.3）

## クライアント側の規約

- `@inject HttpClient Http`。認証ヘッダは `Auth/AuthMessageHandler` が付与する
- 冒頭に根拠コメント：`@* ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01） *@`
- メッセージ表示は `<Notice Error="@_error" Message="@_message" />`、権限制御は `<AuthorizeView Roles="@($"{MesRoles.SystemAdmin},...")">`
- CSV 入出力は `<CsvIoPanel Kind="..." Label="..." KeyLabel="..." OnImported="LoadAsync" />`
- マスタ画面は `Pages/Masters/<名前>Tab.razor` を追加し `MastersPage.razor` に登録。独立画面は `Pages/` に置き `Layout/NavMenu.razor` に導線を追加
- フィールドは `_camelCase`、共通スタイルは既存の `card` / `form-grid` / `form-field` / `btn` / `btn-secondary` / `actions` / `text-muted` を使う（新規 CSS クラスを増やさない）

## テストの規約

- `ApiFactory`（一時ディレクトリ + 一時 SQLite）を使う。既存の `TestAuth` / `Phase3TestData` を再利用する
- 領域ごとに既存クラス（`MasterTests` / `ProductionTests` / `InventoryTests` / `QualityTests` / `MaintenanceTests` / `ExecutionTests` / `MasterCsvTests`）へ追加。新しいテスト基盤は作らない
- **画面のテストは `tests/MesApp.Client.Web.Tests`（bUnit）に置くが、対象は全画面に効く横断的な振る舞いに限る**（`MainLayout` の初期パスワード誘導・`ErrorBoundary` 等）。個別画面の表示・入力はAPIテストと手動確認でカバーし、画面ごとのテストは増やさない

## 新機能を追加する順序

Entity（`Core/Entities`）→ `MesAppDbContext` 設定 → マイグレーション → DTO（`Core/Contracts`）→ Controller → 必要なら `RoleGroups` → Razor 画面 → `MastersPage`/`NavMenu` 登録 → CSV 対応（`MasterCsvKinds`）→ テスト → `Spec.md` 更新

## 完了の定義（DoD・全項目必須）

1. ビルド成功、**新規の警告を増やしていない**
2. `dotnet test` 全緑
3. 仕様に触れる変更なら `Spec.md` を同一コミットで更新（改訂履歴行も追加）
4. その変更が MES.md のどの業務プロセス（No）に当たるか説明できる
5. 変更ファイル数が着手前の想定と一致している（乖離は設計ミスの信号）

## 進め方の要点（詳細は Orchestration.md）

- 1セッション＝1タスク。無関係な作業に移るときは `/clear`、同じファイル群を触るなら継続
- 探索は Glob → Grep（`-n`・パス限定）→ 該当行±40行の Read。1タスクあたり読込1,500行・Grep 8回まで
- **同じエラーで2回失敗したら3回目を同じやり方で試さない**。前提（変更対象・受入条件）を疑う
- 機械的に確認できることは build / test / grep で確認する。推測で「直った」と言わない
