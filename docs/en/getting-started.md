---
layout: default
title: Installation and initial setup
nav_url: getting-started.html
lead: The steps from starting the system to creating the initial administrator account, signing in and adding users. The person setting up the system does this once, at the start.
prev_url: index.html
prev_title: About this guide
next_url: basics.html
next_title: Screen layout and common operations
---

## Choosing how to install

| Method | Best for | Get it from | URL to open |
|---|---|---|---|
| Microsoft Store edition | Trying out the features or checking procedures on one PC (other devices cannot connect) | [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=en-US&gl=US) | The app window opens directly |
| ZIP (Windows / Linux) | Running it on a server on your company LAN and using it from shop-floor devices in a browser | [GitHub Releases](https://github.com/msmsrep/ParallelFactoryMES/releases/latest) | `http://<server>:5000` |
| Docker (amd64 / arm64) | Same as above, running in a container | `ghcr.io/msmsrep/parallelfactorymes` | `http://<server>:8080` |
| Run from source | Development and customization | [GitHub](https://github.com/msmsrep/ParallelFactoryMES) | `http://localhost:5288` |

Every method is free, with no activation or license key. Except when running from source, you do not need to install .NET either.

## Requirements

| Item | Details |
|---|---|
| Server | One Windows / Linux (x64) PC, or an environment that runs Docker (amd64 / arm64). The Store edition needs Windows 10 (2004 or later) / 11 |
| Database | Not required. By default an SQLite file (`mesapp.db`) is created automatically. PostgreSQL / SQL Server can also be used ([Administration](operations.html#database)) |
| Client devices | A modern browser (Chrome, Edge and so on). No dedicated app needs to be installed |
| Network | Client devices must be able to reach the server's URL |

## Starting the system

### Microsoft Store edition

Install it from the [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=en-US&gl=US) and open **Parallel Factory MES** from the Start menu.
The initial administrator is created automatically, and its user name and initial password are shown at the top of the screen (you can skip "Creating the initial administrator account" below).

### ZIP

<ol class="steps">
<li>Download <code>ParallelFactoryMES-&lt;version&gt;-win-x64.zip</code> (<code>-linux-x64.zip</code> for Linux) from <a href="https://github.com/msmsrep/ParallelFactoryMES/releases/latest">Releases</a> and extract it.</li>
<li>On Windows, double-click <strong>start.cmd</strong>. On Linux, run <code>sh start.sh</code>.</li>
<li>Open <code>http://localhost:5000</code> in a browser.</li>
</ol>

<div class="warn">
<p>If Windows shows "Windows protected your PC", open the properties of the ZIP file before extracting it, check <strong>Unblock</strong>, and extract it again (<a href="troubleshooting.html#install">Troubleshooting</a>).</p>
</div>

### Docker

```bash
docker run -d -p 8080:8080 -v mesapp-data:/data ghcr.io/msmsrep/parallelfactorymes
```

Open `http://localhost:8080` in a browser. Without `-v mesapp-data:/data`, your data is lost when the container is recreated.
To run it together with PostgreSQL, see [Administration](operations.html#docker).

### Running from source (for development)

Install the .NET 10 SDK and run the following in the repository root. By default it starts on `http://localhost:5288`.

```bash
dotnet run --project src/MesApp.Api
```

For the settings needed to use it from other PCs on the same LAN (port, firewall, HTTPS), see [Administration](operations.html).

<div class="note">
<p>The database file and signing key are created automatically in the <strong>data folder</strong>. You do not need to create a database.
By default the data folder is <code>%LOCALAPPDATA%\ParallelFactoryMES</code> (<code>~/.local/share/ParallelFactoryMES</code> on Linux, <code>/data</code> on Docker).</p>
</div>

## Creating the initial administrator account

If you open the system in a browser while no users are registered, the sign-in screen shows
"Initial setup is required."

<ol class="steps">
<li>On the sign-in screen, open the <strong>Create the initial administrator account</strong> link.</li>
<li>Enter a <strong>User name</strong> (sign-in ID), a <strong>Full name (display name)</strong> and a <strong>Password</strong>.</li>
<li>Press <strong>Create and go to sign-in</strong>.</li>
</ol>

The password must be **at least 8 characters long and contain a lowercase letter and a digit** (for example `Passw0rd123`).
If it does not meet these conditions, an error is shown and the account is not created.

<div class="warn">
<p>The initial setup screen works <strong>only while there are no users</strong>. Once an account has been created, it can no longer be used.
If the administrator password is lost, see <a href="troubleshooting.html">Troubleshooting</a>.</p>
</div>

The account created here is given the **System administrator (SystemAdmin)** role.

## Signing in

<ol class="steps">
<li>Open the server's URL in a browser.</li>
<li>Enter your user name and password and press <strong>Sign in</strong> (you can also press Enter).</li>
<li>After signing in, the dashboard is shown.</li>
</ol>

If sign-in keeps failing, the account is temporarily locked.
In that case "The account is temporarily locked." is shown. Wait a while and try again.

### Changing the initial password

Users created by an administrator **must change their password the first time they sign in**.
A warning that you are still using the initial password appears at the top of the dashboard.
Open the password change screen from its link and change your password.

## Registering users  {#add-users}

A system administrator adds users from the **Master Data → Users** tab.

<ol class="steps">
<li>Enter the <strong>User name</strong> and <strong>Full name (display name)</strong>.</li>
<li>Enter an <strong>Initial password</strong> (the user is required to change it the first time they sign in).</li>
<li>Check the <strong>Roles</strong> that match the person's work. You can select more than one.</li>
<li>Press <strong>Register</strong>.</li>
</ol>

What a user can do depends on their roles. For details, see [Roles and permissions](roles.html).

If you match skills and qualifications when dispatching, also define the qualifications under **Master Data → Skills & Qualifications**,
and link them to users from "Skills & Qualifications" in each user's row.

## What to do next

You need to register master data before you can start work. In particular, unless you register them in the order **Processes → Products → Routings (BOP)**,
you cannot release manufacturing orders to processes. Continue to [Master data](masters.html).

If you want to try things out first, load the sample data. It gives you a complete set of records from manufacturing order to shipment, plus maintenance, tools, inventory, logistics and nonconformances.
Import the [master data sample](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-master-csv.zip)
with bulk import via ZIP on **Master Data**, and then the [actuals sample](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv.zip)
with bulk import via ZIP on **Administration → Actuals CSV Import** ([Master data CSV](masters.html#csv)).
