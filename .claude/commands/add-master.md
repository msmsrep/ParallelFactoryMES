---
description: マスタ（エンティティ＋API＋画面タブ＋CSV＋テスト）の追加を定型手順で実行する
argument-hint: "<マスタ名（例: 取引先マスタ）>"
allowed-tools: Read, Grep, Glob, Edit, Write, PowerShell, Bash
---

追加するマスタ: $ARGUMENTS

既存マスタの**完全な模倣**で実装する。設計を新しく起こさない。参照する手本は
`LocationsController.cs` ＋ `Web/Pages/Masters/LocationsTab.razor`（最小構成のマスタ）。

## 事前確認（実装前に必ず出す）

1. 項目一覧（列名・型・必須・既定値）と一意キーをユーザーに確認する。**確認が取れるまでコードを書かない。**
2. `docs-dev/CodeMap.md` で近いマスタを特定し、手本にするファイルを1つ決めて宣言する。
3. `Spec.md` の 5.1 を grep し、エンティティを追記すべき箇所を特定する。

## 実装順序（各ステップ完了後に次へ）

1. **エンティティ**: `Core/Entities/Masters.cs` にクラス追加。`Id` / 一意コード / `IsActive` を持たせる。ステータス系は `Core/Entities/Enums.cs` に追加。
2. **DbContext**: `Infra/MesAppDbContext.cs` に `DbSet` を、`Infra/Configurations/MasterConfigurations.cs` に `IEntityTypeConfiguration<T>` のクラス（一意インデックス・必須・最大長）を追加（`ApplyConfigurationsFromAssembly` で自動で読み込まれる）。既存マスタの記述に揃える。
3. **マイグレーション**: CLAUDE.md「コマンド」の3行（SQLite / PostgreSQL / SQL Server）を `<Name>` = `Add<Name>Master` で**すべて**実行する（1つでも欠けると `DatabaseProviderTests` が落ちる）。
   生成物は**開かない・編集しない**。
4. **DTO**: `Core/Contracts/Masters/` に `<Name>Request` / `<Name>Response` を `record` で追加。
5. **Controller**: `Api/Controllers/<Name>sController.cs`。
   - `[ApiController]` / `[Route("api/<kebab-case複数形>")]` / `[Authorize]`
   - プライマリコンストラクタで `(MesAppDbContext db, IAuditLogger auditLogger)`
   - クラスのXMLコメントに「（Spec.md 5.1 <Entity>。<MES No>）」を書く
   - List / Get / Create / Put / Delete。書き込みは `[Authorize(Roles = RoleGroups.MasterWrite)]`
   - 参照は `AsNoTracking()`、全アクションに `CancellationToken ct`
   - 重複は `Conflict(new ProblemDetails { Title = "…は既に存在します。" })`（日本語）
   - 書き込み後に `auditLogger.LogAsync("Master", "Create"|"Update"|"Delete", nameof(<Entity>), …)`
6. **画面タブ**: `Web/Pages/Masters/<Name>sTab.razor` を `LocationsTab.razor` の構造で作る。
   `@inject HttpClient Http` / 冒頭の根拠コメント / `<Notice>` / `<AuthorizeView Roles="@($"{MesRoles.SystemAdmin},{MesRoles.ProductionManager}")">` /
   `<CsvIoPanel Kind="…" Label="…" KeyLabel="…" OnImported="LoadAsync" />` / 既存CSSクラスのみ使用。
7. **タブ登録**: `Web/Pages/MastersPage.razor` にボタン（`TabClass`）と `@switch` の分岐を追加。
8. **CSV**: `Api/Services/MasterCsvKinds.cs` に定数と `CsvKindInfo`（列名は英語固定、Labelは日本語、必須列・説明を明記）を追加し、
   `Api/Services/MasterCsvService.Import.cs` に取込処理を既存マスタと同じ形で追加。
9. **テスト**: `Tests/MasterTests.cs` にCRUD＋権限、`Tests/MasterCsvTests.cs` に入出力を追加。`ApiFactory` / `TestAuth` を使う。
10. **仕様更新**: `Spec.md` の 5.1（データモデル）と 6章（画面一覧）に追記し、冒頭に改訂履歴行を追加。
11. **CodeMap 更新**: `docs-dev/CodeMap.md` の該当 Division に行を追加。

## 完了判定

`/verify` を実行し、DoD 5項目すべてが○になってから完了と報告する。
同じエラーで2回失敗したら、3回目を同じやり方で試さず前提を疑う（Orchestration.md §5.2）。
