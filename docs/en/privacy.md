---
layout: default
title: Privacy policy
nav_url: privacy.html
description: The data Parallel Factory MES handles, where it is stored and whether it is sent anywhere.
lead: This page describes the data handled by Parallel Factory MES ("the app"). It mainly covers the Microsoft Store edition (a desktop app that runs entirely on a single PC), and also describes how data is handled when the app is deployed on a server.
prev_url: operations.html
prev_title: Administration
---

Last updated: September 21, 2026

This is an English translation. If it differs from the [Japanese version](../privacy.html), the Japanese version prevails.

## Summary

- **The app does not send your data to the developer or to any third party.**
- In the Microsoft Store edition, all data is stored **on your PC**.
- The app collects **no** usage data of any kind (no telemetry, analytics or advertising).
- The app does not require a connection to any external server or cloud service.

## 1. Data the app stores

The app is a manufacturing execution system (MES) and stores business records. What it stores is the content users enter on its screens and a record of those operations.

| Type | Contents |
|---|---|
| User information | User name, display name, department, assigned work center and shift, roles (permissions) and password (**stored as a hash**; the original password is not kept) |
| Business data | Master data (items, processes, equipment, business partners and so on), manufacturing orders, work orders, work actuals, inventory, inspection records, shipment records, maintenance records and so on |
| Operation history (audit log) | Date and time of the operation, the user name of the person who performed it, the type and target of the operation, what was changed and the source IP address |

Work actuals and the audit log record **who did what and when**. This feature ensures that manufacturing records can be traced, and these records stay on your PC (or on your organization's server) just like the rest of the business data.

## 2. Where data is stored

### Microsoft Store edition (desktop app)

This is the desktop app available from the [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=en-US&gl=US).

The database, the signing key used for authentication, the startup log and the WebView2 user data are stored in an area that Windows reserves for this app.

```
%LOCALAPPDATA%\Packages\msmsrep.ParallelFactoryMES_77t1an0ygyrva\LocalState
```

The app runs its application server **only inside your PC (on the loopback address `127.0.0.1`)**. This traffic never leaves your PC, and other devices on the same network cannot connect to it.

**Uninstalling the app deletes this folder together with all its data.** If there is data you want to keep, take a backup before uninstalling (see [Administration](operations.html)).

### When deployed on a server

Data is stored on the server and database of the organization that deployed the app. Managing and protecting it is that organization's responsibility. The developer of the app has no access to that data.

## 3. Disclosure to third parties and external transmission

The app does not send or disclose stored data to any third party, including the developer. Data leaves the app only when users themselves perform one of the following operations, and in each case the user chooses the destination.

- Exporting CSV files, or printing reports and labels
- Backing up the database file (saving it to a location the user chooses)

## 4. Microsoft Edge WebView2

The app uses the Microsoft Edge WebView2 Runtime (provided by Microsoft Corporation) to display its screens. The app does not load any external website in WebView2 (it only displays the loopback address described above). Updates to the WebView2 Runtime itself and any communication by its components are under Microsoft's control, and the [Microsoft Privacy Statement](https://privacy.microsoft.com/en-us/privacystatement) applies to them.

## 5. Your rights

All data is under the user's control.

- **Viewing and correcting**: You can check and correct data on the app's screens. Corrections are recorded in the audit log.
- **Deleting**: Uninstalling the app deletes all stored data. You can also delete only the audit log for a specified period (system administrator permission required).
- There is no need to ask the developer to delete or disclose data. The developer does not hold any of your data.

## 6. Children's privacy

The app is business software for organizations and is not directed at children. It does not collect any information about age.

## 7. Changes to this policy

If this policy changes, this page will be updated and the "Last updated" date at the top will be revised.

## 8. Contact

Please send questions about the app or this policy as an Issue in the GitHub repository.

<https://github.com/msmsrep/ParallelFactoryMES/issues>
