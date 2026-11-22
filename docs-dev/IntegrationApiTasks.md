# 連携API（APIキー＋OpenAPI） バックログ

工場の中に置いて使う配布（Docker・zip）で、販売管理・生産管理（ERP）や社内のスクリプトが
**プログラムから** MES とデータをやり取りできるようにする（Spec.md 3.8・7.4。A-20-10-01 ほか）。
フォルダ連携（改訂149）が「プログラムを書かない利用者」向けなのに対し、こちらは「APIを呼べる利用者・システム納入業者」向け。

1カード＝1セッションで進める（`Orchestration.md` §4.1）。カードの間はセッションをクリアし、次へ進む前に前のカードをコミットする。
新しいセッションでは「`docs-dev/IntegrationApiTasks.md` の INT-0x を実行して」とだけ頼めば着手できるように書いてある。
**着手時にカードの「変更ファイルの想定」を確かめ、完了時に実際の件数と突き合わせる**（DoD 5）。

## 背景

- 今の API（`/api/...`）は Blazor 画面のためのもので、認証はログインの JWT（60分）とリフレッシュ Cookie だけ。
  夜間に無人で動くスクリプトや ERP は、パスワードを持ってログインし続けるしかない。
- 画面用の DTO は内部ID で指し、画面の都合で形が変わる。外部が依存すると、画面を直しただけで連携が壊れる。
- OpenAPI の文書は開発環境（`IsDevelopment()`）でしか出しておらず、全 API が1つの文書に入っている。
- 一方、マスタCSV・実績CSVは**列キーが英語で固定**され（Spec.md 3.8）、自然キー（コード・番号）で指し、
  単票APIと同じ業務サービス・権限を通る。外部との約束として既に安定している。

## 進み具合

| カード | 状態 |
|:--|:--|
| INT-01 APIキーの土台 | 完了（2026-11-22。Spec.md 改訂150） |
| INT-02 CSVの取込・出力を連携APIで | 未着手 |
| INT-03 実績の取得 | 未着手 |
| INT-04 OpenAPI 文書と利用者ガイド | 未着手 |

## 決定事項（2026-11-22 確定）

| 論点 | 案 | 理由 |
|:--|:--|:--|
| キーと権限 | **APIキーは MES の利用者に紐づけ、権限はその利用者のロールで判定する**。キー独自の権限（スコープ）は持たない | フォルダ連携と同じ考え方。新しい権限モデルを作らずに済み、画面・CSV・連携で判定がずれない。用途ごとに専用の利用者を作り、必要なロールだけを与える |
| キーが使える範囲 | **連携API（`/api/integration/v1/...`）だけ**。既存の画面用 API は JWT のまま受け付けない | 画面用 API を外部の約束にしない（変えられなくなる）。キーが漏れたときの影響も絞れる |
| v1 の中身 | ①**CSVの取込・出力を HTTP で**（既存のマスタCSV・実績CSVの種別と列をそのまま使う）②**実績の取得**（生産実績と、その訂正を ID の続きから読む） | 列キーが既に固定されていて、JSON の DTO を種別ごとに作り直さずに全種別を外へ開ける。業務別の JSON API（指図の登録など）は要望が出てから v1 に足す |
| 再送への備え | 取込は **`Idempotency-Key` ヘッダ**を受け付ける。同じキー・同じ内容なら前の結果を返し、同じキーで内容が違えば 409。24時間保持 | 実績CSVは常に新規登録なので、通信が切れて送り直すと二重に登録される。自然キーの重複で弾ける種別ばかりではない |
| 失敗の応答 | 取込のエラーは **422＋取込結果（行ごとのエラー）**。認証 401、権限 403、種別なし 404 | 画面用は 200 で `Succeeded=false` を返すが、機械は HTTP の状態で分岐したい |
| OpenAPI | 連携APIだけの文書 `/openapi/integration-v1.json` を**全環境で**出す（認証不要。中身に秘密は無い）。**画面（Swagger UI 等）は入れない** | 外部パッケージを増やさない。利用者は文書を Postman・Visual Studio などに読み込める。ガイドに呼び出し例を載せる |
| 互換の約束 | v1 の中では**項目を足すだけ**。消す・名前や意味を変えるときは v2 を作る。CSV の列キーも同じ扱いになることを Spec.md に書く | 利用者が自分のペースで版を上げるので、上げても連携が動き続けることが最優先 |
| CSV の列の約束 | **列は消さない／必須の列を増やさない（新しい列は省けば今までどおりに動く形で足す）／名前を変えるときは古い名前も受け付ける**。全種別の列定義を基準ファイルに固定し、テストで突き合わせる（足すのは通り、消す・改名・必須化は落ちる） | 連携APIとフォルダ連携で CSV の列が外部との約束になる。これまでも列は「省いてよい列」として足してきたが、うっかり壊したことに気づく仕組みが無かった。v2 を作るより古い名前を受け付けるほうが安い |
| 実績CSVの位置づけ | 「試験・移行・教育用」から**本番の連携にも使う取込**へ Spec.md 3.8 の記述を改める | 連携の約束にする以上、試験用という位置づけのままでは変更の判断基準がぶれる |
| 機械が読めるエラー | v1 では今の `CsvImportError(Line, Message)` のまま出す。後から `Code`（と `Column`）を**足す余地を残す**（足すのは互換を崩さない） | 今のエラーは訳された文章だけで、機械は分岐できない。全種別に付けるのは量が多いので、要望が出てから足す |
| レート制限 | 入れない | 工場の LAN の中で使う前提。件数・サイズの上限は既存の CSV 取込の上限（5MB・20,000行）で足りる |

## カード

### INT-01 APIキーの土台（発行・失効・認証）

**目的**：システム管理者が利用者ごとに APIキーを発行・失効でき、連携API がそのキーで認証できる状態にする（まだ連携APIは無いので、テスト用に1本だけ読み取りの口を置く）。

- エンティティ `ApiKey`（`Core/Entities/`）：`Id`・`Name`（用途）・`UserId`・`KeyHash`（SHA-256。平文は保存しない＝`RefreshToken` と同じ）・`Prefix`（先頭8文字。一覧で見分ける用）・`CreatedAt`・`ExpiresAt`（任意）・`LastUsedAt`・`RevokedAt`
- キーの形は `pfm_` ＋ 乱数32バイトの Base64URL。**発行時に一度だけ**応答に平文を載せる
- 認証ハンドラ（`Api/ApiKeyAuthentication.cs`）：`X-Api-Key` ヘッダを読み、ハッシュで引く。**利用者が有効・キーが失効していない・期限内**を毎回確かめ、ロールは毎回 DB から読む（`AccessTokenValidation` と同じく、無効化・ロール変更を即時に効かせる）。`LastUsedAt` は1分に1回だけ書く
- 連携API の Controller には `[Authorize(AuthenticationSchemes = "Bearer,ApiKey")]` を付ける（画面からの確認用に JWT も通す）。既存の Controller は既定の JWT のまま
- 管理 API `api/api-keys`（一覧・発行・失効。**システム管理者だけ**）と画面 `/api-keys`（`NavMenu` の「管理」、監査ログ・フォルダ連携と並べる）
- 監査ログ：発行・失効を記録（平文・ハッシュは残さない）。キーで呼ばれた操作は紐づく利用者として記録され、IP も残る
- 疎通確認用 `GET api/integration/v1/whoami`（キーの名前・利用者・ロールを返す）

**変更ファイルの想定（約20＋マイグレーション）**：`Core/Entities/ApiKey.cs`、`Infra/MesAppDbContext.cs`、`Infra/Configurations/SystemConfigurations.cs`、マイグレーション3プロバイダー（`scripts/Migrations.ps1 -Add AddApiKeys`）、`Core/Contracts/Integration/ApiKeyDtos.cs`、`Api/ApiKeyAuthentication.cs`、`Api/MesAppHost.cs`、`Api/Controllers/ApiKeysController.cs`、`Api/Controllers/Integration/WhoAmIController.cs`、`Web/Pages/ApiKeys.razor`、`Web/Layout/NavMenu.razor`、`UiText.en.resx`、`ApiText.en.resx`、`Tests/AuthTests.cs`、`Spec.md`（7.4・6.1・改訂履歴）、`docs-dev/CodeMap.md`、`docs/operations.md`・`docs/roles.md`（＋英語版）
**受入基準**：キーで whoami が通る／失効・期限切れ・利用者の無効化で即 401／キーで画面用 API（例 `api/products`）は 401／発行の応答以外に平文が出ない（一覧・監査ログ）／システム管理者以外は管理 API が 403

**実施メモ（2026-11-22）**：変更は20ファイル＋マイグレーション9（想定どおり。想定に無かったのは `Core/Constants/MesClaimTypes.cs` にキーのID・名前のクレームを足した1件）。SQLite で全件、SQL Server・PostgreSQL で `AuthTests`・`DatabaseProviderTests` を確認した。
- 連携API の Controller は `[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]` を付ける（INT-02 以降も同じ。付け忘れるとキーでは401）
- 画面の文言「発行」は既存の訳が帳票の **Print** なので、キーの発行ボタンは「キーを発行」にした。短い既存のキーを流用するときは英訳の意味を確かめる

### INT-02 CSVの取込・出力を連携APIで（再送への備えつき）

**目的**：ERP・スクリプトが、マスタCSV・実績CSVの全種別を APIキーで取り込み・出力できるようにする。

- `POST api/integration/v1/masters/{kind}`・`POST .../actuals/{kind}`（本文に CSV、または multipart）、`POST .../masters/bundle`・`.../actuals/bundle`（ZIP）、`GET .../masters/{kind}`（出力）、`GET .../kinds`（種別と列定義）
- 取込は既存の `MasterCsvService`・`ActualCsvService` をそのまま呼び、権限は `MasterCsvKinds.WriteRolesOf`・`ActualCsvKind.WriteRoles` で判定する（画面・フォルダ連携と同じ）。`dryRun` も受け付ける
- 成功 200、取込のエラー 422（本文は `CsvImportResult` / `CsvBundleImportResult`）
- `Idempotency-Key`：エンティティ `IntegrationRequest`（キー・APIキー（または利用者）・要求本文のハッシュ・応答の状態と本文・日時）。同じキー＋同じ本文は保存した応答を返す、本文が違えば 409、24時間を過ぎたものは起動時と取込のついでに消す。**dryRun は保存しない**
- 画面用の `MasterCsvController`・`ActualCsvController` の受付処理（種別の解決・ZIP の振り分け）は、フォルダ連携と合わせて3か所目になるので、共通の受付（`Api/Services/CsvIntake.cs` など）へ寄せてから使う
- **列の約束を固定する**：全種別（マスタ・実績）の列定義（種別・列名・必須か）を基準ファイル `tests/MesApp.Api.Tests/CsvContract.v1.json` に書き出し、テストで今の定義と突き合わせる。**列の追加と、必須→任意は通し、種別・列の削除、改名、任意→必須は落とす**。列を足したら基準ファイルを更新する（落ちたテストのメッセージに更新のしかたを出す）
- **古い列名を受け付ける仕組み**：列定義に別名（`Aliases`）を持たせ、`CsvTable` が別名の列も同じ列として読む（ヘッダーに新旧両方があればエラー）。今は使う列が無いので、仕組みとテストだけ入れる
- Spec.md 3.8 に列の約束（消さない・必須を増やさない・改名は別名で受ける）を書き、実績CSVの位置づけを「試験・移行・教育用」から本番の連携にも使う取込へ改める。`CLAUDE.md` の「新機能を追加する順序」の CSV 対応の段に「列の約束（基準ファイルの更新）」を足す

**変更ファイルの想定（約19＋マイグレーション）**：`Core/Entities/IntegrationRequest.cs`、`MesAppDbContext.cs`、`SystemConfigurations.cs`、マイグレーション3プロバイダー（`AddIntegrationRequests`）、`Api/Services/CsvIntake.cs`（新規）、`MasterCsvController.cs`・`ActualCsvController.cs`・`FolderLinkService.cs`（共通の受付へ寄せる）、`Api/Controllers/Integration/IntegrationCsvController.cs`、`Api/Services/IdempotencyService.cs`、`Core/Contracts/Masters/MasterCsvContracts.cs`（`CsvColumnInfo` に別名）、`Api/Services/CsvTable.cs`（別名の解決）、`tests/MesApp.Api.Tests/CsvContract.v1.json`（新規）、`ApiText.en.resx`、`Tests/MasterCsvTests.cs`、`Spec.md`（3.8・改訂履歴）、`CodeMap.md`、`CLAUDE.md`
**受入基準**：キーでマスタ・実績・ZIP を取り込める／ロールの無い種別は 403／同じ `Idempotency-Key` で2回送っても実績は1回分／本文を変えて同じキーなら 409／既存の画面用CSVと FolderLink のテストが変わらず通る／基準ファイルから列を1つ消す・必須にする・名前を変えると約束のテストが落ち、列を足すだけなら通る／別名の列名で書いた CSV が取り込める

### INT-03 実績の取得（生産実績と訂正）

**目的**：ERP が完成実績を取りに来られるようにする（MES から送り出す仕組みを作らずに済む）。

- `GET api/integration/v1/production-results?afterId=&limit=`：生産実績を ID の昇順で返す。指図番号・工程順序・品目コード・工程コード・良品／不良／廃棄／手直し数・開始／終了・産出ロット番号・入庫先ロケーションコード・直コード・承認日時。**内部ID は `id`（続きを読むための位置）以外に出さない**
- `GET api/integration/v1/production-result-corrections?afterId=`：訂正（`ProductionRecordCorrection`）を同じ形で返す。ERP は「実績の続き」と「訂正の続き」の2本を読めば取りこぼさない
- 応答に `nextAfterId` を入れる。`limit` は既定500・上限5,000
- 承認前の実績も返す（`approvedAt` が null）。承認済みだけ欲しい側は自分で絞る

**変更ファイルの想定（約8）**：`Core/Contracts/Integration/ProductionResultDtos.cs`、`Api/Controllers/Integration/ProductionResultsController.cs`、`ApiText.en.resx`、`Tests/ProductionTests.cs`、`Spec.md`、`CodeMap.md`、（必要なら）`Infra/Configurations` に索引
**受入基準**：`afterId` で続きだけが返る／訂正が訂正の口に出る／取消された作業指示の実績の扱いを決めて Spec.md に書く／参照の権限は認証済みなら誰でも（既存の参照系と同じ）

### INT-04 OpenAPI 文書と利用者ガイド

**目的**：利用者が仕様書を見て自分で連携を組めるようにする。

- `AddOpenApi("integration-v1")` で連携API だけを集めた文書を作り、`/openapi/integration-v1.json` を全環境で出す（既存の開発用文書は開発環境のまま）
- セキュリティの定義に `X-Api-Key` を載せる。説明は XML コメントから（`GenerateDocumentationFile`。警告を増やさないよう公開型すべてにコメントがあるか確かめる）
- 運用ガイドに「連携API」の節（日英）：キーの発行、PowerShell と curl の呼び出し例、`Idempotency-Key` の使い方、実績の続きの読み方、互換の約束
- Spec.md 7.x に互換の約束（v1 は足すだけ、CSV の列キーも同じ）

**変更ファイルの想定（約8）**：`Api/MesAppHost.cs`、`Api/MesApp.Api.csproj`、（必要なら）`Api/Integration/IntegrationOpenApi.cs`、`Tests/AuthTests.cs`（文書が取れる・連携API だけが載る）、`Spec.md`、`docs/operations.md`・`docs/en/operations.md`、`CodeMap.md`
**受入基準**：本番構成で文書が 200／画面用 API が文書に載らない／ガイドの例を新しい DB でそのまま実行して通る

## 順序と依存

INT-01 → INT-02 → INT-03 → INT-04。INT-02 と INT-03 は INT-01 だけに依存するので、順番は入れ替えてよい。
INT-04 は全部の口が揃ってから（文書とガイドの例を一度で確かめるため）。

## 対象外（このバックログでは作らない）

- MES から外へ送る連携（Webhook・Outbox）
- 設備イベントの受信（PLC・ゲートウェイ）→ 別のバックログ
- 業務別の JSON API（製造指図の登録など）→ CSV の口で足りないと分かってから v1 に足す
- キー独自の権限（スコープ）、レート制限
- 取込エラーの機械向けコード（`CsvImportError.Code`・`Column`）→ 要望が出てから足す（追加なので v1 のまま）
