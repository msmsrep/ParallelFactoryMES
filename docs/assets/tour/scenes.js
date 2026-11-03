// 工場模型の配置データ（docs-dev/FactoryTour.md「工場の配置」）。処理は tour.js に書く
// 座標は床の上の単位。x は配置図の左から右、y は配置図の上から下へ増える
window.TOUR_DATA = {
  // 工場の床（建屋の範囲）
  floor: { x: 0, y: -2.5, w: 26.5, d: 20 },

  // 床に引く区画。code は場面データの station・actors・moves から指す名前
  // label はラベルの置き方（既定は奥の角の真上。center は区画の中央で、name がなければコードだけ）
  zones: [
    { code: 'OFFICE', name: '事務所', x: 9, y: -2, w: 5, d: 3.5, kind: 'office', spot: [13.2, 0.4] },
    { code: 'MES', name: 'MES サーバー', x: 15, y: -2, w: 3, d: 3.5, kind: 'server' },

    { code: 'YARD-IN', name: '受入ヤード', x: 0, y: 3.5, w: 4, d: 5, kind: 'yard', spot: [2.3, 8] },
    { code: 'WH-M01', name: '部材倉庫', x: 5, y: 2.5, w: 3.5, d: 3.3, kind: 'warehouse', spot: [5.7, 5.3] },
    { code: 'WH-M02', name: '部材倉庫', x: 5, y: 6.2, w: 3.5, d: 3.3, kind: 'warehouse', spot: [5.7, 9] },
    { code: 'WC-MC1', name: 'マシニング加工', x: 10, y: 3.5, w: 9.5, d: 5, kind: 'workcenter', spot: [10.5, 7.2] },
    { code: 'WIP-01', name: '', x: 16.6, y: 4, w: 2.4, d: 2.4, kind: 'wip', parent: 'WC-MC1', label: 'center' },
    { code: 'DOCK', name: '搬出口', x: 21.5, y: 3.5, w: 4.5, d: 5, kind: 'yard', spot: [22.2, 7.8] },
    { code: 'OUTSIDE', name: '外注先（工場の外）', x: 29, y: 3.5, w: 4.5, d: 5, kind: 'outside' },

    { code: 'SHIP-01', name: '出荷ヤード', x: 0, y: 12.5, w: 4, d: 4.5, kind: 'yard', spot: [3.3, 16.3] },
    { code: 'WH-P01', name: '製品倉庫', x: 5, y: 12.5, w: 3.5, d: 4.5, kind: 'warehouse', spot: [5.7, 16.4] },
    { code: 'WC-PK1', name: '梱包', x: 9.5, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [12.8, 15.6] },
    { code: 'WC-QC1', name: '最終検査', x: 14, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [17.3, 15.6] },
    { code: 'WC-AS1', name: 'ポンプ組立', x: 18.5, y: 12.5, w: 3.5, d: 4.5, kind: 'workcenter', spot: [21.8, 15.6] },
    { code: 'WIP-02', name: '仕掛置場', x: 23, y: 12.5, w: 3, d: 4.5, kind: 'wip' }
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
  ]
};
