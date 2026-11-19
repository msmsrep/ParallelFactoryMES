---
layout: default
title: Production execution
nav_url: execution.html
lead: How work orders released to processes are carried out and recorded on the shop floor. Covers dispatching, setup, recording results, completion approval and trouble reports.
prev_url: production.html
prev_title: Production management
next_url: logistics.html
next_title: Logistics and inventory
---

<div class="flow">
  <span>Dispatching</span><em>→</em>
  <span>Setup and checklists</span><em>→</em>
  <span>Start</span><em>→</em>
  <span>Record results</span><em>→</em>
  <span>Completion approval</span>
</div>

A work order changes status in the following order.

<div class="flow">
  <span>Not dispatched</span><em>→</em><span>Dispatched</span><em>→</em><span>Started</span><em>→</em><span>Completed</span><em>→</em><span>Approved</span>
</div>

---

## Dispatching

**Left menu: Production & Execution → Dispatching**

Assign a worker, equipment and start sequence to the work orders created by release to processes. Initially, work orders that are "Not dispatched" are shown.

<ol class="steps">
<li>Choose the <strong>Worker</strong> for the row from the drop-down.</li>
<li>Choose the <strong>Equipment</strong>.</li>
<li>Enter a number in <strong>Start Sequence</strong> (smaller numbers go first).</li>
<li>Press <strong>Assign</strong> at the end of the row.</li>
</ol>

After assignment, the status becomes "Dispatched". All three fields are optional, so you can also assign only the equipment and start sequence.

### Checking skills and qualifications

When you assign a worker, the system **automatically checks whether they hold the required skills and qualifications** set in the routing for that process.
If they do not hold a qualification, or **the qualification has expired**, an error occurs and the assignment is not made.

The same check is made **at the start of work, for the person who starts it**. Even if dispatching was skipped, a different person from the one assigned starts the work, or the qualification expired after dispatching, a person without the qualification cannot start the work.

> Example: `Skill 'Welding' of user 'John Smith' has expired.`

Add qualifications or update their expiry dates from Skills & Qualifications under Master Data → Users.

<div class="note">
<p><strong>If the Worker drop-down shows only "Unassigned"</strong>, the signed-in user does not have permission to view the user list
(system administrator). Even then, equipment and start sequence can still be assigned.</p>
</div>

---

## Work order list

**Left menu: Production & Execution → Work Orders**

You can check all work orders by status. From each row you can go to the following three.

- **Setup**: recording setups and checklists
- **Record Results**: starting work, reporting output and completion approval
- **Instruction**: printing the work instruction (new tab). It prints the components used in this process (those in the MBOM at release whose consuming step is this process) and the process information

---

## Setup and checklists  {#setup}

**Work order list → Setup**

### Recording a setup

<ol class="steps">
<li>In <strong>Category</strong>, choose "Pre-setup" or "Post-setup".</li>
<li>Enter the <strong>Start time</strong> and <strong>End time</strong>.</li>
<li>If anything was abnormal, write it in <strong>Abnormality Report</strong>.</li>
<li>Press <strong>Record Setup</strong>.</li>
</ol>

Records accumulate in the table below, and the person who did the work is recorded automatically. Both pre-setups and post-setups can be recorded any number of times.

### Carrying out a checklist

<ol class="steps">
<li>From the <strong>Checklists</strong> drop-down, choose the one to carry out. Its category (Common, Process, Product, Setup, Maintenance, HSE) is shown in parentheses.</li>
<li>Tick <strong>Check</strong> for each item. Add <strong>Notes</strong> if needed.</li>
<li>Press <strong>Record Checklist</strong>.</li>
</ol>

<div class="warn">
<p><strong>It cannot be saved while a required item is unchecked.</strong> Tick every item marked "Required" before recording.</p>
</div>

After recording, the history keeps "who carried it out, when, and checked items / all items".

---

## Recording results

**Work order list → Record Results**

### Starting work

While a work order is "Not dispatched" or "Dispatched", a **Started** button for starting the work is shown.
Pressing it changes the status to "Started".

### Reporting output

<ol class="steps">
<li>Enter the <strong>Good qty</strong> and <strong>Defect qty</strong>. Their total must be greater than 0.</li>
<li>Enter the <strong>Start time</strong> and <strong>End time</strong>.</li>
<li>For the final process, choose the <strong>Put-away location</strong> (<strong>required</strong>).</li>
<li>To consume components automatically, tick <strong>Backflush</strong>.</li>
<li>Press <strong>Register Result (work completion)</strong>.</li>
</ol>

On registration the work order becomes **Completed**. Registered results remain in the table at the bottom of the screen.

#### Put-away in the final process

When results are registered in the last process of the routing, **the good qty is put into inventory at the put-away location as the output lot**.
If no put-away location is specified, an error occurs.

> `A record for the final process requires a put-away location (outputLocationId).`

#### Backflush (automatic consumption of components)

When ticked, the required quantity is calculated as **MBOM × (good qty + defect qty)**
and automatically deducted from inventory on a **first-in, first-out** basis.
Only **components whose consuming step in the MBOM is this process** are deducted (components without a consuming step are deducted in the final process).
Ticking it in every process never deducts components twice.

<div class="warn">
<p>If there are no planned materials because no MBOM was registered for the product at release, an error occurs (<code>Backflush is not possible because the planned materials are missing (no MBOM at release).</code>).
An error also occurs when there is not enough component inventory. Prepare inventory first with <a href="logistics.html#receiving">Receiving</a> or
<a href="logistics.html#picking">issuing to processes</a>.</p>
</div>

<div class="note">
<p>Instead of backflushing, you can also record the component lots used one at a time.
See <a href="#issue">Material consumption</a> below.</p>
</div>

### Material consumption  {#issue}

In **Material Consumption** on the result entry screen, record the component lots used one at a time (System administrator, Production management and Operator).

<ol class="steps">
<li>Choose the <strong>Lot to Consume</strong>. You can search by scanning the lot number. The shelf of the chosen stock is used.</li>
<li>Enter the <strong>Quantity</strong>.</li>
<li>When consuming an alternate part line of the planned materials (a component set as an alternate part in the MBOM), a <strong>Substitution Reason</strong> is required.</li>
<li>Press <strong>Record Consumption</strong>. The inventory is deducted and the consumption is recorded.</li>
</ol>

<div class="warn">
<p>Only <strong>products included in the order's planned materials</strong> can be consumed (fixed by the MBOM at release).
Choosing a different product causes an error. This prevents consuming the wrong material.</p>
</div>

The list shows both the records entered by hand (manual) and those calculated automatically by backflushing.

### Process condition data  {#data}

Keep the conditions during work, such as temperature, speed and pressure, as pairs of **Field** and **Value**.
These records explain "what the temperature was at the time" in certificates and in later root-cause investigation.

### Correcting results  {#correct}

From **Correct** in the list of registered results, you can correct the good qty, defect qty, scrap qty and awaiting rework qty
(**System administrators and Production management** only).

<ol class="steps">
<li>Press <strong>Correct</strong> in the row of the result to correct.</li>
<li>Enter the correct quantities and a <strong>Correction Reason</strong> (<strong>required</strong>).</li>
<li>Press <strong>Register Correction</strong>.</li>
</ol>

<div class="note">
<p>The values before correction are kept as the <strong>correction history</strong> and are also recorded in the audit log.
When results of the final process are corrected, the inventory quantity of the output lot is adjusted automatically by the difference.
The correction history can also be checked on the <a href="assurance.html">Traceability</a> screen.</p>
</div>

### Status history  {#history}

At the bottom of the screen, a history shows **when and what caused** each status change of the work order
(dispatching, start, result entry, approval, order cancellation). You can see how it got there, which the current status alone does not tell you.

### Completion approval

When a work order becomes "Completed", an **Approve Production Completion** button is shown to **System administrators and Production management**.
Pressing it changes the status to "Approved", and the work order is finalized.

---

## Recording work time  {#work-time}

**Left menu: Production & Execution → Work Time**

Workers record their own work time (System administrator, Production management and Operator).

| Category | When to use | What to enter |
|---|---|---|
| Direct work | Work linked to a work order | The work order |
| Indirect work | Setup, material preparation, cleaning, equipment handling and so on | A name for the category (free text) |

The start time is required. The end time can be left blank while the work is in progress.
The list shows your own records (System administrators and Production management see everyone's).

---

## Process progress and trouble reports  {#progress}

**Left menu: Production & Execution → Process Progress & Issues**

### Progress by process

For each process, the number of work orders that are Not dispatched, Dispatched, Started, Completed and Approved is totaled.
You can see in which process work in progress is piling up.

### Reporting trouble

<ol class="steps">
<li>Choose the <strong>Category (QCDS)</strong>: Quality, Cost, Delivery or Safety.</li>
<li>Enter <strong>Occurred At</strong>.</li>
<li>Choose the related <strong>Work Order</strong> and <strong>Equipment</strong> (both optional).</li>
<li>Write the <strong>Details</strong> and press <strong>Report Trouble</strong>.</li>
</ol>

### Recording the response

Enter what was done in the "Response Record" field of the list and press a button.

| Button | What it does |
|---|---|
| Add | Adds to the response history and sets the status to "In progress" |
| Completed | Adds to the response history and sets the status to "Completed" |

Completed trouble reports can no longer be edited. Choose "Open (not handled)" in the status filter
to see only the ones not yet handled.
