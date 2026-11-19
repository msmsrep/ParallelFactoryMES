// ユーザーガイド「サンプルで見る 製造から出荷まで」（docs/walkthrough.md）の画面写真8枚を撮る。
//
//   node build/Capture-WalkthroughScreens.js --password xxxx [--lang en] [--base http://localhost:5212]
//                                            [--user admin] [--out <PNGの出力先>]
//
// 前提：空のDBで起動したアプリ（--base）。初期管理者が無ければ --user / --password で作る。
// 1. samples/master-csv と samples/actual-csv の 01〜15 をZIPにして一括取込する（品目が既にあれば飛ばす）
// 2. 写真の状態に合わせる：SMP-SF-001・SMP-FG-001 の作業指示を完了承認、判定済みの検査を承認、
//    SMP-FG-002 を工程展開して -10 を op002・EQ-02 に差立
// 3. ヘッドレス Edge を --lang の表示言語で開き、ログインして各画面を撮る（幅1280・DPR2、ページ全体）
//
// 出力はPNG（既定 build/guide-shots/<lang>/）。ガイドへは build/Convert-GuideShots.py で
// 本文の下の余白を落とし、1440幅の webp にして docs/assets/<lang>/walkthrough/ に置く。
// 撮り方の注意は build/Capture-AppScreens.js と同じ（URL直開きはトークンが消えるので、リンクを押して遷移する）。
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn, execFileSync } = require('child_process');

const arg = (name, fallback) => {
    const i = process.argv.indexOf('--' + name);
    return i >= 0 ? process.argv[i + 1] : fallback;
};

const BASE = arg('base', 'http://localhost:5212');
const USER = arg('user', 'admin');
const PASSWORD = arg('password', null);
const LANG = arg('lang', 'en');
const EDGE = arg('edge', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const PORT = Number(arg('port', '9224'));
const ROOT = path.join(__dirname, '..');
const OUT = arg('out', path.join(__dirname, 'guide-shots', LANG));
const ACTUALS_UPTO = 15; // 在庫照会の写真は実績 01〜15 を取り込んだ時点の状態（ガイドの説明と合わせる）

const WIDTH = 1280;
const HEIGHT = 820;
const sleep = ms => new Promise(r => setTimeout(r, ms));

if (!PASSWORD) {
    console.error('--password を指定してください（初期管理者のパスワード。無ければこの値で作ります）。');
    process.exit(1);
}

// ---- API ----
let token = null;
async function api(method, url, body, { form, allowFail } = {}) {
    const headers = token ? { Authorization: 'Bearer ' + token } : {};
    let payload;
    if (form) {
        payload = form;
    } else if (body !== undefined) {
        headers['Content-Type'] = 'application/json';
        payload = JSON.stringify(body);
    }
    const res = await fetch(BASE + url, { method, headers, body: payload });
    const text = await res.text();
    if (!res.ok && !allowFail) throw new Error(`${method} ${url}: ${res.status} ${text.slice(0, 300)}`);
    try { return { ok: res.ok, data: text ? JSON.parse(text) : null }; } catch { return { ok: res.ok, data: text }; }
}
const items = r => (r.data && r.data.items) || r.data || [];

function zip(dir, files, dest) {
    // Windows の tar（bsdtar）は -a で拡張子から ZIP を作る
    execFileSync('tar', ['-a', '-cf', dest, '-C', dir, ...files]);
}

async function prepare() {
    const status = await api('GET', '/api/setup/status');
    if (status.data.setupRequired) {
        await api('POST', '/api/setup/initialize', { userName: USER, password: PASSWORD, displayName: 'Administrator' });
        console.log('初期管理者を作りました: ' + USER);
    }
    const login = await api('POST', '/api/auth/login', { userName: USER, password: PASSWORD });
    token = login.data.accessToken;

    const products = await api('GET', '/api/products');
    if (items(products).length === 0) {
        const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'mes-guide-zip-'));
        const masterDir = path.join(ROOT, 'samples', 'master-csv');
        const actualDir = path.join(ROOT, 'samples', 'actual-csv');
        const masters = fs.readdirSync(masterDir).filter(f => f.endsWith('.csv'));
        const actuals = fs.readdirSync(actualDir)
            .filter(f => f.endsWith('.csv') && Number(f.slice(0, 2)) <= ACTUALS_UPTO);
        zip(masterDir, masters, path.join(tmp, 'masters.zip'));
        zip(actualDir, actuals, path.join(tmp, 'actuals.zip'));
        for (const [url, file] of [['/api/masters/csv/bundle', 'masters.zip'], ['/api/actuals/csv/bundle', 'actuals.zip']]) {
            const form = new FormData();
            form.append('file', new Blob([fs.readFileSync(path.join(tmp, file))]), file);
            await api('POST', url, undefined, { form });
            console.log('取り込みました: ' + file);
        }
        fs.rmSync(tmp, { recursive: true, force: true });
    } else {
        console.log('品目が登録済みなので、サンプルの取込は飛ばします。');
    }

    const orders = items(await api('GET', '/api/manufacturing-orders?pageSize=200'));
    const order = no => orders.find(o => o.orderNo === no);

    // 完了承認（完了していない作業指示は失敗するので無視する）
    for (const no of ['SMP-SF-001', 'SMP-FG-001']) {
        const wos = items(await api('GET', `/api/work-orders?manufacturingOrderId=${order(no).id}&pageSize=200`));
        for (const wo of wos) await api('POST', `/api/work-orders/${wo.id}/approve`, undefined, { allowFail: true });
    }
    // 検査承認（判定前・承認済みは失敗するので無視する）
    for (const io of items(await api('GET', '/api/inspection-orders?pageSize=200'))) {
        await api('POST', `/api/inspection-orders/${io.id}/approve`, undefined, { allowFail: true });
    }
    // SMP-FG-002 を工程展開し、-10 を op002・EQ-02 に差立
    const fg2 = order('SMP-FG-002');
    await api('POST', `/api/manufacturing-orders/${fg2.id}/expand`, { lotNumber: null }, { allowFail: true });
    const wo10 = items(await api('GET', `/api/work-orders?manufacturingOrderId=${fg2.id}&pageSize=200`))
        .find(w => w.workOrderNo.endsWith('-10'));
    const op002 = (await api('GET', '/api/users')).data.find(u => u.userName === 'op002');
    const eq02 = (await api('GET', '/api/equipments')).data.find(e => e.assetNo === 'EQ-02');
    await api('PUT', `/api/work-orders/${wo10.id}/dispatch`,
        { assignedUserId: op002.id, assignedEquipmentId: eq02.id, dispatchOrder: 1 }, { allowFail: true });
    console.log('承認・工程展開・差立を済ませました。');

    return {
        fg1: order('SMP-FG-001').id,
        fg1wo10: items(await api('GET', `/api/work-orders?manufacturingOrderId=${order('SMP-FG-001').id}&pageSize=200`))
            .find(w => w.workOrderNo.endsWith('-10')).id,
    };
}

// ---- CDP ----
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
// 画面内のリンクを押して遷移する（無いリンクは差し込んでから押す）
const go = (ws, href) => evalJs(ws, `(() => {
    let a = document.querySelector('a[href="${href}"]');
    if (!a) { a = document.createElement('a'); a.href = '${href}'; document.body.appendChild(a); }
    a.click(); return true; })()`);

async function shoot(ws, name) {
    // ページ全体の高さに合わせて撮る（アプリの画面に画像は無いので、全体を1枚で撮っても固まらない）
    const height = await evalJs(ws, `Math.max(document.documentElement.scrollHeight, document.body.scrollHeight,
        ...[...document.querySelectorAll('main, .content, article')].map(e => e.scrollHeight))`);
    await send(ws, 'Emulation.setDeviceMetricsOverride',
        { width: WIDTH, height: Math.max(HEIGHT, height), deviceScaleFactor: 2, mobile: false });
    await sleep(800);
    const result = await send(ws, 'Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(path.join(OUT, name + '.png'), Buffer.from(result.data, 'base64'));
    await send(ws, 'Emulation.setDeviceMetricsOverride',
        { width: WIDTH, height: HEIGHT, deviceScaleFactor: 2, mobile: false });
    console.log('  ' + name + '.png');
}

(async () => {
    const ids = await prepare();
    fs.mkdirSync(OUT, { recursive: true });
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'mes-guide-shots-'));
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

        // 表示言語はブラウザの localStorage に持つ（Shared/AppCulture.cs・wwwroot/js/app.js）
        await send(ws, 'Page.navigate', { url: BASE + '/' });
        await waitFor(ws, `document.readyState === 'complete'`, '読み込み');
        await evalJs(ws, `(localStorage.setItem('mesapp.culture', ${JSON.stringify(LANG)}), true)`);
        await send(ws, 'Page.reload');
        await waitFor(ws, `document.querySelectorAll('input').length >= 2`, 'ログイン画面');
        await sleep(1500);

        // ログイン欄は oninput で値を拾うので、値を入れた後に input と change を投げる。
        // ボタンは表示言語で文言が変わるので、ログイン画面に1つだけある .btn を押す
        await evalJs(ws, `(() => {
            const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            const inputs = document.querySelectorAll('input');
            const fill = (el, v) => { set.call(el, v); for (const t of ['input', 'change']) el.dispatchEvent(new Event(t, { bubbles: true })); };
            fill(inputs[0], ${JSON.stringify(USER)});
            fill(inputs[1], ${JSON.stringify(PASSWORD)});
            return true; })()`);
        await sleep(300);
        await evalJs(ws, `(document.querySelector('button.btn').click(), true)`);
        await waitFor(ws, `!!document.querySelector('a[href="manufacturing-orders"]')`, 'ログイン後');
        await sleep(2000);

        const shots = [
            { name: 'inventory', href: 'inventory' },
            { name: 'order-detail', href: `manufacturing-orders/${ids.fg1}` },
            { name: 'dispatch', href: 'dispatch' },
            { name: 'record', href: `work-orders/${ids.fg1wo10}/record` },
            { name: 'inspections', href: 'inspections' },
            { name: 'shipment-judgments', href: 'shipment-judgments' },
            { name: 'shipping', href: 'shipping' },
            {
                name: 'trace', href: 'traceability', after: async () => {
                    await evalJs(ws, `(() => {
                        const set = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                        const input = document.querySelector('.scan-input input');
                        set.call(input, 'FG1000-S001'); input.dispatchEvent(new Event('input', { bubbles: true }));
                        return true; })()`);
                    await sleep(300);
                    await evalJs(ws, `([...document.querySelectorAll('.scan-input button')].pop().click(), true)`);
                    await sleep(3000);
                },
            },
        ];
        for (const shot of shots) {
            await go(ws, shot.href);
            await sleep(3500);
            if (shot.after) await shot.after();
            await shoot(ws, shot.name);
        }
        ws.close();
        console.log('撮影しました: ' + OUT);
    } finally {
        edge.kill();
        await sleep(1500);
        try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* 一時プロファイルの残骸 */ }
    }
})().catch(e => { console.error('失敗しました: ' + e.message); process.exit(1); });
