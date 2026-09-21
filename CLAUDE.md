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

実DB（PostgreSQL / SQL Server）で同じテストを流す（DBに触れる変更のとき。テストごとに `mesapp_test_<guid>` を作って消す）:
```powershell
# SQL Server（このPCの LocalDB。Windows認証なのでパスワード不要）
$env:MESAPP_TEST_PROVIDER='SqlServer'; $env:MESAPP_TEST_CONNECTION='Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True'
# PostgreSQL（18 は 5432、17 は 5433。パスワードは %APPDATA%\postgresql\pgpass.conf から読まれる）
$env:MESAPP_TEST_PROVIDER='PostgreSql'; $env:MESAPP_TEST_CONNECTION='Host=localhost;Port=5432;Username=mesapp_test'
dotnet test tests/MesApp.Api.Tests -v q --nologo 2>&1 | Select-String -Pattern "error|Failed|成功!|失敗" | Select-Object -First 40
$env:MESAPP_TEST_PROVIDER=$null; $env:MESAPP_TEST_CONNECTION=$null
```

失敗時のみ該当クラスを単体で再実行して詳細を取る:
```powershell
dotnet test tests/MesApp.Api.Tests --filter FullyQualifiedName~MasterCsvTests -v n
```

マイグレーション（EF ツールは `dotnet-tools.json` で固定）。**スキーマを変えたら3プロバイダーすべてで同じ名前で追加する**ので、手で3回打たずスクリプトを使う（最後に `DatabaseProviderTests` で同期を確認する。DBには接続しない）。**マイグレーションに生SQLを書かない**（3方言になる。データの手当ては C# の起動時処理かサービス側で行う）:
```powershell
./scripts/Migrations.ps1 -Add <Name>      # 3プロバイダーに追加して検査（/add-migration でも可）
./scripts/Migrations.ps1 -RemoveLast      # 3プロバイダーの最新を取り消す
```

アプリ起動は `.claude/launch.json` の `mesapp`（preview_start）を使う。シェルから `dotnet run` を常駐させない。

## 構成（依存は Core ← Infrastructure ← Api、Client.Web → Core）

| プロジェクト | 責務 |
|:--|:--|
| `src/MesApp.Core` | エンティティ（`Entities/`）、DTO（`Contracts/<領域>/`、record）、`Constants/MesRoles.cs`・`Constants/MesRoleGroups.cs`、抽象（`Abstractions/`）。外部依存なし |
| `src/MesApp.Infrastructure` | `MesAppDbContext`、エンティティ設定（`Configurations/<領域>Configurations.cs`）、EF Core（SQLite / PostgreSQL / SQL Server）、SQLite 用 `Migrations/`、`Services/AuditLogger.cs`、DI 拡張 |
| `src/MesApp.Migrations.PostgreSql` / `.SqlServer` | PostgreSQL・SQL Server 用のマイグレーションだけを置く（SQLite 用は Infrastructure の `Migrations/`。Spec.md 4章） |
| `src/MesApp.Api` | Controllers、業務サービス（`Services/`）、JWT 認証、Blazor WASM の静的配信 |
| `src/MesApp.Client.Web` | Blazor WASM。`Pages/`、`Pages/Masters/*Tab.razor`、`Shared/` 共通コンポーネント、`Auth/` |
| `tests/MesApp.Api.Tests` | xUnit + `WebApplicationFactory`。テストごとに一時 SQLite（環境変数で実DBにも向けられる） |
| `tests/MesApp.Client.Web.Tests` | xUnit + bUnit。全画面に効く横断的な振る舞い（`MainLayout` 等）だけを対象にする |

DB は既定 SQLite（`mesapp.db`）、`Database:Provider` で PostgreSQL / SQL Server に切替可（Spec.md 4章）。起動時に `MigrateAsync()` で自動適用される。

## 探索の起点

**まず `docs-dev/CodeMap.md`**（業務機能 → Controller / エンティティ / 画面 / テストの対応表）を見る。
ここで対象ファイルが分かれば grep しない。足りないときだけ Grep（`-n`・パス限定）で補う。

補助コマンド: `/task-card`（タスクカード生成）、`/verify`（ビルド＋テスト＋DoD判定）、`/add-master`（マスタ追加の定型手順）、`/add-migration`（3プロバイダーのマイグレーション追加）。

## 読み込み禁止・注意

- `src/MesApp.Infrastructure/Migrations/**`（`*.Designer.cs`、`MesAppDbContextModelSnapshot.cs` 含む、約13,700行）と `src/MesApp.Migrations.*/Migrations/**` — **開かない**。スキーマは `Configurations/` とエンティティで確認する。
- `Spec.md`（496行）/ `MES.md`（518行）— **全文を読まない。必ず grep で該当節・該当業務Noだけ**を読む。
- `MES.md` は ENAA 著作物のためリポジトリに含めない（`.gitignore` 済み・ローカルのみ）。内容を他ファイルに転記しない。
- 500行超のファイル（`MasterCsvService.Import.cs`、`ActualCsvService.cs` 等）は該当行 ±40行のみ読む。

## API 側の規約

- コントローラは**プライマリコンストラクタで DI**：`public class XController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase`
- 属性は `[ApiController]` / `[Route("api/xxx")]`（小文字複数形）/ クラスに `[Authorize]`。**クラスに `[Authorize(Roles = ...)]` は付けない**（属性がアクションと合成され、参照系を開放できなくなる）
- クラスの XML コメントに**根拠を書く**：`/// <summary>ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01）</summary>`
- 書き込み系アクションに `[Authorize(Roles = MesRoleGroups.Xxx)]`。ロール定数は `MesRoles`、組み合わせは `MesRoleGroups`（新しい組み合わせが要るときだけ `Core/Constants/MesRoleGroups.cs` に追加）。**API と画面で同じ定数を使う**（別々に書くと片方だけ直したときに表示と権限がずれる）
- 参照系は `AsNoTracking()`、全アクションに `CancellationToken ct`
- DTO は `MesApp.Core/Contracts/<領域>/` の `record`。エンティティを直接返さない
- エラーは `ProblemDetails` + **日本語のメッセージ**。`new ProblemDetails` を直接書かず `ProblemResultExtensions` を使う（例: `return this.ConflictProblem($"ロケーションコード '{request.Code}' は既に存在します。");`）。重複は `ConflictProblem`、未存在は `NotFoundProblem`、入力不正は `BadRequestProblem`
- 作成・更新・削除の後に `auditLogger.LogAsync(...)` を呼ぶ。**変更前後を追跡する操作（訂正・調整・ステータス変更）は `detail:` に匿名オブジェクト `new { before, after, reason }` を渡す**（JSONで保存される）。要約で足りる操作は文字列でよい
- **複数の経路で必要になる業務判定は Controller に書かない**。`Api/Policies/` に置き、Controller はそれを呼んで結果を `ProblemDetails` に変換するだけにする（`LotUsabilityPolicy` / `MaterialIssuePolicy` / `ShipmentGatePolicy`）
- **ロット・作業指示のステータスを直接代入しない**。`LotStatusService` / `WorkOrderStatusService` 経由で変更し、遷移を状態履歴に残す（Spec.md 5.2・5.3）。製造指図・検査指示の状態も Controller で代入せず、`ManufacturingOrderService` / `InspectionService` に集める（状態履歴は持たず監査ログで追う）

## クライアント側の規約

- `@inject HttpClient Http`。認証ヘッダは `Auth/AuthMessageHandler` が付与する
- 冒頭に根拠コメント：`@* ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01） *@`
- API の失敗応答は `Shared/ApiErrors.ReadErrorAsync` で読む（`IsSuccessStatusCode` と ProblemDetails の読み取りを各画面に書かない）：`if (await response.ReadErrorAsync("登録に失敗しました。") is { } error) { _error = error; return; }`
- メッセージ表示は `<Notice Error="@_error" Message="@_message" />`、権限制御は `<AuthorizeView Roles="@MesRoleGroups.Xxx">`（APIと同じ定数を使う。書き込みの操作要素だけを隠し、画面自体は開けたままにする）
- CSV 入出力は `<CsvIoPanel Kind="..." Label="..." KeyLabel="..." OnImported="LoadAsync" />`
- マスタ画面は `Pages/Masters/<名前>Tab.razor` を追加し `MastersPage.razor` に登録。独立画面は `Pages/` に置き `Layout/NavMenu.razor` に導線を追加
- **タブを持つ画面は1ファイルに詰めない**。`Pages/<領域>Page.razor`（タブバーと `@switch` だけ）＋ `Pages/<領域>/<名前>Tab.razor` に分ける。タブ側が自分で `@inject HttpClient Http`・`_error`/`_message`・`<Notice>` を持つ（`MastersPage` / `MaintenancePage` が見本。フォルダ名とファイル名が衝突するのでページ側に `Page` を付ける）
- フィールドは `_camelCase`、共通スタイルは既存の `card` / `form-grid` / `form-field` / `btn` / `btn-secondary` / `actions` / `text-muted` を使う（新規 CSS クラスを増やさない）

## テストの規約

- `ApiFactory`（一時ディレクトリ + 一時 SQLite）を使う。既存の `TestAuth` / `Phase3TestData` を再利用する
- 領域ごとに既存クラス（`MasterTests` / `ProductionTests` / `InventoryTests` / `QualityTests` / `MaintenanceTests` / `ExecutionTests` / `MasterCsvTests`）へ追加。新しいテスト基盤は作らない
- **画面のテストは `tests/MesApp.Client.Web.Tests`（bUnit）に置くが、対象は全画面に効く横断的な振る舞いに限る**（`MainLayout` の初期パスワード誘導・`ErrorBoundary` 等）。個別画面の表示・入力はAPIテストと手動確認でカバーし、画面ごとのテストは増やさない

## 新機能を追加する順序

Entity（`Core/Entities`）→ `MesAppDbContext` の `DbSet` と `Configurations/` の設定 → マイグレーション → DTO（`Core/Contracts`）→ Controller → 必要なら `MesRoleGroups` → Razor 画面 → `MastersPage`/`NavMenu` 登録 → CSV 対応（`MasterCsvKinds`）→ テスト → `Spec.md` 更新

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
