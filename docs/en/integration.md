---
layout: default
title: Integrations
nav_url: integration.html
lead: "How to connect the MES with other systems and equipment: folder integration for CSV, API keys and the integration API, receiving equipment events, and Webhook notifications."
prev_url: operations.html
prev_title: Server deployment and settings
next_url: security.html
next_title: Security and audit log
---

## Folder integration  {#folder-link}

CSV files placed in a chosen folder are imported automatically. CSV files exported by sales or production management software
can be imported without writing a program (for example, have the manufacturing order CSV exported every morning placed in a shared folder).

### Setting it up

1. Create the user who performs the imports (for example `folder-link`). Give it **only the roles needed for the types you want to import**
   (Production management for manufacturing orders, Logistics / warehouse for receiving; see [Roles and permissions](roles.html) for what each role can do).
   Whether an import is allowed, who is recorded on actuals, and the audit log are all decided and recorded as this user.
2. Set the integration folder and the user, then restart.

```powershell
$env:FolderLink__Root="D:\mes-link"; $env:FolderLink__UserName="folder-link"; ./start.cmd
```

With Docker, mount the integration folder from a folder on the host (example for `compose.yaml`).

```yaml
    volumes:
      - mesapp-data:/data
      - ./mes-link:/link
    environment:
      FolderLink__Root: /link
      FolderLink__UserName: folder-link
```

Changing the settings requires a restart. On startup, `inbox`, `processed` and `error` are created under the integration folder.

### Where to put files

| Location | How the type is determined | Example |
|---|---|---|
| Directly in `inbox` | File name (the part after the first "_") | `inbox/manufacturing-orders.csv`, `inbox/20261122_manufacturing-orders.csv` |
| `inbox/<type>/` | Folder name (any file name) | `inbox/manufacturing-orders/orders.csv` |
| A ZIP directly in `inbox` | File names inside (same as ZIP bulk import) | `inbox/masters.zip` |

- The type names and CSV columns are the same as the templates on the Master Data and Actuals CSV Import screens. For software that cannot change the name of the file it exports, have it export into the type's folder.
- Files other than CSV and ZIP are ignored. Files being written are imported after writing finishes.
- Files are imported one at a time in the order they were placed (oldest modification time first). When a later file refers to an earlier one (receiving after manufacturing orders, for example), place them in order or put them in one ZIP.

### Checking the results

- **Imported files are moved to `processed` and files that could not be imported to `error`**, under `date/time/`, with a result file (`.result.txt`) beside them. The line numbers and reasons for errors are written there.
- Imports go through the same checks as importing from the screens, and **if even one row has an error, nothing in that file is applied**. Put the corrected file back in the same place in `inbox` to import it again.
- System administrators can check settings errors and recently processed files under "Administration" → "Folder integration" in the menu. "Import now" imports without waiting for the next interval.
- If the user does not exist, is inactive or is locked out, nothing is imported and the files stay in `inbox` (the reason is shown on the screen).

There is no integration that writes data out of the MES yet (automatic export of actuals).

## API keys  {#api-keys}

Keys that systems such as sales or production management software, or in-house scripts, use to call the MES **integration API** (`/api/integration/v1/...`) without signing in.

### Issuing a key

1. Create the user the key will act as (for example `erp-link`). **What the key can do is determined by this user's roles.** Create a dedicated user for each purpose and give it only the roles it needs ([Roles and permissions](roles.html)).
2. A system administrator opens "Administration" → "API keys" in the menu, specifies the purpose, the user and an optional expiry, and clicks **Issue key**.
3. Copy the key shown (a string starting with `pfm_`) into the settings of the connecting system. **The key is shown only at this time.** If it is lost, revoke it and issue a new one.

### Trying it

Send the key in the `X-Api-Key` request header. The following endpoint shows who you are treated as (the user and roles).

```powershell
Invoke-RestMethod http://localhost:5000/api/integration/v1/whoami -Headers @{ "X-Api-Key" = "pfm_..." }
```

```bash
curl -H "X-Api-Key: pfm_..." http://localhost:5000/api/integration/v1/whoami
```

### Notes

- Keys work only with the integration API. The APIs used by the screens (such as `/api/products`) cannot be called with a key (401).
- Operations performed with a key are recorded in the audit log as the linked user.
- **A revoked key cannot be restored.** A key also stops working immediately when its user is deactivated or its expiry passes. When the user's roles change, the next call is checked with the new roles.
- Treat keys like passwords. Do not paste them into email or chat; keep them only in the settings of the connecting system.

## Integration API  {#integration-api}

An API for exchanging data with the MES from sales or production management software (ERP) or in-house scripts. Calls use an [API key](#api-keys).

### Specification (OpenAPI)

The integration API specification is published at `/openapi/integration-v1.json` (for example `http://localhost:5000/openapi/integration-v1.json`).
Load it into Postman, Visual Studio or similar tools to see the endpoints, parameters and response shapes. No key is needed to get the specification.

### Endpoints

| Endpoint | Purpose |
|---|---|
| `GET /api/integration/v1/whoami` | Check that the key works and who you are treated as, with which roles |
| `GET /api/integration/v1/csv-kinds` | The CSV types and columns that can be imported (the same as the Master Data and Actuals CSV Import screens) |
| `POST /api/integration/v1/masters/{type}` | Import a master data CSV (overwritten by code) |
| `POST /api/integration/v1/actuals/{type}` | Import an actuals CSV (always registered as new) |
| `POST /api/integration/v1/masters/bundle`, `actuals/bundle` | Import a ZIP of CSV files |
| `GET /api/integration/v1/masters/{type}` | Export master data as CSV |
| `GET /api/integration/v1/production-results` | Read production results from where you left off |
| `GET /api/integration/v1/production-result-corrections` | Read corrections to production results from where you left off |
| `POST /api/integration/v1/equipment-events` | Send equipment status changes ([Receiving equipment events](#equipment-events)) |

### Importing CSV

Send the CSV as the request body (UTF-8 or Shift_JIS). Add `?dryRun=true` to only validate without applying anything.
The PowerShell examples on this page are for PowerShell 7 (`pwsh`).

```powershell
$headers = @{ "X-Api-Key" = "pfm_..."; "Idempotency-Key" = "orders-20261123" }
Invoke-RestMethod -Method Post -Headers $headers -ContentType "text/csv; charset=utf-8" -InFile .\manufacturing-orders.csv -Uri http://localhost:5000/api/integration/v1/actuals/manufacturing-orders
```

```bash
curl -H "X-Api-Key: pfm_..." -H "Idempotency-Key: orders-20261123" -H "Content-Type: text/csv" --data-binary @manufacturing-orders.csv http://localhost:5000/api/integration/v1/actuals/manufacturing-orders
```

- **200** when imported, and **422** if even one row has an error, in which case nothing is applied. In both cases the response body contains the counts and errors with line numbers (in PowerShell 7, add `-SkipHttpErrorCheck` to also receive the body of a 422).
- Import permission is checked with the roles of the user linked to the key, the same as on the screens (403 without permission).

### Sending again without registering twice (Idempotency-Key)

Actuals CSV files are **always registered as new**, so sending the same CSV again after a dropped connection can register it twice.
To prevent this, put a value chosen for each content you send (for example the order export time and a sequence number, or a UUID) in the `Idempotency-Key` header.

- If you send the same key with the same content again, **the previous response is returned without importing again** (the response has the header `Idempotent-Replayed: true`).
- A request with the same key but different content gets 409. Use a new key for new content.
- Keys are remembered for 24 hours, separately for each API key.

### Reading production results (from where you left off)

Pass the previous `nextAfterId` as `afterId` to get only the results recorded after it. Keep reading while `hasMore` is true.
Save the last `nextAfterId` so that the next run starts where this one ended.

```powershell
$headers = @{ "X-Api-Key" = "pfm_..." }
$after = if (Test-Path .\last-id.txt) { Get-Content .\last-id.txt } else { 0 }
do {
  $page = Invoke-RestMethod -Headers $headers -Uri "http://localhost:5000/api/integration/v1/production-results?afterId=$after"
  $page.items | Export-Csv .\production-results.csv -Append -Encoding utf8
  $after = $page.nextAfterId
} while ($page.hasMore)
Set-Content .\last-id.txt $after
```

- Quantities are the current values after any correction. To learn that a result you already read was corrected later, read `production-result-corrections` from where you left off in the same way.
- Results not yet approved are also returned (`approvedAt` is empty). Results of canceled orders are also returned because the physical lots remain (see `workOrderStatus`).

### Promises that keep integrations working after upgrades

- Within v1 (`/api/integration/v1`), **fields and endpoints are only added**, never removed, renamed or given a new meaning. Such changes go into a v2.
- The same applies to CSV columns. Columns are not removed and no required columns are added (if you leave out a new column, things work as before). When a column is renamed, its former name can still be read.
- Build your integrations to skip fields they do not know, in case new fields are added to responses.

## Receiving equipment events  {#equipment-events}

A mechanism that records equipment operation status (running, stopped, setup, failure, idle) automatically from the equipment.
**The MES does not communicate with PLCs directly.** A gateway (such as Node-RED) reads the PLC status and sends it to the MES integration API when the status changes.
The MES sorts the received status changes by time for each piece of equipment and builds them into **intervals in the operation history** (start to end).

```
Equipment / PLC ──(OPC UA, MC protocol, Modbus, etc.)──▶ Gateway ──(HTTP, API key)──▶ MES
                                                          (Node-RED, etc.)  POST /api/integration/v1/equipment-events
```

### Preparation

1. In the equipment master (the **Equipment (BOE)** tab in master management), turn on **Collect operation status from equipment events** for the target equipment.
   Operation cannot then be registered for that equipment by manual entry or actuals CSV (so the same time is not counted twice by hand and automatically). Stop causes can still be added.
2. Create a user for the gateway and issue an [API key](#api-keys). Receiving does not depend on roles.
3. If needed, register a **Stop cause mapping** (master management) that replaces alarm codes with stop causes.

### What to send

Create one event each time the status changes, collect them, and send them together (up to 1,000 at a time).

```json
{
  "events": [
    { "equipmentCode": "EQ-06", "eventId": "EQ-06-1732323600000-1", "occurredAt": "2026-11-23T09:00:00+09:00", "state": "Running" },
    { "equipmentCode": "EQ-06", "eventId": "EQ-06-1732324200000-2", "occurredAt": "2026-11-23T09:10:00+09:00", "state": "Stopped", "alarmCode": "A-101" }
  ]
}
```

| Item | Description |
|---|---|
| `equipmentCode` | The equipment asset No. |
| `eventId` | A unique ID assigned by the gateway (unique per equipment, up to 100 characters). Keep **the same value when resending** |
| `occurredAt` | The time the status changed on the equipment (with an offset such as `+09:00`) |
| `state` | `Running` / `Stopped` / `Setup` / `Failure` / `Idle` / `Off` (power off or connection lost) |
| `alarmCode` | Optional. The alarm code for a stop or failure (the cause is looked up in the stop cause mapping) |
| `workOrderNo` + `operationSeq` | Optional. The work order being processed (a pair of order number and operation sequence) |
| `note` | Optional. A note |

```powershell
$headers = @{ "X-Api-Key" = "pfm_..." }
$body = @{ events = @(@{ equipmentCode = "EQ-06"; eventId = "test-1"; occurredAt = (Get-Date).ToString("o"); state = "Running" }) } | ConvertTo-Json -Depth 4
Invoke-RestMethod -Method Post -Headers $headers -ContentType "application/json" -Body $body -Uri http://localhost:5000/api/integration/v1/equipment-events
```

### What happens after an event arrives

- When a new status arrives, the open interval is closed and the next interval is opened. Repeated events with the same status do not change the interval. `Off` only closes the interval.
- Events sent together may be out of time order; they are re-sorted by time before being applied.
- **Resending the same `eventId` results in "Duplicate" and is not recorded twice.** When the connection drops, resend the collected events as they are.
- **Events earlier than the last applied time are rejected as late arrivals.** Past intervals are not rebuilt. Synchronize the clocks of the equipment and gateway with NTP. Times too far ahead of the server time (more than 300 seconds by default) are also rejected.
- The response is 200 and contains a result for each event (`Accepted`, `Duplicate`, or `Rejected` with the reason). It is 200 even if all are rejected. A rejected `eventId` is judged again when it is corrected and resent.
- For stop and failure intervals, the stop cause is looked up from the alarm code in the stop cause mapping. If it is not found, the interval is recorded **without a stop cause**, which can be entered later on the operation monitoring tab in maintenance.
- The work order is linked if specified. If not specified, it is linked **only when exactly one started work order is assigned to that equipment**.

You can see what is being received under **Current equipment status** on the operation monitoring tab in maintenance. When no events have been received for a while (10 minutes by default), "(no events received)" appears for that equipment, so you can notice that the gateway has stopped.

### Trying the sample (Docker)

`compose.yaml` has an `equipment` profile that also starts an MQTT broker (Eclipse Mosquitto) and a Node-RED gateway.
Node-RED contains a **simulated piece of equipment** that changes status every few seconds and a flow that receives MQTT messages and sends them to the MES together (`samples/equipment-gateway`).

1. Run `docker compose --profile equipment up -d` in the repository root.
2. Import the sample master data into the MES (http://localhost:8080). `EQ-06` is set to collect status from equipment events.
3. Issue an API key, write `MES_API_KEY=pfm_...` in `.env` in the repository root, and run `docker compose --profile equipment up -d` again.
4. On the operation monitoring tab, check that the current status and operation history of `EQ-06` grow. In Node-RED (http://localhost:1880), the status of the node that checks the result (the last function node in step ③ of the flow) shows the numbers accepted, duplicate and rejected.

To adapt it to your own equipment, replace the simulated equipment (the function node in step ① of the flow) with a node that reads the status from the PLC (OPC UA, MC protocol, Modbus, etc.).
The sample MQTT broker is a minimal setup without authentication or encryption. Configure passwords and TLS for production.

## Webhooks (notifications from the MES)  {#webhooks}

When an event you want to know about **right away** happens, such as a nonconformance being raised, equipment stopping or a lot being put on hold, the MES notifies the URLs you register over HTTP.
Use it for alerts to the team leaders' chat or to start the next process when production is completed.
Systems that only need data periodically (such as inventory and costing in the ERP) can read production results with the [integration API](#integration-api) instead.

Notifications are kept **thin**. Only the type, the time, the numbers of what changed and a one-sentence summary are sent.
A notification is a cue to fetch data. Get the correct current values from the integration API (this also lets you catch up when a notification did not arrive or arrived out of order).

```
MES ──(POST, signed)──▶ Receiver (Node-RED, in-house script, etc.) ──▶ Chat, display board, ERP
```

### Registering a destination

1. A system administrator opens "Administration" → "Webhooks" in the menu, and on the **Destinations** tab chooses the purpose, the destination URL and the events to notify, then clicks **Register**.
2. Copy the **Signing secret** shown (a string starting with `whsec_`) into the receiver's settings. **The value is shown only at this time.** If it is lost, click **Reissue secret** (the old value stops working immediately, so update the receiver's settings too).
3. Click **Send test** to check that the receiver gets it. The result is shown on the spot (a test that fails is not retried).

- The URL can also be `http://` (for the factory's internal network). Bodies sent to an `http://` destination are not encrypted, so use `https://` when sending outside the company.
- A URL containing a user name or password cannot be registered (the URL is shown as it is in the list and the audit log). The receiver verifies the sender by the signature.
- To stop sending without deleting, clear **Active**. Deleting a destination also deletes its pending notifications and its delivery records.

### Events to notify

| Type | When | Main fields |
|---|---|---|
| `production-result.created` | A production result was recorded (from the screens, actual CSV or the integration API) | Order number, work order number, sequence, item code, good/defect quantities, output lot |
| `production-result.corrected` | A production result was corrected | `productionResultId` of the corrected result, quantities before and after, reason |
| `work-order.status-changed` | A work order's status changed (dispatch, start, completion, approval, cancellation) | Work order number, status before and after |
| `manufacturing-order.status-changed` | A manufacturing order was completed or canceled | Order number, order type, status before and after |
| `lot.status-changed` | A lot's stock status changed (hold, release, awaiting inspection, etc.) | Lot number, item code, before and after, trigger, reason |
| `nonconformance.created` | A nonconformance was raised (including those raised automatically when an inspection fails) | Nonconformance number, source, lot number, item code |
| `equipment.stopped` | Equipment entered a stopped or failure interval (from manual entry, actual CSV or [equipment events](#equipment-events)) | Asset number, status, stop cause, alarm code |

Types and statuses are sent as English names (such as `OnHold`) so that they do not change with the display language. Internal IDs are not sent; records are identified by numbers and codes.
The summary (`summary`) is in Japanese.

### What is sent

```json
{
  "id": "c548bec2-5e89-4210-ba22-2110c0325f09",
  "sequence": 7,
  "type": "equipment.stopped",
  "version": 1,
  "occurredAt": "2026-11-23T11:02:01+00:00",
  "summary": "…",
  "data": { "equipmentAssetNo": "EQ-01", "status": "Stopped", "startedAt": "2026-11-23T10:32:00+00:00", "endedAt": null,
            "stopCause": "Material shortage", "alarmCode": null, "source": "Manual", "workOrderNo": null }
}
```

The `summary` is a Japanese sentence such as `設備 EQ-01 が「停止」になりました（Material shortage）。` (equipment EQ-01 has stopped). <!-- 訳さない -->

| Field or header | Description |
|---|---|
| `id` | The notification's unique ID. **The same value when resent** (the receiver uses it to discard duplicates) |
| `sequence` | A number that increases across types (not necessarily consecutive) |
| `data` | The content for each type (the table above). The list of fields is in the `webhooks` section of the [specification (OpenAPI)](#integration-api) |
| `X-Mes-Event` | The type (the same as `type` in the body) |
| `X-Mes-Delivery` | The delivery ID (the same when resent) |
| `X-Mes-Timestamp` | The time it was sent (Unix seconds) |
| `X-Mes-Signature` | The signature: `sha256=` followed by the lowercase hexadecimal HMAC-SHA256 of "the `X-Mes-Timestamp` value, `.`, the body", keyed with the secret |

### What the receiver should check

1. **Verify the signature** using the exact bytes of the body you received. If you parse the JSON and turn it back into a string, the way numbers are written and other details change and the signature no longer matches.
2. **Check the time.** Reject the request if `X-Mes-Timestamp` is off by more than 5 minutes (to block old requests being replayed). Synchronize the clocks of the MES and the receiver with NTP.
3. **Return 2xx right away.** The MES waits up to 10 seconds by default and resends if no response arrives. Do heavy work after responding.
4. **Discard the second and later copies of the same `id`.** Notifications are delivered at least once, so the same notification can arrive twice. The order is not guaranteed either.

### When delivery fails

- If the destination cannot be reached, does not respond in time, or returns `5xx`, `408` or `429`, the MES resends at increasing intervals (1 minute, 5 minutes, 30 minutes, 2 hours, 6 hours, 12 hours, 24 hours; up to 8 attempts by default).
- Other `4xx` and `3xx` responses stop after one attempt, because resending would give the same result (redirects are not followed).
- For a stopped notification, fix the receiver, then click **Resend** on the **Deliveries** tab. The notification's `id` does not change, so a receiver that already got it discards it as a duplicate.
- Destinations with notifications waiting to be resent or stopped appear under "Undelivered" on the **Destinations** tab and at the top of the system administrator's **Dashboard**. Destinations are never stopped automatically.
- Delivery records are kept for 30 days by default.
- If the `dataprotection-keys` folder (the keys that encrypt the secrets) in the data folder is lost, the secrets cannot be decrypted and notifications to those destinations stop. Reissue the secret, update the receiver's settings, then resend (save the folder together in [Backups](database.html#backup)).

### Trying the samples

- **Node-RED (Docker)**: the Node-RED in the `equipment` profile ([equipment event sample](#equipment-events)) has a tab that receives notifications, verifies the signature and forwards them to a chat (Slack or Teams) in the chat's format.
  1. Start it with `docker compose --profile equipment up -d` and register `http://node-red:1880/mes-webhook` as a destination.
  2. Write the secret as `MES_WEBHOOK_SECRET=whsec_...` in `.env` in the repository root and run `docker compose --profile equipment up -d` again.
  3. Click **Send test**. The received notification and the form that reached the chat appear in the debug pane of Node-RED (http://localhost:1880).
     By default the notifications are forwarded to a mock inside the flow, not to a real chat. To send them, write `CHAT_WEBHOOK_URL` (a Slack incoming webhook, or the URL of a Teams workflow triggered when a webhook request is received) in `.env`, plus `CHAT_KIND=teams` for Teams.
- **Scripts**: `samples/webhook` has minimal receivers in Python (`receive_mes_webhook.py`) and PowerShell (`Receive-MesWebhook.ps1`). See the README for where to add your own processing.

### Promises that keep receivers working across upgrades

- Fields in the body are not removed, and their names and types do not change. A field that always had a value does not become one that may have none.
- Fields, types and values may be added. Make the receiver skip fields it does not know.
- When the shape must change, it is added as a separate type (v2).
