# 設備イベントのゲートウェイ（サンプル）

設備の状態変化を MQTT で受け、Parallel Factory MES の連携API（`POST /api/integration/v1/equipment-events`）へまとめて送る Node-RED のフローです。
PLC との通信は MES に持たせず、このゲートウェイ側で行う、という構成の見本です。

| ファイル | 内容 |
|---|---|
| `flows.json` | Node-RED のフロー。①模擬設備（数秒ごとに状態を変えて MQTT に出す）②MQTT を受けて送信待ちにためる ③5秒ごとにまとめて送る（送れなかった分は次の回に送り直す） |
| `mosquitto.conf` | MQTT ブローカー（Eclipse Mosquitto）の最小構成。**認証も暗号化もしない**ので試すときだけ使う |

## 動かし方

リポジトリの直下で、MES と一緒に起動します（`compose.yaml` の `equipment` プロファイル）。

1. `docker compose --profile equipment up -d` で起動する
2. MES（http://localhost:8080）で、サンプルのマスタ（`samples/master-csv`）を取り込む。`EQ-06` が「設備イベントで収集する」設備になる
3. 画面の「APIキー」でゲートウェイ用のキーを発行し、`.env` に `MES_API_KEY=pfm_...` と書いて `docker compose --profile equipment up -d` をもう一度実行する（Node-RED が新しいキーで起動し直す）
4. 保全管理の稼働監視タブで、`EQ-06` の今の状態と稼働履歴が増えていくのを確かめる。Node-RED（http://localhost:1880）の「結果を見る」ノードに、採用・重複・拒否の件数が出る

詳しくは利用者ガイドの運用ガイド「設備イベントの受信」を見てください。

## 自分の設備に合わせるとき

- 「状態を変える」（模擬設備）を、PLC から状態を読むノード（OPC UA・MC プロトコル・Modbus など）に置き換え、
  `mes/equipment/<資産番号>/state` へ `{"state":"Running","alarmCode":null,"occurredAt":"2026-11-23T08:00:00+09:00"}` の形で出します。
  `state` は `Running` / `Stopped` / `Setup` / `Failure` / `Idle` / `Off` のどれかです
- 送信待ちは Node-RED のメモリ（フローのコンテキスト）に置いています。Node-RED を再起動すると、送れていなかった分は消えます。
  消えては困るときは、Node-RED の設定（`settings.js` の `contextStorage`）でファイルに保存するようにします
- 設備とゲートウェイの時計は NTP で揃えてください。MES は、最後に反映した時刻より前のイベントを遅着として拒否します
