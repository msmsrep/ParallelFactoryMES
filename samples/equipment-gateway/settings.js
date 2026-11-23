// Node-RED の設定（サンプル）。compose.yaml の node-red が `--settings /gateway/settings.js` で読む。
// 書いていない項目は Node-RED の既定のまま（画面は http://localhost:1880、送信待ちなどのコンテキストはメモリ）。

// Webhook の受け側（flows.json の「Webhook の受け側」タブ）のために、本文を JSON に読み替える前のまま残す。
// MES の署名は送ったバイト列そのものに付くので、読み替えた後の JSON から作り直すと合わない
// （数値の書き方などが変わるため）。/mes-webhook への POST だけを対象にし、他の口は今までどおり。
function keepRawWebhookBody(req, res, next) {
    // 読み終えた本文はもう流れてこないので、2回目に呼ばれたら（下の2か所の両方に掛けている）何もしない
    if (req._body || req.method !== 'POST' || req.path !== '/mes-webhook') {
        return next();
    }
    const chunks = [];
    req.on('data', (chunk) => chunks.push(chunk));
    req.on('end', () => {
        // 読み終えた印を付けると、以降の JSON の読み替えは飛ばされ、HTTP 受信ノードの msg.payload に文字列のまま入る
        req.body = Buffer.concat(chunks).toString('utf8');
        req._body = true;
        next();
    });
    req.on('error', next);
}

module.exports = {
    // 画面（エディタ）は既定で / に置かれ、すべての要求の JSON を先に読み替えてしまう。
    // その手前で本文を残すため、画面の側の口にも掛ける（画面を別の場所に移したときは、下の HTTP 受信ノードの側で効く）
    httpAdminMiddleware: keepRawWebhookBody,
    httpNodeMiddleware: keepRawWebhookBody,

    // 送信待ちを Node-RED の再起動をまたいで残したいときは、次を有効にする（/data/context にファイルで残る）
    // contextStorage: { default: { module: 'localfilesystem' } },
};
