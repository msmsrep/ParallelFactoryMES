---
layout: default
title: Master data
nav_url: masters.html
lead: The basic data you need to register before starting work. There is a required order of registration, so follow the steps in this order.
prev_url: walkthrough.html
prev_title: "Sample walkthrough: from production to shipping"
next_url: production.html
next_title: Production management
---

**Left menu: Administration → Master Data**

Switch between the kinds of master data with the tabs at the top of the screen.

<div class="warn">
<p><strong>The order of registration matters.</strong> You cannot create a routing (BOP) until the processes are registered,
and you cannot release a manufacturing order to processes without a routing. Proceed in the following order.</p>
</div>

<div class="flow">
  <span>① Processes</span><em>→</em>
  <span>② Products</span><em>→</em>
  <span>③ MBOM</span><em>→</em>
  <span>④ Routing (BOP)</span><em>→</em>
  <span>⑤ Locations</span><em>→</em>
  <span>⑥ Equipment, tools, inspection items, checklists, skills and users</span>
</div>

Master data can be registered and edited by **System administrators** and **Production management**
(users and skill links by system administrators only).

When you have many records, you can use [bulk registration with CSV](#csv) instead of entering them one at a time.

---

## ① Processes

**Tab: Process**

Register the manufacturing processes (machining, assembly, inspection and so on).

| Field | Description |
|---|---|
| Process code | A unique code. Referred to by work orders and routings |
| Process name | Display name |
| Process type | In-house / Outsourced |

---

## ② Products

**Tab: Products, MBOM & Routings**

Register finished products, semi-finished products and components all as "products".

| Field | Description |
|---|---|
| Product code | A unique code. **It is also used at the start of lot numbers** |
| Product name | Display name |
| Unit | pcs, kg and so on |
| Product type | Product / Semi-finished / intermediate / Component |
| Specification | Any specification information |
| Standard defect rate (%) | When a manufacturing order is released, the planned materials are increased by this rate (0–99.99) |

Registered products are shown in the list. Use **Edit** to change them and **Deactivate** to remove them from future choices
(past actuals are kept).

---

## ③ MBOM (bill of materials)  {#mbom}

Press the **MBOM / Routing** button in a row of the product list to edit that product's structure.

<ol class="steps">
<li>Choose a <strong>Child product</strong> (a component that is used).</li>
<li>Enter the <strong>Qty required</strong>. This is the quantity <strong>per unit of the parent</strong>.</li>
<li>Specify the <strong>Make / buy type</strong> and, if needed, an <strong>Alternate part group</strong>.</li>
<li>In <strong>Consuming step</strong>, choose the process (routing sequence) in which the component is used. If you do not choose one, it is treated as used in the final process.</li>
<li>Press <strong>Add line</strong> to add rows one by one.</li>
<li>When everything has been added, press <strong>Save MBOM (replace all)</strong>.</li>
</ol>

<div class="warn">
<p>Saving <strong>replaces everything</strong>. The existing structure is replaced as a whole by the lines shown on the screen.
Even if you only want to add one line, keep the existing lines and save with the new one added.</p>
<p>A product cannot be its own child. A structure that loops back on itself (for example, a semi-finished product as a child of a product and that product as a child of the semi-finished product) cannot be registered either. The error shows the path of the loop.</p>
<p>An alternate part group must contain <strong>exactly one main material (a line not checked as an alternate part)</strong>, and the rest are alternate parts. A line checked as an alternate part needs a group name.</p>
</div>

The MBOM is used for **backflushing** (automatic consumption of components) when recording results.
If you plan to use backflushing, be sure to register it.
When a manufacturing order is released, the planned materials are created from the MBOM. The planned quantity is **Qty required × order quantity ÷ (1 − standard defect rate)**.
Components are also used for units that turn out defective, so extra is allowed for by the parent product's standard defect rate (for example, with Qty required 2, order quantity 10 and standard defect rate 20%, the planned quantity is 25).
Backflushing consumes each component **only in the work order of its consuming step**, so for components used in assembly, choose the assembly process as the consuming step.

<div class="warn">
<p>If a consuming step is set to a routing sequence that is not in the routing, the manufacturing order cannot be released. When you rebuild a routing, also review the consuming steps in the MBOM (the list shows "(not in routing)").</p>
</div>

---

## ④ Routing (BOP)  {#bop}

This is also edited from **MBOM / Routing** in the product list. **A routing is required to release a manufacturing order to processes.**

| Field | Description |
|---|---|
| Sequence | A number for the order of execution. Work orders are created in ascending order |
| Process | Choose from the registered processes |
| Standard work time (min) / Standard setup time (min) | Reference values |
| Required skill / qualification | The operator's qualifications are checked against this when dispatching |
| Equipment / Tools used | The default equipment and tools |
| Checklists | The checklist carried out in this process |
| Process Control Items | Choose one or more manufacturing conditions to record in this process (temperature, speed and so on) from the process control item master. The set values and tolerances are copied to the released work orders and checked against the actuals |
| Control item notes | Notes such as checks that have no set value |

Add rows with **Add process** and confirm with **Save routing (replace all)**. As with the MBOM, saving replaces everything.

<div class="note">
<p><strong>The last process is treated as the "final process".</strong> When recording results in the final process,
the receiving location must be specified, and the product lot is put into inventory there.</p>
</div>

---

## ⑤ Locations

**Tab: Location**

Places where inventory is kept. Register at least one "Material warehouse" and one "Product warehouse".

| Field | Description |
|---|---|
| Location code | A unique code |
| Warehouse / area type | Material warehouse / In-process / Product warehouse / Shipping area |
| Shelf no. | Optional |

---

## ⑥ Equipment (BOE)

**Tab: Equipment (BOE)**

| Field | Description |
|---|---|
| Asset no. | A unique number |
| Equipment name / Location | Display information |
| Status | Available / Stopped / Under maintenance / Retired |
| Maintenance type | None / Calendar / Run time / Usage count |
| Maintenance threshold | The interval, hours or count, depending on the maintenance type |
| Maintenance parts | Notes on replacement parts |

---

## ⑦ Tools  {#tools}

**Tab: Tools**

| Field | Description |
|---|---|
| Tool code / Name / Type | Basic information |
| Life threshold (uses) | The upper limit of the cumulative number of uses |
| Life threshold (hours) | The upper limit of the cumulative hours of use |
| Status | Available / In use / Under maintenance / Scrap |

If you set life thresholds, the [Tool Management](maintenance.html#tool-management) screen shows how much of the life has been used and notifies you when replacement is due.
Tools without a threshold are shown as "No threshold" and are not notified.

---

## ⑧ Inspection items and criteria  {#inspection-items}

**Tab: Inspection Items**

When an inspection order is issued, **the inspection items that match the inspection type, product and process are selected automatically**.
This means you cannot issue inspection orders unless items are registered here.

| Field | Description |
|---|---|
| Inspection item code / Name | Basic information |
| Inspection type | Receiving inspection / In-process inspection / Final inspection / Sample inspection |
| Product / Process | Where the item applies. Whichever you specify must match; if you specify both, the item is used only for "that process of that product". Whichever you leave blank is not checked. A process can be specified only for in-process inspections; receiving, final and sample inspections require a product |
| Spec lower limit / upper limit | Measured values are judged pass or fail automatically against this range |
| Nominal value | The target value (for display) |
| Method | A description such as visual or caliper |
| Sample size | The minimum number of samples needed for a judgment. The overall judgment cannot be made until this many results with different sample numbers are in (measuring more is fine) |
| Required skill / qualification | Specify this for items whose inspection requires a qualification. If the person registering the inspection result does not hold the qualification, or it has expired, an error occurs (the `RequiredSkillCode` column in CSV) |

<div class="note">
<p>Items with neither an upper nor a lower limit <strong>cannot be judged automatically</strong>.
For those items, you must choose pass or fail manually when entering inspection results.</p>
</div>

---

## ⑨ Checklists

**Tab: Checklists**

Define the checks to make for setup and before work. The categories are Common / Process / Product / Setup / Maintenance / HSE (health, safety and environment).

For each item, set the **Order**, the **Check item** and whether it is **Required**.
On the [setup screen](execution.html#setup), a record cannot be saved while any required item is unchecked.

---

## ⑩ Skills and qualifications

**Tab: Skills & Qualifications**

| Field | Description |
|---|---|
| Code / Name | Basic information |
| Type | Skill / Certification |

If you set what you define here as the "Required skill / qualification" of a routing and link it to users,
it is checked automatically when dispatching.

---

## ⑪ Users

**Tab: Users** (system administrators only)

Register and edit users and give them roles. For the steps, see [Installation and initial setup](getting-started.html#add-users).

From **Skills & Qualifications** in a user's row, register the skills and qualifications that the user holds.

| Field | Description |
|---|---|
| Skills & Qualifications | Choose from the registered skills and qualifications |
| Acquired | Optional |
| Expires | Optional. **An expired qualification causes an error when dispatching** |

---

## Bulk registration and export with CSV  {#csv}

Open **CSV import/export** on each tab to register or export master data in bulk with a CSV file.
Use it when there are many records at initial setup, or when you want to review everything at once in Excel.

| Button | What it does |
|---|---|
| Export CSV | Exports the registered contents as CSV (including inactive records) |
| Template (empty CSV) | Exports a CSV with only the column names. A starting point for new registrations |
| Import | Imports the selected CSV file |

Supported kinds: products, MBOM, routings (BOP), processes, equipment, tools, locations,
inspection items and criteria, checklists, skills and qualifications, users, and users' skills and qualifications.

**Importing requires the same permission as the form on that tab.**
Only system administrators can import skills and qualifications, users, and users' skills and qualifications;
the others require master data permission (System administrator or Production management).
Without permission, the import section itself is not shown (anyone can export,
except that user-related CSVs can be exported only by administrators).

### Sample CSV  {#sample-csv}

As an example of how to write them, a **complete set of sample data** that can be imported as is is available.
It models a small factory that produces gear pumps. If you import the files in the order of their numbers,
you can try everything from manufacturing order to shipment.

**[samples-master-csv.zip](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-master-csv.zip)**
(work centers, processes, locations, products, skills, shifts, equipment, maintenance parts, tools, checklists, defect reasons, inspection items, process control items, inspection devices, MBOM, work procedures, routings, users and maintenance procedures).
You can pass it as is to "Importing and exporting in bulk with ZIP" below. The CSVs inside can also be viewed in [samples/master-csv](https://github.com/msmsrep/ParallelFactoryMES/tree/main/samples/master-csv).

The sample data values (product names and so on) are in Japanese.
An easy way to start is to import this sample, see how things work, and then export the CSVs
and rewrite them with your own company's data.

### Importing and exporting in bulk with ZIP

With **Bulk import/export via ZIP** at the top of the Master Data screen, you can import several CSVs at once.

- Choose a ZIP containing the CSVs. **They are imported in file name order**, and the kind is determined by the part of the file name after "_"
  (`01_work-centers.csv` → work centers). The sample `samples-master-csv.zip` can be imported as is.
- **If even one file has an error, all files in the ZIP are rolled back.** The result shows the errors for each file and the files that were not processed.
- You need **permission to import every kind** contained in the ZIP (for example, if it contains a user CSV, only system administrators can import it).
- Actuals CSVs cannot be included. Actuals are imported as a ZIP on the **Actuals CSV Import** screen.
- Production plans are not master data either, so they cannot be included. Import production plans from the Plan entry tab of the **Production Plan vs. Actual** screen ([Production management](production.html#plan)).
- **Export all master data as ZIP** exports all the CSVs at once, numbered in import order. You can bulk import them as is into another environment
  (passwords are not exported for users, so when using it in a new environment, add `InitialPassword` to the user CSV).

### Import rules

- **If the code matches an existing record it is updated; otherwise a new record is registered** (product code, process code and so on).
  For equipment the key is the asset number, and for users it is the user name.
- **Columns can be in any order.** You can delete columns you do not need (deleted columns keep their current values).
  Conversely, if you keep a column and **leave it blank, that field becomes empty**.
- Files in **either UTF-8 or Shift_JIS can be imported** (files saved from Excel as "CSV (Comma delimited)" can be used as is).
- Types (product type, process type and so on) can be given as English values such as `Product`. <!-- 訳さない --> The Japanese labels (製品, 部材 and so on) are also accepted.
- Active / inactive is `true` / `false` (`1` / `0` are also accepted). <!-- 訳さない --> So are the Japanese `有効` / `無効`.

<div class="warn">
<p><strong>If even one row has an error, all rows are rolled back.</strong>
The errors are listed as "which row is wrong and how", so correct the CSV and import it again.
It never happens that only some rows are registered.</p>
<p>If you check <strong>Validate only</strong> before importing, only the presence of errors is checked and nothing is saved to the database.
Use it to check before importing into production data.</p>
</div>

### Bulk registration of MBOM, routings and checklists

For master data with lines, **rows with the same key are treated together as one record**, and **their lines are replaced as a whole**.

- **MBOM**: rows with the same `ParentProductCode` (parent product code) become the MBOM of that parent.
  In `RoutingSequence`, write the routing sequence of the process in which the component is used (blank means the final process). The rules for alternate part groups and the ban on loops are the same as when registering on the screen.
- **Routings**: rows with the same `ProductCode` (product code) become the routing of that product.
- **Checklists**: rows with the same `Code` become the items of one checklist.

The MBOM and routings of parent products that do not appear in the CSV are not changed.

<div class="note">
<p>The MBOM, routing and inspection item CSVs refer to other master data by product code, process code and so on.
<strong>Register products, processes and so on first</strong>, and then import them (unregistered codes cause errors).</p>
</div>

## Importing actuals with CSV {#actual-csv}

For testing, migration and training, **actuals can also be registered in bulk with CSV**: receiving, manufacturing orders, setups, checklists, material consumption, production results, process conditions, inspections, work time, trouble reports, equipment operation records, shipping orders, shipment judgments, shipments, maintenance plans, maintenance orders, maintenance results, tool usage and allocation, inspection device calibration, inventory operations, transfers, picking, stocktaking, nonconformances and retained samples.
Open **Administration → Actuals CSV Import** in the menu and import from the panel for each kind.

- Imports go through **the same checks** as registering one record at a time on the screen (an inspection device past its calibration date, consumption of an alternate part without a reason, insufficient inventory and so on cannot be imported).
  Inventory, lot statuses and the audit log are recorded in the same way.
- **Importing always registers new records.** Importing the same file twice gives you two sets of actuals.
- Work orders are identified by **order number + routing sequence**; lots to consume, inspect or ship by **lot number**; shipping orders by **shipment number**; maintenance orders by **maintenance order number**; stocktakes by **stocktake number**; and nonconformances by **nonconformance number** or **lot number** (when that lot has exactly one open nonconformance).
  Specify in the CSV the order numbers of manufacturing orders, the lot numbers of lots to be consumed later, the shipment numbers of shipping orders, the maintenance order numbers of maintenance orders, and the numbers of picking and stocktaking (automatically assigned numbers are not known until imported).
- **The recorder is the user who imports.** There is no column for specifying the operator.
- The permission needed to import each kind is the same as when registering it on the screen (logistics for receiving, shipping orders and shipments; production management for manufacturing orders; quality control for inspections; quality assurance for shipment judgments; and so on. Anyone can import maintenance spot requests and results and tool usage; only maintenance staff can import maintenance plans and planned maintenance orders; production management for tool allocation; quality control for inspection device calibration).
- "If even one row has an error, all rows are rolled back" and "Validate only" work the same as for master data CSVs.
- With **Bulk import via ZIP** at the top of the screen, you can import several CSVs at once (in file name order; if even one file has an error, all files in the ZIP are rolled back).

A **sample** with a complete set from receiving to shipment is available at
**[samples-actual-csv.zip](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv.zip)**
(contents in [samples/actual-csv](https://github.com/msmsrep/ParallelFactoryMES/tree/main/samples/actual-csv)).
After importing the master data from the [Sample CSV](#sample-csv), import these in the order of their file numbers.

**About three months of actuals** for trying the weekly and monthly trends on the dashboard and the breakdowns by process, shift, line and equipment are available at
**[samples-actual-csv-bulk.zip](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv-bulk.zip)**
(contents and highlights in [samples/actual-csv-bulk](https://github.com/msmsrep/ParallelFactoryMES/tree/main/samples/actual-csv-bulk)).
The period is 2026-06-29 to 2026-09-20, so set the dashboard's reference date within that period.
A sample production plan for the same period ([samples-production-plans.csv](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-production-plans.csv)) is also available (see [Production management](production.html#plan)).

### User CSV (system administrators only)

- New users need `InitialPassword` (the initial password: at least 8 characters, containing a lowercase letter and a digit).
  They are asked to change the password the first time they sign in.
- `Roles` is separated by semicolons (for example `Operator;QualityControl`).
- Writing `InitialPassword` in the row of an existing user **resets the password**. If it is blank, the password is not changed.
- Setting `IsActive` to `false` deactivates the user, and that user's sessions expire.

<div class="warn">
<p>The user CSV contains initial passwords in plain text. Handle the file with care, for example by deleting it
after importing. Exported CSVs do not contain passwords.</p>
</div>

---

## Notes on changing master data

- Changes to master data are **recorded in the audit log**.
- Saving an MBOM or routing replaces everything. For a design change, put together the complete changed structure before saving.
- Changing a routing **does not change work orders that have already been released**. The new routing applies to orders released after that.
- Instead of deleting products you no longer use, **deactivate** them. This keeps the links to past actuals and lots.
