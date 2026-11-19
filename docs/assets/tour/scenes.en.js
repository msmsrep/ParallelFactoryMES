// 工場模型の英語の文言（docs/en/factory-tour.html だけが読む）。scenes.js の後に読み込み、TOUR_DATA の文言を上書きする。
// 配置・数量・場面の並びは scenes.js が正で、ここには文言だけを持つ。場面は id、区画は code、物は名前のキー、ロットはロット番号で引く。
// scenes.js の場面や配列の数が変わってここと合わなくなったら、TOUR_DATA.translationErrors に積む（scripts/GuideEnCheck.ps1 が拾う）。
(function (data) {
  'use strict';
  var errors = data.translationErrors = [];

  function apply(target, source, path) {
    Object.keys(source).forEach(function (key) {
      var value = source[key];
      if (!(key in target)) { errors.push(path + '.' + key + ' が scenes.js に無い'); return; }
      if (Array.isArray(value)) {
        if (!Array.isArray(target[key]) || target[key].length !== value.length) {
          errors.push(path + '.' + key + ' の件数が scenes.js と違う');
          return;
        }
        value.forEach(function (v, i) {
          if (v && typeof v === 'object') apply(target[key][i], v, path + '.' + key + '[' + i + ']');
          else target[key][i] = v;
        });
      } else if (value && typeof value === 'object') {
        apply(target[key], value, path + '.' + key);
      } else {
        target[key] = value;
      }
    });
  }

  // 区画の名前（code で引く）
  var zones = {
    'OFFICE': 'Office', 'MES': 'MES server', 'YARD-IN': 'Receiving yard',
    'WH-M01': 'Materials', 'WH-M02': 'Materials', 'WC-MC1': 'Machining',
    'WIP-01': '', 'DOCK': 'Loading dock', 'OUTSIDE': 'Subcontractor (offsite)',
    'SHIP-01': 'Shipping yard', 'WH-P01': 'Products', 'WC-PK1': 'Packing',
    'WC-QC1': 'Final inspection', 'WC-AS1': 'Pump assembly', 'WIP-02': 'WIP area'
  };
  data.zones.forEach(function (z) {
    if (z.code in zones) z.name = zones[z.code];
    else errors.push('zones.' + z.code + ' の英訳が無い');
  });
  data.flows.forEach(function (f) { if (f.label) f.label = 'Materials'; });

  var items = {
    casting: 'Housing casting', gear: 'Gear set', shaft: 'Shaft', oring: 'O-ring',
    oringOil: 'O-ring (oil-resistant)', bolt: 'Hex bolt', oil: 'Lubricating oil',
    machined: 'Machined housing', assembled: 'Assembled pump', product: 'Gear pump', rework: 'Awaiting rework'
  };
  Object.keys(data.items).forEach(function (key) {
    if (key in items) data.items[key].name = items[key];
    else errors.push('items.' + key + ' の英訳が無い');
  });

  // 台帳のロット（品目名は英語にし、単位は英語の表記にする）
  var lots = {
    'R3001-260901': ['Housing casting', 'pcs'], 'R3002-260901': ['Gear set', 'sets'],
    'R3003-260901': ['Shaft', 'pcs'], 'R3004-260901': ['O-ring', 'pcs'],
    'R3007-260901': ['O-ring (oil-resistant)', 'pcs'], 'R3005-260901': ['Hex bolt M6x20', 'pcs'],
    'R3006-260901': ['Lubricating oil', 'L'], 'SF2000-S001': ['Machined housing', 'pcs'],
    'FG1000-S001': ['Gear pump assembly', 'pcs']
  };
  data.lots.forEach(function (l) {
    if (l.lot in lots) { l.name = lots[l.lot][0]; l.unit = lots[l.lot][1]; }
    else errors.push('lots.' + l.lot + ' の英訳が無い');
  });

  // 状態は画面の英訳（UiText.en.resx・CoreText.en.resx）と同じ語にする
  var statuses = {
    '正常': 'Normal', '検査待ち': 'Awaiting inspection', '展開済': 'Released',
    '未配布': 'Not dispatched', '完了': 'Completed', '承認済': 'Approved'
  };
  data.scenes.forEach(function (scene) {
    (scene.effects || []).forEach(function (e) {
      ['status', 'from'].forEach(function (key) {
        if (!e[key]) return;
        if (e[key] in statuses) e[key] = statuses[e[key]];
        else errors.push(scene.id + ' の状態「' + e[key] + '」の英訳が無い');
      });
    });
  });

  var WT = '../assets/en/walkthrough/';
  var inspectionsAlt = 'Inspections screen listing four approved inspection orders: three passed, and the receiving inspection of R3001-260902 failed';
  var record = 'Production & Execution → Work Orders → Record Results';

  var scenes = {
    'receiving': {
      title: 'Materials arrive',
      floor: 'A truck delivers seven kinds of materials, which go into WH-M01 and WH-M02.',
      input: [{ menu: 'Logistics & Inventory → Receiving', record: 'Product, quantity, location, lot number, expiry date' }],
      note: 'The lot number is born here and links everything that follows: stock, consumption and tracing. The O-rings and lubricating oil have expiry dates.',
      ledgerNote: 'Receiving creates stock for each lot.'
    },
    'receiving-inspection': {
      title: 'The casting passes receiving inspection',
      floor: 'qc001 visually checks the casting R3001-260901 and confirms there are no blowholes or chips.',
      input: [{ menu: 'Quality → Inspections', record: 'Issue the inspection order, INS-03 appearance "no blowholes or chips", overall judgment' }],
      note: 'Issuing an inspection order puts the lot in "Awaiting inspection", and it cannot be issued to a process. When it passes, it returns to "Normal".',
      ledgerNote: 'The quantity stays the same; only the lot status changes.',
      detail: { image: WT + 'inspections.webp', alt: inspectionsAlt }
    },
    'order': {
      title: 'From manufacturing order to work orders',
      floor: 'pm001 registers and approves SMP-SF-001 (20 pcs) and SMP-FG-001 (10 units), then releases them to processes. A work order for each process reaches the shop floor.',
      input: [{ menu: 'Production & Execution → Manufacturing Orders', record: 'Register the product, quantity and due date; approve; release to processes' }],
      note: 'Work orders are created for each process following the routing. The output lots (SF2000-S001, FG1000-S001) and the planned materials are also fixed here.',
      ledgerNote: 'The order and work order statuses start moving here. Stock does not change yet.',
      detail: { image: WT + 'order-detail.webp', alt: 'Detail screen of manufacturing order SMP-FG-001 showing seven planned materials and the three work orders created by the release' }
    },
    'dispatch': {
      title: 'Dispatching (an on-screen operation)',
      floor: 'pm001 assigns an operator, equipment and start sequence to each work order. As an example, SMP-FG-002 is released on screen and its -10 is assigned to op002 and EQ-02.',
      input: [
        { menu: 'Production & Execution → Manufacturing Orders', record: 'Release SMP-FG-002 to processes' },
        { menu: 'Production & Execution → Dispatching', record: 'Operator op002, equipment EQ-02 and start sequence for SMP-FG-002-10' }
      ],
      changeNotes: ['The example SMP-FG-002-10 becomes Dispatched (the ledger rows do not change)'],
      note: 'The sample CSV does not import dispatching, so this is an operation to try on screen. SMP-FG-001 skips dispatching and can be started while still Not dispatched. Operator skills are checked too, so people without SK-04 (assembly work) or whose qualification has expired cannot be assigned.',
      ledgerNote: 'The ledger does not change. The rest of the story goes on by starting work while still Not dispatched.',
      detail: { image: WT + 'dispatch.webp', alt: 'Dispatching screen with the two work orders of SMP-FG-002 listed as Not dispatched, where an operator, equipment and start sequence can be chosen' }
    },
    'machining': {
      title: 'Machining on the night shift',
      when: '9/1 21:00–9/2 3:00 (night shift N)',
      floor: 'op003 machines 20 castings on EQ-01.',
      input: [{ menu: record, record: 'Consumed lots, good quantity, process conditions' }],
      alerts: [
        { tag: 'Deviation', text: 'Deviation: feed rate 1350 is outside the allowed range 1100–1300' },
        { tag: 'Stop', text: 'Stop: EQ-01 stopped from 0:40 to 1:10 because the spindle belt slipped (an equipment operation record; the ledger figures do not change)' }
      ],
      note: 'The 20 good pieces are on the floor, but this is not the last process, so they are not added to stock.',
      ledgerNote: 'Materials decrease when a work order consumes them. Only good output of the last process enters stock.'
    },
    'in-process-inspection': {
      title: 'Passing in-process inspection',
      floor: 'qc001 measures the inner diameter of two housings with calipers and confirms there are no scratches.',
      input: [{ menu: 'Quality → Inspections', record: 'In-process inspection (SMP-SF-001-10), INS-01 inner diameter, INS-02 appearance "no scratches", overall judgment' }],
      gauges: [{ name: 'INS-01 Housing inner diameter' }],
      changeNotes: ['The in-process inspection of SMP-SF-001-10 is confirmed as passed (the ledger rows do not change)'],
      note: 'When you enter the measurements, they are checked against the specification and the result is decided automatically.',
      ledgerNote: 'In-process inspection targets the work order, so the lot status does not move.',
      detail: { image: WT + 'inspections.webp', alt: inspectionsAlt }
    },
    'outsourcing': {
      title: 'Surface treatment at a subcontractor',
      when: '9/2 8:00–12:00 (day shift D)',
      floor: 'A truck takes 20 machined parts to the subcontractor. The 19 that come back from surface treatment go into WIP-02, and op001 checks the return and enters the results.',
      input: [{ menu: record, record: 'Good quantity, defects and defect details, put-away location WIP-02' }],
      alerts: [{ tag: 'Defect', text: 'Defect: 1 piece (DR-01 blowhole) scrapped; it does not go into WIP-02' }],
      note: 'Surface treatment is the last process of SF-2000, so the 19 good pieces enter stock here as SF2000-S001.',
      ledgerNote: 'Only good output of the last process enters stock. The defective piece does not.'
    },
    'assembly': {
      title: 'Assembling the gear pumps',
      when: '9/3 8:30–11:30 (day shift D)',
      floor: 'op002 assembles the housings and parts on EQ-02.',
      input: [{ menu: record, record: 'Consumed lots, substitution reason, good quantity, tightening torque' }],
      note: 'Some O-rings are replaced with oil-resistant ones, and the reason is kept. Items that are not in the plan cannot be consumed.',
      ledgerNote: 'Semi-finished goods decrease when consumed, just like materials.',
      detail: { image: WT + 'record.webp', alt: 'Record Results screen for SMP-FG-001-10 listing seven material consumptions, including O-rings with a substitution reason' }
    },
    'final-test': {
      title: 'Checking 10 units in the final inspection process',
      when: '9/3 12:30–13:00 (day shift D)',
      floor: 'op001 tests the 10 units on EQ-03. The test pressure is 0.5 MPa, and there is no abnormal discharge noise.',
      input: [{ menu: record, record: 'Good quantity, test pressure, discharge noise' }],
      note: 'This is a work order process, so a shop-floor operator with SK-03 enters it, not qc001. It is different from the finished-goods inspection of the lot (scene 12).',
      ledgerNote: 'Good output of an intermediate process does not add to stock.'
    },
    'packing': {
      title: 'Packing, then into the finished-goods warehouse',
      when: '9/3 13:30–14:00 (day shift D)',
      floor: 'op002 packs the units on EQ-04 and carries 9 of them to WH-P01.',
      input: [{ menu: record, record: 'Good quantity, defects and defect details, put-away location' }],
      alerts: [
        { tag: 'Defect', text: 'Defect: 1 unit (DR-05, awaiting rework)' },
        { tag: 'Stop', text: 'Stop: EQ-04 stopped from 14:00 to 14:20 waiting for the seal heater to warm up (an equipment operation record; the ledger figures do not change)' }
      ],
      note: 'This is the first time the product enters stock.',
      ledgerNote: 'Only good output of the last process enters stock. The defective unit does not.'
    },
    'completion-approval': {
      title: 'Completion approval (an on-screen operation)',
      floor: 'pm001 checks the results and approves the completed work orders one by one.',
      input: [{ menu: record + ' (Approve Production Completion)', record: 'Approval of each work order' }],
      note: 'This on-screen operation is not in the sample CSV. Once every process is approved, the manufacturing order becomes Completed.',
      ledgerNote: 'With the CSV import, the work orders stay Completed and the orders stay Released. From here on, the status column shows the values you get after approving on screen.'
    },
    'final-inspection': {
      title: 'FG1000-S001 passes finished-goods inspection',
      floor: 'qc001 measures the discharge flow of FG1000-S001 with a flow meter and confirms there are no leaks in an airtightness test.',
      input: [{ menu: 'Quality → Inspections', record: 'Issue the inspection order, INS-04 discharge flow, INS-05 airtightness test "no leaks", overall judgment (grade A)' }],
      gauges: [{ name: 'INS-04 Discharge flow' }],
      note: 'While it is awaiting inspection, this lot cannot be shipped. Passing with grade A returns it to "Normal".',
      ledgerNote: 'The quantity stays the same; only the lot status changes.',
      detail: { image: WT + 'inspections.webp', alt: inspectionsAlt }
    },
    'shipment-judgment': {
      title: 'Shipping order and shipment judgment',
      when: 'Planned shipment 10/16',
      floor: 'lg001 creates shipping order SMP-SH-001 for 5 units to Sample Trading, and qc001 gives FG1000-S001 an "Approved" judgment and approves it.',
      input: [
        { menu: 'Logistics & Inventory → Shipping', record: 'Ship-to, planned date, product, quantity' },
        { menu: 'Quality → Shipment Judgments', record: 'Target shipping order and lot, judgment "Approved", approval' }
      ],
      changeNotes: ['The shipment judgment of SMP-SH-001 is "Approved" and becomes approved (the ledger rows do not change)'],
      note: 'For comparison: another shipping order, SMP-SH-002, keeps an "On hold" judgment and cannot go on to shipping.',
      ledgerNote: 'Orders and judgments do not move stock.',
      detail: { image: WT + 'shipment-judgments.webp', alt: 'Shipment Judgments screen: the "Approved" judgment for SMP-SH-001 and FG1000-S001 is approved, and the "On hold" judgment for SMP-SH-002 is not yet approved' }
    },
    'shipment': {
      title: 'Shipping 5 units to Sample Trading',
      when: 'Planned 10/16',
      floor: 'lg001 loads 5 units from WH-P01 onto the truck.',
      input: [{ menu: 'Logistics & Inventory → Shipping', record: 'Lot and quantity for shipping order SMP-SH-001' }],
      note: 'Without an approved shipment judgment, shipping stops.',
      ledgerNote: 'Shipping reduces product stock.',
      detail: { image: WT + 'shipping.webp', alt: 'Shipping screen: SMP-SH-001 is completed (5 ordered, 5 shipped) and SMP-SH-002 is still an open order' }
    },
    'trace': {
      title: 'Following the lots with trace',
      floor: 'When qc001 traces FG1000-S001, the records link all the way down to the semi-finished and material lots it used.',
      input: [{ menu: 'Quality → Traceability', record: 'Lot number to trace' }],
      changeNotes: ['It only looks records up, so nothing changes'],
      trace: {
        back: {
          name: 'From product to materials',
          links: [
            { text: 'FG1000-S001 ← SF2000-S001 10 pcs' },
            { text: 'SF2000-S001 ← R3001-260901 20 pcs' },
            { tag: 'R3002, R3003', text: 'FG1000-S001 ← R3002-260901 10 sets, R3003-260901 10 pcs' },
            { tag: 'R3004 +3', text: 'FG1000-S001 ← R3004-260901 18 pcs, R3007-260901 2 pcs, R3005-260901 40 pcs, R3006-260901 0.5 L' }
          ]
        },
        forward: {
          name: 'From casting to product and shipment',
          links: [
            { text: 'R3001-260901 → SF2000-S001' },
            { text: 'SF2000-S001 → FG1000-S001' },
            { tag: 'Shipped 5', text: 'FG1000-S001 → 5 units to Sample Trading (SMP-SH-001)' }
          ]
        }
      },
      note: 'If you find that "the castings have many blowholes", tracing from the casting to products and shipments shows the affected products and customers at once.',
      ledgerNote: 'Every record is linked by lot number.',
      detail: { image: WT + 'trace.webp', alt: 'Traceability screen: the trace back of FG1000-S001 shows SF2000-S001 with R3001-260901 under it, plus six more material lots, in a tree' }
    }
  };

  data.scenes.forEach(function (scene) {
    var en = scenes[scene.id];
    if (!en) { errors.push('場面 ' + scene.id + ' の英訳が無い'); return; }
    apply(scene, en, scene.id);
    // 文言を持つ配列は、英訳が無いまま日本語が残らないよう件数も見る
    ['input', 'alerts', 'changeNotes', 'gauges'].forEach(function (key) {
      if (scene[key] && !en[key]) errors.push(scene.id + '.' + key + ' の英訳が無い');
    });
    ['when', 'note', 'ledgerNote', 'trace'].forEach(function (key) {
      if (scene[key] && !en[key]) errors.push(scene.id + '.' + key + ' の英訳が無い');
    });
    if (scene.detail && scene.detail.image && !(en.detail && en.detail.image)) errors.push(scene.id + '.detail の英訳が無い');
  });
  Object.keys(scenes).forEach(function (id) {
    if (!data.scenes.some(function (s) { return s.id === id; })) errors.push('場面 ' + id + ' が scenes.js に無い');
  });
  if (errors.length && window.console) console.error('工場模型の英訳が scenes.js と合いません:\n' + errors.join('\n'));
})(window.TOUR_DATA);
