---
layout: default
title: Logistics and inventory
nav_url: logistics.html
lead: From receiving components, through inventory operations and issuing to processes, to shipping products and stocktaking. This is the work of logistics and warehouse staff.
prev_url: execution.html
prev_title: Production execution
next_url: quality.html
next_title: Quality control
---

Operations that move inventory (inventory operations other than receiving, picking, shipping and stocktaking)
can be performed by **System administrators, Production management and Logistics / warehouse**.

---

## Receiving  {#receiving}

**Left menu: Logistics & Inventory → Receiving**

Put purchased components into inventory. **Before starting production, receive the components you need here.**

<ol class="steps">
<li>Choose the <strong>Product</strong>.</li>
<li>Enter the <strong>Quantity</strong>.</li>
<li>Choose the <strong>Put-away location</strong>.</li>
<li>Enter the <strong>Lot number</strong>. <strong>If you leave it blank, a number is assigned automatically</strong> (you can also scan the supplier's lot number).</li>
<li>If needed, enter <strong>Expires</strong> and <strong>Notes</strong>.</li>
<li>Press <strong>Register receipt</strong>.</li>
</ol>

On registration, a received lot is created and shown in the "Received lots" list below.

### Printing lot tags

With the **Lot tag** button in the list, you can print a lot tag with a barcode (Code39) of the lot number.

### Canceling a receipt

Pressing **Cancel receipt** cancels the receipt and the inventory it added.

<div class="warn">
<p><strong>A lot whose inventory has changed since receiving cannot be canceled.</strong>
(<code>The receipt cannot be canceled because the stock has changed since receiving (use a quantity adjustment instead).</code>)
In this case, use <strong>Adjust quantity</strong> on the Inventory screen instead.</p>
</div>

---

## Inventory inquiry and inventory operations

**Left menu: Logistics & Inventory → Inventory**

The screen has four tabs.

### Inquiry tab

Search inventory by lot number (scannable) and location. From each row you can do the following.

| Button | What it does |
|---|---|
| Actions | Goes to the "Operations" tab with that lot selected |
| Lot tag | Prints the lot tag (new tab) |
| Trace | Goes to the traceability screen for that lot |

### Operations tab  {#stock-ops}

<div class="note">
<p>First specify the lot, location, quantity and reason in <strong>"Select stock"</strong> at the very top of the screen,
and then press one of the operation buttons below. If you came from the <strong>Actions</strong> button on the Inquiry tab, the stock is already selected.</p>
</div>

| Operation | What it does | Fields used |
|---|---|---|
| Move | Moves inventory to another location | Destination location, quantity |
| Adjust quantity | Corrects the quantity to match the physical count | Counted quantity (after adjustment), reason |
| Split | Cuts part of a lot out into a new lot | Quantity, new lot number (blank for automatic numbering) |
| Merge | Combines the lot into a target lot | Target lot |
| Reassign | Moves inventory with a changed product or lot number | Target item or new lot number, quantity |
| Change status | Changes the stock status | Stock status, reason |
| Scrap | Scraps inventory | Quantity, reason |
| Return to supplier | Returns goods to the supplier and deducts them from inventory | Quantity, reason |
| Return from issue | Returns components issued to a process back to inventory | Quantity |

There are five **stock statuses**: Normal / On hold / Awaiting inspection / Defective / To be scrapped.
Issuing an inspection automatically sets the stock to "Awaiting inspection", and the judgment changes it to "Normal" or "Defective".

<div class="warn">
<p><strong>A quantity adjustment requires a reason</strong> (if none is entered, it is recorded with the fixed default reason <code>実棚差異</code>, meaning "count variance"). <!-- 訳さない -->
Quantity adjustments, status changes and scrapping are <strong>recorded in the audit log</strong>.</p>
<p>Only inventory of <strong>the same product in the same location</strong> can be merged.</p>
</div>

### Expiry alerts tab

Enter a number of days in "Show expiry within (days)" to list inventory that expires within that period
and inventory that has already expired. Expired stock has a red badge and stock expiring soon has a yellow one.

### Transactions tab

All inventory transactions are shown in date order.
The types are Receiving / Put-away / Issue / Process issue / Issue return / Move / Adjustment / Scrap / Return / Transfer / Split / Merge / Stocktake adjustment / Shipping.

---

## Transfer orders between processes  {#transfer}

**Left menu: Logistics & Inventory → Transfer Orders**

When you carry goods from one process to another, **create an order before moving them, and execute it once they have arrived**.
Separating the order from its execution lets the system keep the state "being carried, not yet arrived".

<ol class="steps">
<li>Choose the <strong>Lot</strong> (scannable). The source is set to the shelf of the chosen stock.</li>
<li>Specify the <strong>Quantity</strong> and <strong>Destination location</strong>, and press <strong>Create transfer order</strong>.</li>
<li>When the goods have been moved, press <strong>Execute (move stock)</strong> in the list. The inventory is moved at this point.</li>
</ol>

| Status | Meaning |
|---|---|
| Instructed | Created. The inventory has not moved yet |
| Completed | Executed. The inventory has moved to the destination |
| Canceled | Canceled. The inventory does not move |

<div class="note">
<p>If you only need to move inventory on the spot, use "Move" in <a href="#stock-ops">inventory operations</a>.
Use transfer orders when you want to keep the transport instruction and the completion report separately.</p>
</div>

The permissions are the same as for inventory operations (System administrator, Production management and Logistics / warehouse).

---

## Picking and issuing to processes  {#picking}

**Left menu: Logistics & Inventory → Picking & Issuing**

Use this when issuing components to a process, or when preparing products for shipment.

### Creating a picking order

<ol class="steps">
<li>Choose the <strong>Category</strong>: "Process issue" to issue to a process, or "Shipping" for shipment.</li>
<li>Depending on the category, choose <strong>Issue to work order</strong> or <strong>Issue to shipping order</strong>.</li>
<li>Enter the <strong>Product</strong> and <strong>Quantity</strong> and press <strong>Add line</strong>. You can add several lines.</li>
<li>Press <strong>Create picking order</strong>.</li>
</ol>

On creation, **inventory is allocated automatically on a first-in, first-out basis (earliest expiry first)**,
which decides which lot to take from which location.

<div class="warn">
<p>If there is not enough inventory, an error occurs here. Prepare inventory by receiving or by registering results in the previous process, and create it again.</p>
</div>

### Carrying out picking

<ol class="steps">
<li>Check the picking order in the list. Its lines show the allocated lot and location.</li>
<li>Scan the label of the actual goods in the "Verify" field. A green <strong>Match</strong> is shown if it matches the allocated lot, and a red <strong>Mismatch</strong> if not.</li>
<li>When everything has been checked, press <strong>Pick</strong>. The inventory is deducted and the status becomes "Completed".</li>
</ol>

<div class="note">
<p>The verification result is <strong>only a display for visual checking</strong>. The execute button can be pressed even on a mismatch,
so always confirm a match before executing.</p>
</div>

If you created one by mistake, you can cancel it with **Cancel** (only while it is still instructed).

---

## Shipping

**Left menu: Logistics & Inventory → Shipping**

<div class="warn">
<p><strong>To carry out a shipment, an approved shipment judgment ("Approved" or "Special acceptance") for that shipping order is required.</strong>
Without a judgment, an error occurs when shipping. The judgment is made by quality assurance staff
on the <a href="assurance.html">Shipment Judgments</a> screen.</p>
</div>

### Creating a shipping order

<ol class="steps">
<li>Enter the <strong>Destination</strong> and <strong>Planned ship date</strong>.</li>
<li>Enter the <strong>Product</strong> and <strong>Quantity</strong> and press <strong>Add line</strong>.</li>
<li>Press <strong>Create shipping order</strong>. A shipment number is assigned automatically.</li>
</ol>

### Carrying out a shipment

<ol class="steps">
<li>Open the shipping order from the list.</li>
<li>Choose the <strong>Lot to ship</strong> (only inventory of the products in the lines is shown).</li>
<li>Enter the <strong>Quantity</strong>.</li>
<li>Press <strong>Ship and report completion</strong>.</li>
</ol>

Shipping deducts the inventory and increases the "Shipped" quantity of the line.

If the shipment judgment has not been approved, the following error occurs.

> `Shipping is not possible because there is no approved shipment judgment (approved or special acceptance) (H-10-10).`

### Shipping slips

You can print a slip from the **Shipping slip** button of each shipping order.

---

## Stocktaking

**Left menu: Logistics & Inventory → Stocktaking**

<ol class="steps">
<li>Choose the <strong>Location</strong> (if none is chosen, all locations).</li>
<li>Press <strong>Create stocktake (snapshot current stock)</strong>. The inventory at this point is fixed as the book quantity.</li>
<li>Press <strong>Open</strong> in the list to show the lines.</li>
<li>Count the actual goods and enter the <strong>Counted qty</strong> on each line.</li>
<li>Press <strong>Save counts</strong>. The variances are calculated, and lines that are not 0 are shown in red.</li>
<li>After checking the variances, press <strong>Finalize (adjust variances)</strong>. The variances are applied to inventory and the status becomes "Finalized".</li>
</ol>

With the **Count sheet** button, you can print a count sheet to fill in on the floor.
To abandon it, cancel the stocktake itself with **Cancel**.

<div class="note">
<p>If there is no target inventory when creating a stocktake, an error occurs (<code>There is no target stock.</code>).
Review the location you specified.</p>
</div>
