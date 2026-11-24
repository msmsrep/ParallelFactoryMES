# Microsoft Store 掲載情報（英語）

Partner Center の「Store 掲載情報 → 英語（米国）」に貼り付ける原稿。日本語版は `StoreListing.md`。書くときの約束も同じ。
欄の並びは日本語版と揃えてある。日本語版を変えたら、こちらも同じコミットで直す。

**提出の前に**：マニフェスト（`src/MesApp.Desktop/Package.appxmanifest`）の `Resources` が `ja-JP` だけだと、
Partner Center に英語の掲載欄が出ない。英語の掲載を足すときは `<Resource Language="en-US" />` を加えてパッケージを作り直す。

**用語**：機能名は英語表示の画面（`UiText.en.resx`）のメニュー名に合わせた（Manufacturing Orders / Dispatching / Work Orders /
Receiving / Stocktaking / Inspections / Nonconformances / Shipment Judgments / Traceability / Maintenance / Work Procedures /
Tool Management / Master Data / Audit Log / Roles / Lot tag）。工程展開は画面に合わせて release とする。

---

## 製品名

```
Parallel Factory MES
```

## 短いタイトル（任意・50文字以内）

```
Parallel Factory MES
```

## 説明（10,000文字以内）

```
Parallel Factory MES is a manufacturing execution system (MES) that records manufacturing orders, work results, inventory, quality and equipment maintenance as one continuous flow. No server or cloud setup is required. Install it and you can start keeping your factory's records on this PC alone.

■ Who it is for
・Shops that keep production records on paper or in Excel and want to trace them lot by lot
・Operations too small for a dedicated server that still want a consistent record from order to shipment
・Teams that want to try out MES workflows hands-on before a full-scale rollout

■ Main features
[Production] Register and approve manufacturing orders, release them to processes, track progress, dispatch work
[Execution] Setup and checklists, recording results (good quantity, defects, working time), material backflush, issue reports
[Logistics & Inventory] Receiving, inventory by lot and location, picking, issuing, transfers and adjustments, shipping, stocktaking
[Quality Control] Inspection orders and results, automatic pass/fail judgment against specification limits, nonconformance records, quality analysis
[Quality Assurance] Shipment judgments, two-way traceability between material lots and product lots
[Maintenance] Equipment operation records, maintenance plans, orders and results, work procedures, tool life management
[Reports] Print work orders and lot tags or save them as PDF (with barcodes). Works with USB barcode readers
[Master Data & Integration] Bulk import and export of master data such as products, processes, routings and equipment via CSV (UTF-8 / Shift_JIS)
[Notifications] Webhooks that immediately notify a URL you register (such as your team chat) when a nonconformance is raised, equipment stops, a lot is put on hold and so on
[Administration] Users and roles (permissions), audit log of operations

■ Records you can trace later
Status changes of manufacturing orders, work orders and lots are kept as history, and the audit log shows who changed what and when. When a record is corrected, its original content and the reason for the correction are kept as well.

■ Your data stays on this PC
All data you enter is stored on this PC and is never sent to anyone, including the developer (if you register a webhook, notifications are sent only to that destination). There is no usage tracking and no advertising. The app's screens run only inside this PC and cannot be reached from other devices on the network.

■ Getting started
On first launch, the initial setup screen appears, where you choose the user name and password for the system administrator account. Sign in, then register processes, products and routings under Master Data. The user guide (website) explains each operation with screenshots.

■ Notes
・This app is designed for use on a single PC. If you need to share the same data across multiple PCs, consider deploying it on a server (the source code is public).
・Uninstalling the app deletes all saved data. To keep your data, close the app and copy the data folder beforehand (see "Administration" in the user guide).
・Even in a narrow window, the app stays usable with a collapsible menu and horizontally scrolling tables.
・The app uses the Microsoft Edge WebView2 Runtime to display its screens (included with Windows 11).
・The app's screens are available in Japanese and English.

This app is open source software (GNU Affero General Public License v3.0).
```

## 新機能（1,500文字以内。初回提出では空欄でもよい）

```
First release.
```

## 製品の機能（1項目200文字以内・最大20項目）

```
Register, approve and release manufacturing orders, and dispatch work
Setup, checklists and result recording in one flow from the work order
Inventory by lot and location, with history of receipts, issues, transfers and adjustments
Inspections from receiving to finished goods, with automatic pass/fail against specification limits
Shipment judgments and two-way traceability between material lots and product lots
Equipment maintenance plans, orders and results, plus tool life management
Print work orders and lot tags or save them as PDF (with barcodes)
Bulk CSV import and export of master data (UTF-8 / Shift_JIS detected automatically)
Role-based permissions and an audit log that keeps values before and after each change
Webhooks that immediately notify a registered URL of nonconformances, equipment stops and more
Data is stored only on this PC. No server needed, no usage tracking (the only thing sent out is to webhook destinations you register)
```

## スクリーンショット

英語表示で撮った `build/store/en/screenshot-1〜5-1920x1080.png` をこの順で登録する。キャプション（200文字以内）は画像内の見出しと揃える。
画面の品目名などが日本語なのは、サンプルデータ（`samples/`）が日本語しか無いため（英語版ユーザーガイドの写真と同じ）。

| 画像 | キャプション |
|:--|:--|
| screenshot-1 | Today’s factory at a glance. Good quantity, defect rate and inspection results for the day on a single screen. |
| screenshot-2 | From order entry to process release. Register manufacturing orders, then approve, release and dispatch. Status history is kept. |
| screenshot-3 | Record results from the work order. Start, setup, checklists, material input and result reporting on the same screen. |
| screenshot-4 | Track stock and history by lot. Receipts, issues, transfers, adjustments and expiry alerts. Trace down to materials. |
| screenshot-5 | One inspection flow, receiving to finished. Issue inspection orders and judge results from measurements. Log nonconformances. |

作り直し方（空のDBで起動したアプリが要る。手順は `build/Capture-AppScreens.js` の冒頭）：

1. `node build/Capture-GuideScreens.js --password <pw> --page intro --out <作業用フォルダー>`（初期管理者の作成とサンプル取込。写真は使わない）
2. `node build/Capture-AppScreens.js --lang en --password <pw>`（`build/store/raw/en/` に実画面）
3. `python build/New-StoreScreenshots.py --lang en`（`build/store/en/` に合成）

見出しを変えたときは `build/New-StoreScreenshots.py` の `SHOTS_EN` とこの表の両方を直す。

## ストアロゴ・トレーラー

タイル以外は英語版を `build/store/en/` に置いた（`python build/New-StoreArt.py --lang en`。
写っている実画面は英語表示の `build/store/raw/en/`）。図柄だけのタイルは日本語版（`build/store/`）と共通。

| 欄 | ファイル | 中身 |
|:--|:--|:--|
| 9:16 ポスターアート（720×1080） | `en/poster-art-720x1080.png` | 図柄＋製品名＋「Manufacturing Execution System」＋「Start your factory records on a single PC.」＋実画面 |
| 1:1 ボックスアート（1080×1080） | `en/box-art-1080x1080.png` | 図柄＋製品名＋「Manufacturing Execution System」 |
| 1:1 アプリタイルアイコン（300×300） | `app-tile-icon-300x300.png` | 日本語版と共通 |
| 16:9 スーパーヒーローアート（1920×1080） | `en/super-hero-art-1920x1080.png` | 文字なし。英語表示の実画面2枚。主役は上2/3に収める（下1/3にストアが文字を重ねる） |

## 検索語句（最大7個・1個30文字以内・合計21語以内）

```
MES
manufacturing execution
production management
traceability
inventory management
quality control
lot tracking
```

## 著作権と商標情報（200文字以内）

```
Copyright (C) 2026 msmsrep
```

## 追加のライセンス条項（10,000文字以内）

```
This app is open source software provided under the GNU Affero General Public License v3.0 (AGPL-3.0). The full license text and source code are available at:
https://github.com/msmsrep/ParallelFactoryMES

This app is provided without any warranty, including the implied warranties of merchantability or fitness for a particular purpose. See the full license text for details.
```

## 開発元

```
msmsrep
```

## 追加のシステム要件

| 欄 | 内容 |
|:--|:--|
| 最小（メモリ） | 4 GB |
| 推奨（メモリ） | 8 GB |
| その他（任意の記述） | Microsoft Edge WebView2 Runtime (included with Windows 11; on Windows 10, the app guides you through installing it if it is missing) |

## プロパティ

プロパティは言語ごとではないので日本語版の設定のまま。英語の掲載で URL を書く欄があれば英語版のページを使う：
プライバシーポリシー `https://msmsrep.github.io/ParallelFactoryMES/en/privacy.html`、
Webサイト `https://msmsrep.github.io/ParallelFactoryMES/en/intro.html`。
