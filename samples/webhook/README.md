# Webhook の受け側（サンプル）

Parallel Factory MES が送る Webhook（業務の出来事の通知）を受ける最小の見本です。
どれも、**署名を確かめる → 時刻のずれを弾く → 2xx を返す → 同じ `id` の2回目以降を捨てる** の順で受け、通知の要約を表示します。

| ファイル | 内容 |
|---|---|
| `receive_mes_webhook.py` | Python 3.9 以降（標準ライブラリだけ）。`0.0.0.0` で待つので、別の PC や Docker の MES からも受けられる |
| `Receive-MesWebhook.ps1` | PowerShell 7。MES と同じ PC で動かす（`http://localhost` だけで待つので管理者の権限は要らない） |

チャット（Slack・Teams）へ転送する見本は、Node-RED のフロー（`samples/equipment-gateway/flows.json` の「Webhook の受け側」タブ）にあります。

## 動かし方

1. MES の「管理」→「Webhook」で送信先を登録し、表示された秘密の値（`whsec_...`）を写す。URL は次のとおり
   - Python：`http://<この PC>:9000/mes-webhook`（Docker の MES からなら `http://host.docker.internal:9000/mes-webhook`）
   - PowerShell：`http://localhost:9000/mes-webhook`
2. 受け側を起動する

   ```powershell
   python receive_mes_webhook.py --secret whsec_...
   ./Receive-MesWebhook.ps1 -Secret whsec_...
   ```

3. 送信先の一覧の **テスト送信** を押す。受け側に `[webhook.ping] Webhook のテスト送信です（…）。` と出れば届いている

## 自分の処理に書き換えるとき

- 処理は `handle()`（Python）・`Invoke-MesEvent`（PowerShell）に書きます。**重い処理は 2xx を返した後に**回してください。MES は応答を既定10秒まで待ち、待ち切れなければ送り直します
- 署名は、**受け取ったバイト列そのもの**で確かめます。JSON に読み替えてから文字列に戻すと、数値の書き方などが変わって合いません
- 通知は「読みに行くきっかけ」です。届かなかった・順が入れ替わったときに備え、正しい今の値は連携API（生産実績のフィードなど）で取りに行ってください
- 覚えておく `id` はメモリにあるので、受け側を再起動すると忘れます。二重に処理すると困るときは、`id` をファイルや DB に残してください

詳しくは利用者ガイドの運用ガイド「Webhook」を見てください。
