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

3つの値がどれか1つでも `PLACEHOLDER` のままだと `Pack-Msix.ps1` は停止する。
リポジトリ外で管理したい場合は `-IdentityFile <パス>` で別ファイルを指定する。

`Version` は **`x.y.z.0`（第4桁は必ず 0）**。ストアに提出するたびに上げる。

---

## 3. ローカルで動作確認する

```powershell
./build/Pack-Msix.ps1 -SelfSign
```

自己署名証明書を作成して署名する。インストールには証明書を信頼させる必要がある
（スクリプトが実行すべきコマンドを表示する。**管理者権限の PowerShell** で実行する）。

```powershell
Export-Certificate -Cert Cert:\CurrentUser\My\<拇印> -FilePath $env:TEMP\mes-test.cer
Import-Certificate -FilePath $env:TEMP\mes-test.cer -CertStoreLocation Cert:\LocalMachine\Root
Add-AppxPackage 'artifacts\msix\ParallelFactoryMES_1.0.0.0_x64.msix'
```

パッケージ化せずに素早く確認したいときは、ステージングされた実行ファイルを直接起動してもよい。

```powershell
./artifacts/msix/stage-x64/ParallelFactoryMES.exe
```

### 確認する項目

- ウィンドウが開き、ログイン画面が表示される
- 初期管理者（`admin` / `Mes-admin1`）でログインでき、パスワード変更を求められる
- マスタ登録・指図・実績入力が一通り動く
- データが `%LOCALAPPDATA%\Packages\<パッケージファミリー名>\LocalCache\Local\ParallelFactoryMES\` に作られる
  （パッケージ外実行時は `%LOCALAPPDATA%\ParallelFactoryMES\`）
- アプリを閉じて再起動しても入力したデータが残っている

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
| ログインできず機能を確認できない | 認定メモに初期管理者の資格情報を書く（上記） |
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
