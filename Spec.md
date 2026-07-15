# 製造実績管理システム 仕様書（ASP.NET Core バックエンド + .NET MAUI Blazor Hybrid / Web クライアント版）
プロダクト名：Parallel Factory MES

作成日: 2026-07-15（改訂2）
参考: みんなのMES（min-MES） https://min-mes.com/ / OSS: https://github.com/mihatama/open-mes-project

---

## 1. プロジェクト概要

### 1.1 目的
製造現場における生産実績・在庫・品質・設備情報を一元管理するオールインワン型MES（製造実行システム）を開発する。オープンソースの「みんなのMES」を機能面の参考とする。バックエンドはASP.NET Core APIに一本化し、データベースはバックエンドのみが保持する単一DBとする。クライアントは「.NET MAUI Blazor Hybrid（デスクトップ）」と「Webブラウザ（Blazorクライアント）」の2種類を用意し、いずれもAPI経由でのみデータにアクセスする。

### 1.2 対象ユーザー
- 現場作業者（実績入力、作業指示確認）
- 生産管理担当者（作業指示発行、進捗管理）
- 品質管理担当者（検査記録、不良分析）
- 設備保全担当者（設備稼働履歴、保全記録）
- システム管理者（マスタ管理、ユーザー管理、ライセンス管理）

### 1.3 機能スコープ
みんなのMES同等の4本柱＋共通基盤：
1. 生産管理（作業指示・実績・進捗）
2. 在庫管理（入庫・在庫・出庫）
3. 品質管理（検査記録・分析）
4. 設備管理（稼働履歴・資産管理）
5. 共通基盤（認証・ユーザー管理・ダッシュボード・バーコード/QRスキャン・ライセンス管理）

### 1.4 製品構成・課金モデル

| 種別 | 内容 |
|---|---|
| **標準版** | .NET MAUI Blazor Hybridデスクトップアプリからのアクセスのみ。バックエンドAPI・DBは必須（ローカルにバックエンドを同梱 or 社内サーバーに配置）。 |
| **プレミアム版** | 標準版の全機能に加え、**Webブラウザからのアクセス機能**を解放。同一バックエンドAPI・同一DBに対して、ブラウザ上のBlazorクライアントからもアクセス可能になる。 |

- 課金はユーザー単位（Windows Store上のIn-App Purchase、`Windows.Services.Store` APIで検証）。
- プレミアム状態はバックエンドAPI側でも保持し、Webクライアントへのアクセス許可判定に利用する（デスクトップ側だけでなくAPI側でもライセンス検証を行うことで、Webクライアントの不正利用を防ぐ）。

---

## 2. システムアーキテクチャ

バックエンド（ASP.NET Core API + DB）を中心に、クライアントはMAUI Blazor HybridとWebブラウザの2種類。両クライアントはUIコンポーネントを共有し、いずれもデータアクセスはAPI経由のみで、DBには直接触れない。

```
┌─────────────────────────┐        ┌─────────────────────────┐
│ MesApp.Client.Maui       │        │ MesApp.Client.Web         │
│ (.NET MAUI Blazor Hybrid)│        │ (Blazor WebAssembly /     │
│  Windows Store配布        │        │  ブラウザからアクセス)       │
│  デスクトップ本体           │        │  プレミアム時のみ利用可      │
└────────────┬─────────────┘        └────────────┬─────────────┘
             │  HTTPS / REST API                  │  HTTPS / REST API
             └───────────────┬─────────────────────┘
                              ▼
                 ┌─────────────────────────┐
                 │  MesApp.Api               │
                 │ (ASP.NET Core Web API)    │
                 │ 認証(JWT等)・業務ロジック   │
                 │ ライセンス検証             │
                 └────────────┬─────────────┘
                              ▼
                 ┌─────────────────────────┐
                 │   単一DB（バックエンドのみ保持）│
                 │  既定: SQLite／設定で変更可     │
                 │  (PostgreSQL, SQL Server 等)  │
                 └─────────────────────────┘

共有UIコンポーネント: MesApp.UI（Razor Class Library）
  → MesApp.Client.Maui と MesApp.Client.Web の両方から参照
```

### 2.1 各コンポーネントの役割
- **MesApp.Api（バックエンド）**: 唯一のデータアクセス主体。認証、業務ロジック（在庫整合性チェック、実績集計など）、ライセンス検証をすべて担う。DBは1つのみ保持し、クライアントからの直接アクセスは行わせない。
- **MesApp.Client.Maui**: Windows Store配布のデスクトップクライアント。HTTPでMesApp.Apiと通信。ローカルDBは持たない（オフラインキャッシュのみ検討、7.3節参照）。
- **MesApp.Client.Web**: Blazor WebAssembly（またはBlazor ServerをAPI経由で組む場合はBlazor Web App）として実装し、ブラウザから同じAPIを呼び出す。プレミアム契約時のみ有効化。
- **MesApp.UI（Razor Class Library）**: 画面・コンポーネントの実体をここに集約し、MAUIクライアント・Webクライアント双方で再利用する。
- **MesApp.Core**: ドメインモデル・DTO・APIコントラクト（リクエスト/レスポンス型）。API・両クライアントで共有。

### 2.2 認証・ライセンスの流れ
1. クライアント（MAUI/Web問わず）はログイン時にMesApp.Apiへ認証リクエストを送り、JWTトークンを取得する。
2. 以降のAPI呼び出しはすべてJWT付きで行う。
3. Webクライアントからのアクセス時は、API側でユーザーのライセンス種別（標準/プレミアム）を確認し、プレミアムでなければWebアクセスを拒否する。
4. デスクトップ（MAUI）側でのプレミアム購入はWindows StoreのIn-App Purchaseで行い、購入結果をAPIに送信してユーザーのライセンス状態を更新する。

---

## 3. 機能モジュール詳細

### 3.1 生産管理（作業指示・実績）
- 作業指示（ワークオーダー）の発行・一覧・詳細確認
- 実績入力：ワークオーダー選択（またはQR/バーコードスキャンで自動選択）→ 良品数・不良数・作業時間・作業者を記録
- 進捗状況のリアルタイム表示（計画数に対する実績数、達成率）
- 日報・実績集計レポート出力（CSV/Excel）

### 3.2 在庫管理
- 入庫登録
- 在庫照会（品目別・ロケーション別のリアルタイム在庫数）
- 出庫登録（生産実績と連動した部材消費、または出荷に伴う出庫）
- 棚卸機能（実棚数入力と理論在庫との差異表示）

### 3.3 品質管理
- 検査記録入力（合格/不合格、不良項目、不良数、検査者）
- 不良分析（不良項目別・工程別・期間別の集計）
- トレーサビリティ：ロット番号・シリアル番号による製造履歴の追跡

### 3.4 設備管理
- 設備マスタ（資産番号、設置場所、稼働状態）
- 稼働履歴記録（稼働/停止/段取り替え等のステータス変更ログ）
- 保全記録（点検・修理履歴、次回保全予定）

### 3.5 共通基盤
- ユーザー管理・ロールベースアクセス制御（API側で一元管理）
- ログイン認証（ASP.NET Core Identity + JWT、API側に集約）
- ダッシュボード（当日実績サマリ、稼働率、不良率などのKPI表示）
- バーコード/QRスキャン共通コンポーネント（MAUI: ZXing.Net.MAUIでカメラ利用／Web: ブラウザのカメラAPI or USB HIDリーダー入力）
- **ライセンス管理**：API側でユーザーごとのライセンス種別を保持し、Webクライアントアクセス可否を判定。MAUI側はWindows StoreのIn-App Purchase結果をAPIに連携。

---

## 4. DBプロバイダー切替の設計（バックエンドのみ）

DBはバックエンド（MesApp.Api）のみが保持し、既定はSQLiteとする。EF Coreの`DbContext`をプロバイダー非依存に設計し、`appsettings.json`でプロバイダーと接続文字列を切り替えられるようにする。クライアント側はDB設定を一切持たない。

```json
{
  "Database": {
    "Provider": "Sqlite",   // "Sqlite" | "PostgreSql" | "SqlServer"
    "ConnectionString": "Data Source=mesapp.db"
  }
}
```

- `MesApp.Infrastructure`内でプロバイダーごとの`UseSqlite` / `UseNpgsql` / `UseSqlServer`を設定値に応じて切り替える。
- マイグレーションはプロバイダーごとに作成が必要（EF Coreの制約）。初期実装ではSQLiteのみ対応し、後続フェーズでPostgreSQL/SQL Server対応を追加する。
- SQLiteのままでも複数クライアントからの同時アクセスはAPI経由であれば問題ない（DBファイルへの直接同時アクセスはAPIプロセス内でシリアライズされるため）。ただし高負荷が想定される場合はPostgreSQL等への切替を推奨する旨をドキュメントに明記する。

---

## 5. データモデル（主要エンティティ）

| エンティティ | 主な項目 |
|---|---|
| User | ユーザーID、氏名、ロール、パスワードハッシュ、ライセンス種別 |
| Product（品目マスタ） | 品目コード、品目名、単位、規格 |
| Process（工程マスタ） | 工程コード、工程名、標準作業時間 |
| Equipment（設備マスタ） | 資産番号、設備名、設置場所、状態 |
| Location（棚卸ロケーション） | ロケーションコード、倉庫名 |
| WorkOrder（作業指示） | 指示番号、品目、工程、計画数、納期、状態 |
| ProductionRecord（生産実績） | 作業指示ID、作業者、良品数、不良数、開始/終了時刻 |
| InventoryTransaction（入出庫履歴） | 品目、数量、区分（入庫/出庫）、日時、関連ロット |
| InventoryStock（在庫） | 品目、ロケーション、現在数量 |
| QualityInspection（検査記録） | 対象ロット、検査項目、判定、不良数、検査者 |
| EquipmentLog（設備稼働履歴） | 設備ID、ステータス、開始/終了時刻、備考 |
| LicenseInfo（ライセンス情報） | ユーザーID、ライセンス種別（標準/プレミアム）、購入日、有効期限、StoreトランザクションID |

---

## 6. 画面一覧（共有Razorコンポーネント／MesApp.UI）

1. ログイン画面
2. ダッシュボード（KPIサマリ、当日実績、稼働率）
3. 作業指示一覧・詳細
4. 実績入力画面（バーコード/QRスキャン対応）
5. 在庫照会・入出庫登録画面
6. 品質検査入力・不良分析画面
7. 設備管理・稼働履歴画面
8. マスタ管理画面（品目／工程／設備／ユーザー）
9. 設定画面（API接続先、ライセンス状態表示・プレミアムアップグレード導線）

---

## 7. 非機能要件

### 7.1 Windows Store公開関連（MAUIクライアント）
- MSIXパッケージとしてビルド（`dotnet publish` + `WindowsPackageType=MSIX`）
- Microsoft Store Partner Centerでアプリ登録、In-App Purchase（Add-on）としてプレミアムアップグレードを設定
- ライセンス確認は`Windows.Services.Store`名前空間のAPIを使用し、購入結果をMesApp.Apiに送信してユーザーのライセンス状態を更新
- コード署名、Microsoft Store認定ポリシーへの準拠

### 7.2 配布形態
- **Windows Store（MSIX）**: MesApp.Client.Maui（デスクトップクライアント）の主要配布経路
- **Zip配布**: MesApp.Api（バックエンド）のビルド済みバイナリをzip化し、社内サーバー等に手動配置できるようにする
- **Docker配布**: MesApp.Apiおよび既定DBをDockerイメージ化し、`docker-compose.yml`で一括起動できるようにする（バックエンドのセルフホスト手段として）
- MesApp.Client.Webはバックエンドと同じサーバー上でホストするか、静的ファイルとして別途配信する（Blazor WebAssemblyの場合）

### 7.3 オフライン動作について
- 本構成はクライアントがAPI経由でしかDBにアクセスしないため、API・DBがダウンしているとクライアントは基本的に操作不能になる。
- 完全オフライン対応が必要な場合は、MAUIクライアント側にローカルキャッシュ（SQLite等）と再送信キューを追加するオプションを将来検討する（初期リリースではスコープ外とし、Phase以降で拡張）。

### 7.4 セキュリティ
- 認証はASP.NET Core Identity + JWT、API側に集約
- 通信は全てHTTPS
- ロールベースアクセス制御はAPI側で一元的に判定（クライアント側の表示制御はUXのためのみで、最終判定は常にAPI側）
- ライセンス状態の改ざん防止のため、Webアクセス可否判定は必ずAPI側で行う（クライアント側のフラグのみに依存しない）

### 7.5 性能・規模
- 想定同時接続端末数、日次実績データ件数などは導入規模に応じて別途設定（初期は数十端末規模を想定）

### 7.6 ログ・監査
- 操作ログ（誰が・いつ・何を変更したか）をAPI側で記録し、追跡可能にする

---

## 8. 技術スタック

| 分類 | 技術 |
|---|---|
| 共有UI | Razor Class Library（Blazorコンポーネント、MesApp.UI） |
| デスクトップクライアント | .NET 8 / .NET MAUI Blazor Hybrid |
| Webクライアント | Blazor WebAssembly（または、要件に応じてBlazor Server） |
| バックエンドAPI | ASP.NET Core Web API（.NET 8） |
| ORM | Entity Framework Core（プロバイダー切替対応、バックエンドのみ） |
| 既定DB | SQLite |
| 追加対応DB | PostgreSQL（Npgsql）、SQL Server |
| 認証 | ASP.NET Core Identity + JWT（APIに集約） |
| バーコード/QRスキャン | ZXing.Net.MAUI（デスクトップ）／ブラウザカメラAPI or USB HIDリーダー（Web） |
| ライセンス管理 | Windows.Services.Store API（Store側）＋ API側でのライセンス状態管理 |
| パッケージング | MSIX（Windows Store、MAUIクライアント）、自己完結型Zip（API）、Dockerイメージ（API） |

---

## 9. ディレクトリ構成案

```
/MesApp
  /src
    /MesApp.UI                  … 共有Razorコンポーネント（Razor Class Library）
      /Pages
      /Components
    /MesApp.Client.Maui          … .NET MAUI Blazor Hybrid（デスクトップ、Windows Store配布）
      /Services                  … APIクライアント、スキャン処理、StoreライセンスAPI連携
    /MesApp.Client.Web           … Blazor WebAssembly（Webブラウザ用クライアント）
      /Services                  … APIクライアント
    /MesApp.Api                  … ASP.NET Core Web API（バックエンド本体）
      /Controllers
      /Services                  … 業務ロジック、ライセンス検証
      Dockerfile
    /MesApp.Core                 … 共通ドメインモデル・DTO・APIコントラクト
    /MesApp.Infrastructure        … EF Core、DBプロバイダー切替、リポジトリ実装（API専用）
  /tests
    /MesApp.Api.Tests
  /docker
    docker-compose.yml            … MesApp.Api + DB（必要に応じ）を一括起動
  /docs
    仕様書.md（本ドキュメント）
```

---

## 10. 開発フェーズ（マイルストーン）

| フェーズ | 内容 |
|---|---|
| Phase 1 | バックエンド基盤構築（MesApp.Api / Core / Infrastructure、EF Core+SQLiteでのDB設計・マイグレーション、JWT認証） |
| Phase 2 | 生産管理API（作業指示・実績入力・進捗）の実装 |
| Phase 3 | 在庫管理API |
| Phase 4 | 品質管理API |
| Phase 5 | 設備管理API |
| Phase 6 | MesApp.UI（共有Razorコンポーネント）でのAPI呼び出しUI実装（作業指示・実績入力から着手） |
| Phase 7 | MesApp.Client.Maui（デスクトップ）でMesApp.UIを組み込み、動作確認。バーコード/QRスキャン統合 |
| Phase 8 | Windows Store向けMSIXパッケージング、In-App Purchase（ライセンス）実装、APIとのライセンス連携 |
| Phase 9 | MesApp.Client.Web（Blazor WebAssembly）の追加、プレミアム判定によるアクセス制御実装 |
| Phase 10 | DBプロバイダー切替対応（PostgreSQL/SQL Server）、Dockerイメージ化、Zip配布パッケージ作成、最終テスト |

---

## 11. Claude Codeへの実装依頼時の進め方（推奨）

本仕様書をベースに、フェーズごとに区切って実装を依頼することを推奨する。

**Phase 1の指示例:**
> この仕様書（MES仕様書.md）を読み込んだ上で、Phase 1（バックエンド基盤構築）を実施してください。ソリューション構成（MesApp.Api / MesApp.Core / MesApp.Infrastructure）を作成し、EF Core + SQLiteでのDBモデル定義・マイグレーション、ASP.NET Core Identity + JWTによる認証基盤を実装してください。DBプロバイダーは設定ファイルで切替可能な形で抽象化してください。

**Phase 6の指示例（共有UI着手時）:**
> Phase 1〜5で実装済みのMesApp.Apiを呼び出す形で、MesApp.UI（Razor Class Library）に作業指示一覧・実績入力画面のコンポーネントを実装してください。この時点ではまだMAUI/Webクライアントには組み込まず、コンポーネント単体でビルドが通ることを確認してください。

**Phase 7の指示例（MAUIクライアント統合時）:**
> MesApp.UIのコンポーネントを参照する`MesApp.Client.Maui`（.NET MAUI Blazor Hybrid）プロジェクトを作成し、MesApp.Apiと通信するHTTPクライアントサービスを実装してください。

各フェーズ完了後に動作確認を行い、次フェーズへ進む。

---

## 12. 参考

- みんなのMES（min-MES）: https://min-mes.com/
- OSSリポジトリ: https://github.com/mihatama/open-mes-project
