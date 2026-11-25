// ユーザーガイド（docs/）の画面写真を撮る。
//
//   node build/Capture-GuideScreens.js --password xxxx [--page walkthrough|intro] [--lang en|ja]
//                                     [--base http://localhost:5212] [--user admin] [--out <PNGの出力先>]
//
// --page walkthrough（既定）：「サンプルで見る 製造から出荷まで」（walkthrough.md）の8枚。幅1280・DPR2でページ全体
// --page intro：紹介ページ（intro.html）の13枚。幅1440・DPR1で、画面の決まった範囲を切り出す
//   稼働監視・Webhook の2枚のため、APIキーを発行して EQ-06 の設備イベントを送り、
//   このスクリプトの中に立てた受け側（127.0.0.1:--hook-port）を Webhook の送信先に登録する
//
// 前提：空のDBで起動したアプリ（--base）。初期管理者が無ければ --user / --password で作る。
// 1. samples/master-csv と samples/actual-csv の 01〜15 をZIPにして一括取込する（品目が既にあれば飛ばす）
// 2. walkthrough だけ、写真の状態に合わせる：SMP-SF-001・SMP-FG-001 の作業指示を完了承認、判定済みの検査を承認、
//    SMP-FG-002 を工程展開して -10 を op002・EQ-02 に差立。
//    intro は取り込んだままの状態を撮るので、同じDBで撮るなら intro を先にする（SMP-FG-002 が展開済みなら警告する）
// 3. ヘッドレス Edge を --lang の表示言語で開き、ログインして各画面を撮る
//
// 出力はPNG（既定 build/guide-shots/<page>/<lang>/）。ガイドへは build/Convert-GuideShots.py で webp にして
// docs/assets/[en/]<page>/ に置く（walkthrough は余白を落として1440幅に、intro は --keep-size でそのままの寸法）。
// 撮り方の注意は build/Capture-AppScreens.js と同じ（URL直開きはトークンが消えるので、リンクを押して遷移する）。
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn, execFileSync } = require('child_process');
const http = require('http');

const arg = (name, fallback) => {
    const i = process.argv.indexOf('--' + name);
    return i >= 0 ? process.argv[i + 1] : fallback;
};

const BASE = arg('base', 'http://localhost:5212');
const USER = arg('user', 'admin');
const PASSWORD = arg('password', null);
const LANG = arg('lang', 'en');
const PAGE = arg('page', 'walkthrough');
const EDGE = arg('edge', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const PORT = Number(arg('port', '9224'));
const HOOK_PORT = Number(arg('hook-port', '5299'));
const ROOT = path.join(__dirname, '..');
const OUT = arg('out', path.join(__dirname, 'guide-shots', PAGE, LANG));
const ACTUALS_UPTO = 15; // 在庫照会の写真は実績 01〜15 を取り込んだ時点の状態（ガイドの説明と合わせる）

// walkthrough はサイドバーごとページ全体を縮めて載せるのでDPR2、intro は切り出して等倍で載せるのでDPR1
const WIDTH = PAGE === 'intro' ? 1440 : 1280;
const HEIGHT = 820;
const DPR = PAGE === 'intro' ? 1 : 2;
const sleep = ms => new Promise(r => setTimeout(r, ms));

if (!['walkthrough', 'intro'].includes(PAGE)) {
    console.error('--page は walkthrough か intro です。');
    process.exit(1);
}
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
    // Windows の tar（bsdtar）は -a で拡張子から ZIP を作る。
    // Git Bash から動かすと PATH 先頭の GNU tar が選ばれ、C: をホスト名と解釈して失敗するので場所を指定する
    const tar = process.platform === 'win32'
        ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe') : 'tar';
    execFileSync(tar, ['-a', '-cf', dest, '-C', dir, ...files]);
}

async function prepare() {
    const status = await api('GET', '/api/setup/status');
    if (status.data.setupRequired) {
        await api('POST', '/api/setup/initialize', { userName: USER, password: PASSWORD, displayName: LANG === 'ja' ? 'システム管理者' : 'Administrator' });
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
    const fg2 = order('SMP-FG-002');

    if (PAGE === 'intro') {
        if (!['Approved', 1].includes(fg2.status)) {
            console.warn('注意: SMP-FG-002 が承認済みのままではありません（walkthrough の撮影で展開済み？）。'
                + '紹介ページの写真は取込直後の状態を前提にしています。');
        }
        await prepareIntegration();
    } else {
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
        await api('POST', `/api/manufacturing-orders/${fg2.id}/expand`, { lotNumber: null }, { allowFail: true });
        const wo10 = items(await api('GET', `/api/work-orders?manufacturingOrderId=${fg2.id}&pageSize=200`))
            .find(w => w.workOrderNo.endsWith('-10'));
        const op002 = (await api('GET', '/api/users')).data.find(u => u.userName === 'op002');
        const eq02 = (await api('GET', '/api/equipments')).data.find(e => e.assetNo === 'EQ-02');
        await api('PUT', `/api/work-orders/${wo10.id}/dispatch`,
            { assignedUserId: op002.id, assignedEquipmentId: eq02.id, dispatchOrder: 1 }, { allowFail: true });
        console.log('承認・工程展開・差立を済ませました。');
    }

    return {
        fg1: order('SMP-FG-001').id,
        fg1wo10: items(await api('GET', `/api/work-orders?manufacturingOrderId=${order('SMP-FG-001').id}&pageSize=200`))
            .find(w => w.workOrderNo.endsWith('-10')).id,
        // 現品票の写真に使う受入ロット
        r3001: items(await api('GET', '/api/inventory/stocks?pageSize=200'))
            .find(st => st.lotNumber === 'R3001-260901').lotId,
    };
}

// ---- 稼働監視・Webhook の写真の下ごしらえ（intro） ----
// 受け側はこのスクリプトの中に立て、届いた通知に 204 を返すだけにする（撮り終えたら閉じる）
let hookServer = null;
async function prepareIntegration() {
    hookServer = http.createServer((req, res) => { req.resume(); req.on('end', () => { res.writeHead(204); res.end(); }); });
    await new Promise(r => hookServer.listen(HOOK_PORT, '127.0.0.1', r));
    const subs = items(await api('GET', '/api/webhook-subscriptions'));
    if (subs.length === 0) {
        const created = await api('POST', '/api/webhook-subscriptions', {
            name: LANG === 'ja' ? '班長チャットへの通知' : 'Shift leader chat',
            url: `http://127.0.0.1:${HOOK_PORT}/mes-webhook`,
            eventTypes: ['equipment.stopped', 'nonconformance.created'], isActive: true,
        });
        await api('POST', `/api/webhook-subscriptions/${created.data.subscription.id}/test`);
    }
    // ゲートウェイの代わりに EQ-06（設備イベントで収集する設備）の状態の変化を送る。
    // 最後に反映した時刻より前は「遅着」で拒否されるので、時刻は今から遡った近い過去にする
    const admin = (await api('GET', '/api/users')).data.find(u => u.userName === USER);
    const key = await api('POST', '/api/api-keys', { name: LANG === 'ja' ? '設備ゲートウェイ' : 'Equipment gateway', userId: admin.id, expiresAt: null });
    const now = Date.now(), at = min => new Date(now - min * 60000).toISOString();
    const events = [
        { min: 58, state: 'Running' }, { min: 41, state: 'Stopped', alarmCode: 'A-101' }, { min: 36, state: 'Running' },
        { min: 17, state: 'Setup' }, { min: 11, state: 'Running' }, { min: 3, state: 'Stopped', alarmCode: 'A-102' },
    ].map((e, i) => ({ equipmentCode: 'EQ-06', eventId: `guide-${now}-${i}`, occurredAt: at(e.min), state: e.state, alarmCode: e.alarmCode }));
    const res = await fetch(BASE + '/api/integration/v1/equipment-events', {
        method: 'POST', headers: { 'X-Api-Key': key.data.plainKey, 'Content-Type': 'application/json' },
        body: JSON.stringify({ events }),
    });
    if (!res.ok) throw new Error('設備イベントを送れませんでした: ' + res.status + ' ' + (await res.text()).slice(0, 300));
    // 送信は常駐処理が数秒おきに拾う。テスト送信と停止2件の3件が送信済みになるまで待つ
    for (let i = 0; i < 40; i++) {
        const list = items(await api('GET', '/api/webhook-deliveries'));
        if (list.length >= 3 && list.every(d => ['Delivered', 1].includes(d.state))) break;
        await sleep(1000);
    }
    console.log('設備イベントを送り、Webhook の送信先を登録しました。');
}

// ---- CDP ----
let seq = 0;
function send(ws, method, params) {
    return new Promise((resolve, reject) => {
        const id = ++seq;
        // 応答が来ないまま固まるのを避ける（撮影は大きいページでも数秒で終わる）
        const timer = setTimeout(() => {
            ws.removeEventListener('message', onMessage);
            reject(new Error(method + ': 60秒待っても応答がありません'));
        }, 60000);
        const onMessage = ev => {
            const m = JSON.parse(ev.data);
            if (m.id !== id) return;
            clearTimeout(timer);
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
// 画面内のリンクを押して遷移する（無いリンクは差し込んでから押す）。
// 別タブで開くリンク（作業指示一覧の「指示書」など）は押しても今のタブが移らないので使わない
const go = (ws, href) => evalJs(ws, `(() => {
    let a = document.querySelector('a[href="${href}"]:not([target])');
    if (!a) { a = document.createElement('a'); a.href = '${href}'; document.body.appendChild(a); }
    a.click(); return true; })()`);

// 入力欄に値を入れて Blazor に拾わせる（@bind は change、oninput は input で拾う）
const fillJs = (selector, value) => `(() => {
    const el = document.querySelector(${JSON.stringify(selector)});
    const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(value)});
    for (const t of ['input', 'change']) el.dispatchEvent(new Event(t, { bubbles: true }));
    return true; })()`;

const resetMetrics = ws => send(ws, 'Emulation.setDeviceMetricsOverride',
    { width: WIDTH, height: HEIGHT, deviceScaleFactor: DPR, mobile: false });

async function shoot(ws, name) {
    // ページ全体の高さに合わせて撮る（アプリの画面に画像は無いので、全体を1枚で撮っても固まらない）
    const height = await evalJs(ws, `Math.max(document.documentElement.scrollHeight, document.body.scrollHeight,
        ...[...document.querySelectorAll('main, .content, article')].map(e => e.scrollHeight))`);
    await send(ws, 'Emulation.setDeviceMetricsOverride',
        { width: WIDTH, height: Math.max(HEIGHT, height), deviceScaleFactor: DPR, mobile: false });
    // 画面遷移で見出しにフォーカスが移り、枠が写るので外す
    await evalJs(ws, `(document.activeElement && document.activeElement.blur(), true)`);
    await sleep(800);
    await send(ws, 'Page.bringToFront');
    const result = await send(ws, 'Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(path.join(OUT, name + '.png'), Buffer.from(result.data, 'base64'));
    await resetMetrics(ws);
    console.log('  ' + name + '.png');
}

// 画面の一部を切り出して撮る（紹介ページ用）。
// viewport: true なら画面の左上から height まで（サイドバーごと）。
// sheet: true なら from の要素そのもの（帳票）。
// それ以外は本文（main.content）の左端〜右端を、from の要素の上端から pad だけ上を起点に height の高さで。
// until を渡すと高さはその要素の下端（＋pad）までにする（表示言語で行の高さが変わる一覧用）
async function shootClip(ws, name, { from, pad = 0, height = HEIGHT, until, sheet = false, viewport = false }) {
    const pageHeight = await evalJs(ws, `document.documentElement.scrollHeight`);
    await send(ws, 'Emulation.setDeviceMetricsOverride',
        { width: WIDTH, height: Math.max(HEIGHT, pageHeight), deviceScaleFactor: DPR, mobile: false });
    // 画面遷移で見出しにフォーカスが移り、枠が写るので外す
    await evalJs(ws, `(document.activeElement && document.activeElement.blur(), true)`);
    await sleep(800);
    const rect = await evalJs(ws, `(() => {
        if (${!!until}) {
            const el = ${from || 'null'}, end = ${until || 'null'};
            const left = document.querySelector('main.content').getBoundingClientRect().left;
            const y = el.getBoundingClientRect().top + scrollY - ${pad};
            return { x: left, y, width: ${WIDTH} - left, height: end.getBoundingClientRect().bottom + scrollY + ${pad} - y };
        }
        if (${viewport}) return { x: 0, y: 0, width: ${WIDTH}, height: ${height} };
        const el = ${from || 'null'};
        if (!el) return null;
        const r = el.getBoundingClientRect();
        if (${sheet}) return { x: r.left, y: r.top + scrollY, width: r.width, height: r.height };
        // 本文は max-width で右に余白が残るので、幅は本文の左端から画面の右端まで
        const left = document.querySelector('main.content').getBoundingClientRect().left;
        return { x: left, y: r.top + scrollY - ${pad}, width: ${WIDTH} - left, height: ${height} };
    })()`);
    if (!rect) throw new Error('切り出しの起点が見つかりません: ' + name);
    await send(ws, 'Page.bringToFront');
    const result = await send(ws, 'Page.captureScreenshot',
        { format: 'png', clip: { ...rect, scale: 1 } });
    fs.writeFileSync(path.join(OUT, name + '.png'), Buffer.from(result.data, 'base64'));
    await resetMetrics(ws);
    console.log(`  ${name}.png  ${Math.round(rect.width)}x${Math.round(rect.height)}`);
}

(async () => {
    const ids = await prepare();
    fs.mkdirSync(OUT, { recursive: true });
    const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'mes-guide-shots-'));
    const edge = spawn(EDGE, ['--headless=new', `--remote-debugging-port=${PORT}`,
        `--user-data-dir=${profile}`, '--no-first-run', '--disable-extensions', '--disable-sync', '--hide-scrollbars',
        `--window-size=${WIDTH},${HEIGHT}`, 'about:blank'], { stdio: 'ignore' });
    try {
        let targets;
        for (let i = 0; i < 40 && !targets; i++) {
            try { targets = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json(); }
            catch { await sleep(500); }
        }
        if (!targets) throw new Error('Edge のデバッグポートに接続できませんでした。');
        // Edge が自分のダイアログ（同期の案内など）をページとして開くことがあるので、起動時の about:blank を選ぶ
        const target = targets.find(t => t.type === 'page' && t.url === 'about:blank')
            || targets.find(t => t.type === 'page');
        const ws = new WebSocket(target.webSocketDebuggerUrl);
        await new Promise(r => ws.addEventListener('open', r));
        await send(ws, 'Page.enable');
        // 拡張機能の画面や同期の案内が別タブで開くと撮るタブが後ろに回り、描画が止まって撮影が返らなくなる。
        // 撮る前に前面へ出し、フォーカスも保つ
        await send(ws, 'Emulation.setFocusEmulationEnabled', { enabled: true });
        await send(ws, 'Runtime.enable');
        await resetMetrics(ws);

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

        const traceFg = async () => {
            await evalJs(ws, fillJs('.scan-input input', 'FG1000-S001'));
            await sleep(300);
            await evalJs(ws, `([...document.querySelectorAll('.scan-input button')].pop().click(), true)`);
            await sleep(3000);
        };

        if (PAGE === 'walkthrough') {
            const shots = [
                { name: 'inventory', href: 'inventory' },
                { name: 'order-detail', href: `manufacturing-orders/${ids.fg1}` },
                { name: 'dispatch', href: 'dispatch' },
                { name: 'record', href: `work-orders/${ids.fg1wo10}/record` },
                { name: 'inspections', href: 'inspections' },
                { name: 'shipment-judgments', href: 'shipment-judgments' },
                { name: 'shipping', href: 'shipping' },
                { name: 'trace', href: 'traceability', after: traceFg },
            ];
            for (const shot of shots) {
                await go(ws, shot.href);
                await sleep(3500);
                if (shot.after) await shot.after();
                await shoot(ws, shot.name);
            }
        } else {
            // 寸法は紹介ページ（docs/intro.html）の <img width height> に合わせる
            const h1 = `document.querySelector('main.content h1')`;
            const card = i => `document.querySelectorAll('main.content .card')[${i}]`;
            const shots = [
                {
                    // サンプルの作業時刻は 2026-09-01〜03 に固定なので、ダッシュボードは2026年9月の月次で見せる
                    name: 'dashboard', href: '', clip: { viewport: true, height: 660 }, after: async () => {
                        await evalJs(ws, fillJs('main.content select', 'Month'));
                        await sleep(1500);
                        await evalJs(ws, fillJs('main.content input[type=date]', '2026-09-03'));
                        await sleep(2500);
                    },
                },
                { name: 'orders', href: 'manufacturing-orders', clip: { from: h1, pad: 20, height: 660 } },
                { name: 'workorders', href: 'work-orders', clip: { from: h1, pad: 20, until: card(0) } },
                { name: 'print-wo', href: `print/work-order/${ids.fg1wo10}`, clip: { from: `document.querySelector('.print-sheet')`, sheet: true } },
                { name: 'print-lot', href: `print/lot/${ids.r3001}`, clip: { from: `document.querySelector('.label-sheet')`, sheet: true } },
                { name: 'trace', href: 'traceability', after: traceFg, clip: { from: h1, pad: 20, height: 760 } },
                { name: 'inventory', href: 'inventory', clip: { from: h1, pad: 20, height: 760 } },
                { name: 'productivity', href: 'productivity', clip: { from: card(0), pad: 14, height: 750 } },
                { name: 'quality', href: 'quality-analysis', clip: { from: card(1), pad: 16, height: 580 } },
                { name: 'audit', href: 'audit-logs', clip: { from: h1, pad: 20, height: 860 } },
                { name: 'masters', href: 'masters', clip: { from: h1, pad: 20, height: 580 } },
                // 保全管理は稼働監視タブが既定。「設備の今の状態」のカードまで
                { name: 'equipment', href: 'maintenance', clip: { from: h1, pad: 20, until: card(0) } },
                {
                    name: 'webhooks', href: 'webhooks', clip: { from: h1, pad: 20, until: card(1) }, after: async () => {
                        await evalJs(ws, `(document.querySelectorAll('main.content .tabs button')[1].click(), true)`);
                        await sleep(2500);
                    },
                },
            ];
            for (const shot of shots) {
                await go(ws, shot.href);
                await sleep(3500);
                if (shot.after) await shot.after();
                await shootClip(ws, shot.name, shot.clip);
            }
        }
        ws.close();
        console.log('撮影しました: ' + OUT);
    } finally {
        // Edge は子プロセスを作るので、親だけ止めるとデバッグポートを掴んだまま残り、次の実行がそこへつながる
        if (process.platform === 'win32') {
            try { execFileSync('taskkill', ['/PID', String(edge.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* 止まっている */ }
        } else {
            edge.kill();
        }
        if (hookServer) hookServer.close();
        await sleep(1500);
        try { fs.rmSync(profile, { recursive: true, force: true }); } catch { /* 一時プロファイルの残骸 */ }
    }
})().catch(e => { console.error('失敗しました: ' + e.message); process.exit(1); });
