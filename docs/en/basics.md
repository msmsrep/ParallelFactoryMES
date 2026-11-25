---
layout: default
title: Screen layout and common operations
nav_url: basics.html
lead: How to read and use the parts common to every screen. Reading this first makes the rest of the guide easier to follow.
prev_url: getting-started.html
prev_title: Installation and initial setup
next_url: walkthrough.html
next_title: "Sample walkthrough: from production to shipping"
---

## Screen layout

After you sign in, the screen consists of the menu on the left and the work area on the right.

### Left menu

| Group | Menu | Contents |
|---|---|---|
| — | Dashboard | Daily, weekly and monthly trends of the KPIs (defect rate, operating rate) with breakdowns by process, product, shift, line and equipment, and the process progress of manufacturing orders |
| Production & Execution | Manufacturing Orders | Registering, approving and releasing orders to processes |
| | Dispatching | Assigning operators, equipment and start order to work orders |
| | Work Orders | The list of work orders and the entry point to setup and recording results |
| | Process Progress & Issues | Progress totals by process, trouble reports and their handling history |
| Logistics & Inventory | Receiving | Registering received materials and printing lot tags |
| | Inventory | Searching inventory, and moving, adjusting, splitting, scrapping and so on |
| | Picking & Issuing | Creating and carrying out picking orders |
| | Shipping | Shipping orders, carrying out shipments and delivery slips |
| | Stocktaking | Stocktaking orders, entering counts and adjusting differences |
| Quality | Inspections | Issuing inspection orders, entering results, judging and approving |
| | Nonconformances | Raising and handling deviations and defects |
| | Quality Analysis | Totals of defect rates and nonconformances |
| | Shipment Judgments | Judging and approving whether goods may be shipped |
| | Traceability | Tracing lots |
| Equipment & Maintenance | Maintenance | Operation records, maintenance plans, orders and results, procedures |
| | Tool Management | Usage records and life management |
| Administration | Master Data | Registering and editing all kinds of master data |

Every menu item is shown to everyone, regardless of role. However, **for operations you are not permitted to perform,
the buttons and forms (for registering, approving and so on) are not shown at all** (you can still view the lists).

## Common operations

### Filtering lists

Many list screens have a status drop-down at the top. Choosing a status filters the list immediately.
Choose "All statuses" to clear the filter.

### Messages

- Green bar: a message that the operation succeeded
- Red bar: what went wrong. The operation was not carried out

Error messages show the reason given by the server as is. The messages and what to do about them are collected in [Troubleshooting](troubleshooting.html).

### Status badges

The "Status" column of a list is shown as a colored badge.

| Color | Example meanings |
|---|---|
| Blue (information) | In progress, Instructed |
| Yellow (attention) | Judged, Action needed |
| Green (done) | Approved, Completed, Pass |
| Red (warning) | Canceled, Fail, Expired, Past Due |

The statuses of each business area are listed in the [Reference](reference.html).

### Tabs

The Inventory, Maintenance, Master Data and Traceability screens switch their contents with tabs at the top.
Each time you switch tabs, the data is loaded again.

## Reading barcodes / QR codes

Fields with a **📷 Camera** button to their right accept scanned input.

| Method | How to use it |
|---|---|
| USB barcode reader | Click the field and then scan. The Enter sent by the reader confirms the input |
| Camera | Press **📷 Camera** to show the video. Point it at a code and it is entered automatically. Press **Stop** to finish |
| Typing | You can simply type with the keyboard |

<div class="warn">
<p><strong>Camera scanning works only over HTTPS</strong> (<code>localhost</code> is the exception).
This is a browser restriction. If you run the system over HTTP, use a USB reader or type the code.
Where the camera cannot be used, a message says that the camera is unavailable.</p>
</div>

Scanned input is supported for the lot number on Receiving, lot search on Inventory,
checking during picking, the target lot of an inspection order and the lot number on Traceability.

## Printing reports

Pressing a button such as "Lot tag", "Instruction" or "Inspection certificate" in a list opens a print page **in a new tab**.
You can print or save as PDF with the print button at the top of the page or with the browser's print function (Ctrl+P).
For details, see [Reports, labels and scanning](reports.html).

## How dates are handled (business date)

Dashboard totals, quality analysis periods and the dates used in numbering are based not on the calendar date but on the **business date (production date)**.
By default the date changes at **6:00 a.m.**

This means that actuals registered at 3:00 a.m. on May 2 are counted under the production date "May 1".
This keeps night-shift actuals that cross midnight together under the same production date. The boundary time can be changed in the settings
([Server deployment and settings](operations.html#settings)).
