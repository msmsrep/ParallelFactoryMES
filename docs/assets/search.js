/* ガイド内検索。ヘッダーの検索欄に打つと、言語ごとの索引（search.json / en/search.json）を探して結果を出す。
   索引は _includes/search-index.json がビルド時に作る（ページを見出しごとの節に区切ったもの）。
   日本語は語を空白で区切らないので、形態素解析はせず文字列をそのまま探す。空白で区切った語は「かつ」で扱う */
(function () {
  var box = document.querySelector('.search');
  if (!box) return;
  var input = box.querySelector('input');
  var panel = box.querySelector('.search-results');
  var src = box.getAttribute('data-index');
  var noneText = box.getAttribute('data-none');
  var index = null, loading = null, active = -1;

  // 全角・半角や大文字・小文字の違いで取りこぼさないようにそろえる
  function norm(s) { return (s || '').normalize('NFKC').toLowerCase(); }
  function esc(s) {
    return s.replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
  }

  function load() {
    if (!loading) {
      loading = fetch(src).then(function (r) { return r.json(); }).then(function (d) {
        index = d.map(function (r) { return { r: r, p: norm(r.p), h: norm(r.s + ' ' + r.h), t: norm(r.t) }; });
      });
    }
    return loading;
  }

  function count(hay, term) {
    var n = 0, i = hay.indexOf(term);
    while (i >= 0 && n < 20) { n++; i = hay.indexOf(term, i + term.length); }
    return n;
  }

  // text の start〜end を切り出し、当たった語を <mark> で囲む。
  // 正規化で長さが変わる文字（合字など）を含むときは位置がずれるので、囲まずにそのまま出す
  function mark(text, terms, start, end) {
    var part = text.slice(start, end);
    var low = norm(text);
    if (low.length !== text.length) return esc(part);
    var lowPart = low.slice(start, end), out = '', i = 0;
    while (i < part.length) {
      var hit = 0;
      for (var j = 0; j < terms.length; j++) {
        if (terms[j].length > hit && lowPart.startsWith(terms[j], i)) hit = terms[j].length;
      }
      if (hit) { out += '<mark>' + esc(part.substr(i, hit)) + '</mark>'; i += hit; }
      else { out += esc(part[i]); i++; }
    }
    return out;
  }

  // 本文から、最初に当たった語の前後を抜き出す
  function snippet(text, terms) {
    var low = norm(text), at = -1;
    for (var k = 0; k < terms.length && at < 0; k++) at = low.indexOf(terms[k]);
    if (at < 0 || low.length !== text.length) at = 0;
    var start = Math.max(0, at - 30), end = Math.min(text.length, at + 90);
    return (start > 0 ? '…' : '') + mark(text, terms, start, end) + (end < text.length ? '…' : '');
  }

  function search(q) {
    var terms = norm(q).split(/\s+/).filter(Boolean);
    if (!terms.length) return [];
    var res = [];
    index.forEach(function (e) {
      var score = 0;
      for (var k = 0; k < terms.length; k++) {
        // h には属する h2 の見出し（s）も含めてある
        var t = terms[k], inH = e.h.indexOf(t) >= 0, inP = e.p.indexOf(t) >= 0, n = count(e.t, t);
        if (!inH && !inP && !n) return;
        // 見出しに当たった節を上に、次にページ名、あとは本文に出てくる回数で並べる
        score += (inH ? 20 : 0) + (inP ? 8 : 0) + n;
      }
      res.push({ r: e.r, s: score });
    });
    res.sort(function (a, b) { return b.s - a.s; });
    return res.slice(0, 30).map(function (x) { return { r: x.r, terms: terms }; });
  }

  function render() {
    var q = input.value.trim();
    if (!q) { close(); return; }
    load().then(function () {
      if (input.value.trim() !== q) return;
      var found = search(q);
      active = -1;
      if (!found.length) {
        panel.innerHTML = '<p class="search-none">' + esc(noneText) + '</p>';
      } else {
        panel.innerHTML = found.map(function (f, i) {
          var r = f.r;
          var href = r.u + (r.a ? '#' + encodeURIComponent(r.a) : '');
          var sep = ' <span aria-hidden="true">›</span> ';
          var title = mark(r.p, f.terms, 0, r.p.length) +
            (r.s ? sep + mark(r.s, f.terms, 0, r.s.length) : '') +
            (r.h ? sep + mark(r.h, f.terms, 0, r.h.length) : '');
          return '<a role="option" id="sr-' + i + '" href="' + esc(href) + '"><b>' + title + '</b>' +
            '<span>' + snippet(r.t, f.terms) + '</span></a>';
        }).join('');
      }
      panel.hidden = false;
      input.setAttribute('aria-expanded', 'true');
    });
  }

  function close() {
    panel.hidden = true;
    panel.innerHTML = '';
    active = -1;
    input.setAttribute('aria-expanded', 'false');
    input.removeAttribute('aria-activedescendant');
  }

  function move(d) {
    var links = panel.querySelectorAll('a');
    if (!links.length) return;
    if (active >= 0) links[active].classList.remove('active');
    active = (active + d + links.length) % links.length;
    links[active].classList.add('active');
    links[active].scrollIntoView({ block: 'nearest' });
    input.setAttribute('aria-activedescendant', links[active].id);
  }

  var timer;
  input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(render, 120); });
  input.addEventListener('focus', function () { load(); if (input.value.trim()) render(); });
  input.addEventListener('keydown', function (ev) {
    if (ev.key === 'ArrowDown') { ev.preventDefault(); move(1); }
    else if (ev.key === 'ArrowUp') { ev.preventDefault(); move(-1); }
    else if (ev.key === 'Enter') {
      var links = panel.querySelectorAll('a');
      if (links.length) { ev.preventDefault(); links[Math.max(active, 0)].click(); }
    } else if (ev.key === 'Escape') { close(); input.blur(); }
  });
  // 同じページ内の節へ飛んだときも結果とスマホのメニューを閉じる
  panel.addEventListener('click', function (ev) {
    if (ev.target.closest('a')) { close(); document.body.classList.remove('nav-open'); }
  });
  document.addEventListener('click', function (ev) { if (!box.contains(ev.target)) close(); });
  // 「/」で検索欄へ（入力欄で打っているときは除く）
  document.addEventListener('keydown', function (ev) {
    if (ev.key !== '/' || ev.ctrlKey || ev.metaKey || ev.altKey) return;
    var t = ev.target.tagName;
    if (t === 'INPUT' || t === 'TEXTAREA' || t === 'SELECT' || ev.target.isContentEditable) return;
    ev.preventDefault();
    input.focus();
  });
})();
