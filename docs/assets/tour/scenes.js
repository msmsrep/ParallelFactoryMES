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
    { code: 'WIP-01', name: '', x: 16.6, y: 4, w: 2.4, d: 2.4, kind: 'wip', parent: 'WC-MC1', label: 'center' },
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
  orders: ['SMP-SF-001-10', 'SMP-SF-001-20', 'SMP-FG-001-10', 'SMP-FG-001-20', 'SMP-FG-001-30'],

  // 場面。各場面は差分だけを持ち、tour.js が1場面目から足し直して表示する（docs-dev/FactoryTour.md「場面データの形」）
  // moves の via は、足し直しに関わらない見た目の起点・終点（from なしの生成がどこから来るか、to なしの消滅がどこへ行くか）
  // alerts の labelAt は異常のラベルを置く床の座標（既定は区画の手前の角）
  // around は moves に出ない前後の工程の持ち場（部分表示でラベルを出す）
  // note は説明欄の「見どころ」、ledgerNote は台帳に添える数字の見方。equipment は第1拍で動かす設備
  // stub は未実装の場面。足し直しの対象にするが、場面の列と前後の移動では飛ばす
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
    { id: 'receiving-inspection', no: 2, title: '受入検査で鋳造品が合格', stub: true, moves: [], effects: [] },
    {
      id: 'order',
      no: 3,
      title: '製造指図から作業指示5件を作成',
      stub: true,
      moves: [],
      effects: [
        { wo: 'SMP-SF-001-10', status: '未配布' },
        { wo: 'SMP-SF-001-20', status: '未配布' },
        { wo: 'SMP-FG-001-10', status: '未配布' },
        { wo: 'SMP-FG-001-20', status: '未配布' },
        { wo: 'SMP-FG-001-30', status: '未配布' }
      ]
    },
    { id: 'dispatch', no: 4, title: '差立（画面で行う操作）', stub: true, moves: [], effects: [] },
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
    { id: 'in-process-inspection', no: 6, title: '工程内検査で合格', stub: true, moves: [], effects: [] },
    {
      id: 'outsourcing',
      no: 7,
      title: '表面処理で 19 個が WIP-02 へ',
      stub: true,
      moves: [
        { item: 'machined', qty: 19, from: 'WC-MC1', to: 'WIP-02' },
        { item: 'machined', qty: 1, from: 'WC-MC1' }
      ],
      effects: [{ lot: 'SF2000-S001', delta: 19 }, { wo: 'SMP-SF-001-20', status: '完了' }]
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
      stub: true,
      moves: [{ item: 'assembled', qty: 10, from: 'WC-AS1', to: 'WC-QC1' }],
      effects: [{ wo: 'SMP-FG-001-20', status: '完了' }]
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
    { id: 'completion-approval', no: 11, title: '完了承認（画面で行う操作）', stub: true, moves: [], effects: [] },
    { id: 'final-inspection', no: 12, title: '完成品検査で FG1000-S001 が合格', stub: true, moves: [], effects: [] },
    { id: 'shipment-judgment', no: 13, title: '出荷指示を作り、出荷判定「可」を承認', stub: true, moves: [], effects: [] },
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
    { id: 'trace', no: 15, title: 'トレース', stub: true, moves: [], effects: [] }
  ]
};
