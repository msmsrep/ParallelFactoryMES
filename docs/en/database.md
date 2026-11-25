---
layout: default
title: Databases and backups
nav_url: database.html
lead: "Switching to PostgreSQL / SQL Server, and backing up and restoring data."
prev_url: security.html
prev_title: Security and audit log
next_url: reference.html
next_title: Reference
---

## Using PostgreSQL / SQL Server {#database}

The default SQLite needs no extra installation and can be used as is for up to a few dozen devices.
When many devices write at the same time, the data volume is large, or you want to manage it together on an existing DB server,
switch to PostgreSQL or SQL Server.

1. Create an **empty database** and a login for connecting on the DB server.
   Give the login permission to create and change tables in that database (the schema is created and updated at startup).
2. Set `Database__Provider` and `Database__ConnectionString` and start it.

```powershell
# PostgreSQL
$env:Database__Provider="PostgreSql"; $env:Database__ConnectionString="Host=db;Database=mesapp;Username=mesapp;Password=..."
# SQL Server
$env:Database__Provider="SqlServer"; $env:Database__ConnectionString="Server=db;Database=mesapp;User Id=mesapp;Password=...;TrustServerCertificate=True"
```

3. The tables are created at startup. When you open it in a browser, the initial setup guidance is shown, just as with SQLite.
   In later upgrades, the differences are also applied automatically at startup.

<div class="warn">
<p><strong>Existing SQLite data is not carried over.</strong> After switching, you start from an empty database.
Decide which database to use before you start operating. If you switch the setting back to SQLite, the original <code>mesapp.db</code> is used as is (the two sets of data are separate).</p>
</div>

### Differences from SQLite

| Item | Difference |
|---|---|
| Upper and lower case in codes | With SQL Server's default collation they are not distinguished (`ABC` and `abc` are treated as the same, duplicate code, and searches do not distinguish them either). SQLite and PostgreSQL distinguish them |
| Date and time | PostgreSQL stores them in UTC. They refer to the same moment, so screen display, totals and CSV results do not change |
| Decimals | Up to 6 decimal places are stored (trailing zeros are dropped so that values look the same as in SQLite) |
| Desktop edition | Intended for use with SQLite (because it is designed to run entirely on one PC) |

### When it does not start

| Symptom | What to do |
|---|---|
| It stops with an "unknown DB provider" error (the message is in Japanese: <code>不明なDBプロバイダー</code>) <!-- 訳さない --> | Write `Database__Provider` as exactly one of `Sqlite` / `PostgreSql` / `SqlServer`, including upper and lower case |
| It stops with a connection timeout or authentication error | Check the host name, port, user name and password in the connection string, the firewall on the DB server, and, for SQL Server, whether TCP/IP connections are enabled |
| SQL Server gives a certificate error | For a server with an internal self-signed certificate, add `TrustServerCertificate=True` to the connection string, or configure a trusted certificate on the server |
| It stops with a permission error (cannot create tables) | Give the connecting login permission to create and change tables in the target database |

## Backups  {#backup}

### SQLite

<div class="warn">
<p><strong>Do not simply copy the DB file while it is running.</strong>
SQLite runs in WAL mode, so the copied file may be corrupted.</p>
</div>

There are two safe methods.

1. Stop the server, then copy `mesapp.db`
2. While it is running, create a backup file with SQLite's `VACUUM INTO`

With Docker, stop the container and save the whole volume (the signing key and the keys that encrypt the webhook secrets are kept along with it).
Check the volume name with `docker volume ls` (with compose it is `<folder name>_mesapp-data`).

```bash
docker compose stop
docker run --rm -v <volume name>:/data -v "$PWD":/backup alpine tar czf /backup/mesapp-data.tgz -C /data .
docker compose start
```

Deleting all of `mesapp.db*` (including `-wal` / `-shm`) and restarting returns it to the initial state.
You can use this to reset a test environment.

### PostgreSQL / SQL Server

Back up with each database's standard tools (backups can be taken while it is running).

- PostgreSQL: `pg_dump -Fc -d mesapp -f mesapp.dump` (restore with `pg_restore`)
- SQL Server: `BACKUP DATABASE mesapp TO DISK = N'...\mesapp.bak'` (or a backup from SQL Server Management Studio)

To return to the initial state, drop the database, create an empty one again and restart.

### Keys in the data folder

Besides the database, also save `jwt-signing.key` (the sign-in signing key) and the `dataprotection-keys` folder (the keys that encrypt the [webhook](integration.html#webhooks) secrets) in the data folder.
If `dataprotection-keys` is lost, the webhook secrets in a restored database cannot be decrypted, and you will need to reissue the secret for each destination.
