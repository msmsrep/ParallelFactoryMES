"""Parallel Factory MES の Webhook を受ける最小の見本（Python 3.9 以降。標準ライブラリだけで動く）。

署名（X-Mes-Signature）を確かめ、時刻のずれと重複を弾いて、通知の要約を表示する。
自分の処理（在庫の取り込み、表示盤への出力など）は handle() に書く。

    python receive_mes_webhook.py --secret whsec_... --port 9000

MES の送信先には http://<この PC>:9000/mes-webhook を登録する（Docker の MES からなら http://host.docker.internal:9000/mes-webhook）。
秘密の値は環境変数 MES_WEBHOOK_SECRET でも渡せる。
"""

import argparse
import collections
import hashlib
import hmac
import json
import os
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

TOLERANCE_SECONDS = 300  # これより古い・先の時刻の要求は、送り直し（再生攻撃）とみなして弾く
SEEN_LIMIT = 1000  # 重複を見分けるために覚えておく通知の数


def verify(secret: str, timestamp: str, signature: str, body: bytes) -> bool:
    """署名は「X-Mes-Timestamp.本文」の HMAC-SHA256 を小文字の16進にし、sha256= を付けたもの。
    本文は受け取ったバイト列そのものを使う（JSON に読み替えてから作り直すと、数値の書き方などが変わって合わない）。"""
    if not timestamp.isdigit() or abs(time.time() - int(timestamp)) > TOLERANCE_SECONDS:
        return False
    expected = "sha256=" + hmac.new(
        secret.encode("utf-8"), timestamp.encode("ascii") + b"." + body, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature)


def handle(event: dict) -> None:
    """届いた通知を処理する。ここを自分の処理に書き換える。
    通知は「読みに行くきっかけ」。正しい今の値は連携API（生産実績のフィードなど）で取りに行く。"""
    print(f"[{event['type']}] {event['summary']}  (sequence {event['sequence']})", flush=True)


class Handler(BaseHTTPRequestHandler):
    secret = ""
    seen: "collections.OrderedDict[str, None]" = collections.OrderedDict()

    def do_POST(self) -> None:
        if self.path != "/mes-webhook":
            self.send_response(404)
            self.end_headers()
            return
        body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        if not verify(self.secret, self.headers.get("X-Mes-Timestamp", ""),
                      self.headers.get("X-Mes-Signature", ""), body):
            print("署名か時刻が合わないので受け付けませんでした", flush=True)
            self.send_response(401)
            self.end_headers()
            return

        event = json.loads(body)
        # 先に 2xx を返す（MES は応答を待っている。重い処理は応答の後に回すか、別の仕組みにためる）
        self.send_response(200)
        self.end_headers()

        # 少なくとも1回届くので、同じ id は2回目以降を捨てる
        if event["id"] in self.seen:
            print(f"重複を捨てました：{event['id']}", flush=True)
            return
        self.seen[event["id"]] = None
        while len(self.seen) > SEEN_LIMIT:
            self.seen.popitem(last=False)
        handle(event)

    def log_message(self, format: str, *args) -> None:  # 要求ごとの既定のログは出さない
        pass


def main() -> None:
    parser = argparse.ArgumentParser(description="Parallel Factory MES の Webhook を受ける見本")
    parser.add_argument("--secret", default=os.environ.get("MES_WEBHOOK_SECRET", ""),
                        help="送信先を登録したときの秘密の値（whsec_...）")
    parser.add_argument("--port", type=int, default=9000)
    args = parser.parse_args()
    if not args.secret:
        parser.error("--secret か環境変数 MES_WEBHOOK_SECRET に秘密の値を指定してください")
    Handler.secret = args.secret
    print(f"http://0.0.0.0:{args.port}/mes-webhook で待っています（Ctrl+C で止める）", flush=True)
    ThreadingHTTPServer(("0.0.0.0", args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
