// ストア掲載用スクリーンショットの素材として、アプリの実画面をPNGに撮る。
//
//   node build/Capture-AppScreens.js [--base http://localhost:5212]
//                                    [--user admin] [--password xxxx]
//
// 撮影先は build/store/raw/。組み立て（見出し・背景）は build/New-StoreScreenshots.py が行う。
// Edge をヘッドレスで起動し、CDP（DevTools プロトコル）を直接叩く。追加のパッケージは要らない。
// デモ用のデータは samples/master-csv・samples/actual-csv を一括取込した空DBを前提にする。
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const arg = (name, fallback) => {
    const i = process.argv.indexOf('--' + name);
    return i >= 0 ? process.argv[i + 1] : fallback;
};

const BASE = arg('base', 'http://localhost:5212');
const USER = arg('user', 'admin');
const PASSWORD = arg('password', 'Shots#2026pw');
const EDGE = arg('edge', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const PORT = Number(arg('port', '9223'));
const OUT = path.join(__dirname, 'store', 'raw');

// 撮る画面（href はナビゲーションのリンク。null はログイン直後のダッシュボード）
const SHOTS = [
    { name: 'dashboard', href: null },
    { name: 'orders', href: 'manufacturing-orders' },
    { name: 'work-orders', href: 'work-orders' },
    { name: 'inventory', href: 'inventory' },
    { name: 'inspections', href: 'inspections' },
];

// 実画面はDPR2で撮る（縮小して使うので、等倍だと文字がにじむ）
const WIDTH = 1280;
const HEIGHT = 820;

const sleep = ms => new Promise(r => setTimeout(r, ms));

let seq = 0;
function send(ws, method, params) {
    return new Promise((resolve, reject) => {
        const id = ++seq;
        const onMessage = ev => {
            const m = JSON.parse(ev.data);
            if (m.id !== id) return;
            ws.removeEventListener('message', onMessage);
            m.error ? reject(new Error(method + ': ' + JSON.stringify(m.error))) : resolve(m.result);
        };
        ws.addEventListener('message', onMessage);
        ws.send(JSON.stringify({ id, method, params: params || {} }));
    });
}

const evalJs = (ws, expression) =>
    send(ws, 'Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true })
        .then(r => r.result && r.result.value);

async function waitFor(ws, expression, label, timeout = 60000) {
    const start = Date.now();
    for (;;) {
        if (await evalJs(ws, expression)) return;
        if (Date.now() - start > timeout) throw new Error('待っても現れませんでした: ' + label);
        await sleep(500);
    }
}

(async () => {
    fs.mkdirSync(OUT, { recursive: true });
    const profile = fs.mkdtempSync(path.join(require('os').tmpdir(), 'mes-shots-'));
    const edge = spawn(EDGE, ['--headless=new', `--remote-debugging-port=${PORT}`,
        `--user-data-dir=${profile}`, '--no-first-run', '--hide-scrollbars',
        `--window-size=${WIDTH},${HEIGHT}`, 'about:blank'], { stdio: 'ignore' });

    try {
        let targets;
        for (let i = 0; i < 40 && !targets; i++) {
            try { targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json(); }
            catch { await sleep(500); }
        }
        if (!targets) throw new Error('Edge のデバッグポートに接続できませんでした。');

        const ws = new WebSocket(targets.find(t => t.type === 'page').webSocketDebuggerUrl);
        await new Promise(r => ws.addEventListener('open', r));
        await send(ws, 'Page.enable');
        await send(ws, 'Runtime.enable');
        await send(ws, 'Emulation.setDeviceMetricsOverride',
            { width: WIDTH, height: HEIGHT, deviceScaleFactor: 2, mobile: false });

        await send(ws, 'Page.navigate', { url: BASE + '/' });
        await waitFor(ws, `!!document.querySelector('input')`, 'ログイン画面');
        await sleep(1500);

        // ログイン欄は @bind:event="oninput" なので、値を入れた後に input と change を投げる
        await evalJs(ws, `(() => {
            const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            const inputs = document.querySelectorAll('input');
            const fill = (el, v) => {
                set.call(el, v);
                for (const type of ['input', 'change']) el.dispatchEvent(new Event(type, { bubbles: true }));
            };
            fill(inputs[0], ${JSON.stringify(USER)});
            fill(inputs[1], ${JSON.stringify(PASSWORD)});
            return true;
        })()`);
        await sleep(300);
        await evalJs(ws,
            `([...document.querySelectorAll('button')].find(b => b.textContent.includes('ログイン')).click(), true)`);
        await waitFor(ws, `!!document.querySelector('a[href="manufacturing-orders"]')`,
            'ログイン後のダッシュボード');
        await sleep(2000);

        for (const shot of SHOTS) {
            if (shot.href) {
                // トークンは画面の中だけに持つので、URLを直接開かずリンクを押して遷移する
                await evalJs(ws, `(document.querySelector('a[href="${shot.href}"]').click(), true)`);
                await sleep(3000);
            }
            const result = await send(ws, 'Page.captureScreenshot', { format: 'png' });
            fs.writeFileSync(path.join(OUT, shot.name + '.png'), Buffer.from(result.data, 'base64'));
            console.log('  ' + shot.name + '.png');
        }
        ws.close();
        console.log('撮影しました: ' + OUT);
    } finally {
        edge.kill();
        // プロセスが終わりきる前に消すと EPERM になる。消せなくても撮影結果には影響しない
        await sleep(1500);
        try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* 一時プロファイルの残骸 */ }
    }
})().catch(e => { console.error('失敗しました: ' + e.message); process.exit(1); });
