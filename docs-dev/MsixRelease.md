# MSIX リリース手順（Microsoft Store 公開）

`MesApp.Desktop` を MSIX パッケージにして Microsoft Store で配布する手順（Spec.md 7.8）。

配布形態は **単独PC完結**。1つのパッケージに API・DB・Blazor クライアントが同梱され、
プロセス内で Kestrel をループバック起動し、WebView2 で表示する。サーバーもネット接続も不要。

---

## 1. 前提

| 必要なもの | 確認方法 |
|:--|:--|
| Windows 10/11 SDK（`makeappx.exe` / `signtool.exe`） | `C:\Program Files (x86)\Windows Kits\10\bin\<版>\x64\` に存在すること |
| .NET 10 SDK | `dotnet --version` |
| Partner Center 開発者アカウント | https://partner.microsoft.com/dashboard |

WebView2 ランタイムは**配布先**の前提条件。Windows 11 には標準搭載されている。
未導入の環境では起動時に日本語の案内メッセージを出す（`MainForm.OnLoad`）。

---

## 2. パッケージIDの設定（初回のみ）

Partner Center でアプリ名を予約すると「製品管理 → 製品ID」に以下が表示される。
これを `build/msix-identity.json` に転記する。

```json
{
  "IdentityName": "12345Publisher.ParallelFactoryMES",
  "Publisher": "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX",
  "PublisherDisplayName": "あなたの発行元表示名",
  "Version": "1.0.0.0"
}
```

| キー | Partner Center 上の名称 |
|:--|:--|
| `IdentityName` | パッケージ/ID/名前 |
| `Publisher` | パッケージ/ID/発行者 |
| `PublisherDisplayName` | パッケージ/ID/発行者表示名 |

本リポジトリでは設定済み（Store ID: `9P9FQJZH23HC` / PFN: `msmsrep.ParallelFactoryMES_77t1an0ygyrva`）。
いずれも公開される識別子であり秘密情報ではない。

3つの値がどれか1つでも `PLACEHOLDER` のままだと `Pack-Msix.ps1` は停止する。
リポジトリ外で管理したい場合は `-IdentityFile <パス>` で別ファイルを指定する。

`Version` は **`x.y.z.0`（第4桁は必ず 0）**。ストアに提出するたびに上げる。

---

## 3. ローカルで動作確認する

```powershell
./build/Pack-Msix.ps1 -SelfSign
```

**`-AllowUnsigned` は使えない。** フルトラストのデスクトップアプリを含むパッケージは
開発者モードが有効でも未署名ではインストールできない
（`0x80073D2B`：未署名のパッケージに実行可能ファイルのアクティブ化を含めることはできません）。
署名が必須。

`-SelfSign` は提出用パッケージを書き換えず、`..._signed.msix` を別に作る。
発行元（`CN=5E0CB4C9-...`）が一致する有効な証明書が `Cert:\CurrentUser\My` にあれば
それを再利用し、無いときだけ新規作成する（検証のたびに証明書が増えないようにするため）。

証明書が `Cert:\LocalMachine\TrustedPeople` に未登録のときだけ、スクリプトが登録コマンドを表示する。
その場合は**管理者権限の PowerShell** で実行する（MSIXのサイドロードでは
`Root` ではなく `TrustedPeople` に入れれば足りる。信頼範囲を広げないため）。

インストール（管理者権限は不要）:

```powershell
Add-AppxPackage -Path 'artifacts\msix\ParallelFactoryMES_1.0.0.0_x64_signed.msix'
```

**同じバージョンの入れ直しはブロックされる**（`0x80073CFB`：同じIDで内容が異なるパッケージ）。
検証中に作り直したものを入れるときは、先にアンインストールする。

```powershell
Get-AppxPackage *ParallelFactoryMES* | Remove-AppxPackage
```

起動:

```powershell
Start-Process "shell:AppsFolder\msmsrep.ParallelFactoryMES_77t1an0ygyrva!ParallelFactoryMES"
```

### 確認する項目

- ウィンドウが開き、ログイン画面が表示される
- ウィンドウ上端に初回ログインの案内（`admin` / `Mes-admin1`）が表示されている
- その資格情報でログインでき、パスワード変更を求められる
- パスワード変更後に再起動すると、上端の案内が消えている
- マスタ登録・指図・実績入力が一通り動く
- アプリを閉じて再起動しても入力したデータが残っている

### 起動できないとき

起動処理は `%LOCALAPPDATA%\ParallelFactoryMES\startup.log` に記録される。
ウィンドウが出ない・出たまま進まない場合は、まずこれを見る。

```powershell
Get-Content "$env:LOCALAPPDATA\ParallelFactoryMES\startup.log" -Tail 30
```

どの行で止まっているかで切り分けられる（Webアプリの組み立て → DB初期化 → Kestrel起動 →
WebView2初期化 → 画面表示）。起動が2分を超えると打ち切ってウィンドウにエラーを表示する。

正常に終了したときは「終了処理を完了しました」「正常終了」まで残る。
「終了処理を開始します」で止まっている場合はプロセスが終了しきれていない。

二重起動は抑止され、2つ目以降は既存のウィンドウを前面に出して終了する
（同じSQLiteファイルを複数プロセスで奪い合わないようにするため）。ログには
「既に起動しているため既存のウィンドウを前面に出して終了します」と残る。

### データの置き場所

インストール先は `C:\Program Files\WindowsApps\...`（読み取り専用）。
DB・JWT署名鍵・WebView2ユーザーデータは `%LOCALAPPDATA%\ParallelFactoryMES\` に書かれる。

MSIXのファイルシステムリダイレクトは**起きない**（`%LOCALAPPDATA%\Packages\<PFN>\LocalCache\` 配下ではなく実パス）。
そのため**アンインストールしてもデータは残る**。まっさらな状態で初回起動を確認したいときは、
アンインストール後にこのフォルダーを手動で削除する。

アンインストール:

```powershell
Get-AppxPackage *ParallelFactoryMES* | Remove-AppxPackage
```

---

## 4. 提出用パッケージを作る

```powershell
./build/Pack-Msix.ps1
./build/Pack-Msix.ps1 -Architecture arm64
```

`-SelfSign` を付けない＝**未署名**。署名は Microsoft Store が行うため、これで正しい。

出力先は `artifacts/msix/ParallelFactoryMES_<版>_<アーキテクチャ>.msix`。

スクリプトが行うこと:

1. `dotnet publish -c Release -r win-<arch>`（自己完結）
2. `appsettings.Development.json`・`*.pdb`・`*.br`・`*.gz` を除去
   （`.br`/`.gz` は `MapStaticAssets` 向けの事前圧縮で、本構成の `UseStaticFiles` では配信されない）
3. `Package.appxmanifest` のプレースホルダーを `msix-identity.json` の値で置換して `AppxManifest.xml` を生成
4. `makeappx pack`

---

## 5. Partner Center へ提出

1. **パッケージ**：x64 / arm64 の `.msix` をアップロード
2. **年齢区分**：業務用ソフトのため、暴力・性的表現なしで回答する
3. **価格と提供状況**：提供する国・地域を選ぶ
4. **プロパティ**
   - カテゴリ: 「ビジネス」
   - プライバシーポリシーURL: **必須**。データがローカルPCのみに保存され外部送信がないことを明記する
   - サポート連絡先情報
5. **Store 掲載情報**：日本語の説明・スクリーンショット（最低1枚、1366×768 以上）
6. **申請オプション → 認定メモ**：審査員が動かせるよう、以下を必ず書く

   ```
   初回起動時に初期管理者が自動作成されます。
     ユーザー名: admin
     パスワード: Mes-admin1
   初回ログイン後にパスワード変更画面が表示されます。
   ネットワーク接続・外部サーバーは不要で、データはすべてローカルPCに保存されます。
   ```

---

## 6. 審査で問題になりやすい点

| 項目 | 対応 |
|:--|:--|
| ログインできず機能を確認できない | 初回ログイン前はウィンドウ上端に資格情報を常時表示する実装済み。加えて認定メモにも書く（上記） |
| プライバシーポリシー未記載 | 必須。ローカル保存のみ・外部送信なしを明記 |
| WebView2 未導入環境で起動しない | 起動時に検出して案内済み。認定メモにも前提として書いておく |
| タイル画像が既定のまま | `build/New-MsixAssets.ps1` の `Draw-Mark` を書き換えて再生成する |

---

## 7. タイル画像・アイコンを差し替える

```powershell
./build/New-MsixAssets.ps1
```

`build/New-MsixAssets.ps1` の `Draw-Mark` 関数が唯一の描画箇所。
既製のPNGに差し替える場合は `src/MesApp.Desktop/Assets/` の同名ファイルを上書きする
（必要なサイズはスクリプト末尾の一覧のとおり）。

実行ファイルのアイコンは `Assets/AppIcon.ico`（csproj の `ApplicationIcon`）。
