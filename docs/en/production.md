---
layout: default
title: Production management
nav_url: production.html
lead: Registering production plans and checking plan against actual, and registering, approving and releasing manufacturing orders to processes. This is the work of production management staff.
prev_url: masters.html
prev_title: Master data
next_url: execution.html
next_title: Production execution
---

## Dashboard

**Left menu: Dashboard**

This screen is shown right after you sign in. Switch between three views with the tabs at the top.

| Tab | Contents |
|---|---|
| Summary | Good qty, defect qty, defect rate, equipment operating rate, inspection pass/fail and open nonconformances for the selected period, plus a progress list of manufacturing orders |
| Trend (day / week / month) | Good qty and defect qty (bars), defect rate (points) and a table of operating rates: 14 days for daily, 12 weeks for weekly and 12 months for monthly. Can be filtered by process, product and work center |
| Breakdown (process / item / shift / line / equipment) | Tables of the actuals for the selected period, broken down by process, product, shift, line, work center and equipment |

Choose **Daily**, **Weekly** or **Monthly** for the period and move with **◀ Previous / Next ▶** (you can also choose the reference date directly in the date field; **Today** takes you back).

- "Today" and the date boundaries are based on the **production date**, not the calendar date (by default the date changes at 6:00 a.m.). Night-shift actuals go into the production date on which they started.
- Weeks **start on Monday**.
- **The operating rate cannot be broken down by process or product**, because linking equipment operation records to work orders is optional. Look at operating rates by line, work center or equipment. In a trend filtered by process or product, the operating rate column disappears.
- Conversely, **the breakdown by equipment does not show production quantities or defect rates** (because equipment is not fixed for each work order).

The progress list in the summary shows the current state, regardless of the period.

| Column | Contents |
|---|---|
| Order no. | Click to go to the order details |
| Product / Quantity | What is being made and the planned quantity |
| Due date | Orders past their due date get a red **Past Due** badge |
| Status | Draft / Approved / Released / Completed / Canceled |
| Process progress | Shows the number of completed work orders, for example "2 / 5 processes completed" |

Canceled orders are not shown here. To see all of them, open the Manufacturing Orders screen.

---

## Production plans and plan vs. actual  {#plan}

**Left menu: Production & Execution → Production Plan vs. Actual**

On this screen you register planned quantities for each **production date × product × process**, and compare them with the output (good qty) of each process to see the achievement rate.
Switch between **Plan entry** and **Plan vs. actual** with the tabs at the top.

<div class="note">
<p><strong>The MES does not make plans.</strong> It is a place to receive plans made elsewhere, for example in a production scheduler or Excel.
Plans are not linked to manufacturing orders; registering them does not change orders, work orders or actuals.
Separately from the quantity and due date of an order, a plan holds "how many are planned to be made in this process on this day".</p>
</div>

### Registering a plan

The registration form is shown only to **System administrators and Production management**.

<ol class="steps">
<li>Choose the <strong>Production date</strong> (the initial value is today's production date).</li>
<li>Choose the <strong>Product</strong> and <strong>Process</strong>.</li>
<li>If needed, choose a <strong>Work center</strong> (optional; you can choose any level: factory, line, area or work center).</li>
<li>Enter the <strong>Planned Quantity</strong> and <strong>Notes</strong>, and press <strong>Register</strong>.</li>
</ol>

- **A planned quantity of 0 means "planned downtime"** (holidays, planned stoppages and so on). Plan vs. actual does not show an achievement rate for it.
- **You cannot register two plans for the same production date, product, process and work center.** To correct the quantity, change it from **Edit** in the list.
  A plan with a blank work center is also treated as one combination.
- **Plans for the same production date, product and process whose work center ranges overlap cannot coexist.**
  For example, "no work center" and "work center WC-MC1", or "Machining line" and "WC-MC1 under it", cannot be held at the same time
  (plan vs. actual adds them up, so an overall plan alongside a plan for part of it would be counted twice).
  Separate work centers (such as WC-MC1 and WC-MC2) can sit side by side.
- Plans you no longer need can be **deleted** (they are deleted, not deactivated).

The list can be filtered by a production date range (initially 7 days from today), product, process and work center.
Filtering by work center also shows the plans of the work centers under it.

### Registering and exporting with CSV

From **CSV import/export** on the Plan entry tab, you can import and export plans in bulk with CSV.
This is intended for re-importing plans made in an external scheduler or Excel every day or every week.

| Column | Required | Contents |
|---|:-:|---|
| `BusinessDate` | ○ | Production date (`yyyy-MM-dd`) |
| `ProductCode` | ○ | Product code |
| `ProcessCode` | ○ | Process code |
| `WorkCenterCode` | | Work center code (any level). A blank is also treated as one key |
| `PlannedQuantity` | ○ | Planned quantity (0 or more; 0 means planned downtime) |
| `Note` | | Notes |

- **A row with the same production date + product code + process code + work center code overwrites the existing plan**; otherwise a new plan is registered.
  When a plan is revised, correct the same CSV and import it again to apply the change (rows whose values have not changed are not updated).
- Plans that are not in the CSV are not removed. Delete plans you no longer need on the screen.
- Two rows with the same key in one file, or rows whose work center ranges overlap (including with existing plans), cause an error.
- "If even one row has an error, all rows are rolled back", "Validate only" and "either UTF-8 or Shift_JIS" work the same as for master data CSVs.
- **Export CSV does not export everything; it exports what matches the list's filter fields (production date range, product, process and work center).**
  You can correct the exported CSV and import it again as is.
- The permission to import is the same as for registering on the screen (System administrator and Production management).

<div class="note">
<p>Production plans are not master data, so they are not included in <strong>Bulk import/export via ZIP</strong> on Master Data
(this keeps data that grows day by day out of master data migration and full exports). Always handle plan CSVs from this tab.</p>
</div>

### Viewing plan vs. actual

The **Plan vs. actual** tab lists the **Planned Quantity, Actual Quantity and Achievement rate** for each production date × product × process.

| Column | Contents |
|---|---|
| Production date / Product / Process | The unit of comparison |
| Planned Quantity | The plan for that day. Plans for the same production date, product and process with different work centers are added together |
| Actual Quantity | The **good qty** of the production results for that process |
| Achievement rate | Actual Quantity ÷ Planned Quantity × 100. "-" on days with a plan of 0 |

As on the dashboard, choose **Daily**, **Weekly** or **Monthly** for the period and move with **◀ Previous / Next ▶**.
You can filter by product, process and work center. **When you choose exactly one product and one process**, a chart of the daily plan (bars) and actual (line) appears
(across products the units differ, and across processes the same goods would be counted once per process, so no chart is shown unless you filter).

- Actuals go into **the production date on which they started** (as on the dashboard; night-shift actuals count for the day they started).
- **Output of rework orders is not counted** (so the same goods are not counted twice; the same rule as for productivity totals).
- Days with only a plan (not yet made) and days with only actuals (production without a plan) each get a row, with 0 on the missing side.
- **Filtering by work center limits both plans and actuals to those under that work center.** Plans without a work center and
  actuals of processes without a work center (outsourced and so on) are left out. The work center of an actual is the work center in the routing at the time of release.

<div class="note">
<p>To try it with the sample data, import the master data sample (<code>samples-master-csv.zip</code>) and
<a href="masters.html#actual-csv">three months of actuals</a> (<code>samples-actual-csv-bulk.zip</code>), and then import
<a href="https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-production-plans.csv">samples-production-plans.csv</a>
from CSV import/export on the Plan entry tab. On the Plan vs. actual tab, choose <strong>Monthly</strong> and go back to July–September 2026.
In the week of 8/24, when tool wear occurred, the achievement rate of machining drops, and the Obon holidays (8/10–8/14) appear as days with a plan of 0.</p>
</div>

---

## Registering a manufacturing order

**Left menu: Production & Execution → Manufacturing Orders**

The registration form is shown only to **System administrators and Production management**.

<ol class="steps">
<li>Choose the <strong>Product</strong> (a finished or semi-finished product).</li>
<li>Enter the <strong>Quantity</strong>. It must be greater than 0.</li>
<li>Specify the <strong>Due date</strong> (optional).</li>
<li>Choose the <strong>Order Type</strong>. Use "Normal" for regular production and "Unplanned" for urgent orders raised from the shop floor.</li>
<li>Enter <strong>Notes</strong> if needed, and press <strong>Register</strong>.</li>
</ol>

On registration, **an order number is assigned automatically** (in the form `MO20260501-0001`) and the status becomes **Draft**.

<div class="note">
<p><strong>Rework orders</strong> are not created on this screen.
When "Rework" is instructed as the action in Nonconformances, a rework order is created automatically
(see <a href="quality.html#nc-action">Quality control</a>).</p>
</div>

### Filtering the list

With the drop-down at the top of the list, you can filter by status: Draft, Approved, Released, Completed or Canceled.
The "Output Lot" column shows the product lot number assigned at release.

---

## Order details, approval and release to processes

Open the order details from the **Details** button in the list (or the order number link).

The buttons shown depend on the status of the order.

| Status | Buttons shown |
|---|---|
| Draft | Approve / Cancel |
| Approved | Release to Processes / Cancel |
| Released | Cancel |
| Completed, Canceled | None |

### Approving

Pressing **Approve** changes the status to "Approved". An order cannot be released to processes until it is approved.

### Releasing to processes

Pressing **Release to Processes** does the following all at once.

- **Work orders for each process are created automatically, based on the routing (BOP)** registered for the product
- **An output lot number is assigned automatically** (in the form `<product code>-20260501-001`)
- The status of the order becomes "Released"

After release, the list of work orders is shown at the bottom of the screen, and you can go on to the next screen from each row.

| Button | Goes to |
|---|---|
| Setup | The setup and checklist screen |
| Record Results | The result entry screen |
| Instruction | The print page for the work instruction (new tab) |

<div class="warn">
<p>When release to processes fails, the cause is almost always that <strong>no routing (BOP) is registered</strong>.
Register the routing of the product under Master Data → Products, MBOM & Routings, and try again.</p>
</div>

### Changing  {#edit}

Draft and approved orders can have their quantity, due date and notes corrected from **Change** (System administrator and Production management).

<div class="note">
<p><strong>Changing an approved order returns it to Draft, and it must be approved again</strong>
(so that what was approved and what is actually ordered do not differ).
The product cannot be changed. If the product is wrong, cancel the order and register it again.</p>
</div>

### Canceling  {#cancel}

Pressing **Cancel** cancels the order, and the work orders linked to it are canceled as well.
Completed and canceled orders cannot be canceled.

<div class="note">
<p>Once an order is released, its quantity, due date and so on <strong>cannot be changed</strong> (it can only be canceled).
If you need to correct it, cancel it and register it again.</p>
</div>

---

## Checking progress

There are four places to check progress.

| Screen | What you see |
|---|---|
| Dashboard | "Completed processes / all processes" for each order, and orders past due |
| Production Plan vs. Actual | Planned quantity, actual quantity and achievement rate for each production date × product × process ([above](#plan)) |
| Manufacturing Orders → Details | The status, assignee and equipment of each work order of that order |
| Process Progress & Issues | For each process, the number of work orders that are Not dispatched / Dispatched / Started / Completed / Approved |

For the process progress screen, see [Production execution](execution.html#progress).
