---
layout: default
title: Reports, labels and scanning
nav_url: reports.html
lead: The reports you can print, and how to work with barcodes and QR codes.
prev_url: maintenance.html
prev_title: Equipment maintenance and tools
next_url: roles.html
next_title: Roles and permissions
---

## Reports you can print

Pressing a report button opens a print page **in a new tab**.
Print or save as PDF with the print button at the top of the page or with the browser's print function (Ctrl+P).

| Report | Where to print it from | Contents |
|---|---|---|
| Work instruction | **Instruction** in the Work Orders list or the manufacturing order details | Work order number, order number, product, process, planned quantity, assignee, equipment, and the components used in this process (the order's planned materials) |
| Lot tag (label) | **Lot tag** on Receiving, Inventory or Traceability | Product, quantity, production / receiving date, expiry date, status, grade, and a **barcode of the lot number** |
| Inspection certificate | **Inspection certificate** in the inspection details | Specification limits, measured values and judgment for each inspection item |
| Shipping slip | **Shipping slip** in Shipping | Destination, planned date and lines |
| Shipment judgment | **Judgment doc** in Shipment Judgments | Judgment result, judged by and judgment date |
| Count sheet | **Count sheet** in Stocktaking | The lines to be counted (for writing in the counts) |

<div class="note">
<p>To keep a PDF, choose "Save as PDF" (Microsoft Print to PDF and so on) as the printer in the print dialog.</p>
</div>

---

## Working with barcodes and QR codes

### Barcodes on lot tags

The lot number is printed on the lot tag as a **Code39** barcode.
If you print lot tags when receiving and when production is completed and attach them to the goods, later work can be done by scanning.

### Where scanning can be used

| Screen | Used for |
|---|---|
| Receiving | Registering the supplier's lot number as is |
| Inventory | Searching inventory by lot number |
| Picking & Issuing | Checking the actual label against the allocated lot when picking |
| Inspections | Specifying the lot to inspect |
| Traceability | Specifying the lot to trace |

### How to read codes

| Method | Preparation | How to use it |
|---|---|---|
| USB barcode reader | Just connect it to the device by USB (it is recognized as a keyboard) | Click the field and then scan. The Enter sent by the reader confirms the input |
| The device's camera | An HTTPS connection is required | Press **📷 Camera** and show the code. Once read, it is entered automatically and the camera stops |
| Typing | None | Type directly with the keyboard |

<div class="warn">
<p><strong>Camera scanning works only over HTTPS or on <code>localhost</code>.</strong>
This is a browser security restriction and cannot be worked around by the system.</p>
<p>If you run on a LAN over HTTP, <strong>we recommend using a USB barcode reader</strong>.
To use the camera, put the system behind a reverse proxy (IIS / nginx) with HTTPS
(<a href="operations.html#https">Running with HTTPS</a>).</p>
</div>

Where the camera cannot be used, the following message is shown.

> The camera is unavailable (unsupported browser or permission denied). Use a USB barcode reader or type the code.

If the browser asks for permission to use the camera, choose "Allow".
If you denied it once, you can change the permission from the icon in the browser's address bar.

---

## Tips for the shop floor

- **Print and attach a lot tag as soon as goods are received.** All later picking checks, inspections and tracing can then be done by scanning.
- **Always check before carrying out picking.** The Match / Mismatch display is for visual checking, and the execute button can be pressed even on a mismatch.
- **Hand out work instructions to each process.** They list the components used in that process (including alternate parts), which helps prevent mixing up components.
