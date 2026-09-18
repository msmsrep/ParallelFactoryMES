---
description: ビルドとテストをフィルタ実行し、DoD（Orchestration.md §5.1）を判定する
argument-hint: "[テストクラス名（任意・失敗調査時のみ）]"
allowed-tools: PowerShell, Bash, Read, Grep, Glob
---

`Orchestration.md` §4.4／§5.1 に従って検証する。**生ログを出力しないこと。**

## 1. ビルド

```powershell
dotnet build ParallelFactoryMES.slnx -v q --nologo 2>&1 | Select-String -Pattern "error|warning CS|Build succeeded|ビルドに成功" | Select-Object -First 40
```

## 2. テスト

```powershell
dotnet test ParallelFactoryMES.slnx -v q --nologo 2>&1 | Select-String -Pattern "error|Failed|Passed!|成功!|失敗|合計" | Select-Object -First 40
```

失敗した場合、**同じコマンドを繰り返さない**。該当クラスだけを詳細実行する（`$ARGUMENTS` が指定されていればそれを使う）:

```powershell
dotnet test tests/MesApp.Api.Tests --filter FullyQualifiedName~<TestClass> -v n
```

## 3. 差分の確認

`git status --short` と `git diff --stat` を実行し、変更ファイル数と対象を把握する。

## 4. DoD 判定（5項目すべてを○/×で報告）

1. ビルド成功、かつ新規の警告が増えていない
2. テスト全緑
3. 仕様に触れる変更なら `Spec.md` を同一コミットで更新済み（改訂履歴行を含む）
4. 変更が MES.md のどの業務プロセス（No）に当たるか説明できる → `docs-dev/CodeMap.md` で確認
5. 変更ファイル数が着手前の想定と一致（乖離していれば「設計ミスの疑い」として明示する）

×があるものは何が足りないかを1行で書く。**推測で○を付けない。** 全項目○のときだけ「完了」と報告する。
