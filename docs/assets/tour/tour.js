// 工場模型の等角描画とカメラ（docs-dev/FactoryTour.md TOUR-01）
// 配置は scenes.js の TOUR_DATA に持ち、ここには描き方だけを書く
(function () {
  'use strict';

  var data = window.TOUR_DATA;
  var svg = document.getElementById('tour-svg');
  var NS = 'http://www.w3.org/2000/svg';

  var UNIT = 24;                     // 床の1単位の長さ（SVG の座標）
  var COS30 = Math.cos(Math.PI / 6);
  var NARROW = '(max-width: 999.98px)';
  var LABEL_PAD = 34;                // カメラの余白。持ち場の上に出るラベルの分
  var MIN_VIEW_W = 300;              // 寄りすぎて持ち場の外が分からなくなるのを防ぐ
  var MIN_LABEL_SCALE = 0.5;         // これより小さい全景ではラベルが重なる

  // 等角投影：床の (x, y) と高さ z を SVG の座標へ
  function project(x, y, z) {
    return [(x - y) * COS30 * UNIT, (x + y) * 0.5 * UNIT - (z || 0) * UNIT];
  }

  function el(name, attrs, parent) {
    var node = document.createElementNS(NS, name);
    for (var key in attrs) node.setAttribute(key, attrs[key]);
    if (parent) parent.appendChild(node);
    return node;
  }

  function points(list) {
    return list.map(function (p) { return p[0].toFixed(1) + ',' + p[1].toFixed(1); }).join(' ');
  }

  // 床の上の四角形
  function floorRect(parent, x, y, w, d, cls, z) {
    return el('polygon', {
      points: points([project(x, y, z), project(x + w, y, z), project(x + w, y + d, z), project(x, y + d, z)]),
      class: cls
    }, parent);
  }

  // 箱：見える3面（上・左手前・右手前）を描く。tone は CSS の面の濃さ
  function box(parent, x, y, z, w, d, h, tone) {
    var top = z + h;
    var g = el('g', { class: 'box ' + (tone || 'tone-body') }, parent);
    el('polygon', { class: 'f-left', points: points([project(x, y + d, z), project(x + w, y + d, z), project(x + w, y + d, top), project(x, y + d, top)]) }, g);
    el('polygon', { class: 'f-right', points: points([project(x + w, y, z), project(x + w, y + d, z), project(x + w, y + d, top), project(x + w, y, top)]) }, g);
    el('polygon', { class: 'f-top', points: points([project(x, y, top), project(x + w, y, top), project(x + w, y + d, top), project(x, y + d, top)]) }, g);
    return g;
  }

  // 設備と什器の形。座標は左奥の角からの相対で、奥から手前へ順に描く
  var SHAPES = {
    machining: function (g, x, y) {
      box(g, x, y, 0, 2, 2, 1.7);
      box(g, x + 0.5, y + 0.5, 1.7, 1, 1, 0.5, 'tone-dark');
      el('polygon', { class: 'window', points: points([project(x + 0.35, y + 2, 0.55), project(x + 1.65, y + 2, 0.55), project(x + 1.65, y + 2, 1.45), project(x + 0.35, y + 2, 1.45)]) }, g);
      box(g, x + 2, y + 1.3, 0.5, 0.25, 0.5, 0.8, 'tone-dark');
    },
    bench: function (g, x, y) {
      box(g, x, y, 0, 0.2, 0.2, 0.75, 'tone-dark');
      box(g, x + 1.7, y, 0, 0.2, 0.2, 0.75, 'tone-dark');
      box(g, x, y + 1.1, 0, 0.2, 0.2, 0.75, 'tone-dark');
      box(g, x + 1.7, y + 1.1, 0, 0.2, 0.2, 0.75, 'tone-dark');
      box(g, x, y, 0.75, 1.9, 1.3, 0.13);
      box(g, x + 0.65, y + 0.35, 0.88, 0.6, 0.6, 0.35, 'tone-dark');
    },
    gantry: function (g, x, y) {
      box(g, x, y, 0, 1.9, 1.6, 0.4);
      box(g, x + 0.1, y + 0.55, 0.4, 0.2, 0.3, 1.1, 'tone-dark');
      box(g, x + 1.6, y + 0.55, 0.4, 0.2, 0.3, 1.1, 'tone-dark');
      box(g, x + 0.1, y + 0.55, 1.5, 1.7, 0.3, 0.22);
      box(g, x + 0.85, y + 0.6, 0.85, 0.2, 0.2, 0.65, 'tone-dark');
    },
    packer: function (g, x, y) {
      box(g, x, y, 0, 1.9, 1.4, 0.75);
      box(g, x + 0.2, y + 0.2, 0.75, 1.5, 1, 0.25, 'tone-dark');
      box(g, x + 0.3, y + 1.4, 0.28, 1.3, 0.8, 0.12, 'tone-light');
    },
    rack: function (g, x, y, zone) {
      var length = zone.d - 1.3;
      box(g, x, y, 0, 0.8, length, 0.12, 'tone-light');
      box(g, x, y, 0.65, 0.8, length, 0.1, 'tone-light');
      box(g, x, y, 1.3, 0.8, length, 0.1, 'tone-light');
      box(g, x + 0.75, y, 0, 0.05, 0.05, 1.4, 'tone-dark');
      box(g, x + 0.75, y + length - 0.05, 0, 0.05, 0.05, 1.4, 'tone-dark');
    },
    desk: function (g, x, y) {
      box(g, x, y, 0, 2.6, 1, 0.65);
      box(g, x + 0.3, y + 0.15, 0.65, 0.7, 0.08, 0.45, 'tone-dark');
      box(g, x + 1.5, y + 0.15, 0.65, 0.7, 0.08, 0.45, 'tone-dark');
    },
    server: function (g, x, y) {
      box(g, x, y, 0, 1.4, 1.4, 2.2, 'tone-dark');
      for (var i = 0; i < 4; i++) {
        var z = 0.35 + i * 0.45;
        el('polygon', { class: 'server-led', points: points([project(x + 0.2, y + 1.4, z), project(x + 1.2, y + 1.4, z), project(x + 1.2, y + 1.4, z + 0.12), project(x + 0.2, y + 1.4, z + 0.12)]) }, g);
      }
    },
    plant: function (g, x, y) {
      box(g, x, y, 0, 2.9, 2.6, 1.3);
      box(g, x + 0.4, y + 0.4, 1.3, 0.5, 0.5, 1, 'tone-dark');
    },
    truck: function (g, x, y) {
      box(g, x, y + 0.1, 0.25, 3, 1.7, 0.1, 'tone-dark');
      box(g, x + 0.1, y, 0.35, 2.1, 1.9, 1.3, 'tone-light');
      box(g, x + 2.25, y + 0.1, 0.35, 0.85, 1.7, 0.95);
      el('polygon', { class: 'window', points: points([project(x + 2.5, y + 1.8, 0.8), project(x + 3.1, y + 1.8, 0.8), project(x + 3.1, y + 1.8, 1.2), project(x + 2.5, y + 1.8, 1.2)]) }, g);
      box(g, x + 0.35, y + 1.7, 0, 0.5, 0.2, 0.25, 'tone-tire');
      box(g, x + 2.4, y + 1.7, 0, 0.5, 0.2, 0.25, 'tone-tire');
    }
  };

  var FOOTPRINT = { machining: [2.3, 2], bench: [1.9, 1.3], gantry: [1.9, 1.6], packer: [1.9, 2.2], rack: [0.8, 2], desk: [2.6, 1], server: [1.4, 1.4], plant: [2.9, 2.6], truck: [3.1, 1.9] };

  var zoneByCode = {};
  data.zones.forEach(function (z) { zoneByCode[z.code] = z; });

  var layers = {};   // floor → flows → things → labels の順に重ねる
  var labels = [];   // 画面上の大きさを一定に保つラベル（カメラが変わると縮尺を掛け直す）
  var pin;           // MES サーバーが画面の外に出たときの目印
  var camera = { rect: null, codes: null };

  function drawFloor() {
    var f = data.floor;
    var thick = 0.35;
    var bx = f.x, by = f.y, w = f.w, d = f.d;
    el('polygon', { class: 'slab-side-l', points: points([project(bx, by + d), project(bx + w, by + d), project(bx + w, by + d, -thick), project(bx, by + d, -thick)]) }, layers.floor);
    el('polygon', { class: 'slab-side-r', points: points([project(bx + w, by), project(bx + w, by + d), project(bx + w, by + d, -thick), project(bx + w, by, -thick)]) }, layers.floor);
    floorRect(layers.floor, bx, by, w, d, 'slab');

    // 奥の壁。左の壁はトラックの出入口の分だけ切る
    box(layers.floor, bx, by - 0.15, 0, w, 0.15, 1.1, 'tone-wall');
    [[by, 3.5 - by], [8.5, 4], [17, by + d - 17]].forEach(function (seg) {
      box(layers.floor, bx - 0.15, seg[0], 0, 0.15, seg[1], 1.1, 'tone-wall');
    });

    // 工場の外の構内路と、外注先の土台
    var out = zoneByCode.OUTSIDE;
    floorRect(layers.floor, f.x + f.w, out.y - 0.3, out.x + out.w + 0.3 - f.x - f.w, 15.7 - out.y + 0.3, 'road');
    floorRect(layers.floor, out.x - 0.3, out.y - 0.3, out.w + 0.6, out.d + 0.6, 'slab');

    data.zones.forEach(function (z) {
      floorRect(layers.floor, z.x, z.y, z.w, z.d, 'zone zone-' + z.kind);
    });
  }

  function drawFlows() {
    data.flows.forEach(function (flow) {
      var pts = flow.path.map(function (p) { return project(p[0], p[1]); });
      el('polyline', { class: 'flow' + (flow.material ? ' flow-material' : ''), points: points(pts) }, layers.flows);
      var a = pts[pts.length - 2], b = pts[pts.length - 1];
      var angle = Math.atan2(b[1] - a[1], b[0] - a[0]);
      var size = 7;
      el('polygon', {
        class: 'flow-head' + (flow.material ? ' flow-material' : ''),
        points: points([
          b,
          [b[0] - size * Math.cos(angle - 0.5), b[1] - size * Math.sin(angle - 0.5)],
          [b[0] - size * Math.cos(angle + 0.5), b[1] - size * Math.sin(angle + 0.5)]
        ])
      }, layers.flows);
    });
  }

  // 建物の中の物は、1台ごとに奥から手前の順で描く（区画が重ならないのでこの粒度で足りる）
  function drawThings() {
    var things = [];
    data.equipments.concat(data.fixtures, data.trucks).forEach(function (t) {
      things.push({ x: t.x, y: t.y, draw: function (g) { SHAPES[t.shape](g, t.x, t.y, zoneByCode[t.zone]); }, code: t.code, zone: t.zone, shape: t.shape, label: t.label });
    });
    data.people.forEach(function (p) {
      var zone = zoneByCode[p.at];
      var x = p.x === undefined ? zone.spot[0] : p.x;
      var y = p.y === undefined ? zone.spot[1] : p.y;
      things.push({ x: x, y: y, draw: function (g) { drawPerson(g, p, x, y); }, zone: p.at, person: p });
    });
    things.sort(function (a, b) { return (a.x + a.y) - (b.x + b.y); });
    things.forEach(function (t) {
      var g = el('g', { 'data-zone': t.zone }, layers.things);
      t.draw(g);
      if (t.code) {
        var fp = FOOTPRINT[t.shape];
        var left = t.label === 'left';
        var at = project(left ? t.x : t.x + fp[0], t.y + fp[1], 0);
        addLabel(t.code, at[0] + (left ? 0 : 14), at[1] + 11, 'label-equipment', t.zone);
      }
      if (t.person) {
        var foot = project(t.x, t.y, 0);
        addLabel(t.person.user, foot[0], foot[1] + 13, 'label-person role-' + t.person.role, t.zone);
      }
    });
  }

  // 人は高さ方向に立つ簡単な人形。横に端末（入力するときに光る）を持つ
  function drawPerson(g, p, x, y) {
    var person = el('g', { class: 'person role-' + p.role, 'data-user': p.user }, g);
    var foot = project(x, y, 0);
    el('ellipse', { class: 'person-shadow', cx: foot[0], cy: foot[1], rx: 9, ry: 4.5 }, person);
    el('path', {
      class: 'person-body',
      d: 'M' + (foot[0] - 7) + ' ' + foot[1] + ' l1 -17 q0 -5 6 -5 q6 0 6 5 l1 17 q-7 3.5 -14 0 z'
    }, person);
    el('circle', { class: 'person-head', cx: foot[0], cy: foot[1] - 27, r: 5.5 }, person);
    var tx = foot[0] + 10, ty = foot[1] - 19;
    el('polygon', {
      class: 'terminal',
      'data-terminal': p.user,
      points: points([[tx, ty], [tx + 9, ty - 4], [tx + 9, ty + 7], [tx, ty + 11]])
    }, person);
  }

  function addLabel(text, x, y, cls, zone) {
    var g = el('g', { class: 'label ' + cls, 'data-zone': zone }, layers.labels);
    var bg = el('rect', { rx: 4, ry: 4 }, g);
    var t = el('text', { 'text-anchor': 'middle', 'dominant-baseline': 'central' }, g);
    t.textContent = text;
    labels.push({ g: g, x: x, y: y, bg: bg, text: t });
  }

  function zoneLabels() {
    data.zones.forEach(function (z) {
      var apex = z.label === 'center' ? project(z.x + z.w / 2, z.y + z.d / 2, 0) : project(z.x, z.y, 0);
      var title = /^[A-Z]+-/.test(z.code) && z.code !== 'YARD-IN' ? (z.code + ' ' + z.name).trim() : z.name;
      addLabel(title, apex[0], apex[1] - (z.label === 'center' ? 0 : 12), 'label-zone', z.code);
    });
    data.flows.forEach(function (flow) {
      if (!flow.label) return;
      var p = flow.path[1];
      var at = project(p[0] + 5, p[1], 0);
      addLabel(flow.label, at[0], at[1] - 11, 'label-flow', 'FLOW');
    });
  }

  function measureLabels() {
    labels.forEach(function (l) {
      var box = l.text.getBBox();
      l.halfWidth = box.width / 2 + 5;
      el2attrs(l.bg, { x: box.x - 5, y: box.y - 2, width: box.width + 10, height: box.height + 4 });
    });
  }

  function el2attrs(node, attrs) {
    for (var key in attrs) node.setAttribute(key, attrs[key]);
  }

  // 持ち場の画面上の範囲（SVG 座標）
  function zoneBounds(code) {
    var z = zoneByCode[code];
    if (!z) return null;
    var extra = code === 'MES' ? 3 : 2.3;  // 背の高い物が上に伸びる分
    var corners = [project(z.x, z.y, extra), project(z.x + z.w, z.y, extra), project(z.x + z.w, z.y + z.d), project(z.x, z.y + z.d)];
    var xs = corners.map(function (c) { return c[0]; });
    var ys = corners.map(function (c) { return c[1]; });
    return { x0: Math.min.apply(null, xs), y0: Math.min.apply(null, ys), x1: Math.max.apply(null, xs), y1: Math.max.apply(null, ys) };
  }

  function allBounds() {
    var f = data.floor;
    var out = zoneByCode.OUTSIDE;
    return {
      x0: project(f.x, f.y + f.d)[0] - 12,
      y0: project(f.x, f.y, 1.1)[1] - 12,
      x1: project(out.x + out.w, out.y)[0] + 12,
      y1: Math.max(project(f.x + f.w, f.y + f.d)[1], project(out.x + out.w, 15.7)[1]) + 14
    };
  }

  // 範囲を枠の縦横比に広げて viewBox にする。これで viewBox ＝ 見えている範囲になる
  function fitRect(bounds) {
    var rect = svg.getBoundingClientRect();
    var aspect = rect.width > 0 && rect.height > 0 ? rect.width / rect.height : 1.7;
    var w = Math.max(bounds.x1 - bounds.x0, MIN_VIEW_W);
    var h = bounds.y1 - bounds.y0;
    if (w / h < aspect) w = h * aspect; else h = w / aspect;
    var cx = (bounds.x0 + bounds.x1) / 2, cy = (bounds.y0 + bounds.y1) / 2;
    return { x: cx - w / 2, y: cy - h / 2, w: w, h: h };
  }

  function isNarrow() { return window.matchMedia(NARROW).matches; }

  // カメラの適用。ラベルは文字の大きさが画面上で一定になるよう縮尺を打ち消す
  function applyCamera() {
    var bounds;
    if (camera.codes) {
      bounds = null;
      camera.codes.forEach(function (code) {
        var b = zoneBounds(code);
        if (!b) return;
        bounds = bounds
          ? { x0: Math.min(bounds.x0, b.x0), y0: Math.min(bounds.y0, b.y0), x1: Math.max(bounds.x1, b.x1), y1: Math.max(bounds.y1, b.y1) }
          : b;
      });
      bounds.x0 -= LABEL_PAD; bounds.x1 += LABEL_PAD; bounds.y0 -= LABEL_PAD; bounds.y1 += LABEL_PAD;
    } else {
      bounds = allBounds();
    }
    var view = fitRect(bounds);
    camera.rect = view;
    svg.setAttribute('viewBox', [view.x, view.y, view.w, view.h].map(function (n) { return n.toFixed(1); }).join(' '));

    var box = svg.getBoundingClientRect();
    var scale = box.width > 0 ? box.width / view.w : 1;
    var k = 1 / scale;
    labels.forEach(function (l) {
      var x = l.x + clampShift(l.x, l.halfWidth * k, view);
      l.g.setAttribute('transform', 'translate(' + x.toFixed(1) + ' ' + l.y.toFixed(1) + ') scale(' + k.toFixed(3) + ')');
    });
    updateLabelVisibility(scale);
    updatePin(view, k);
  }

  // 端で切れかけたラベルを枠の中へ押し戻す量。完全に外のものは動かさない
  function clampShift(center, half, view) {
    var over = center + half - (view.x + view.w);
    var under = view.x - (center - half);
    if (over > 0 && over < half * 2) return -over;
    if (under > 0 && under < half * 2) return under;
    return 0;
  }

  // 狭い画面では、今の持ち場に関わるラベルと MES サーバーだけを出す。縮尺が小さい全景では読めないので出さない（CSS の is-overview）
  function updateLabelVisibility(scale) {
    var narrow = isNarrow();
    svg.classList.toggle('is-overview', !camera.codes && scale < MIN_LABEL_SCALE);
    labels.forEach(function (l) {
      var zone = l.g.getAttribute('data-zone');
      var hide = false;
      if (narrow) {
        if (camera.codes) hide = zone !== 'MES' && camera.codes.indexOf(zone) < 0 && !isChildOf(zone, camera.codes);
      }
      l.g.classList.toggle('is-hidden', hide);
    });
  }

  function isChildOf(code, codes) {
    var z = zoneByCode[code];
    return !!(z && z.parent && codes.indexOf(z.parent) >= 0);
  }

  // MES サーバーが画面の外に出たら、その方向の画面の端に目印を出す
  function updatePin(view, k) {
    var b = zoneBounds('MES');
    var cx = (b.x0 + b.x1) / 2, cy = (b.y0 + b.y1) / 2;
    var inside = cx >= view.x && cx <= view.x + view.w && cy >= view.y && cy <= view.y + view.h;
    pin.g.classList.toggle('is-hidden', inside);
    if (inside) return;
    var vcx = view.x + view.w / 2, vcy = view.y + view.h / 2;
    var dx = cx - vcx, dy = cy - vcy;
    var margin = 54 * k, marginY = 18 * k;
    var t = Math.min(
      dx ? (view.w / 2 - margin) / Math.abs(dx) : Infinity,
      dy ? (view.h / 2 - marginY) / Math.abs(dy) : Infinity
    );
    pin.x = vcx + dx * t;
    pin.y = vcy + dy * t;
    pin.g.setAttribute('transform', 'translate(' + pin.x.toFixed(1) + ' ' + pin.y.toFixed(1) + ') scale(' + k.toFixed(3) + ')');
  }

  function drawPin() {
    var g = el('g', { class: 'label label-pin is-hidden' }, layers.labels);
    var bg = el('rect', { rx: 4, ry: 4 }, g);
    var t = el('text', { 'text-anchor': 'middle', 'dominant-baseline': 'central' }, g);
    t.textContent = 'MES サーバー';
    pin = { g: g, bg: bg, text: t, x: 0, y: 0 };
  }

  function measurePin() {
    var box = pin.text.getBBox();
    el2attrs(pin.bg, { x: box.x - 6, y: box.y - 3, width: box.width + 12, height: box.height + 6 });
  }

  // 骨組みの枠。値は TOUR-02 で入る
  function buildFrames() {
    var rail = document.getElementById('tour-rail');
    for (var i = 1; i <= 15; i++) {
      var li = document.createElement('li');
      var button = document.createElement('button');
      button.type = 'button';
      button.disabled = true;
      button.setAttribute('aria-label', '場面 ' + i);
      li.appendChild(button);
      rail.appendChild(li);
    }
    fillBlankRows('tour-ledger-lots', 9, 4);
    fillBlankRows('tour-ledger-orders', 5, 2);
  }

  function fillBlankRows(id, rows, cols) {
    var body = document.getElementById(id);
    for (var r = 0; r < rows; r++) {
      var tr = document.createElement('tr');
      for (var c = 0; c < cols; c++) {
        var td = document.createElement('td');
        td.textContent = '―';
        tr.appendChild(td);
      }
      body.appendChild(tr);
    }
  }

  function init() {
    layers.floor = el('g', { class: 'layer-floor' }, svg);
    layers.flows = el('g', { class: 'layer-flows' }, svg);
    layers.things = el('g', { class: 'layer-things' }, svg);
    layers.labels = el('g', { class: 'layer-labels' }, svg);

    drawFloor();
    drawFlows();
    drawThings();
    zoneLabels();
    drawPin();
    measureLabels();
    measurePin();
    buildFrames();
    applyCamera();

    if (window.ResizeObserver) new ResizeObserver(applyCamera).observe(svg);
    else window.addEventListener('resize', applyCamera);
  }

  // カメラの公開窓口。場面の再生（TOUR-02）とコンソールの確認から呼ぶ
  // focus('WC-MC1') / focus(['WH-M01', 'WC-MC1']) / focus('all')
  window.TourModel = {
    focus: function (target) {
      if (target === 'all' || target === undefined) {
        camera.codes = null;
      } else {
        var codes = [].concat(target);
        var unknown = codes.filter(function (code) { return !zoneByCode[code]; });
        if (unknown.length) throw new Error('未知の持ち場: ' + unknown.join(', '));
        camera.codes = codes;
      }
      applyCamera();
      return svg.getAttribute('viewBox');
    },
    zones: function () { return Object.keys(zoneByCode); }
  };

  init();
})();
