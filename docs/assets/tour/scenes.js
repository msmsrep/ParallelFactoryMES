// 工場模型の配置データ（docs-dev/FactoryTour.md「工場の配置」）。処理は tour.js に書く
// 座標は床の上の単位。x は配置図の左から右、y は配置図の上から下へ増える
window.TOUR_DATA = {
  // 工場の床（建屋の範囲）
  floor: { x: 0, y: -2.5, w: 26.5, d: 20 },

  // 床に引く区画。code は場面データの station・actors・moves から指す名前
  // label はラベルの置き方（既定は奥の角の真上。center は区画の中央で、name がなければコードだけ）
  // labelAt はラベルを置く床の座標（隣の物と重なる区画だけ）、labelNarrow は狭い画面でラベルを置く床の座標（寄せると手前の物のラベルと重なる区画だけ）
  // slots は物を置く位置。置く順に使う。3つ目の値は高さ（トラックの荷台の上など）
  zones: [
    { code: 'OFFICE', name: '事務所', x: 9, y: -2, w: 5, d: 3.5, kind: 'office', spot: [13.2, 0.4] },
    { code: 'MES', name: 'MES サーバー', x: 15, y: -2, w: 3, d: 3.5, kind: 'server' },

    { code: 'YARD-IN', name: '受入ヤード', x: 0, y: 3.5, w: 4, d: 5, kind: 'yard', spot: [2.3, 8] },
    { code: 'WH-M01', name: '部材倉庫', x: 5, y: 2.5, w: 3.5, d: 3.3, kind: 'warehouse', spot: [5.7, 5.3], slots: [[6.6, 3], [5.9, 3.7], [6.6, 4.6]] },
    { code: 'WH-M02', name: '部材倉庫', x: 5, y: 6.2, w: 3.5, d: 3.3, kind: 'warehouse', spot: [5.7, 9], labelAt: [4.3, 6.2], slots: [[6.6, 6.7], [5.6, 7.7], [6.6, 8.2], [5.6, 9.2]] },
    { code: 'WC-MC1', name: 'マシニング加工', x: 10, y: 3.5, w: 9.5, d: 5, kind: 'workcenter', spot: [10.5, 7.2], labelNarrow: [14, 3.5], slots: [[18.6, 7.6]] },
    { code: 'WIP-01', name: '', x: 16.6, y: 4, w: 2.4, d: 2.4, kind: 'wip', parent: 'WC-MC1', label: 'center', spot: [16.4, 7.4] },
    { code: 'DOCK', name: '搬出口', x: 21.5, y: 3.5, w: 4.5, d: 5, kind: 'yard', spot: [22.2, 7.8] },
    { code: 'OUTSIDE', name: '外注先（工場の外）', x: 29, y: 3.5, w: 4.5, d: 5, kind: 'outside' },

    { code: 'SHIP-01', name: '出荷ヤード', x: 0, y: 12.5, w: 4, d: 4.5, kind: 'yard', spot: [3.3, 16.3], slots: [[1.8, 14.6, 1.65]] },
    { code: 'WH-P01', name: '製品倉庫', x: 5, y: 12.5, w: 3.5, d: 4.5, kind: 'warehouse', spot: [5.7, 16.4], slots: [[6.2, 14.2]] },
    { code: 'WC-PK1', name: '梱包', x: 9.5, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [12.8, 15.6], slots: [[10, 14.3]] },
    { code: 'WC-QC1', name: '最終検査', x: 14, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [17.3, 15.6], slots: [[14.6, 14.2]] },
    { code: 'WC-AS1', name: 'ポンプ組立', x: 18.5, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [21.8, 15.6], slots: [[20.2, 13]] },
    { code: 'WIP-02', name: '仕掛置場', x: 23, y: 12.5, w: 3, d: 4.5, kind: 'wip', labelNarrow: [26, 12.5], slots: [[24.3, 15.5]] }
  ],

  // 設備。shape は tour.js の形の名前。label: 'left' はコードを手前の角でなく左の角に出す
  equipments: [
    { code: 'EQ-01', zone: 'WC-MC1', x: 11, y: 4.8, shape: 'machining' },
    { code: 'EQ-05', zone: 'WC-MC1', x: 13.6, y: 4.8, shape: 'machining' },
    { code: 'EQ-02', zone: 'WC-AS1', x: 19.9, y: 13.6, shape: 'bench', label: 'left' },
    { code: 'EQ-03', zone: 'WC-QC1', x: 15.4, y: 13.6, shape: 'gantry', label: 'left' },
    { code: 'EQ-04', zone: 'WC-PK1', x: 10.9, y: 13.4, shape: 'packer', label: 'left' }
  ],

  // 什器。設備マスタには載らない見た目だけのもの
  fixtures: [
    { zone: 'WH-M01', x: 7.3, y: 2.9, shape: 'rack' },
    { zone: 'WH-M02', x: 7.3, y: 6.6, shape: 'rack' },
    { zone: 'WH-P01', x: 7.3, y: 13, shape: 'rack' },
    { zone: 'OFFICE', x: 10.2, y: -1.4, shape: 'desk' },
    { zone: 'MES', x: 15.8, y: -1.4, shape: 'server' },
    { zone: 'OUTSIDE', x: 29.8, y: 4.1, shape: 'plant' }
  ],

  // トラック
  trucks: [
    { zone: 'YARD-IN', x: 0.4, y: 4.6, shape: 'truck' },
    { zone: 'DOCK', x: 22.6, y: 4.2, shape: 'truck' },
    { zone: 'SHIP-01', x: 0.4, y: 13.2, shape: 'truck' }
  ],

  // 人の持ち場。場面データの actors が別の持ち場を指すと、そこへ移す
  people: [
    { user: 'pm001', role: 'pm', at: 'OFFICE' },
    { user: 'lg001', role: 'lg', at: 'YARD-IN' },
    { user: 'op003', role: 'op', at: 'WC-MC1' },
    { user: 'op001', role: 'op', at: 'WC-MC1', x: 13, y: 8.3 },
    { user: 'op002', role: 'op', at: 'WC-AS1' },
    { user: 'qc001', role: 'qc', at: 'WC-QC1' }
  ],

  // 床に引く物の流れ。material は部材の供給で、本流と線種を変える
  flows: [
    { path: [[4, 6], [5, 6]] },
    { path: [[8.5, 6], [10, 6]] },
    { path: [[19.5, 6], [21.5, 6]] },
    { path: [[26, 6], [29, 6]] },
    { path: [[31.2, 8.5], [31.2, 14.7], [26, 14.7]] },
    { path: [[23, 14.7], [22, 14.7]] },
    { path: [[18.5, 14.7], [17.5, 14.7]] },
    { path: [[14, 14.7], [13, 14.7]] },
    { path: [[9.5, 14.7], [8.5, 14.7]] },
    { path: [[5, 14.7], [4, 14.7]] },
    { path: [[6.7, 9.5], [6.7, 10.9], [20.2, 10.9], [20.2, 12.5]], material: true, label: '部材' }
  ],

  // 模型の物。kind で色を変える（material 部材／semi 半製品／product 製品／rework 再作業待ち）
  items: {
    casting: { name: 'ハウジング鋳造品', kind: 'material' },
    gear: { name: 'ギアセット', kind: 'material' },
    shaft: { name: 'シャフト', kind: 'material' },
    oring: { name: 'Oリング', kind: 'material' },
    oringOil: { name: 'Oリング（耐油）', kind: 'material' },
    bolt: { name: '六角ボルト', kind: 'material' },
    oil: { name: '潤滑油', kind: 'material' },
    machined: { name: 'ハウジング加工品', kind: 'semi' },
    assembled: { name: '組立済みのポンプ', kind: 'semi' },
    product: { name: 'ギアポンプ', kind: 'product' },
    rework: { name: '再作業待ち', kind: 'rework' }
  },

  // 台帳の行。samples/actual-csv の 01〜15 に出る、物語に関係するロットと作業指示だけ
  lots: [
    { lot: 'R3001-260901', name: 'ハウジング鋳造品', at: 'WH-M01', unit: '個' },
    { lot: 'R3002-260901', name: 'ギアセット', at: 'WH-M01', unit: 'セット' },
    { lot: 'R3003-260901', name: 'シャフト', at: 'WH-M01', unit: '本' },
    { lot: 'R3004-260901', name: 'Oリング', at: 'WH-M02', unit: '個' },
    { lot: 'R3007-260901', name: 'Oリング（耐油）', at: 'WH-M02', unit: '個' },
    { lot: 'R3005-260901', name: '六角ボルト M6x20', at: 'WH-M02', unit: '本' },
    { lot: 'R3006-260901', name: '潤滑油', at: 'WH-M02', unit: 'L' },
    { lot: 'SF2000-S001', name: 'ハウジング加工品', at: 'WIP-02', unit: '個' },
    { lot: 'FG1000-S001', name: 'ギアポンプ組立品', at: 'WH-P01', unit: '個' }
  ],
  manufacturingOrders: ['SMP-SF-001', 'SMP-FG-001'],
  orders: ['SMP-SF-001-10', 'SMP-SF-001-20', 'SMP-FG-001-10', 'SMP-FG-001-20', 'SMP-FG-001-30'],

  // 場面。各場面は差分だけを持ち、tour.js が1場面目から足し直して表示する（docs-dev/FactoryTour.md「場面データの形」）
  // moves の via は、足し直しに関わらない見た目の起点・終点（from なしの生成がどこから来るか、to なしの消滅がどこへ行くか）
  // alerts の labelAt は異常のラベルを置く床の座標（既定は区画の手前の角）
  // around は moves に出ない前後の工程の持ち場（部分表示でラベルを出す）
  // note は説明欄の「見どころ」、ledgerNote は台帳に添える数字の見方。equipment は第1拍で動かす設備
  // moves の route は、途中で通る持ち場（外注先への往復など）。papers は作業指示の紙が事務所から飛ぶ先の持ち場
  // effects の { lot, status, from } はロットの状態の変化。from は場面の中で一度なった状態（検査待ちなど）で、足し直しには使わない
  // changeNotes は台帳の行が変わらない記録の変化。gauges は説明欄に出す測定値と規格の帯
  // trace は模型の上でたどるロットのつながり（back／forward を説明欄で切り替える）。labelAt はラベルを置く床の座標（既定は弧の中央）
  scenes: [
    {
      id: 'receiving',
      no: 1,
      title: '部材が届く',
      station: 'YARD-IN',
      around: ['WC-MC1'],
      actors: [{ user: 'lg001', role: 'lg', at: 'YARD-IN' }],
      moves: [
        { item: 'casting', qty: 50, via: 'YARD-IN', to: 'WH-M01' },
        { item: 'gear', qty: 50, via: 'YARD-IN', to: 'WH-M01' },
        { item: 'shaft', qty: 50, via: 'YARD-IN', to: 'WH-M01' },
        { item: 'oring', qty: 200, via: 'YARD-IN', to: 'WH-M02' },
        { item: 'oringOil', qty: 40, via: 'YARD-IN', to: 'WH-M02' },
        { item: 'bolt', qty: 500, via: 'YARD-IN', to: 'WH-M02' },
        { item: 'oil', qty: 20, via: 'YARD-IN', to: 'WH-M02' }
      ],
      floor: 'トラックが7種類の部材を届け、WH-M01・WH-M02 へ入れます。',
      input: [{ who: 'lg001', menu: '物流・在庫 → 受入', record: '品目・数量・置場・ロット番号・有効期限' }],
      effects: [
        { lot: 'R3001-260901', delta: 50 },
        { lot: 'R3002-260901', delta: 50 },
        { lot: 'R3003-260901', delta: 50 },
        { lot: 'R3004-260901', delta: 200 },
        { lot: 'R3007-260901', delta: 40 },
        { lot: 'R3005-260901', delta: 500 },
        { lot: 'R3006-260901', delta: 20 }
      ],
      note: 'ロット番号がここで生まれ、以降の在庫・投入・追跡をすべてつなぎます。Oリング・潤滑油には有効期限があります。',
      ledgerNote: '受入で、ロットごとの在庫ができる。',
      detail: { href: 'walkthrough.html#receiving', image: null }
    },
    {
      id: 'receiving-inspection',
      no: 2,
      title: '受入検査で鋳造品が合格',
      station: 'WH-M01',
      around: ['YARD-IN', 'WC-MC1'],
      actors: [{ user: 'qc001', role: 'qc', at: 'WH-M01' }],
      floor: 'qc001 が鋳造品 R3001-260901 の外観を目視で確かめ、巣や欠けがないことを確認します。',
      input: [{ who: 'qc001', menu: '品質 → 検査管理', record: '検査指示の発行・INS-03 外観「巣・欠けなし」・総合判定' }],
      effects: [{ lot: 'R3001-260901', status: '正常', from: '検査待ち' }],
      note: '検査指示を発行するとロットは「検査待ち」になり、工程へ払い出せません。合格すると「正常」に戻ります。',
      ledgerNote: '数量は変わらず、ロットの状態だけが動く。',
      detail: {
        href: 'walkthrough.html#inspection',
        image: 'assets/walkthrough/inspections.webp',
        alt: '検査管理画面。受入・工程内・完成品の3件の検査指示が、承認済・合格で並んでいる'
      }
    },
    {
      id: 'order',
      no: 3,
      title: '製造指図から作業指示へ',
      station: 'OFFICE',
      around: ['WC-MC1', 'DOCK', 'WC-AS1', 'WC-QC1', 'WC-PK1'],
      actors: [{ user: 'pm001', role: 'pm', at: 'OFFICE' }],
      papers: ['WC-MC1', 'DOCK', 'WC-AS1', 'WC-QC1', 'WC-PK1'],
      floor: 'pm001 が SMP-SF-001（20個）と SMP-FG-001（10台）を登録・承認し、工程展開します。工程ごとの作業指示が現場へ届きます。',
      input: [{ who: 'pm001', menu: '生産・製造実行 → 製造指図', record: '品目・数量・納期の登録、承認、工程展開' }],
      effects: [
        { mo: 'SMP-SF-001', status: '展開済' },
        { mo: 'SMP-FG-001', status: '展開済' },
        { wo: 'SMP-SF-001-10', status: '未配布' },
        { wo: 'SMP-SF-001-20', status: '未配布' },
        { wo: 'SMP-FG-001-10', status: '未配布' },
        { wo: 'SMP-FG-001-20', status: '未配布' },
        { wo: 'SMP-FG-001-30', status: '未配布' }
      ],
      note: '工順に沿って、工程ごとの作業指示ができます。産出ロット（SF2000-S001・FG1000-S001）と予定材料もここで決まります。',
      ledgerNote: '指図と作業指示の状態がここから動く。在庫はまだ変わらない。',
      detail: {
        href: 'walkthrough.html#order',
        image: 'assets/walkthrough/order-detail.webp',
        alt: '製造指図 SMP-FG-001 の詳細画面。予定材料7品目と、工程展開でできた作業指示3件が表示されている'
      }
    },
    {
      id: 'dispatch',
      no: 4,
      title: '差立（画面で行う操作）',
      station: 'OFFICE',
      around: ['WC-AS1'],
      actors: [{ user: 'pm001', role: 'pm', at: 'OFFICE' }],
      papers: ['WC-AS1'],
      floor: 'pm001 が作業指示ごとに、作業者・設備・着手順を割り当てます。ここでは例として、SMP-FG-002 を画面で工程展開し、-10 を op002・EQ-02 へ割り当てます。',
      input: [
        { who: 'pm001', menu: '生産・製造実行 → 製造指図', record: 'SMP-FG-002 の工程展開' },
        { who: 'pm001', menu: '生産・製造実行 → 差立', record: 'SMP-FG-002-10 の作業者 op002・設備 EQ-02・着手順' }
      ],
      changeNotes: ['例の SMP-FG-002-10 が配布済になる（台帳の行は変わらない）'],
      note: 'サンプルの CSV は差立を取り込まないので、画面で試せる操作です。SMP-FG-001 は差立を省いて、未配布のまま着手できます。作業者のスキルも照合するので、SK-04（組立作業）を持たない人や資格の期限が切れた人は割り当てられません。',
      ledgerNote: '台帳は変わらない。このあとの物語は未配布のまま着手して進む。',
      detail: {
        href: 'walkthrough.html#order',
        image: 'assets/walkthrough/dispatch.webp',
        alt: '差立画面。SMP-FG-002 の作業指示2件が未配布で並び、作業者・設備・着手順を選べる'
      }
    },
    {
      id: 'machining',
      no: 5,
      title: '夜勤で機械加工',
      when: '9/1 21:00〜9/2 3:00（夜勤 N）',
      station: 'WC-MC1',
      around: ['DOCK'],
      equipment: 'EQ-01',
      actors: [{ user: 'op003', role: 'op', at: 'WC-MC1' }],
      moves: [{ item: 'casting', qty: 20, from: 'WH-M01', to: 'WC-MC1', as: 'machined' }],
      floor: 'op003 が EQ-01 で鋳造品20個を削ります。',
      input: [{ who: 'op003', menu: '生産・製造実行 → 作業指示 → 実績入力', record: '投入ロット・良品数・製造条件' }],
      effects: [{ lot: 'R3001-260901', delta: -20 }, { wo: 'SMP-SF-001-10', status: '完了' }],
      alerts: [
        { at: 'WC-MC1', tag: '逸脱', labelAt: [19.5, 5.5], text: '逸脱：送り速度 1350 が許容範囲 1100〜1300 の外' },
        { at: 'WC-MC1', tag: '停止', text: '停止：0:40〜1:10 に EQ-01 が主軸ベルトの滑りで停止（設備稼働の記録。台帳の数字は変わらない）' }
      ],
      note: '良品20個は現場にありますが、最終工程ではないので在庫には増えません。',
      ledgerNote: '部材は作業指示への投入で減る。在庫に入るのは最終工程の良品だけ。',
      detail: { href: 'walkthrough.html#execution', image: null }
    },
    {
      id: 'in-process-inspection',
      no: 6,
      title: '工程内検査で合格',
      station: 'WC-MC1',
      around: ['DOCK'],
      actors: [{ user: 'qc001', role: 'qc', at: 'WIP-01' }],
      floor: 'qc001 がノギスでハウジングの内径を2個測り、外観にキズがないことを確かめます。',
      input: [{ who: 'qc001', menu: '品質 → 検査管理', record: '工程内検査（SMP-SF-001-10）・INS-01 内径・INS-02 外観「キズなし」・総合判定' }],
      gauges: [{ name: 'INS-01 ハウジング内径', min: 49.95, max: 50.05, unit: 'mm', values: [50.01, 49.98] }],
      changeNotes: ['SMP-SF-001-10 の工程内検査が合格で確定する（台帳の行は変わらない）'],
      note: '測定値を入れると、規格と照合して合否が自動で決まります。',
      ledgerNote: '工程内検査は作業指示が対象なので、ロットの状態は動かない。',
      detail: {
        href: 'walkthrough.html#inspection',
        image: 'assets/walkthrough/inspections.webp',
        alt: '検査管理画面。受入・工程内・完成品の3件の検査指示が、承認済・合格で並んでいる'
      }
    },
    {
      id: 'outsourcing',
      no: 7,
      title: '外注で表面処理',
      when: '9/2 8:00〜12:00（昼勤 D）',
      station: 'DOCK',
      actors: [{ user: 'op001', role: 'op', at: 'DOCK' }],
      moves: [
        { item: 'machined', qty: 19, from: 'WC-MC1', to: 'WIP-02', route: ['DOCK', 'OUTSIDE'] },
        { item: 'machined', qty: 1, from: 'WC-MC1', via: 'OUTSIDE', route: ['DOCK'] }
      ],
      floor: 'トラックで加工品20個を外注先へ運びます。表面処理から戻った19個は WIP-02 へ入り、op001 が戻りを確かめて実績を入力します。',
      input: [{ who: 'op001', menu: '生産・製造実行 → 作業指示 → 実績入力', record: '良品数・不良と不良内容・入庫先 WIP-02' }],
      effects: [{ lot: 'SF2000-S001', delta: 19 }, { wo: 'SMP-SF-001-20', status: '完了' }],
      alerts: [{ at: 'DOCK', tag: '不良', labelAt: [24.5, 8.8], text: '不良：1個（DR-01 鋳巣）を廃棄。WIP-02 へは入らない' }],
      note: '表面処理は SF-2000 の最終工程なので、良品19個がここで SF2000-S001 として在庫に入ります。',
      ledgerNote: '最終工程の良品だけが在庫に入る。不良の1個は入らない。',
      detail: { href: 'walkthrough.html#execution', image: null }
    },
    {
      id: 'assembly',
      no: 8,
      title: 'ギアポンプを組み立てる',
      when: '9/3 8:30〜11:30（昼勤 D）',
      station: 'WC-AS1',
      around: ['WC-QC1'],
      equipment: 'EQ-02',
      actors: [{ user: 'op002', role: 'op', at: 'WC-AS1' }],
      moves: [
        { item: 'gear', qty: 10, from: 'WH-M01', via: 'WC-AS1' },
        { item: 'shaft', qty: 10, from: 'WH-M01', via: 'WC-AS1' },
        { item: 'oring', qty: 18, from: 'WH-M02', via: 'WC-AS1' },
        { item: 'oringOil', qty: 2, from: 'WH-M02', via: 'WC-AS1' },
        { item: 'bolt', qty: 40, from: 'WH-M02', via: 'WC-AS1' },
        { item: 'oil', qty: 0.5, from: 'WH-M02', via: 'WC-AS1' },
        { item: 'machined', qty: 10, from: 'WIP-02', to: 'WC-AS1', as: 'assembled' }
      ],
      floor: 'op002 が EQ-02 で、ハウジングと部品を組みます。',
      input: [{ who: 'op002', menu: '生産・製造実行 → 作業指示 → 実績入力', record: '投入ロット・代替理由・良品数・締付トルク' }],
      effects: [
        { lot: 'SF2000-S001', delta: -10 },
        { lot: 'R3002-260901', delta: -10 },
        { lot: 'R3003-260901', delta: -10 },
        { lot: 'R3004-260901', delta: -18 },
        { lot: 'R3007-260901', delta: -2 },
        { lot: 'R3005-260901', delta: -40 },
        { lot: 'R3006-260901', delta: -0.5 },
        { wo: 'SMP-FG-001-10', status: '完了' }
      ],
      note: 'Oリングの一部を耐油品で代替し、理由が残ります。予定にない品目は投入できません。',
      ledgerNote: '半製品も部材と同じく、投入で減る。',
      detail: {
        href: 'walkthrough.html#execution',
        image: 'assets/walkthrough/record.webp',
        alt: 'SMP-FG-001-10 の実績入力画面。代替理由つきの Oリングを含む7件の部材投入が並ぶ'
      }
    },
    {
      id: 'final-test',
      no: 9,
      title: '最終検査の工程で 10 台を確認',
      when: '9/3 12:30〜13:00（昼勤 D）',
      station: 'WC-QC1',
      around: ['WC-AS1', 'WC-PK1'],
      equipment: 'EQ-03',
      actors: [{ user: 'op001', role: 'op', at: 'WC-QC1' }],
      moves: [{ item: 'assembled', qty: 10, from: 'WC-AS1', to: 'WC-QC1' }],
      floor: 'op001 が EQ-03 で10台を試験します。試験圧力は 0.5 MPa で、吐出音に異音はありません。',
      input: [{ who: 'op001', menu: '生産・製造実行 → 作業指示 → 実績入力', record: '良品数・試験圧力・吐出音' }],
      effects: [{ wo: 'SMP-FG-001-20', status: '完了' }],
      note: '作業指示の工程なので、qc001 ではなく SK-03 を持つ現場作業者が入力します。ロットを検査する完成品検査（場面12）とは別物です。',
      ledgerNote: '途中の工程の良品は、在庫に増えない。',
      detail: { href: 'walkthrough.html#execution', image: null }
    },
    {
      id: 'packing',
      no: 10,
      title: '梱包して製品倉庫へ',
      when: '9/3 13:30〜14:00（昼勤 D）',
      station: 'WC-PK1',
      equipment: 'EQ-04',
      actors: [{ user: 'op002', role: 'op', at: 'WC-PK1' }],
      moves: [
        { item: 'assembled', qty: 9, from: 'WC-QC1', to: 'WH-P01', as: 'product' },
        { item: 'assembled', qty: 1, from: 'WC-QC1', to: 'WC-PK1', as: 'rework' }
      ],
      floor: 'op002 が EQ-04 で梱包し、9台を WH-P01 へ運びます。',
      input: [{ who: 'op002', menu: '生産・製造実行 → 作業指示 → 実績入力', record: '良品数・不良と不良内容・入庫先' }],
      effects: [{ lot: 'FG1000-S001', delta: 9 }, { wo: 'SMP-FG-001-30', status: '完了' }],
      alerts: [
        { at: 'WC-PK1', tag: '不良', text: '不良：1台（DR-05・再作業待ち）' },
        { at: 'WC-PK1', tag: '停止', text: '停止：14:00〜14:20 に EQ-04 がシールヒーター温度上昇待ちで停止（設備稼働の記録。台帳の数字は変わらない）' }
      ],
      note: 'ここで初めて製品が在庫に入ります。',
      ledgerNote: '最終工程の良品だけが在庫に入る。不良の1台は入らない。',
      detail: { href: 'walkthrough.html#execution', image: null }
    },
    {
      id: 'completion-approval',
      no: 11,
      title: '完了承認（画面で行う操作）',
      station: 'OFFICE',
      actors: [{ user: 'pm001', role: 'pm', at: 'OFFICE' }],
      floor: 'pm001 が実績を確かめ、完了した作業指示を1件ずつ承認します。',
      input: [{ who: 'pm001', menu: '生産・製造実行 → 作業指示 → 実績入力（製造完了承認）', record: '作業指示ごとの承認' }],
      effects: [
        { wo: 'SMP-SF-001-10', status: '承認済' },
        { wo: 'SMP-SF-001-20', status: '承認済' },
        { mo: 'SMP-SF-001', status: '完了' },
        { wo: 'SMP-FG-001-10', status: '承認済' },
        { wo: 'SMP-FG-001-20', status: '承認済' },
        { wo: 'SMP-FG-001-30', status: '承認済' },
        { mo: 'SMP-FG-001', status: '完了' }
      ],
      note: 'サンプルの CSV には入らない、画面で行う操作です。全工程を承認すると、製造指図が完了になります。',
      ledgerNote: 'CSV の取込では作業指示は完了、指図は展開済のまま。この先の状態欄は、画面で承認した場合の値。',
      detail: { href: 'walkthrough.html#order', image: null }
    },
    {
      id: 'final-inspection',
      no: 12,
      title: '完成品検査で FG1000-S001 が合格',
      station: 'WH-P01',
      around: ['WC-PK1'],
      actors: [{ user: 'qc001', role: 'qc', at: 'WH-P01' }],
      floor: 'qc001 が FG1000-S001 の吐出量を流量計で測り、気密試験で漏れがないことを確かめます。',
      input: [{ who: 'qc001', menu: '品質 → 検査管理', record: '検査指示の発行・INS-04 吐出量・INS-05 気密試験「漏れなし」・総合判定（グレード A）' }],
      gauges: [{ name: 'INS-04 吐出量', min: 19, max: 21, unit: 'L/min', values: [20.3] }],
      effects: [{ lot: 'FG1000-S001', status: '正常', from: '検査待ち' }],
      note: '検査待ちの間、このロットは出荷できません。合格・グレード A で「正常」に戻ります。',
      ledgerNote: '数量は変わらず、ロットの状態だけが動く。',
      detail: {
        href: 'walkthrough.html#inspection',
        image: 'assets/walkthrough/inspections.webp',
        alt: '検査管理画面。受入・工程内・完成品の3件の検査指示が、承認済・合格で並んでいる'
      }
    },
    {
      id: 'shipment-judgment',
      no: 13,
      title: '出荷指示と出荷判定',
      when: '10/16 出荷予定',
      station: 'SHIP-01',
      around: ['WH-P01'],
      actors: [{ user: 'lg001', role: 'lg', at: 'SHIP-01' }, { user: 'qc001', role: 'qc', at: 'WC-QC1' }],
      floor: 'lg001 がサンプル商事へ5台の出荷指示 SMP-SH-001 を作り、qc001 が FG1000-S001 に判定「可」を出して承認します。',
      input: [
        { who: 'lg001', menu: '物流・在庫 → 出荷管理', record: '出荷先・予定日・品目・数量' },
        { who: 'qc001', menu: '品質 → 出荷判定', record: '対象の出荷指示とロット、判定「可」、承認' }
      ],
      changeNotes: ['SMP-SH-001 の出荷判定が「可」で承認済になる（台帳の行は変わらない）'],
      note: '比較：別の出荷指示 SMP-SH-002 は判定が「保留」のままで、出荷実行に進めません。',
      ledgerNote: '指示と判定では、在庫は動かない。',
      detail: {
        href: 'walkthrough.html#shipping',
        image: 'assets/walkthrough/shipment-judgments.webp',
        alt: '出荷判定画面。SMP-SH-001 と FG1000-S001 を対象にした判定「可」が承認済、SMP-SH-002 の判定「保留」が未承認で並んでいる'
      }
    },
    {
      id: 'shipment',
      no: 14,
      title: 'サンプル商事へ5台出荷',
      when: '10/16 予定',
      station: 'WH-P01',
      around: ['WC-PK1'],
      actors: [{ user: 'lg001', role: 'lg', at: 'WH-P01' }],
      moves: [{ item: 'product', qty: 5, from: 'WH-P01', to: 'SHIP-01' }],
      floor: 'lg001 が WH-P01 から5台をトラックへ積みます。',
      input: [{ who: 'lg001', menu: '物流・在庫 → 出荷管理', record: '出荷指示 SMP-SH-001 のロットと数量' }],
      effects: [{ lot: 'FG1000-S001', delta: -5 }],
      note: '承認済みの出荷判定が無いと、出荷実行で止まります。',
      ledgerNote: '出荷で製品の在庫が減る。',
      detail: {
        href: 'walkthrough.html#shipping',
        image: 'assets/walkthrough/shipping.webp',
        alt: '出荷管理画面。SMP-SH-001 が完了（指示数5・出荷済5）、SMP-SH-002 が指示のまま'
      }
    },
    {
      id: 'trace',
      no: 15,
      title: 'トレースでロットをたどる',
      station: 'WH-P01',
      camera: 'all',
      around: ['WIP-02', 'WH-M01', 'WH-M02', 'SHIP-01'],
      actors: [{ user: 'qc001', role: 'qc', at: 'WC-QC1' }],
      floor: 'qc001 が FG1000-S001 を追跡すると、使った半製品と部材のロットまで、記録がつながってたどれます。',
      input: [{ who: 'qc001', menu: '品質 → トレーサビリティ', record: '追跡するロット番号' }],
      changeNotes: ['参照するだけなので、記録は変わらない'],
      trace: {
        back: {
          name: '製品から部材へ',
          links: [
            { from: 'WH-P01', to: 'WIP-02', tag: 'SF2000-S001', text: 'FG1000-S001 ← SF2000-S001 10個' },
            { from: 'WIP-02', to: 'WH-M01', tag: 'R3001-260901', text: 'SF2000-S001 ← R3001-260901 20個' },
            { from: 'WH-P01', to: 'WH-M01', tag: 'R3002・R3003', labelAt: [8.9, 9.3], text: 'FG1000-S001 ← R3002-260901 10セット・R3003-260901 10本' },
            { from: 'WH-P01', to: 'WH-M02', tag: 'R3004 ほか3件', labelAt: [5.2, 10.7], text: 'FG1000-S001 ← R3004-260901 18個・R3007-260901 2個・R3005-260901 40本・R3006-260901 0.5 L' }
          ]
        },
        forward: {
          name: '鋳造品から製品・出荷へ',
          links: [
            { from: 'WH-M01', to: 'WIP-02', tag: 'SF2000-S001', text: 'R3001-260901 → SF2000-S001' },
            { from: 'WIP-02', to: 'WH-P01', tag: 'FG1000-S001', text: 'SF2000-S001 → FG1000-S001' },
            { from: 'WH-P01', to: 'SHIP-01', tag: '出荷 5', labelAt: [2.2, 18], text: 'FG1000-S001 → サンプル商事へ5台（SMP-SH-001）' }
          ]
        }
      },
      note: '「鋳造品に鋳巣が多い」と分かったときは、鋳造品から製品・出荷へたどれば、影響する製品と出荷先がすぐに分かります。',
      ledgerNote: 'どの記録もロット番号でつながっている。',
      detail: {
        href: 'walkthrough.html#trace',
        image: 'assets/walkthrough/trace.webp',
        alt: 'トレーサビリティ画面。FG1000-S001 のトレースバックに、SF2000-S001 とその下の R3001-260901、ほか6件の部材ロットがツリーで並んでいる'
      }
    }
  ]
};
