---
layout: default
title: Quality assurance
nav_url: assurance.html
lead: Judging whether goods may be shipped, and tracing by lot. This is the work of quality assurance staff.
prev_url: quality.html
prev_title: Quality control
next_url: maintenance.html
next_title: Equipment maintenance and tools
---

## Shipment judgments

**Left menu: Quality → Shipment Judgments**

<div class="warn">
<p><strong>Carrying out a shipment requires an approved shipment judgment for that shipping order.</strong>
The judgment must be "Approved" or "Special acceptance", and it must itself be <strong>approved</strong>.
A shipment cannot be made while the judgment is "On hold".</p>
</div>

Judgments can be registered and approved by **System administrators and Quality assurance**.

### Registering a judgment

<ol class="steps">
<li>Choose the <strong>Shipping order</strong>. Always specify it when the judgment is used as the gate for shipping.</li>
<li>If needed, choose the <strong>Lot</strong>.</li>
<li>Choose the <strong>Judgment</strong>: Approved / On hold / Special acceptance.</li>
<li>Enter <strong>Notes</strong> and press <strong>Register judgment</strong>.</li>
</ol>

A judgment number is assigned (`SJ20260501-0001`). You must specify at least one of
the shipping order and the lot.

| Judgment | Meaning |
|---|---|
| Approved | The goods may be shipped as usual |
| On hold | Stops the shipment. Goods cannot be shipped with this judgment |
| Special acceptance | Out of specification, but shipping is allowed under conditions |

### Approving a judgment

Pressing the **Approve** button in the list records the approval date and time, and the judgment becomes "Approved".
**Shipping becomes possible only once the judgment is approved.**

### Issuing a judgment document

You can print the shipment judgment document from the **Judgment doc** button of each row.

---

## Traceability

**Left menu: Quality → Traceability**

From a lot number, follow where the lot came from and where it went.

### Searching for a lot

Enter a lot number in the field at the top and press **Trace**. Barcode / QR code scanning is also supported.

If you came from the **Trace** button on the inventory inquiry screen or the expiry alerts, the screen opens with the search already done.

<div class="note">
<p><strong>Lots whose inventory has reached 0 can also be traced.</strong> Search for shipped and consumed lots in the same way.</p>
</div>

Searching shows the basic information of the lot.

| Field | Contents |
|---|---|
| Lot number / Product | Identifies the lot |
| Stock status | Normal / On hold / Awaiting inspection / Defective / To be scrapped |
| Origin | Production (made in a process) / Receiving (purchased) |
| Production or receiving date / Expires | Date information |
| Grade | The grade set at the inspection judgment |
| Lot tag | Click to print the lot tag |

### Tracing with three tabs

#### Trace back

Shows, going upstream, **the component lots used to make this lot**.
Use it to identify the component that caused a defective product. It expands as a tree, to components further upstream.

If there are no records of consumed components (when neither backflushing nor picking was done),
"No material consumption records." is shown.

#### Trace forward

Shows **where this lot was used**. Use it to identify
which product lots a problem component lot went into.

#### History

Shows three kinds of history together.

- **Production history**: in which work order and process, when and by whom it was made
- **Inspection history**: in which inspection order, and what judgment it received
- **Stock history**: inventory transactions such as receiving, moves, adjustments and shipping

### What to do when a problem is found

Once you have identified the lots concerned with tracing, handle them on the following screens.

| What you want to do | Screen to use |
|---|---|
| Stop shipping or use | [Inventory](logistics.html#stock-ops) → Change status to "On hold" |
| Inspect again | [Inspections](quality.html#issue-inspection) → issue an order with the inspection type "Reinspection" |
| Make it again | [Nonconformances](quality.html#nc-action) → instruct the action "Rework" (a rework order is created automatically) |
| Scrap it | [Inventory](logistics.html#stock-ops) → Scrap |
