---
layout: default
title: Roles and permissions
nav_url: roles.html
lead: The seven roles and a table of the operations each can perform. A user can be given more than one role.
prev_url: reports.html
prev_title: Reports, labels and scanning
next_url: reference.html
next_title: Reference
---

## Roles

| Role | Intended for | Main permissions |
|---|---|---|
| System administrator | IT / implementation staff | All master data management and user management. Can also perform every operation of the other roles |
| Production management | Production planning and control | Registering production plans; issuing, approving, releasing and dispatching manufacturing orders; master data management; inventory operations |
| Operator | Line operators | Checking work orders; recording setups, checklists and actuals |
| Logistics / warehouse | Warehouse and shipping | Receiving, inventory operations, picking and issuing, shipping, stocktaking |
| Quality control | Quality control (QC) | Ordering, recording, judging and approving inspections; instructing and approving the handling of nonconformances |
| Quality assurance | Quality assurance (QA) | Shipment judgments and their approval |
| Equipment maintenance | Maintenance | Managing maintenance plans and procedures, canceling maintenance orders, tool maintenance |

<div class="note">
<p><strong>A system administrator can perform every operation.</strong> For a small operation, it is enough to start with
one system administrator and a few users with the roles for their work.</p>
</div>

## Permissions by operation

○ = allowed. Blank = the buttons and forms are not shown (anyone can view the lists).
**The audit log is the one exception: only system administrators can view it at all** (it is not shown in the menu either).

| Operation | System administrator | Production management | Operator | Logistics / warehouse | Quality control | Quality assurance | Equipment maintenance |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| Registering and editing master data (products, processes, equipment and so on) | ○ | ○ | | | | | |
| User management and granting skills & qualifications | ○ | | | | | | |
| Registering, changing, deleting and importing (CSV) production plans | ○ | ○ | | | | | |
| Registering, approving, changing, releasing and canceling manufacturing orders | ○ | ○ | | | | | |
| Dispatching (assigning operators, equipment and start order) | ○ | ○ | | | | | |
| Recording setups and carrying out checklists | ○ | ○ | ○ | | | | |
| Starting work and registering actuals | ○ | ○ | ○ | | | | |
| Recording material consumption and process control data | ○ | ○ | ○ | | | | |
| Recording work time (direct and indirect) | ○ | ○ | ○ | | | | |
| Approving production completion | ○ | ○ | | | | | |
| Correcting production actuals | ○ | ○ | | | | | |
| Trouble reports and recording their handling | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Registering and canceling receipts | ○ | ○ | | ○ | | | |
| Inventory operations (moving, adjusting, splitting, scrapping and so on) | ○ | ○ | | ○ | | | |
| Creating, carrying out and canceling transfer orders | ○ | ○ | | ○ | | | |
| Creating and carrying out picking orders | ○ | ○ | | ○ | | | |
| Creating shipping orders and carrying out shipments | ○ | ○ | | ○ | | | |
| Creating stocktakes, registering counts and confirming | ○ | ○ | | ○ | | | |
| Issuing inspection orders; entering, judging, approving, correcting and canceling results | ○ | | | | ○ | | |
| Raising nonconformances and recording their handling | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Instructing and approving the handling of nonconformances | ○ | | | | ○ | | |
| Registering and approving shipment judgments | ○ | | | | | ○ | |
| Recording equipment operation | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Creating, changing and canceling maintenance plans | ○ | | | | | | ○ |
| Creating maintenance orders (spot requests) | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Registering maintenance results | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Canceling maintenance orders and managing maintenance procedures | ○ | | | | | | ○ |
| Recording tool usage | ○ | ○ | ○ | ○ | ○ | ○ | ○ |
| Viewing the audit log | ○ | | | | | | |

## Assigning roles

Set roles for each user with the check boxes on the **Master Data → Users** tab (system administrators only).
You can give a user several roles at once.

### Assignment examples

| Person | Roles to give |
|---|---|
| Implementation lead | System administrator |
| Production planner | Production management |
| Line operator | Operator |
| Warehouse staff (who also inspect) | Logistics / warehouse + Quality control |
| Quality assurance manager | Quality assurance |
| Maintenance staff | Equipment maintenance |

## What you see without permission

- Registration forms and approval buttons are **simply not shown** (they are hidden, not grayed out).
- Every role can view the lists.
- If you open something that needs permission for the whole screen, such as the audit log, **"Access Denied"** is shown.
- The Worker drop-down on the Dispatching screen shows **only active users** (deactivated employees do not appear).
  Only system administrators can register and edit users.

If you cannot find the button for the function you want, check your roles first.
