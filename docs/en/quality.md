---
layout: default
title: Quality control
nav_url: quality.html
lead: From issuing inspection orders, through entering results, judging and approving, to raising and handling nonconformances. This is the work of quality control staff.
prev_url: logistics.html
prev_title: Logistics and inventory
next_url: assurance.html
next_title: Quality assurance
---

<div class="flow">
  <span>Issue inspection order</span><em>→</em>
  <span>Enter inspection results</span><em>→</em>
  <span>Overall judgment</span><em>→</em>
  <span>Approve inspection</span><em>→</em>
  <span>(If failed) handle the nonconformance</span>
</div>

Issuing, entering, judging and approving inspections, and instructing and approving the handling of nonconformances,
can be done by **System administrators and Quality control**. Every user can raise nonconformances and record their handling.

---

## Inspections

**Left menu: Quality → Inspections**

### Issuing an inspection order  {#issue-inspection}

<ol class="steps">
<li>Choose the <strong>Inspection type</strong>. There are five: Receiving inspection / In-process inspection / Final inspection / Sample inspection / Reinspection.</li>
<li>For an in-process inspection, choose the <strong>Work Order</strong>. Otherwise, specify the <strong>Lot</strong> (by scanning or from the drop-down).</li>
<li>Enter <strong>Notes</strong> if needed and press <strong>Issue inspection order</strong>.</li>
</ol>

On issue, an inspection order number is assigned (`IN20260501-0001`) and **the stock status of the target lot becomes "Awaiting inspection"**.

<div class="warn">
<p>Inspection orders cannot be issued for the following lots (except sample inspections, which do not hold the lot).</p>
<ul>
<li>Lots that are <strong>On hold or To be scrapped</strong>. A hold is released by the disposition of a nonconformance</li>
<li>Lots that are <strong>Defective</strong>. A <strong>Reinspection</strong> can still be issued, however</li>
<li>Lots that <strong>already have an inspection order awaiting judgment</strong>. Judge or cancel that order first, then issue</li>
</ul>
<p>In-process inspections cannot be issued for canceled work orders. When you specify a lot, it must be a lot of the product of that work order.</p>
</div>

<div class="note">
<p>A <strong>Reinspection</strong> repeats the most recent judged inspection of that lot. The inspection items are carried over from the original inspection, and the specification limits come from the current version of the criteria.
It cannot be issued for a lot that has no judged inspection.</p>
</div>

<div class="note">
<p><strong>Inspection items are selected automatically.</strong> Of the items registered under Master Data → Inspection Items,
those whose inspection type, product and process match are included in the inspection order.
If no criteria match, the following error occurs.</p>
<p><code>No inspection criteria match the target. Register inspection items or specify itemIds.</code></p>
</div>

### Checking inspection progress

The list can be filtered by status.

| Status | Meaning |
|---|---|
| Instructed | Issued, no results entered yet |
| In progress | Results have been entered for some items |
| Judged | The overall judgment has been made (Pass / Fail) |
| Approved | Approved and finalized |
| Canceled | Canceled |

The "Judgment" column shows a green badge for Pass and a red badge for Fail.

---

## Entering inspection results and judging

Open the details of an inspection order from the **Details / results** button in the list.

### Entering inspection results

For each inspection item, the specification limits (lower to upper) and the method are shown.

<ol class="steps">
<li>Enter the <strong>Measured</strong> value. It is checked against the specification limits and judged pass or fail <strong>automatically</strong>.</li>
<li>For inspections that are not numerical, write in <strong>Qualitative record</strong>.</li>
<li>For items that cannot be judged automatically (items without specification limits), choose Pass or Fail in <strong>Judgment (manual)</strong>.
For items whose result is decided by the measured value and the limits, the manual judgment cannot change the result (to use goods that are out of specification, handle them with special acceptance of a nonconformance).</li>
<li>Press <strong>Save results</strong>.</li>
</ol>

Only the items you entered are registered. You may enter them over several sessions.

<div class="warn">
<p>If you enter only a measured value for an item without specification limits, the following error occurs.
<code>Inspection item '...' cannot be judged automatically from specification limits. Specify judgment.</code>
Choose Pass or Fail in that item's "Judgment (manual)".</p>
</div>

<div class="note">
<p><strong>For inspection items with a required skill / qualification, only people holding that qualification can register results.</strong>
The check uses the criteria settings as of when the inspection order was issued (changing the criteria later does not affect inspections already issued).</p>
</div>

### Making the overall judgment

Judge **after registering results for every inspection item**. For items with a set sample size, that many results with different sample numbers must be in.

<ol class="steps">
<li>If needed, enter a <strong>Grade</strong> (for products whose destination or grade depends on the inspection result; optional).</li>
<li>Press <strong>Judge</strong>.</li>
</ol>

The rules and results of the judgment are as follows.

| Result | Condition | Effect on the target lot |
|---|---|---|
| Pass | Every inspection item passed | The stock status becomes **Normal** |
| Fail | At least one item failed | The stock status becomes **Defective**, and **a nonconformance is raised automatically** |

The judgment cannot be made while any item has no results.

> `N inspection items have no results. Register results for all items before judging.`

### Approving

Once judged, an **Approve inspection** button is shown. Pressing it changes the status to "Approved" and finalizes it.

### Issuing an inspection certificate

The **Inspection certificate** button at the top of the screen opens a print page.

### Canceling an inspection order  {#inspection-cancel}

An inspection order issued by mistake can be canceled with **Cancel inspection order** (not when approved or already canceled).

<div class="note">
<p>Canceling <strong>returns the lot that was held as awaiting inspection for that order to its status before the order was issued</strong>.
Usually it returns to Normal, but if a reinspection of a defective lot is canceled, it returns to Defective.
Canceling an order that has already been judged leaves the lot's current status as it is.</p>
</div>

### Correcting inspection results

You can correct results from **Correct** in each row of the results list.

<ol class="steps">
<li>Enter the correct <strong>Measured</strong> value and <strong>Judgment</strong>.</li>
<li>Enter a <strong>Correction Reason</strong> (<strong>required</strong>).</li>
<li>Press <strong>Register Correction</strong>.</li>
</ol>

The correction and its reason are recorded in the audit log.

<div class="warn">
<p><strong>Approved and canceled inspections cannot be corrected.</strong> Also, if you correct a judged inspection,
you must <strong>make the overall judgment again</strong>.</p>
</div>

---

## Nonconformances

**Left menu: Quality → Nonconformances**

Besides those raised automatically by failed inspections, you can raise deviations and defects found on the shop floor by hand.

### Raising a nonconformance

<ol class="steps">
<li>Choose the <strong>Source</strong>: Production record / Inspection / Receiving.</li>
<li>Choose the related <strong>Lot</strong> and <strong>Work Order</strong> (optional).</li>
<li>Enter the <strong>Cause category</strong> (for example equipment fault / human error / material defect). It is used as an axis in quality analysis.</li>
<li>Write the <strong>Details</strong> and <strong>Cause details</strong>, and press <strong>Raise nonconformance</strong>.</li>
</ol>

A nonconformance number is assigned (`NC20260501-0001`).

<div class="note">
<p><strong>The cause category is free text.</strong> If the wording varies, the quality analysis totals are spread out,
so we recommend agreeing on the terms to use in your factory.</p>
</div>

### Instructing the handling of a nonconformance  {#nc-action}

On each nonconformance card in the list, choose the action type and instruct it (quality control staff).

| Action type | What happens |
|---|---|
| Rework | **A rework order is created automatically** |
| On hold | The stock status of the target lot becomes "On hold" |
| Scrap | The stock status of the target lot becomes "To be scrapped" |
| Special acceptance | The lot is released on approval |

Entering the action type and the instruction and pressing **Action** changes the status to "Action instructed".
The number of a created rework order is shown as a badge on the nonconformance card.

### Recording the action taken

When the action is done, enter what was done in the "Action record" field and press **Response Record**. The status becomes "Action completed".

### Approving (closing)

Pressing **Approve (releases the lot for special acceptance)** closes the nonconformance.
If the action type is "Special acceptance", the stock status of the target lot returns to Normal at this point.

A nonconformance cannot be approved before an action has been instructed.

---

## Quality analysis

**Left menu: Quality → Quality Analysis**

Totals are calculated for a specified period. **The period is based on the production date (business date).**

| Section | Contents |
|---|---|
| Summary | The number of passed / failed inspections and the number of open nonconformances |
| Defects by item | Good qty, defect qty and defect rate for each product |
| Defects by process | Good qty, defect qty and defect rate for each process |
| Nonconformances by cause category | The count for each cause category entered when raising |
| Control chart and process capability | A control chart of the measured values of the selected inspection item, and the process capability indices (Cp, Cpk) |

Enter a start date and end date and press **Aggregate** to recalculate. If the dates are blank, the whole period is used.
If there are open nonconformances, an "Action needed" badge is shown.

### Control charts and process capability

Choose an **Inspection item (quantitative)** to show a control chart of that item's measured values.

- The values measured in one inspection order are grouped into one point (subgroup). Items measured two or more times per inspection get an X̄-R chart (mean and range); items measured once get an X-Rs chart (individual values and moving range)
- **Red points** are outside the control limits (UCL, LCL), and **yellow points** are runs of nine or more points on the same side of the center line. Both are signs that something is happening in the process
- The specification limits (upper and lower) are the criteria values as of when the inspection order was issued
- **Cp and Cpk** are process capability indices. They are marked when below 1.33. They cannot be calculated with fewer than two subgroups

<div class="note">
<p><strong>Keep the number of pieces measured per inspection (the sample size) the same.</strong>
Subgroups of different sizes cannot be mixed in one control chart, so subgroups that do not match the most common size are excluded, and the number excluded is shown.</p>
</div>
