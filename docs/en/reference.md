---
layout: default
title: Reference
nav_url: reference.html
lead: Lists of statuses, numbering rules, the business date and a glossary. For reference when you are unsure during operation.
prev_url: database.html
prev_title: Databases and backups
next_url: troubleshooting.html
next_title: Troubleshooting
---

## Statuses

### Manufacturing orders

| Status | Meaning | What you can do in this status |
|---|---|---|
| Draft | Just registered | Approve, change, cancel |
| Approved | Approved | Release to processes, cancel |
| Released | Released into work orders | Cancel only (the contents cannot be changed) |
| Completed | Everything is completed | — |
| Canceled | Canceled | — |

### Work orders

| Status | Meaning | Next operation |
|---|---|---|
| Not dispatched | Just released to processes | Dispatch, start |
| Dispatched | A worker and equipment have been assigned | Start |
| Started | The start of work has been recorded | Register results |
| Completed | Results have been registered | Approve production completion |
| Approved | Approved and finalized | — |
| Canceled | Follows the cancellation of the order | — |

### Stock status (lots)

| Status | Meaning | Main sources |
|---|---|---|
| Normal | Can be used and shipped as usual | Receiving, passed inspection |
| On hold | Use is temporarily stopped | Set manually, "On hold" action of a nonconformance |
| Awaiting inspection | An inspection order has been issued | Automatically when an inspection order is issued |
| Defective | Failed inspection | Automatically when the inspection judgment is "Fail" |
| To be scrapped | Planned to be scrapped | Set manually, "Scrap" action of a nonconformance |

### Inspection orders

Instructed → In progress → Judged → Approved (/ Canceled)

### Nonconformances

Open (not handled) → Action instructed → Action completed → Closed

### Picking orders, shipping orders and maintenance orders

Instructed → Completed (/ Canceled)

### Stocktaking

Instructed (entering counts) → Finalized (/ Canceled)

### Maintenance plans

Planned → Order issued → Completed (/ Canceled)

### Trouble reports

Open → In progress → Completed

### Shipment judgments

The judgment result is Approved / On hold / Special acceptance. Each has a status of Draft / Approved.

---

## Numbering rules

All numbers are assigned as a sequence for each day, based on the **business date**.

| Target | Format | Example |
|---|---|---|
| Manufacturing order number | `MO` + date + 4-digit sequence | `MO20260501-0001` |
| Lot number | Product code + date + 3-digit sequence | `PRD-100-20260501-001` |
| Picking order number | `PK` + date + 4-digit sequence | `PK20260501-0001` |
| Shipment number | `SH` + date + 4-digit sequence | `SH20260501-0001` |
| Stocktake number | `ST` + date + 4-digit sequence | `ST20260501-0001` |
| Inspection order number | `IN` + date + 4-digit sequence | `IN20260501-0001` |
| Nonconformance number | `NC` + date + 4-digit sequence | `NC20260501-0001` |
| Maintenance order number | `MT` + date + 4-digit sequence | `MT20260501-0001` |
| Shipment judgment number | `SJ` + date + 4-digit sequence | `SJ20260501-0001` |

When receiving, a lot number is assigned automatically if the field is left blank.
To use the supplier's lot number, type or scan it.

---

## Business date (production date)

Daily totals and numbering use the **business date**, not the calendar date.

- The default boundary time is **6:00 a.m.**
- Times before 6:00 a.m. are treated as **the previous day's production date**.
- Example: actuals at 3:00 a.m. on May 2 → the production date is "May 1"

This keeps night-shift actuals that cross midnight together under one production date.
The boundary time can be changed in the settings ([Server deployment and settings](operations.html#settings)).

The business date is used for numbering and for the period in quality analysis.

---

## Main business rules

| Rule | Details |
|---|---|
| Release to processes needs a routing | An order cannot be released to processes unless a routing (BOP) is registered for the product |
| No release without approval | A manufacturing order can be released to processes only when it is "Approved" |
| Process order is not enforced | A later process can be started even if the previous one has not finished. The start sequence is managed by dispatching |
| Inventory is added in the final process | When results are registered in the last process of the routing, the good qty is put away as the output lot |
| Backflushing is based on planned materials | Of the order's planned materials, the components used in that process are consumed automatically, first in first out, by unit quantity × (good qty + defect qty) (alternate parts are not deducted) |
| Picking is first in, first out | Allocation is automatic, earliest expiry first |
| Shipping needs a shipment judgment | A shipment cannot be carried out without an approved "Approved" or "Special acceptance" judgment |
| Approval is single-step | Each approval is finalized with one approval operation (there is no multi-step workflow) |
| Corrections need a reason | Quantity adjustments and inspection result corrections require a reason, which is recorded in the audit log |
| Records cannot be erased | The audit log is only ever added to; individual records cannot be changed or deleted. Only entries older than the retention period (5 years by default) can be deleted in bulk by an administrator, with a reason ([Audit log](security.html#audit-log)) |

---

## Functions and screens  {#api-only}

Every function can be operated from the screens. If you cannot find the operation you want, here is where to start.

| What you want to do | Screen |
|---|---|
| Record the component lots consumed, one at a time | [Material consumption](execution.html#issue) on Record Results |
| Keep manufacturing conditions such as temperature and speed | [Process condition data](execution.html#data) on Record Results |
| Record work time (direct / indirect) | [Work Time](execution.html#work-time) |
| Correct registered results | [Correcting results](execution.html#correct) on Record Results |
| Correct the quantity or due date of an order | [Change](production.html#edit) in the manufacturing order details |
| Cancel an inspection order issued by mistake | [Cancel inspection order](quality.html#inspection-cancel) in the inspection details |
| Follow how a work order progressed | [Status history](execution.html#history) on Record Results |
| Instruct a transport between processes and then carry it out | [Transfer Orders](logistics.html#transfer) |

---

## Glossary

| Term | Meaning |
|---|---|
| Manufacturing order | The unit "make this many of this product". Product × quantity |
| Work order | A manufacturing order broken down by process. Product × process |
| Release to processes | Breaking a manufacturing order down into work orders based on the routing |
| Dispatching | Assigning a worker, equipment and start sequence to a work order |
| MBOM | The manufacturing bill of materials. Child products and quantities per unit of the parent |
| Routing (BOP) | The order of processes, and for each process the standard time, required skills, equipment and tools |
| BOE | Equipment information linked to the routing |
| Output lot | The product-side lot assigned when a manufacturing order is released to processes |
| Backflush | Calculating component consumption automatically from the MBOM and the completed quantity, and deducting it from inventory |
| Setup | Preparation before work (pre-setup) and clearing up after work (post-setup) |
| Special acceptance | A judgment that allows use or shipment under conditions even though the goods are out of specification |
| Trace back | Following back the component lots used in a product lot |
| Trace forward | Following the product lots in which a component lot was used |
| Deviation / nonconformance | An event that departs from specifications or procedures. Its handling is recorded in Nonconformances |
| HSE | Health, Safety, Environment. Check items about health and safety |
| QCDS | Quality / Cost / Delivery / Safety. The classification of trouble reports |
| Business date (production date) | The date used for totals and numbering. By default the boundary is 6:00 a.m. |
