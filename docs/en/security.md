---
layout: default
title: Security and audit log
nav_url: security.html
lead: "Authentication, running with HTTPS, and how to read the audit log and purge old records."
prev_url: integration.html
prev_title: Integrations
next_url: database.html
next_title: Databases and backups
---

## Security

<div class="warn">
<p><strong>The app itself does not enforce HTTPS.</strong> For LAN operation, terminate HTTPS at a reverse proxy
(IIS / nginx) (<a href="#https">Running with HTTPS</a>).</p>
<p>Also, <strong>barcode scanning with the camera requires HTTPS anywhere other than <code>localhost</code></strong>.
The camera cannot be used over plain HTTP (USB readers and typing work over HTTP too).</p>
</div>

- Authentication uses ASP.NET Core Identity + JWT.
- Corrections to actuals and inspections, master data changes, inventory adjustments and so on are recorded in the audit log (see below).
- Passwords must be at least 8 characters long and contain a lowercase letter and a digit.

### Running with HTTPS  {#https}

A reverse proxy (IIS / nginx) receives HTTPS from browsers and relays it to the app over HTTP.
Only the proxy holds the certificate.

```
Device browser ──HTTPS──▶ IIS / nginx (certificate) ──HTTP──▶ MesApp (127.0.0.1:5000)
```

**1. Prepare a certificate.** On a company LAN you cannot get a public certificate, so issue one with an internal CA or
[mkcert](https://github.com/FiloSottile/mkcert). With mkcert, run the following on the server.

```bash
mkcert -install
mkcert mes.example.local 192.168.10.5
```

List every name (host name and IP) that devices will use. Configure the resulting `mes.example.local+1.pem` (certificate) and
`mes.example.local+1-key.pem` (private key) in the proxy.

**2. Distribute the CA certificate to devices.** Rename `rootCA.pem` in the folder shown by `mkcert -CAROOT`
to `rootCA.crt`, distribute it to each device and add it to "Trusted Root Certification Authorities"
(run `certutil -addstore -f Root rootCA.crt` on the device, or distribute it by Group Policy if there are many devices).
**Do not distribute** `rootCA-key.pem` in the same folder (anyone holding it can issue any certificate).

**3. Start the app so that it can be reached only from the proxy.** So that nobody can connect to it directly over HTTP,
listen only on the loopback address (the app does not redirect HTTP to HTTPS).

```bash
dotnet publish/MesApp.Api.dll --urls http://127.0.0.1:5000
```

**4. Configure the proxy.** Pass the client IP and scheme with `X-Forwarded-For` / `X-Forwarded-Proto`.

An nginx example:

```nginx
server {
    listen 443 ssl;
    server_name mes.example.local;
    ssl_certificate     mes.example.local+1.pem;
    ssl_certificate_key mes.example.local+1-key.pem;
    client_max_body_size 50m;   # for bulk CSV and ZIP imports

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

For IIS, install URL Rewrite and Application Request Routing (ARR), and enable the ARR proxy.
ARR adds `X-Forwarded-For`. Add `X-Forwarded-Proto` with a rewrite rule, after adding the server variable `HTTP_X_FORWARDED_PROTO`
to "Allowed Server Variables" (a `web.config` example).

```xml
<rewrite>
  <rules>
    <rule name="MesApp" stopProcessing="true">
      <match url="(.*)" />
      <serverVariables>
        <set name="HTTP_X_FORWARDED_PROTO" value="https" />
      </serverVariables>
      <action type="Rewrite" url="http://127.0.0.1:5000/{R:1}" />
    </rule>
  </rules>
</rewrite>
```

**5. Configure the app to trust the proxy.** The app reads forwarded headers only from the sources specified here.
If the proxy is on the same PC, use the following (if it is on another PC, use its IP).

```powershell
$env:ReverseProxy__KnownProxies__0="127.0.0.1"
```

<div class="warn">
<p><strong>Without this setting, forwarded headers are not read.</strong> Even over HTTPS,
the refresh token cookie does not get <code>Secure</code>, and every client IP in the audit log becomes the proxy's IP.
They are not read when this is not set so that a device connecting directly over HTTP cannot forge the headers to fake its source.</p>
</div>

**Check:** Sign in over HTTPS from a device. If the client IP of the "sign-in" entry in the audit log is the device's IP, you are done.
If it is still `127.0.0.1`, the setting in step 5 is not taking effect.

## Audit log  {#audit-log}

A record of "who changed what, and when". You can view it from **"Audit Log" in the menu**
(system administrators only; other roles do not even see the menu item).

### Operations recorded

| Category | Main operations |
|---|---|
| Setup | Creating the initial administrator |
| Auth | Sign-in, failed sign-in, account lockout, sign-out, password change |
| User | Creating and changing users, password resets, granting skills and qualifications |
| Master | Registering, changing and deactivating master data, CSV imports |
| Production | Registering, approving, canceling and releasing manufacturing orders, dispatching |
| Execution | Starting work, registering results, **correcting results**, work time, process condition data, completion approval |
| Inventory | Receiving, moves, **quantity adjustments**, status changes, split / merge / reassign, scrapping, picking, shipping, stocktaking, **cancellations** |
| Quality | Registering, **correcting**, judging and approving inspection results; raising, instructing and approving nonconformances; shipment judgments |
| Maintenance | Maintenance plans, maintenance orders, maintenance records, managing maintenance procedures |
| Equipment | Recording equipment operation, tool usage |

For **operations whose before and after you want to follow**, such as quantity adjustments, status changes, corrections and cancellations, the "Details" column contains **the value before, the value after and the reason**.
Other operations contain a summary.

### Filtering

You can filter by period, category / operation and target (type and ID).
For example, entering `Lot` in "Target type" and the internal ID of a lot in "Target ID"
lists only the operations performed on that lot.

<div class="note">
<p><strong>The period filters by the recorded date (the server's date).</strong>
The date and time in the list are shown in the browser's time, so where the server and browser are in different time zones,
entries around midnight may appear on a different day.</p>
</div>

### Deletion and tampering

- Individual records **cannot be changed or deleted, either from the screens or from the API**. Records are only ever added.
- **The retention period is 5 years** (it can be changed with `Audit__RetentionYears`). **Nothing is deleted automatically** (whether records may be deleted is a business decision).
- Only when storage becomes a problem can you **delete records older than the retention period in bulk** (see below).

### Purging old records

This operation clears out records that are past the retention period. It is **at the very bottom of the Audit Log screen**.
Take a backup first (see below).

<div class="steps">
<ol>
<li>Enter a date in "Delete up to this date (inclusive)" and the reason for deleting in "Reason (required)" (records <strong>on or before the date</strong> are targeted).</li>
<li>Press <strong>Check count to delete</strong>. Nothing is deleted yet at this point. The number of targeted records is shown.</li>
<li>Check the count and press <strong>"Delete N records"</strong> to delete them.</li>
</ol>
</div>

Changing the date or the reason resets the count check, and you must check again
(so that what you checked and what is actually deleted do not differ).

To run it from the API, use the following. While `dryRun=true`, it only returns the count and does not touch the database.

```
POST /api/audit-logs/purge?dryRun=true
{ "to": "2020-12-31", "reason": "To reduce storage" }
```

<div class="warn">
<p><strong>Records within the retention period cannot be deleted.</strong> A date newer than the retention period is rejected
(if the records of recent operations could be deleted to hide them, the audit log would not stand as evidence).</p>
<p><strong>A reason is required</strong>, and <strong>the purge itself (period, count, reason and user) is recorded in the audit log</strong>.
This record is written after the deletion, so it is not removed by the same operation.</p>
</div>
