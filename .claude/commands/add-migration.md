---
description: スキーマ変更のマイグレーションを3プロバイダー（SQLite / PostgreSQL / SQL Server）へ同じ名前でまとめて追加する
argument-hint: "<マイグレーション名（例: AddShiftCalendar）>"
allowed-tools: PowerShell, Bash, Read, Grep, Glob
---

エンティティ・`Configurations/` を変更し終えてから実行する（Spec.md 4章）。**生成物（`Migrations/**`）は開かない・編集しない。**

## 1. 追加

`$ARGUMENTS` を名前にして実行する（名前が無ければ変更内容から `Add<対象>` / `Change<対象>` の形で決める）:

```powershell
./scripts/Migrations.ps1 -Add $ARGUMENTS
```

3プロバイダーに追加し、最後に `DatabaseProviderTests` で3つともモデルに追いついていることを確かめる。
途中のプロバイダーで失敗したときは、作った分をスクリプトが取り消す。

## 2. 失敗したとき

- `DatabaseProviderTests` の「SQL Server で索引とキーに長さ無制限の文字列列を使っていない」／「削除の連鎖が…」が落ちたら、
  マイグレーションを取り消してから設定（`HasMaxLength` / `OnDelete(DeleteBehavior.NoAction)`）を直し、もう一度追加する:
  ```powershell
  ./scripts/Migrations.ps1 -RemoveLast
  ```
- 取り消しは3プロバイダーの最新の名前がそろっているときだけ動く。SQLiteの開発用DBに適用済みなら止まる（アプリを起動した後など）。

## 3. 守ること

- **マイグレーションに生SQL（データの修正・移行）を書かない**。3方言で書き分けることになるため。
  既存データの手当てが要るときは、起動時の処理（`DependencyInjection.InitializeDatabaseAsync`）やサービス側に C# で書く。
- 取り消し後に各 `MesAppDbContextModelSnapshot.cs` の `ToTable("X", (string)null)` だけの差分が残ることがある（意味は同じ）。
  直前に追加したものを取り消しただけなら `git checkout` で戻してよい。
