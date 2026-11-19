---
layout: default
title: Equipment maintenance and tools
nav_url: maintenance.html
lead: Equipment operation records and maintenance work, and tool life management. This is the work of equipment maintenance staff.
prev_url: assurance.html
prev_title: Quality assurance
next_url: reports.html
next_title: Reports, labels and scanning
---

## Equipment maintenance

**Left menu: Equipment & Maintenance → Maintenance**

The screen is divided into tabs. Creating maintenance plans and maintenance procedures and canceling maintenance orders can be done by
**System administrators and Equipment maintenance**. Every user can record operation, request unplanned maintenance and register maintenance records.

---

### Operation monitoring tab

#### Recording operation

<ol class="steps">
<li>Choose the <strong>Equipment</strong>.</li>
<li>Choose the <strong>Status</strong>: Running / Stopped / Setup / Failure.</li>
<li>Enter the <strong>Start time</strong> and <strong>End time</strong>.</li>
<li>For Stopped or Failure, enter the <strong>Stop cause</strong> (<strong>required</strong>).</li>
<li>Press <strong>Record operation</strong>.</li>
</ol>

<div class="warn">
<p>A stop or failure record with a blank stop cause causes an error.
<code>Stop and failure records require a stop cause (stopCause) (B-40-20-02).</code></p>
</div>

The current version does not collect data automatically from sensors. Operation is recorded by hand.

#### Operation summary by equipment

For each piece of equipment, the running time, stopped time, setup time, failure time, number of breakdowns and **availability** are totaled.

<div class="note">
<p>Only <strong>operation records with an end time</strong> are included.
Records registered without an end time are not reflected in availability.</p>
</div>

#### Operation history

The recorded operation is listed in time order.

---

### Maintenance plans tab

#### Creating a plan

<ol class="steps">
<li>Choose the <strong>Equipment</strong>.</li>
<li>Choose the <strong>Maintenance type</strong> (Periodic / Unplanned).</li>
<li>Enter the <strong>Plan year</strong>, <strong>Scheduled date</strong> and <strong>Cycle (days)</strong>.</li>
<li>Enter <strong>Notes</strong> and press <strong>Create plan</strong>.</li>
</ol>

The status of a plan changes from Planned → Order issued → Completed.

#### Issuing a maintenance order from a plan

Pressing **Issue maintenance order** in the list creates a maintenance order based on that plan,
and the status of the plan becomes "Order issued". Orders can be issued only from plans in the "Planned" status.

---

### Orders and records tab

#### Creating a maintenance order

Unplanned maintenance requests that are not based on a plan can also be created here.

<ol class="steps">
<li>Choose the <strong>Equipment</strong> or the <strong>Tool</strong> (<strong>only one of them</strong>).</li>
<li>Choose the <strong>Request type</strong>: Unplanned request / Planned.</li>
<li>If needed, link a <strong>Procedure</strong>.</li>
<li>Enter the <strong>Scheduled date</strong> and <strong>Request details</strong>, and press <strong>Create maintenance order</strong>.</li>
</ol>

Specifying both equipment and a tool, or neither, causes an error.

> `Specify either a target equipment ID or a target tool ID, not both.`

#### Registering a maintenance record

A maintenance order card in the "Instructed" status shows the fields for entering the record.

<ol class="steps">
<li>Enter the <strong>Start time</strong> and <strong>End time</strong>.</li>
<li>Fill in <strong>Parts consumed / replaced</strong>.</li>
<li>Fill in the <strong>Result</strong>.</li>
<li>For tool maintenance, ticking <strong>Reset life counter</strong> returns the cumulative number of uses and hours to 0.</li>
<li>Press <strong>Register maintenance record</strong>.</li>
</ol>

On registration the maintenance order becomes "Completed" and remains as history (when, who and what was replaced).
If needed, you can cancel the order with **Cancel**.

---

### Procedures tab

<ol class="steps">
<li>Enter the <strong>Procedure no.</strong> and <strong>Title</strong>.</li>
<li>Choose the <strong>Equipment</strong> or the <strong>Tool</strong>.</li>
<li>Write the work procedure in <strong>Steps</strong> (line breaks are kept as entered).</li>
<li>For procedures that require a qualification (electrical work and so on), choose a <strong>Required skill / qualification</strong>. For maintenance orders that use this procedure, an error occurs if the person registering the record does not hold the qualification or it has expired.</li>
<li>Press <strong>Register procedure</strong>.</li>
</ol>

<div class="note">
<p><strong>Registering with the same procedure number raises the revision by one.</strong>
The list shows the revision, such as <code>v2</code>. Revisions are kept as history,
so when you review the content, register it again rather than overwriting it.</p>
</div>

To register or review procedures in bulk, import them from **CSV import/export** on the tab.
Write the equipment by asset number, and the tool and required skill / qualification by code. Rows with an existing procedure number are updates, and the revision goes up by one.
The format is the same as for [master data CSVs](masters.html), and the sample is `samples/master-csv/20_maintenance-procedures.csv`.

---

## Tool management  {#tool-management}

**Left menu: Equipment & Maintenance → Tool Management**

Tools are registered in [Master data](masters.html#tools), and maintenance orders and records are handled on
the "Orders and records" tab of the Maintenance screen. This screen deals with usage records and life.

### Recording usage

<ol class="steps">
<li>Choose the <strong>Tool</strong>.</li>
<li>Choose the <strong>Related work order</strong> (optional, but specifying it lets you trace which job the tool was used for).</li>
<li>Enter the <strong>Shots</strong> (number of uses) or <strong>Hours used (h)</strong>. <strong>One of them is required</strong>.</li>
<li>Press <strong>Record usage</strong>.</li>
</ol>

The recorded values are added to the totals.

### Life status

The share of the life threshold set in the master that has been used is shown.

| Display | Condition |
|---|---|
| (nothing) | Less than 80% used |
| **Replacement due soon** (yellow) | 80% or more used |
| **Replace / scrap** (red) | 100% reached |
| No threshold | No life threshold is set in the master |

Tick "Warnings / replace only" to show only the tools that need attention.
It is efficient to check with this ticked during daily inspections.

For tools that have reached the end of their life, record the replacement with a maintenance order in Maintenance,
and tick **Reset life counter** when registering the record.

### Usage history

The recorded date and time, tool, work order, shots and hours used are listed. This is the source data for life analysis.
