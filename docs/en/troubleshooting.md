---
layout: default
title: Troubleshooting
nav_url: troubleshooting.html
lead: Common error messages and what to do about them. Look for the text shown in the red bar.
prev_url: reference.html
prev_title: Reference
next_url: operations.html
next_title: Administration
---

Messages are shown here as they appear when the screen language is English. With the screen in Japanese, they appear in Japanese; see the [Japanese version](../troubleshooting.html) of this page.

## Sign-in and accounts

| Symptom / message | Cause and what to do |
|---|---|
| The user name or password is incorrect. | Check what you entered. Upper and lower case are distinguished |
| The account is temporarily locked. Please wait a while and try again. | The account was locked because sign-in kept failing. Try again after a while, or ask an administrator to reset your password |
| The sign-in screen says "Initial setup is required." | No users are registered. Create the initial administrator from the link |
| The dashboard says you are still using the initial password | Change your password from the link. The message keeps appearing until you do |
| Setup failed. Check the password requirements (8+ characters, lowercase letters and digits). | The password does not meet the condition: **at least 8 characters, containing a lowercase letter and a digit** |
| Initial setup has already been completed. | Users already exist. Sign in from the sign-in screen as usual |
| The button for the operation you want is not on the screen | Your roles do not have the permission. Check [Roles and permissions](roles.html) and ask an administrator to give you the role |

## Manufacturing orders and release to processes

| Message | What to do |
|---|---|
| An order with status '...' cannot be released (only approved orders). | **Approve** it first |
| Release to processes fails / no work orders are created | **No routing (BOP) is registered** for the product. Register one in [Master data](masters.html#bop) |
| An order with status '...' cannot be changed (after release it can only be canceled). | The contents cannot be changed after release. Cancel it and register it again |
| The item ID does not exist or is inactive. | The product has been deactivated. Check it in Master Data |
| A rework order requires the source order ID (sourceOrderId). | Create rework orders from the "Rework" action in Nonconformances |

## Dispatching

| Message | What to do |
|---|---|
| Skill '...' of user '...' has expired. | Update the expiry date under Master Data → Users → Skills & Qualifications (this appears when dispatching, starting work, and registering maintenance records and inspection results) |
| The assigned operator does not exist or is inactive. | The user has been deactivated |
| A work order with status '...' cannot be dispatched. | Work orders that have been started or later cannot be dispatched |
| The Worker drop-down shows only "Unassigned" | You do not have permission to view the user list (system administrator). You can still assign only the equipment and start sequence |

## Recording results

| Message | What to do |
|---|---|
| A record for the final process requires a put-away location (outputLocationId). | Choose the **Put-away location** before registering |
| Backflush is not possible because the planned materials are missing (no MBOM at release). | Register the MBOM in [Master data](masters.html#mbom) and recreate the order, or untick Backflush |
| Cannot backflush because no planned materials are consumed at this step (sequence ...). | No components are used in this process. Untick Backflush. If the process does use components, correct the consuming step in the [MBOM](masters.html#mbom) and recreate the order |
| The consuming step (sequence ...) of MBOM material '...' is not in the routing of item '...'. Correct the MBOM or the routing. | Correct the consuming step in the [MBOM](masters.html#mbom) or the routing, and then release |
| The total of good and defective quantities must be greater than 0. | Enter a quantity in one of them |
| Results cannot be recorded for a work order with status '...'. | It has already been approved or canceled |
| A work order with status '...' cannot be approved (only completed work orders). | Register results to make it "Completed", then approve it |
| Backflushing fails with an insufficient inventory error | Prepare component inventory by receiving or by registering results in the previous process |

## Inventory and receiving

| Message | What to do |
|---|---|
| The receipt cannot be canceled because the stock has changed since receiving (use a quantity adjustment instead). | Correct it with **Adjust quantity** on the Inventory screen |
| No adjustment is needed because the quantity is the same as the current quantity. | The quantity after adjustment is the same as the current quantity |
| Lots of different items cannot be merged. | Only inventory of the same product in the same location can be merged |
| The source and target lots are the same. | Specify a different lot as the target |
| The source lot has no stock. | The inventory of the lot is 0 |
| Specify a new item ID (item transfer) or a new lot number (lot transfer). | Enter either the target item or a new lot number |
| Select a lot. | Choose a lot in "Select stock" on the Operations tab before operating |

## Picking, issuing and shipping

| Message | What to do |
|---|---|
| Creating a picking order fails (insufficient inventory) | Prepare inventory by receiving or with results of the previous process |
| Process issue requires a valid work order ID (workOrderId). | Choose the work order to issue to |
| Shipping picking requires a valid shipping order ID (shippingOrderId). | Choose the shipping order to issue to |
| Shipping is not possible because there is no approved shipment judgment (approved or special acceptance) (H-10-10). | Ask quality assurance staff to register and **approve** a [shipment judgment](assurance.html). Goods cannot be shipped with an "On hold" judgment |
| A shipping order with status '...' cannot be executed. | It has already been completed or canceled |

## Inspections and nonconformances

| Message | What to do |
|---|---|
| No inspection criteria match the target. Register inspection items or specify itemIds. | In [Master Data → Inspection Items](masters.html#inspection-items), register items that match the inspection type, product and process |
| In-process inspection requires a target work order ID (targetWorkOrderId). | Choose the work order for an in-process inspection |
| A target lot ID (targetLotId) is required. | Specify the lot for inspections other than in-process inspections |
| Inspection item '...' cannot be judged automatically from specification limits. Specify judgment. | Choose Pass or Fail in "Judgment (manual)" on that row |
| N inspection items have no results. Register results for all items before judging. | Enter results for every item before judging |
| An approved inspection cannot be corrected. | It cannot be corrected after approval. Issue a reinspection order |
| Enter a correction reason. | A reason is required to correct inspection results |
| A closed nonconformance cannot be given an action instruction. | Raise a new nonconformance |
| A nonconformance cannot be approved before an action is instructed. | Instruct the action type first |

## Equipment maintenance and tools

| Message | What to do |
|---|---|
| Stop and failure records require a stop cause (stopCause) (B-40-20-02). | Enter the stop cause |
| Specify either a target equipment ID or a target tool ID, not both. | Choose only one of equipment or tool |
| Record either the usage count or the usage time. | Tool usage records need either a count or a time |
| The operation summary shows no figures | Only operation records **with an end time** are included |
| An order cannot be created from a maintenance plan with status '...'. | Orders can be issued only from plans in the "Planned" status |

## Scanning and printing

| Symptom | What to do |
|---|---|
| The camera is unavailable (unsupported browser or permission denied). | An HTTPS connection is required (`localhost` is the exception). Use a USB barcode reader or type the code |
| You denied the camera permission once | Change the camera permission to "Allow" from the icon in the browser's address bar |
| The report page does not open | A pop-up blocker may be stopping it. Allow pop-ups for this site in the browser settings |
| You want to save as PDF | Choose "Save as PDF" as the printer in the print dialog |

## Installation and startup  {#install}

| Symptom | What to do |
|---|---|
| Starting the ZIP edition shows "Windows protected your PC" | This warning is attached to files obtained from the internet. You can start it with **More info → Run anyway**. To stop it appearing every time, check **Unblock** in the properties of the ZIP file before extracting, and extract it again |
| On Linux, `./start.sh` fails with "Permission denied" | ZIP files do not keep execute permissions. Start it with `sh start.sh` (it gives the program execute permission when it starts) |
| The ZIP edition closes immediately after starting / says the port is in use | Another application is using port 5000. Change the port with `MESAPP_URLS` before starting (for example `set MESAPP_URLS=http://0.0.0.0:5100`) |
| Data disappeared after recreating the Docker container | `/data` is not a volume. Start it with `-v mesapp-data:/data` ([Administration](operations.html#docker)) |
## Display and connection

| Symptom | What to do |
|---|---|
| The screen stays at "Loading..." | The server has stopped or the network is disconnected. Check with your administrator |
| You are suddenly returned to the sign-in screen while working | The access token has expired. Sign in again. The expiry time can be changed in the settings |
| You cannot connect from another PC | The server's firewall must allow incoming connections on the port ([Administration](operations.html)) |

---

## If the problem is still not solved

When you contact your administrator, giving the following information makes things go smoothly.

- The screen you were using and the button you pressed
- The **exact error message** shown on the screen
- The numbers involved (order number, work order number, lot number and so on)
- The date and time of the operation and the signed-in user name
