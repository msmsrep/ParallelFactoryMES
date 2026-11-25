---
layout: default
title: Server deployment and settings
nav_url: operations.html
lead: "The server architecture, how to start each distribution, and the settings an administrator needs first."
prev_url: roles.html
prev_title: Roles and permissions
next_url: integration.html
next_title: Integrations
---

<!-- This used to be one page. Send old links to moved sections (such as operations.html#webhooks) to their new page -->
<script>
(function () {
  var moved = {"folder-link": "integration.html", "setting-it-up": "integration.html", "where-to-put-files": "integration.html", "checking-the-results": "integration.html", "api-keys": "integration.html", "issuing-a-key": "integration.html", "trying-it": "integration.html", "notes": "integration.html", "integration-api": "integration.html", "specification-openapi": "integration.html", "endpoints": "integration.html", "importing-csv": "integration.html", "sending-again-without-registering-twice-idempotency-key": "integration.html", "reading-production-results-from-where-you-left-off": "integration.html", "promises-that-keep-integrations-working-after-upgrades": "integration.html", "equipment-events": "integration.html", "preparation": "integration.html", "what-to-send": "integration.html", "what-happens-after-an-event-arrives": "integration.html", "trying-the-sample-docker": "integration.html", "webhooks": "integration.html", "registering-a-destination": "integration.html", "events-to-notify": "integration.html", "what-is-sent": "integration.html", "what-the-receiver-should-check": "integration.html", "when-delivery-fails": "integration.html", "trying-the-samples": "integration.html", "promises-that-keep-receivers-working-across-upgrades": "integration.html", "security": "security.html", "https": "security.html", "audit-log": "security.html", "operations-recorded": "security.html", "filtering": "security.html", "deletion-and-tampering": "security.html", "purging-old-records": "security.html", "database": "database.html", "differences-from-sqlite": "database.html", "when-it-does-not-start": "database.html", "backup": "database.html", "sqlite": "database.html", "postgresql--sql-server": "database.html", "keys-in-the-data-folder": "database.html"};
  var id = decodeURIComponent(location.hash.slice(1));
  if (moved[id]) location.replace(moved[id] + location.hash);
})();
</script>

Other information for administrators:

- [Integrations](integration.html)
- [Security and audit log](security.html)
- [Databases and backups](database.html)

## Architecture

The web client (Blazor WebAssembly) is served from the same origin by the API server (ASP.NET Core).
Nothing needs to be installed on users' devices.

| Component | Role |
|---|---|
| MesApp.Api | Business logic and database. Also serves the web client |
| MesApp.Client.Web | The client that runs in the browser |
| Database | One of SQLite (the default; a single file), PostgreSQL or SQL Server. Migrations are applied automatically at startup ([Using PostgreSQL / SQL Server](database.html#database)) |

## Distribution and startup

There are three ways to put it on a server. If you only want to try it on one PC, the [Microsoft Store desktop edition](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=en-US&gl=US) is the easiest
(other devices cannot connect to it, so use one of the methods below for shop-floor operation).

| Method | Best for | Get it from |
|---|---|---|
| ZIP (self-contained) | Putting it directly on a Windows / Linux server. No .NET installation needed | `ParallelFactoryMES-<version>-win-x64.zip` / `-linux-x64.zip` from [GitHub Releases](https://github.com/msmsrep/ParallelFactoryMES/releases/latest) |
| Docker | Running it in a container (amd64 / arm64) | `ghcr.io/msmsrep/parallelfactorymes` |
| Publish from source | Distributing your own modified version | `dotnet publish src/MesApp.Api -c Release -o publish` |

### Starting from the ZIP

In the extracted folder, double-click `start.cmd` on Windows, or run `sh start.sh` on Linux.
The default port is 5000. To change it, set the environment variable `MESAPP_URLS` (for example `http://0.0.0.0:8080`) before starting.

To upgrade, stop the server, replace the extracted folder with the new version and start it.
The data is in the data folder (see below), so it is not lost when the folder is replaced. The database is updated automatically at startup.

### Starting with Docker  {#docker}

```bash
docker compose up -d
```

The repository's `compose.yaml` starts it with SQLite, and `compose.postgres.yaml` starts it with PostgreSQL
(write `POSTGRES_PASSWORD=...` in `.env` first, then run `docker compose -f compose.postgres.yaml up -d`).
Open `http://<server IP>:8080` in a browser.

- The database and signing key are kept in a volume (`/data` inside the container). They are not lost when the container is recreated
- The default time zone is `TZ=Asia/Tokyo`. The business date boundary is judged in this time zone
- The container does not handle HTTPS. Terminate it at a reverse proxy in front, and specify the network the proxy is on
  with `ReverseProxy__KnownNetworks__0` (for example `172.16.0.0/12`) ([Running with HTTPS](security.html#https))
- To upgrade, run `docker compose pull` → `docker compose up -d`

### Publishing from source and starting

```bash
dotnet publish/MesApp.Api.dll --urls http://0.0.0.0:5000
```

The ASP.NET Core 10 Runtime is required. Other PCs on the same LAN can access it at `http://<server IP>:5000`.

<div class="warn">
<p>Windows Firewall must <strong>allow incoming connections</strong> on that port.
Without this, other PCs cannot connect.</p>
</div>

The database file (`mesapp.db`) and the JWT signing key (`jwt-signing.key`) are created automatically in
**the data folder (by default `%LOCALAPPDATA%\ParallelFactoryMES`, `~/.local/share/ParallelFactoryMES` on Linux and `/data` on Docker; it can be changed with the environment variable `MESAPP_DATA_DIR`)**.
If a setting contains an absolute path, that path is used.

## Settings  {#settings}

Edit `appsettings.json` or override the values with environment variables.

| Setting | Environment variable | Default |
|---|---|---|
| DB connection string (the file location for SQLite) | `Database__ConnectionString` | `Data Source=mesapp.db` |
| DB provider | `Database__Provider` | `Sqlite` (`PostgreSql` / `SqlServer` are also possible) |
| Data folder (DB and signing key) | `MESAPP_DATA_DIR` | `%LOCALAPPDATA%\ParallelFactoryMES` (`~/.local/share/ParallelFactoryMES` on Linux, `/data` on Docker) |
| Business date boundary hour | `BusinessDay__BoundaryHour` | `6` (6:00 a.m.) |
| Audit log retention period (years) | `Audit__RetentionYears` | `5` |
| Access token lifetime (minutes) | `Jwt__AccessTokenLifetimeMinutes` | `60` |
| Refresh token lifetime (hours) | `Jwt__RefreshTokenLifetimeHours` | `12` |
| JWT signing key file | `Jwt__SigningKeyFile` | `jwt-signing.key` (created automatically if it does not exist) |
| Automatic creation of the initial administrator | `MesAdmin__UserName` / `MesAdmin__Password` / `MesAdmin__DisplayName` | Not set || IPs of trusted reverse proxies | `ReverseProxy__KnownProxies__0` (`__1`, `__2` … for more) | Not set (forwarded headers are not read) |
| Networks of trusted reverse proxies | `ReverseProxy__KnownNetworks__0` (CIDR, for example `192.168.10.0/24`) | Not set |
| Folder integration: integration folder | `FolderLink__Root` | Not set (no integration) |
| Folder integration: importing user | `FolderLink__UserName` | Not set |
| Folder integration: scan interval (seconds) | `FolderLink__PollSeconds` | `30` |
| Folder integration: language of results | `FolderLink__Language` | `ja` (`en` is also allowed) |
| Equipment events: maximum events per request | `EquipmentEvents__MaxBatchSize` | `1000` |
| Equipment events: allowed lead of event time (seconds) | `EquipmentEvents__MaxFutureSkewSeconds` | `300` |
| Equipment events: time until reception is considered stopped (minutes) | `EquipmentEvents__SilenceMinutes` | `10` |

### Business date boundary hour

Changing `BusinessDay__BoundaryHour` changes the date boundary for totals and numbering.
For example, setting it to `8` makes 8:00 a.m. the point where the date changes.
Set it to match the start time of your shifts.

### Unattended setup

An example of creating the initial administrator without using the screens. **It applies only while there are no users**,
and the password must be changed at the first sign-in (until it is changed, no API other than password change can be used).

```powershell
$env:MesAdmin__UserName="admin"; $env:MesAdmin__Password="Passw0rd123"; dotnet publish/MesApp.Api.dll
```

If `MesAdmin__Password` is not specified, no initial administrator is created (so that no account is left
whose password nobody knows). In that case, create it on the [initial setup screen](getting-started.html).

## Limitations

| Item | Status |
|---|---|
| PostgreSQL / SQL Server | Supported ([Using PostgreSQL / SQL Server](database.html#database)). There is no function for moving existing SQLite data |
| Docker and ZIP distribution | Supported ([Distribution and startup](#distribution-and-startup)) |
| HTTPS | Not enforced by the app (terminate at a reverse proxy; [Running with HTTPS](security.html#https)) |
| Automatic data collection from equipment and scales | Not supported (entered by hand) |
| EDI / ASN integration | Not supported |
| Tracking by serial number (individual unit) | Not supported (by lot only) |
| Multi-step approval workflow | Not supported (single-step approval) |

## Testing

```bash
dotnet test ParallelFactoryMES.slnx
```

This runs the integration tests of the API.

## Publishing this guide

This guide is managed as Markdown in the `docs/` folder. The English version is in `docs/en/`, with the same file names as the Japanese version. To publish it on GitHub Pages,
set the following under the repository's **Settings → Pages**.

- Source: `Deploy from a branch`
- Branch: `master` (or `main`) / folder: `/docs`

To update the content, edit `docs/*.md` (and `docs/en/*.md`) and commit, and it is republished automatically.
When you add a page, also add an item to both `ja` and `en` in `docs/_data/nav.yml` so that it appears in the left menu.
