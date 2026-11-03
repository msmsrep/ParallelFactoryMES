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
  var CROWDED_PILES = 3;             // 同じ置場にこれ以上の物が並ぶと、数量のラベルが混み合う
  var MIN_CROWDED_SCALE = 0.65;      // 混み合う置場の数量を出す最小の縮尺

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

  var layers = {};     // floor → flows → things → stock → people → moving → signal → labels の順に重ねる
  var labels = [];     // 画面上の大きさを一定に保つラベル（カメラが変わると縮尺を掛け直す）
  var pin;             // MES サーバーが画面の外に出たときの目印
  var camera = { rect: null, codes: null, k: 1 };
  var labelMode = 'part'; // ラベルの出し方。part は今の場面に関わるものだけ、all はすべて
  var zonePolygons = {};
  var people = {};     // user → 人形の要素、持ち場、今の床の座標

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
      zonePolygons[z.code] = floorRect(layers.floor, z.x, z.y, z.w, z.d, 'zone zone-' + z.kind);
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
    things.sort(function (a, b) { return (a.x + a.y) - (b.x + b.y); });
    things.forEach(function (t) {
      var g = el('g', { 'data-zone': t.zone }, layers.things);
      t.draw(g);
      if (t.code) {
        g.setAttribute('data-code', t.code);
        var fp = FOOTPRINT[t.shape];
        var left = t.label === 'left';
        var at = project(left ? t.x : t.x + fp[0], t.y + fp[1], 0);
        addLabel(t.code, at[0] + (left ? 0 : 14), at[1] + 11, 'label-equipment', t.zone).crowded = true;
      }
    });
  }

  // 人は場面に応じて持ち場を移るので、設備より手前の層に描く
  function drawPeople() {
    data.people.forEach(function (p) {
      var zone = zoneByCode[p.at];
      var home = [p.x === undefined ? zone.spot[0] : p.x, p.y === undefined ? zone.spot[1] : p.y];
      var g = el('g', {}, layers.people);
      drawPerson(g, p, home[0], home[1]);
      var foot = project(home[0], home[1], 0);
      addLabel(p.user, foot[0], foot[1] + 13, 'label-person role-' + p.role, p.at);
      people[p.user] = {
        role: p.role,
        g: g,
        home: home,
        homeZone: p.at,
        pos: home,
        label: labels[labels.length - 1],
        terminal: g.querySelector('.terminal')
      };
    });
  }

  // 人を床の座標へ置く。ラベルも一緒に動かす
  function setPersonPos(user, pos, zone) {
    var man = people[user];
    var from = project(man.home[0], man.home[1], 0);
    var to = project(pos[0], pos[1], 0);
    man.pos = pos;
    man.g.setAttribute('transform', 'translate(' + (to[0] - from[0]).toFixed(1) + ' ' + (to[1] - from[1]).toFixed(1) + ')');
    man.label.x = to[0];
    man.label.y = to[1] + 13;
    if (zone) man.label.g.setAttribute('data-zone', zone);
    if (camera.rect) placeLabel(man.label);
  }

  function personSpot(user, zone) {
    var man = people[user];
    return zone === man.homeZone ? man.home : zoneByCode[zone].spot;
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

  function addLabel(text, x, y, cls, zone, dynamic) {
    var g = el('g', { class: 'label ' + cls, 'data-zone': zone }, layers.labels);
    var bg = el('rect', { rx: 4, ry: 4 }, g);
    var t = el('text', { 'text-anchor': 'middle', 'dominant-baseline': 'central' }, g);
    t.textContent = text;
    var label = { g: g, x: x, y: y, bg: bg, text: t, dynamic: !!dynamic };
    labels.push(label);
    return label;
  }

  function zoneLabels() {
    data.zones.forEach(function (z) {
      var apex = z.label === 'center' ? project(z.x + z.w / 2, z.y + z.d / 2, 0)
        : z.labelAt ? project(z.labelAt[0], z.labelAt[1], 0)
        : project(z.x, z.y, 0);
      var title = /^[A-Z]+-/.test(z.code) && z.code !== 'YARD-IN' ? (z.code + ' ' + z.name).trim() : z.name;
      var label = addLabel(title, apex[0], apex[1] - (z.label === 'center' ? 0 : 12), 'label-zone', z.code);
      if (z.labelNarrow) {
        var at = project(z.labelNarrow[0], z.labelNarrow[1], 0);
        label.narrowAt = [at[0], at[1] - 12];
      }
    });
    data.flows.forEach(function (flow) {
      if (!flow.label) return;
      var p = flow.path[1];
      var at = project(p[0] + 5, p[1], 0);
      addLabel(flow.label, at[0], at[1] - 11, 'label-flow', 'FLOW');
    });
  }

  function measureLabels() {
    labels.forEach(measureLabel);
  }

  // ラベルの地の大きさを文字に合わせる。全景で隠れている間は測れないので、文字数から見積もる
  function measureLabel(l) {
    var box = l.text.getBBox();
    var width = box.width || l.text.textContent.length * 7;
    var height = box.height || 14;
    l.halfWidth = width / 2 + 5;
    el2attrs(l.bg, { x: -width / 2 - 5, y: -height / 2 - 2, width: width + 10, height: height + 4 });
  }

  function placeLabel(l) {
    var at = l.narrowAt && isNarrow() ? l.narrowAt : [l.x, l.y];
    var x = at[0] + clampShift(at[0], l.halfWidth * camera.k, camera.rect);
    l.g.setAttribute('transform', 'translate(' + x.toFixed(1) + ' ' + at[1].toFixed(1) + ') scale(' + camera.k.toFixed(3) + ')');
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
    camera.k = k;
    labels.forEach(placeLabel);
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
    var scene = current >= 0 ? scenes[current] : null;
    labels.forEach(function (l) {
      var hide = false;
      if (labelMode === 'part') {
        hide = !isRelevantLabel(l, scene);
        // 広い範囲を映すと、設備のコードと棚に並ぶ物の数量は重なって読めない（変わった数は変化の帯に出ている）
        if (narrow && l.crowded && scale < MIN_CROWDED_SCALE) hide = true;
      }
      l.g.classList.toggle('is-hidden', hide);
    });
  }

  // 部分表示で出すラベル。今の作業とその前後の持ち場、担当者、動かす設備、異常、MES サーバーだけ
  // 開始前は持ち場の名前だけを出す
  function isRelevantLabel(l, scene) {
    var classes = l.g.classList;
    var zone = l.g.getAttribute('data-zone');
    if (zone === 'MES') return true;
    if (!scene) return classes.contains('label-zone');
    if (classes.contains('label-alert') || classes.contains('label-trace')) return true;
    if (classes.contains('label-flow')) return false;
    if (classes.contains('label-equipment')) return l.text.textContent === scene.equipment;
    if (classes.contains('label-person')) {
      return (scene.actors || []).some(function (a) { return a.user === l.text.textContent; });
    }
    var codes = sceneCodes(scene).concat(scene.around || []);
    return codes.indexOf(zone) >= 0 || isChildOf(zone, codes);
  }

  function isChildOf(code, codes) {
    var z = zoneByCode[code];
    return !!(z && z.parent && codes.indexOf(z.parent) >= 0);
  }

  // MES サーバーが画面の外に出たら、画面の右上に固定の目印を出す（持ち場のラベルと重ならないように）
  function updatePin(view, k) {
    var b = zoneBounds('MES');
    var cx = (b.x0 + b.x1) / 2, cy = (b.y0 + b.y1) / 2;
    var inside = cx >= view.x && cx <= view.x + view.w && cy >= view.y && cy <= view.y + view.h;
    pin.g.classList.toggle('is-hidden', inside);
    if (inside) return;
    pin.x = view.x + view.w - 54 * k;
    pin.y = view.y + 18 * k;
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

  // ---------- 場面の畳み込み（台帳も模型の物も、1場面目から足し直して作る） ----------

  var scenes = data.scenes;
  var lotByCode = {};
  data.lots.forEach(function (l) { lotByCode[l.lot] = l; });

  function round(n) { return Math.round(n * 1000) / 1000; }

  function addStock(stock, zone, item, qty) {
    var piles = stock[zone] = stock[zone] || [];
    var pile = piles.filter(function (p) { return p.item === item; })[0];
    if (!pile) piles.push(pile = { item: item, qty: 0 });
    pile.qty = round(pile.qty + qty);
    if (pile.qty <= 0) piles.splice(piles.indexOf(pile), 1);
  }

  function applyScene(state, scene) {
    (scene.moves || []).forEach(function (m) {
      if (m.from) addStock(state.stock, m.from, m.item, -m.qty);
      if (m.to) addStock(state.stock, m.to, m.as || m.item, m.qty);
    });
    (scene.effects || []).forEach(function (e) {
      if (e.lot && e.delta !== undefined) {
        state.lots[e.lot] = round((state.lots[e.lot] || 0) + e.delta);
        state.lotStatus[e.lot] = state.lotStatus[e.lot] || '正常';
      }
      if (e.lot && e.status) state.lotStatus[e.lot] = e.status;
      if (e.mo) state.mos[e.mo] = e.status;
      if (e.wo) state.orders[e.wo] = e.status;
    });
    (scene.actors || []).forEach(function (a) { state.people[a.user] = a.at; });
  }

  // count 番目までの場面を足した状態。stateAt(0) は開始前
  function stateAt(count) {
    var state = { stock: {}, lots: {}, lotStatus: {}, mos: {}, orders: {}, people: {} };
    scenes.slice(0, count).forEach(function (s) { applyScene(state, s); });
    return state;
  }

  function stateBefore(scene) { return stateAt(scenes.indexOf(scene)); }
  function stateAfter(scene) { return stateAt(scenes.indexOf(scene) + 1); }

  // ---------- 模型の物 ----------

  var CUBES = [[0, 0], [0.36, 0], [0, 0.36], [0.36, 0.36]];

  function pileSize(qty) { return qty >= 100 ? 4 : qty >= 30 ? 3 : qty >= 10 ? 2 : 1; }

  // 床の原点に物の山を描く。置く場所は呼び出し側が translate で決める
  function drawPile(parent, item, qty) {
    var kind = data.items[item].kind;
    var g = el('g', { class: 'pile' }, parent);
    var title = el('title', {}, g);
    title.textContent = data.items[item].name + ' ' + qty;
    if (kind === 'product') {
      box(g, -0.3, -0.25, 0, 0.6, 0.5, 0.42, 'tone-product');
      return g;
    }
    CUBES.slice(0, pileSize(qty)).forEach(function (c) {
      box(g, c[0] - 0.18, c[1] - 0.18, 0, 0.3, 0.3, 0.3, 'tone-' + kind);
    });
    return g;
  }

  function zoneCenter(code) {
    var z = zoneByCode[code];
    return [z.x + z.w / 2, z.y + z.d / 2, 0];
  }

  // 物の置き場所。slots の無い置場や消える物は、区画の中央か最初の slot へ向かう
  function slotOf(state, zone, item) {
    var slots = zoneByCode[zone].slots;
    if (!slots) return zoneCenter(zone);
    var index = (state.stock[zone] || []).map(function (p) { return p.item; }).indexOf(item);
    var slot = slots[Math.max(index, 0)] || slots[slots.length - 1];
    return [slot[0], slot[1], slot[2] || 0];
  }

  function clearDynamicLabels() {
    labels = labels.filter(function (l) {
      if (l.dynamic) l.g.parentNode.removeChild(l.g);
      return !l.dynamic;
    });
  }

  // 物の山と、異常のラベルを描き直す
  function renderField(state, scene) {
    clearDynamicLabels();
    while (layers.stock.firstChild) layers.stock.removeChild(layers.stock.firstChild);
    var added = [];
    Object.keys(state.stock).forEach(function (zone) {
      state.stock[zone].forEach(function (pile) {
        var at = slotOf(state, zone, pile.item);
        var p = project(at[0], at[1], at[2]);
        var g = drawPile(layers.stock, pile.item, pile.qty);
        g.setAttribute('transform', 'translate(' + p[0].toFixed(1) + ' ' + p[1].toFixed(1) + ')');
        var label = addLabel(String(pile.qty), p[0], p[1] + 12, 'label-stock', zone, true);
        label.crowded = state.stock[zone].length >= CROWDED_PILES;
        added.push(label);
      });
    });
    alertTags(scene).forEach(function (a) {
      var z = zoneByCode[a.at];
      var p = a.labelAt ? project(a.labelAt[0], a.labelAt[1], 0) : project(z.x + z.w, z.y + z.d, 0);
      added.push(addLabel(a.text, p[0], p[1] + (a.labelAt ? 0 : 24), 'label-alert', a.at, true));
    });
    traceLinks(scene).forEach(function (link) {
      var c = traceCurve(link);
      var mid = link.labelAt ? project(link.labelAt[0], link.labelAt[1], 0) : [(c.a[0] + 2 * c.c[0] + c.b[0]) / 4, (c.a[1] + 2 * c.c[1] + c.b[1]) / 4];
      added.push(addLabel(link.tag, mid[0], mid[1], 'label-trace', 'TRACE', true));
    });
    added.forEach(measureLabel);
    applyCamera();
  }

  // 同じ持ち場の異常は1つのラベルにまとめる（「逸脱・停止」）
  function alertTags(scene) {
    var byZone = {};
    ((scene && scene.alerts) || []).forEach(function (a) {
      var entry = byZone[a.at] = byZone[a.at] || { at: a.at, tags: [], labelAt: null };
      entry.tags.push(a.tag);
      entry.labelAt = entry.labelAt || a.labelAt;
    });
    return Object.keys(byZone).map(function (at) {
      return { at: at, text: byZone[at].tags.join('・'), labelAt: byZone[at].labelAt };
    });
  }

  function renderPeople(state) {
    Object.keys(people).forEach(function (user) {
      var zone = state.people[user] || people[user].homeZone;
      setPersonPos(user, personSpot(user, zone), zone);
    });
  }

  function markZones(scene) {
    Object.keys(zonePolygons).forEach(function (code) {
      zonePolygons[code].classList.toggle('is-station', !!scene && scene.station === code);
      zonePolygons[code].classList.toggle('is-alert', alertTags(scene).some(function (a) { return a.at === code; }));
    });
  }

  // カメラは狭い画面だけ寄せる。範囲は station・actors・moves の置場をすべて含む
  function sceneCodes(scene) {
    var codes = [];
    function add(code) { if (code && codes.indexOf(code) < 0) codes.push(code); }
    add(scene.station);
    (scene.actors || []).forEach(function (a) { add(a.at); });
    (scene.moves || []).forEach(function (m) {
      add(m.from); add(m.to); add(m.via);
      (m.route || []).forEach(add);
    });
    return codes;
  }

  function setSceneCamera(scene) {
    camera.codes = scene && isNarrow() && scene.camera !== 'all' ? sceneCodes(scene) : null;
    applyCamera();
  }

  // ---------- 台帳 ----------

  var ledger = { lots: {}, mos: {}, orders: {} };
  var bandEl = document.querySelector('.tour-band');
  var ledgerEl = document.querySelector('.tour-ledger');
  var ledgerNote;

  function cell(tr, text, cls) {
    var td = document.createElement('td');
    td.textContent = text;
    if (cls) td.className = cls;
    tr.appendChild(td);
    return td;
  }

  function buildLedger() {
    var lotBody = document.getElementById('tour-ledger-lots');
    data.lots.forEach(function (l) {
      var tr = document.createElement('tr');
      cell(tr, l.lot);
      cell(tr, l.name);
      cell(tr, l.at);
      ledger.lots[l.lot] = { tr: tr, value: cell(tr, '―', 'num'), status: cell(tr, '―') };
      lotBody.appendChild(tr);
    });
    var statusHead = document.createElement('th');
    statusHead.textContent = '状態';
    lotBody.closest('table').tHead.rows[0].appendChild(statusHead);

    var orderBody = document.getElementById('tour-ledger-orders');
    var orderCol = orderBody.closest('.tour-ledger-col');
    orderCol.querySelector('h2').textContent = '製造指図・作業指示の状態';
    orderBody.closest('table').tHead.rows[0].cells[0].textContent = '指図・作業指示';
    data.manufacturingOrders.forEach(function (mo) {
      var tr = document.createElement('tr');
      tr.className = 'tour-ledger-mo';
      cell(tr, mo);
      ledger.mos[mo] = { tr: tr, value: cell(tr, '―') };
      orderBody.appendChild(tr);
    });
    data.orders.forEach(function (wo) {
      var tr = document.createElement('tr');
      cell(tr, wo);
      ledger.orders[wo] = { tr: tr, value: cell(tr, '―') };
      orderBody.appendChild(tr);
    });
    ledgerNote = document.createElement('p');
    ledgerNote.className = 'text-note tour-ledger-note';
    orderCol.appendChild(ledgerNote);

    // 狭い画面では全行を畳む。変わった行は変化の帯に出ている
    var toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'tour-ledger-toggle';
    toggle.textContent = 'すべて表示';
    toggle.setAttribute('aria-expanded', 'false');
    toggle.addEventListener('click', function () {
      var collapsed = ledgerEl.classList.toggle('is-collapsed');
      toggle.textContent = collapsed ? 'すべて表示' : '畳む';
      toggle.setAttribute('aria-expanded', String(!collapsed));
    });
    ledgerEl.classList.add('is-collapsed');
    ledgerEl.insertBefore(toggle, ledgerEl.firstChild);
  }

  function lotText(lot, qty) { return qty + ' ' + lotByCode[lot].unit; }

  // その場面で変わった行。ロットの状態は、場面の中で一度なった状態（from）があればそこからの変化として見せる
  function changesOf(before, after, scene) {
    var list = [];
    data.lots.forEach(function (l) {
      var from = before.lots[l.lot], to = after.lots[l.lot];
      if (from !== to) list.push({ row: ledger.lots[l.lot], text: l.lot + ' ' + l.name + '：' + (from === undefined ? '新規 ' + lotText(l.lot, to) : from + ' → ' + lotText(l.lot, to)) });
      (scene.effects || []).forEach(function (e) {
        if (e.lot === l.lot && e.status) {
          list.push({ row: ledger.lots[l.lot], text: l.lot + ' ' + l.name + '：' + (e.from || before.lotStatus[l.lot]) + ' → ' + e.status });
        }
      });
    });
    data.manufacturingOrders.forEach(function (mo) {
      var from = before.mos[mo], to = after.mos[mo];
      if (from !== to) list.push({ row: ledger.mos[mo], text: mo + '：' + (from === undefined ? to : from + ' → ' + to) });
    });
    data.orders.forEach(function (wo) {
      var from = before.orders[wo], to = after.orders[wo];
      if (from !== to) list.push({ row: ledger.orders[wo], text: wo + '：' + (from === undefined ? to : from + ' → ' + to) });
    });
    return list;
  }

  function renderLedger(state, scene, changes, flash) {
    data.lots.forEach(function (l) {
      var row = ledger.lots[l.lot], qty = state.lots[l.lot];
      row.value.textContent = qty === undefined ? '―' : lotText(l.lot, qty);
      row.status.textContent = state.lotStatus[l.lot] || '―';
      row.tr.classList.toggle('is-absent', qty === undefined);
    });
    data.manufacturingOrders.forEach(function (mo) {
      var row = ledger.mos[mo], status = state.mos[mo];
      row.value.textContent = status === undefined ? '―' : status;
      row.tr.classList.toggle('is-absent', status === undefined);
    });
    data.orders.forEach(function (wo) {
      var row = ledger.orders[wo], status = state.orders[wo];
      row.value.textContent = status === undefined ? '―' : status;
      row.tr.classList.toggle('is-absent', status === undefined);
    });
    var all = data.lots.map(function (l) { return ledger.lots[l.lot]; })
      .concat(data.manufacturingOrders.map(function (mo) { return ledger.mos[mo]; }))
      .concat(data.orders.map(function (wo) { return ledger.orders[wo]; }));
    all.forEach(function (row) {
      row.tr.classList.remove('is-changed', 'is-flash');
    });
    changes.forEach(function (c) {
      c.row.tr.classList.add('is-changed');
      if (flash) {
        // 同じ行が続けて変わってもアニメーションをやり直す
        void c.row.tr.getBoundingClientRect();
        c.row.tr.classList.add('is-flash');
      }
    });
    scrollToChanged(changes);
    ledgerNote.textContent = (scene && scene.ledgerNote) || '';
  }

  // 変わった行を、欄の中で見える位置へ送る（ページ自体はスクロールさせない）
  function scrollToChanged(changes) {
    document.querySelectorAll('.tour-ledger-scroll').forEach(function (scroller) {
      var first = changes.filter(function (c) { return scroller.contains(c.row.tr); })[0];
      if (!first) return;
      var head = scroller.querySelector('thead');
      var top = first.row.tr.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
      scroller.scrollTop += top - head.getBoundingClientRect().height;
    });
  }

  // 変化の帯。行は先に場所を取っておき、第4拍で見せる（途中でボタンの位置がずれないように）
  function renderBand(scene, changes, revealed) {
    bandEl.textContent = '';
    if (!scene) {
      var empty = document.createElement('p');
      empty.className = 'text-note';
      empty.textContent = 'この場面で変わった記録がここに出ます。';
      bandEl.appendChild(empty);
      return;
    }
    var heading = document.createElement('h2');
    heading.textContent = 'この場面で変わった記録';
    bandEl.appendChild(heading);
    var list = document.createElement('ul');
    list.className = revealed ? 'is-flash' : 'is-pending';
    changeTexts(scene, changes).forEach(function (text) {
      var li = document.createElement('li');
      li.textContent = text;
      list.appendChild(li);
    });
    bandEl.appendChild(list);
    if (scene.ledgerNote) {
      var note = document.createElement('p');
      note.className = 'text-note';
      note.textContent = scene.ledgerNote;
      bandEl.appendChild(note);
    }
  }

  // 台帳の行の変化と、台帳の行が変わらない記録の変化
  function changeTexts(scene, changes) {
    return changes.map(function (c) { return c.text; }).concat(scene.changeNotes || []);
  }

  function revealBand() {
    var list = bandEl.querySelector('ul');
    if (list) list.className = 'is-flash';
  }

  // ---------- トレース ----------

  var traceDirection = 'back';

  function traceLinks(scene) {
    return scene && scene.trace ? scene.trace[traceDirection].links : [];
  }

  // 模型の上を弧でつなぐ（SVG の座標）
  function traceCurve(link) {
    var from = zoneCenter(link.from), to = zoneCenter(link.to);
    var a = project(from[0], from[1], 0), b = project(to[0], to[1], 0);
    return { a: a, b: b, c: [(a[0] + b[0]) / 2, Math.min(a[1], b[1]) - 46] };
  }

  // 光の線。線の長さを1にして、描かれていく動きを CSS で付ける（動きを減らす設定では最初から全部出る）
  function renderTrace(scene, animate) {
    clearLayer(layers.trace);
    traceLinks(scene).forEach(function (link, i) {
      var c = traceCurve(link);
      var d = 'M' + c.a[0].toFixed(1) + ' ' + c.a[1].toFixed(1) + ' Q' + c.c[0].toFixed(1) + ' ' + c.c[1].toFixed(1) + ' ' + c.b[0].toFixed(1) + ' ' + c.b[1].toFixed(1);
      ['trace-glow', 'trace-line'].forEach(function (cls, layer) {
        var path = el('path', {
          class: cls + (animate ? ' is-drawing' : ''),
          d: d,
          pathLength: 1,
          'stroke-width': (layer ? 3 : 9) * camera.k
        }, layers.trace);
        path.style.animationDelay = (i * 180) + 'ms';
      });
      el('circle', { class: 'trace-end', cx: c.b[0], cy: c.b[1], r: 4.5 * camera.k }, layers.trace);
    });
  }

  // ---------- 説明欄 ----------

  var sideEl = document.getElementById('tour-side');
  var liveEl = sideEl.querySelector('.tour-live');
  var sideBody;
  var sideStartHtml;

  function node(tag, cls, text, parent) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text !== undefined) n.textContent = text;
    if (parent) parent.appendChild(n);
    return n;
  }

  function buildSide() {
    sideBody = document.createElement('div');
    while (sideEl.firstChild && sideEl.firstChild !== liveEl) sideBody.appendChild(sideEl.firstChild);
    sideEl.insertBefore(sideBody, liveEl);
    sideStartHtml = sideBody.innerHTML;
  }

  function renderSide(scene, changes) {
    sideEl.scrollTop = 0;
    if (!scene) {
      sideBody.innerHTML = sideStartHtml;
      return;
    }
    sideBody.textContent = '';
    node('p', 'tour-count', '場面 ' + scene.no + '／' + scenes.length, sideBody);
    node('h1', '', scene.title, sideBody);
    if (scene.when) node('p', 'tour-when', scene.when, sideBody);

    node('h2', '', '現場で起きること', sideBody);
    node('p', '', scene.floor, sideBody);
    if (scene.alerts && scene.alerts.length) {
      var alerts = node('ul', 'tour-alerts', undefined, sideBody);
      scene.alerts.forEach(function (a) { node('li', '', a.text, alerts); });
    }
    if (scene.gauges) renderGauges(scene.gauges);
    if (scene.trace) renderTraceChoice(scene);

    var input = node('div', 'tour-section tour-section-input', undefined, sideBody);
    node('h2', '', '誰がどの画面に', input);
    scene.input.forEach(function (i) {
      var p = node('p', '', undefined, input);
      node('strong', 'tour-who role-' + people[i.who].role, i.who, p);
      p.appendChild(document.createTextNode(' ' + i.menu));
      node('p', 'text-note', i.record, input);
    });

    var effects = node('div', 'tour-section tour-section-effects', undefined, sideBody);
    node('h2', '', 'MES で変わること', effects);
    var list = node('ul', 'tour-effects', undefined, effects);
    changeTexts(scene, changes).forEach(function (text) { node('li', '', text, list); });

    if (scene.note) {
      node('h2', '', '見どころ', sideBody);
      node('p', '', scene.note, sideBody);
    }
    if (scene.detail) {
      var link = node('a', 'tour-detail', '詳しく見る →', sideBody);
      link.href = scene.detail.href;
      if (scene.detail.image) {
        var photo = node('a', 'tour-photo', undefined, sideBody);
        photo.href = scene.detail.href;
        var img = node('img', '', undefined, photo);
        img.src = scene.detail.image;
        img.alt = scene.detail.alt || '';
        img.loading = 'lazy';
      }
    }
  }

  // 測定値と規格の帯。帯の左右に余白を取り、規格の外の値も置けるようにする
  function renderGauges(gauges) {
    var box = node('div', 'tour-gauges', undefined, sideBody);
    gauges.forEach(function (g) {
      node('p', 'tour-gauge-name', g.name + '　' + g.values.join('・') + ' ' + g.unit + '（規格 ' + g.min + '〜' + g.max + '）', box);
      var bar = node('div', 'tour-gauge', undefined, box);
      bar.setAttribute('aria-hidden', 'true');
      node('i', 'tour-gauge-band', undefined, bar);
      g.values.forEach(function (v) {
        var dot = node('b', 'tour-gauge-dot', undefined, bar);
        dot.style.left = (25 + (v - g.min) / (g.max - g.min) * 50) + '%';
      });
    });
  }

  function setGaugesPending(on) {
    var box = sideBody.querySelector('.tour-gauges');
    if (box) box.classList.toggle('is-pending', on);
  }

  // トレースの向きの切替と、たどったロットの一覧
  function renderTraceChoice(scene) {
    var group = node('div', 'tour-label-mode tour-trace-mode', undefined, sideBody);
    group.setAttribute('role', 'group');
    group.setAttribute('aria-label', 'たどる向き');
    var list = node('ul', 'tour-effects', undefined, sideBody);
    var choices = Object.keys(scene.trace).map(function (key) {
      var button = node('button', '', scene.trace[key].name, group);
      button.type = 'button';
      button.addEventListener('click', function () {
        traceDirection = key;
        sync();
        renderTrace(scene, true);
        renderField(stateAfter(scene), scene);
      });
      return { key: key, button: button };
    });
    function sync() {
      choices.forEach(function (c) { c.button.setAttribute('aria-pressed', String(c.key === traceDirection)); });
      list.textContent = '';
      traceLinks(scene).forEach(function (link) { node('li', '', link.text, list); });
    }
    sync();
  }

  function lightSection(name, on) {
    var section = sideBody.querySelector('.tour-section-' + name);
    if (section) section.classList.toggle('is-lit', on);
  }

  function announce(scene) {
    var text = scene
      ? '場面 ' + scene.no + '／' + scenes.length + ' ' + scene.title + '。' + scene.floor
      : '工場の全景。受入から加工、外注、組立、検査、梱包を経て出荷までの配置';
    svg.setAttribute('aria-label', text);
    liveEl.textContent = scene ? text : '';
  }

  // ---------- 4拍の進行 ----------

  var BEAT = { move: 1300, stagger: 130, input: 900, signal: 850, flash: 1000, dwell: 5000 };

  var current = -1;      // scenes の位置。-1 は開始前
  var playing = false;   // 自動再生
  var running = false;   // 4拍の途中
  var run = { token: 0, timers: [], dwell: null };

  function reducedMotion() { return window.matchMedia('(prefers-reduced-motion: reduce)').matches; }

  function later(ms, fn) {
    var token = run.token;
    var id = setTimeout(function () { if (token === run.token) fn(); }, ms);
    run.timers.push(id);
  }

  function tween(ms, delay, onFrame) {
    var token = run.token;
    var start = performance.now() + delay;
    (function frame(now) {
      if (token !== run.token) return;
      var t = Math.max(0, Math.min(1, (now - start) / ms));
      onFrame(t * t * (3 - 2 * t));
      if (t < 1) requestAnimationFrame(frame);
    })(performance.now());
  }

  function clearLayer(layer) { while (layer.firstChild) layer.removeChild(layer.firstChild); }

  // 実行中の4拍と、読む時間の待ちをやめる。人の位置や物は呼び出し側が描き直す
  function cancelRun() {
    run.token++;
    run.timers.forEach(clearTimeout);
    run.timers = [];
    clearTimeout(run.dwell);
    running = false;
    clearLayer(layers.moving);
    clearLayer(layers.signal);
    svg.classList.remove('is-receiving');
    svg.querySelectorAll('.is-running').forEach(function (n) { n.classList.remove('is-running'); });
    svg.querySelectorAll('.is-lit').forEach(function (n) { n.classList.remove('is-lit'); });
  }

  // 場面の最終状態をアニメーションなしで表示する
  function renderFinal(scene) {
    var after = stateAfter(scene);
    var changes = changesOf(stateBefore(scene), after, scene);
    traceDirection = 'back';
    setSceneCamera(scene);
    renderTrace(scene, false);
    renderPeople(after);
    markZones(scene);
    renderField(after, scene);
    renderLedger(after, scene, changes, false);
    renderBand(scene, changes, true);
    renderSide(scene, changes);
    announce(scene);
  }

  function showStart() {
    cancelRun();
    current = -1;
    var empty = stateAt(0);
    setSceneCamera(null);
    renderTrace(null, false);
    renderPeople(empty);
    markZones(null);
    renderField(empty, null);
    renderLedger(empty, null, [], false);
    renderBand(null, [], false);
    renderSide(null, []);
    announce(null);
    updateControls();
  }

  function showFinal(index) {
    cancelRun();
    current = index;
    renderFinal(scenes[index]);
    syncHash();
    updateControls();
  }

  function playScene(index) {
    cancelRun();
    current = index;
    syncHash();
    var scene = scenes[index];
    if (reducedMotion()) {
      renderFinal(scene);
      finishScene();
      updateControls();
      return;
    }
    running = true;
    var before = stateBefore(scene);
    var after = stateAfter(scene);
    var changes = changesOf(before, after, scene);
    traceDirection = 'back';
    setSceneCamera(scene);
    renderTrace(scene, true);
    renderPeople(before);
    markZones(scene);
    renderLedger(before, scene, [], false);
    renderBand(scene, changes, false);
    renderSide(scene, changes);
    setGaugesPending(true);
    announce(scene);
    updateControls();

    // 第1拍：現場が動く
    var lifted = stateBefore(scene);
    (scene.moves || []).forEach(function (m) { if (m.from) addStock(lifted.stock, m.from, m.item, -m.qty); });
    renderField(lifted, scene);
    startEquipment(scene, true);
    var moves = scene.moves || [];
    moves.forEach(function (m, i) { flyPiece(m, before, after, i * BEAT.stagger); });
    (scene.papers || []).forEach(function (zone, i) { flyPaper(scene.station, zone, i * BEAT.stagger); });
    walkPeople(scene, before, after);
    var moveTime = BEAT.move + Math.max(0, (scene.papers || []).length - 1) * BEAT.stagger;
    moves.forEach(function (m, i) { moveTime = Math.max(moveTime, moveDuration(m) + i * BEAT.stagger); });

    later(moveTime, function () {
      clearLayer(layers.moving);
      renderPeople(after);
      renderField(after, scene);

      // 第2拍：人が入力する
      setTerminals(scene, true);
      lightSection('input', true);
      setGaugesPending(false);

      later(BEAT.input, function () {
        // 第3拍：信号が飛ぶ
        startEquipment(scene, false);
        lightSection('input', false);
        sendSignals(scene);

        later(BEAT.signal, function () {
          // 第4拍：記録が変わる
          clearLayer(layers.signal);
          setTerminals(scene, false);
          svg.classList.add('is-receiving');
          renderLedger(after, scene, changes, true);
          revealBand();
          lightSection('effects', true);

          later(BEAT.flash, function () {
            svg.classList.remove('is-receiving');
            lightSection('effects', false);
            running = false;
            finishScene();
            updateControls();
          });
        });
      });
    });
  }

  // 4拍の後。自動再生なら、読む時間を置いて次へ進む。最後の場面で止まる
  function finishScene() {
    if (!playing) return;
    if (current >= scenes.length - 1) {
      playing = false;
      return;
    }
    run.dwell = setTimeout(function () { playScene(current + 1); }, BEAT.dwell);
  }

  function startEquipment(scene, on) {
    if (!scene.equipment) return;
    var g = layers.things.querySelector('[data-code="' + scene.equipment + '"]');
    if (g) g.classList.toggle('is-running', on);
  }

  // 途中の持ち場を通る物は、その分だけ時間をかける
  function moveDuration(move) {
    return BEAT.move * (1 + (move.route || []).length * 0.6);
  }

  // 作業指示の紙を事務所から持ち場へ飛ばす
  function flyPaper(fromZone, toZone, delay) {
    var a = zoneCenter(fromZone), b = zoneCenter(toZone);
    var g = el('g', { class: 'moving-piece' }, layers.moving);
    box(g, -0.3, -0.22, 0, 0.6, 0.44, 0.03, 'tone-light');
    g.style.visibility = 'hidden';
    tween(BEAT.move, delay, function (t) {
      if (t > 0) g.style.visibility = '';
      var p = project(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, Math.sin(t * Math.PI) * 1.6);
      g.setAttribute('transform', 'translate(' + p[0].toFixed(1) + ' ' + p[1].toFixed(1) + ')');
    });
  }

  // 物を置場から置場へ飛ばす。as のある物は途中で変わらず、到着後の描き直しで変わる
  function flyPiece(move, before, after, delay) {
    var fromZone = move.from || move.via;
    var toZone = move.to || move.via;
    var a = move.from ? slotOf(before, fromZone, move.item) : zoneCenter(fromZone);
    var b = move.to ? slotOf(after, toZone, move.as || move.item) : slotOf(after, toZone, '');
    var g = el('g', { class: 'moving-piece' }, layers.moving);
    drawPile(g, move.item, move.qty);
    var tag = el('g', { class: 'label label-moving' }, g);
    var bg = el('rect', { rx: 4, ry: 4 }, tag);
    var text = el('text', { 'text-anchor': 'middle', 'dominant-baseline': 'central', y: 0 }, tag);
    text.textContent = data.items[move.item].name + ' ' + move.qty;
    var size = text.getBBox();
    el2attrs(bg, { x: -size.width / 2 - 5, y: -size.height / 2 - 2, width: size.width + 10, height: size.height + 4 });
    g.style.visibility = 'hidden';
    var stops = [a].concat((move.route || []).map(zoneCenter), [b]);
    tween(moveDuration(move), delay, function (t) {
      if (t > 0) g.style.visibility = '';
      var lift = Math.sin(t * Math.PI) * 0.7;
      var at = (stops.length - 1) * t;
      var i = Math.min(Math.floor(at), stops.length - 2);
      var r = at - i, s = stops[i], e = stops[i + 1];
      var p = project(s[0] + (e[0] - s[0]) * r, s[1] + (e[1] - s[1]) * r, s[2] + (e[2] - s[2]) * r + lift);
      g.setAttribute('transform', 'translate(' + p[0].toFixed(1) + ' ' + p[1].toFixed(1) + ')');
      tag.setAttribute('transform', 'translate(0 -16) scale(' + camera.k.toFixed(3) + ')');
    });
  }

  function walkPeople(scene, before, after) {
    (scene.actors || []).forEach(function (actor) {
      var user = actor.user;
      var from = personSpot(user, before.people[user] || people[user].homeZone);
      var to = personSpot(user, after.people[user]);
      if (from === to) return;
      tween(BEAT.move, 0, function (t) {
        setPersonPos(user, [from[0] + (to[0] - from[0]) * t, from[1] + (to[1] - from[1]) * t], actor.at);
      });
    });
  }

  function setTerminals(scene, on) {
    (scene.input || []).forEach(function (i) {
      people[i.who].terminal.classList.toggle('is-lit', on);
    });
  }

  // 信号は人の端末から MES へ飛ばす（設備からの自動収集は無い。決定事項の表）
  function sendSignals(scene) {
    (scene.input || []).forEach(function (i) {
      var man = people[i.who];
      var foot = project(man.pos[0], man.pos[1], 0);
      var a = [foot[0] + 14, foot[1] - 17];
      var pinShown = !pin.g.classList.contains('is-hidden');
      var server = zoneByCode.MES;
      var b = pinShown ? [pin.x, pin.y] : project(server.x + 1.5, server.y + 1.3, 2.2);
      var c = [(a[0] + b[0]) / 2, Math.min(a[1], b[1]) - 30 * camera.k];
      var line = el('path', {
        class: 'signal',
        d: 'M' + a[0].toFixed(1) + ' ' + a[1].toFixed(1) + ' Q' + c[0].toFixed(1) + ' ' + c[1].toFixed(1) + ' ' + b[0].toFixed(1) + ' ' + b[1].toFixed(1)
      }, layers.signal);
      var dot = el('circle', { class: 'signal-dot', r: 4 * camera.k }, layers.signal);
      var length = line.getTotalLength();
      tween(BEAT.signal, 0, function (t) {
        var p = line.getPointAtLength(length * t);
        el2attrs(dot, { cx: p.x, cy: p.y });
      });
    });
  }

  // ---------- 操作 ----------

  var buttons = {};

  function buildControls() {
    var list = document.querySelectorAll('.tour-buttons button');
    buttons.prev = list[0];
    buttons.play = list[1];
    buttons.again = list[2];
    buttons.next = list[3];
    buttons.play.disabled = false;
    buttons.prev.addEventListener('click', goPrev);
    buttons.play.addEventListener('click', togglePlay);
    buttons.again.addEventListener('click', function () {
      if (current >= 0) playScene(current);
    });
    buttons.next.addEventListener('click', goNext);

    var rail = document.getElementById('tour-rail');
    scenes.forEach(function (scene, index) {
      var li = document.createElement('li');
      var button = document.createElement('button');
      button.type = 'button';
      button.title = '場面 ' + scene.no + ' ' + scene.title;
      button.setAttribute('aria-label', button.title);
      button.addEventListener('click', function () {
        playing = false;
        showFinal(index);
      });
      li.appendChild(button);
      rail.appendChild(li);
    });
    legend();
    buildLabelMode();
    document.addEventListener('keydown', onKey);
    window.addEventListener('hashchange', onHashChange);
    window.matchMedia(NARROW).addEventListener('change', function () {
      setSceneCamera(current >= 0 ? scenes[current] : null);
    });
  }

  // ラベルの「部分／全部」の切替
  function buildLabelMode() {
    var group = document.createElement('div');
    group.className = 'tour-label-mode';
    group.setAttribute('role', 'group');
    group.setAttribute('aria-label', '模型のラベルの表示');
    node('span', 'tour-label-mode-name', 'ラベル', group);
    var choices = [['part', '部分'], ['all', '全部']].map(function (c) {
      var button = node('button', '', c[1], group);
      button.type = 'button';
      button.addEventListener('click', function () {
        labelMode = c[0];
        syncLabelMode();
        applyCamera();
      });
      return { mode: c[0], button: button };
    });
    function syncLabelMode() {
      choices.forEach(function (c) {
        c.button.setAttribute('aria-pressed', String(c.mode === labelMode));
      });
    }
    syncLabelMode();
    var controls = document.querySelector('.tour-controls');
    controls.insertBefore(group, document.getElementById('tour-rail'));
  }

  function legend() {
    var box = node('div', 'tour-legend', undefined, document.querySelector('.tour-model'));
    box.setAttribute('aria-hidden', 'true');
    [['material', '部材'], ['semi', '半製品'], ['product', '製品'], ['rework', '再作業待ち']].forEach(function (k) {
      var item = node('span', '', undefined, box);
      node('i', 'swatch-' + k[0], undefined, item);
      item.appendChild(document.createTextNode(k[1]));
    });
  }

  function updateControls() {
    buttons.prev.disabled = current <= 0;
    buttons.again.disabled = current < 0;
    buttons.next.disabled = current >= scenes.length - 1;
    buttons.play.textContent = '';
    node('span', '', playing ? '❚❚ ' : '▶ ', buttons.play).setAttribute('aria-hidden', 'true');
    buttons.play.appendChild(document.createTextNode(playing ? '一時停止' : '再生'));
    document.querySelectorAll('#tour-rail button').forEach(function (button, index) {
      button.classList.toggle('is-past', index < current);
      if (index === current) button.setAttribute('aria-current', 'step');
      else button.removeAttribute('aria-current');
    });
  }

  function goNext() {
    if (current < scenes.length - 1) playScene(current + 1);
  }

  function goPrev() {
    if (current <= 0) return;
    playing = false;
    showFinal(current - 1);
  }

  function togglePlay() {
    if (playing) {
      // 4拍の途中なら最後まで見せ、そこで止める
      playing = false;
      clearTimeout(run.dwell);
      updateControls();
      return;
    }
    playing = true;
    if (current >= scenes.length - 1) { playScene(0); return; }
    if (running) { updateControls(); return; }
    playScene(current + 1);
  }

  function onKey(e) {
    if (e.ctrlKey || e.metaKey || e.altKey || e.shiftKey) return;
    if (e.key === 'ArrowRight') { goNext(); return; }
    if (e.key === 'ArrowLeft') { goPrev(); return; }
    if (e.key !== ' ') return;
    // ボタン・リンクの上の Space は、その要素の操作だけにする
    if (e.target.closest && e.target.closest('button, a, input, select, textarea, summary')) return;
    e.preventDefault();
    togglePlay();
  }

  // 手で移動したときは履歴に積み、自動再生中は積まない
  function syncHash() {
    var hash = '#' + scenes[current].id;
    if (location.hash === hash) return;
    if (playing) history.replaceState(null, '', hash);
    else history.pushState(null, '', hash);
  }

  function indexOfHash() {
    var id = location.hash.slice(1);
    for (var i = 0; i < scenes.length; i++) if (scenes[i].id === id) return i;
    return -1;
  }

  function onHashChange() {
    playing = false;
    var index = indexOfHash();
    if (index < 0) showStart();
    else showFinal(index);
  }

  function init() {
    layers.floor = el('g', { class: 'layer-floor' }, svg);
    layers.flows = el('g', { class: 'layer-flows' }, svg);
    layers.things = el('g', { class: 'layer-things' }, svg);
    layers.stock = el('g', { class: 'layer-stock' }, svg);
    layers.people = el('g', { class: 'layer-people' }, svg);
    layers.trace = el('g', { class: 'layer-trace' }, svg);
    layers.moving = el('g', { class: 'layer-moving' }, svg);
    layers.signal = el('g', { class: 'layer-signal' }, svg);
    layers.labels = el('g', { class: 'layer-labels' }, svg);

    drawFloor();
    drawFlows();
    drawThings();
    drawPeople();
    zoneLabels();
    drawPin();
    measureLabels();
    measurePin();
    applyCamera();
    buildLedger();
    buildSide();
    buildControls();

    var index = indexOfHash();
    if (index < 0) showStart();
    else showFinal(index);

    if (window.ResizeObserver) new ResizeObserver(applyCamera).observe(svg);
    else window.addEventListener('resize', applyCamera);
  }

  // カメラと状態の公開窓口。コンソールの確認から呼ぶ
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
    zones: function () { return Object.keys(zoneByCode); },
    // 場面 id までを足し直した状態（省略すると今の場面）
    state: function (id) {
      var scene = id ? scenes.filter(function (s) { return s.id === id; })[0] : scenes[current];
      return scene ? stateAfter(scene) : stateAt(0);
    }
  };

  init();
})();
