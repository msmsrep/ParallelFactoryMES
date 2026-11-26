---
layout: default
title: データベースとバックアップ
nav_url: database.html
lead: "PostgreSQL / SQL Server への切替と、データのバックアップ・復元です。"
prev_url: security.html
prev_title: セキュリティと監査ログ
next_url: reference.html
next_title: リファレンス
---

## PostgreSQL / SQL Server を使う {#database}

既定の SQLite は追加インストールが要らず、数十端末規模までならそのまま運用できます。
同時に書き込む端末が多い、データ量が大きい、既存のDBサーバーでまとめて管理したい、といった場合は
PostgreSQL または SQL Server に切り替えます。

1. DBサーバーに**空のデータベース**と接続用のログインを作ります。
   ログインには、そのデータベースでテーブルを作成・変更できる権限を与えます（起動時にスキーマを作成・更新するため）。
2. `Database__Provider` と `Database__ConnectionString` を設定して起動します。

```powershell
# PostgreSQL
$env:Database__Provider="PostgreSql"; $env:Database__ConnectionString="Host=db;Database=mesapp;Username=mesapp;Password=..."
# SQL Server
$env:Database__Provider="SqlServer"; $env:Database__ConnectionString="Server=db;Database=mesapp;User Id=mesapp;Password=...;TrustServerCertificate=True"
```

3. 起動時にテーブルが作られます。ブラウザで開くと、SQLiteのときと同じく初期セットアップの案内が表示されます。
   以降のバージョンアップでも、起動時に差分が自動適用されます。

<div class="warn">
<p><strong>既存のSQLiteのデータは移りません。</strong> 切り替えると空のデータベースから始まります。
運用を始める前にどのデータベースを使うかを決めてください。設定をSQLiteに戻すと、元の <code>mesapp.db</code> がそのまま使われます（両者のデータは別々です）。</p>
</div>

### SQLite との違い

| 項目 | 違い |
|---|---|
| コードの大文字・小文字 | SQL Server の既定の照合順序では区別しません（`ABC` と `abc` は同じコードとして重複扱いになり、検索も区別しません）。SQLite・PostgreSQL は区別します |
| 日時 | PostgreSQL ではUTCで保存されます。同じ時点を指すため、画面表示・集計・CSVの結果は変わりません |
| 小数 | 小数点以下6桁まで保存します（SQLite と同じ値の見え方になるよう末尾のゼロは落とします） |
| デスクトップ版 | SQLite で使う前提です（1台のPCで完結させる構成のため） |

### 起動しないとき

| 症状 | 対処 |
|---|---|
| 「不明なDBプロバイダー」で止まる | `Database__Provider` は `Sqlite` / `PostgreSql` / `SqlServer` のいずれかを、大文字・小文字も含めてそのとおりに書いてください |
| 接続のタイムアウト・認証エラーで止まる | 接続文字列のホスト名・ポート・ユーザー名・パスワード、DBサーバー側のファイアウォール、SQL Server では TCP/IP 接続が有効かを確認してください |
| SQL Server で証明書のエラーになる | 社内の自己署名証明書のサーバーなら接続文字列に `TrustServerCertificate=True` を付けるか、信頼された証明書をサーバーに設定してください |
| 権限エラー（テーブルを作成できない）で止まる | 接続ログインに、対象データベースでのテーブル作成・変更の権限を与えてください |

## バックアップ  {#backup}

### SQLite

<div class="warn">
<p><strong>稼働中のDBファイルを単純コピーしないでください。</strong>
SQLiteはWALモードで動作するため、コピーしたファイルが壊れる可能性があります。</p>
</div>

安全な方法は次の2つです。

1. サーバーを停止してから `mesapp.db` をコピーする
2. 稼働中なら SQLite の `VACUUM INTO` でバックアップファイルを作る

Docker の場合は、コンテナを止めてからボリュームごと退避します（署名鍵と、Webhook の秘密の値を暗号化する鍵も一緒に残ります）。
ボリューム名は `docker volume ls` で確認してください（compose では `<フォルダ名>_mesapp-data`）。

```bash
docker compose stop
docker run --rm -v <ボリューム名>:/data -v "$PWD":/backup alpine tar czf /backup/mesapp-data.tgz -C /data .
docker compose start
```

`mesapp.db*`（`-wal` / `-shm` を含む）をすべて削除して再起動すると、初期状態に戻ります。
検証環境をリセットしたいときに使えます。

### PostgreSQL / SQL Server

各データベースの標準の手段でバックアップします（稼働中でも取得できます）。

- PostgreSQL: `pg_dump -Fc -d mesapp -f mesapp.dump`（復元は `pg_restore`）
- SQL Server: `BACKUP DATABASE mesapp TO DISK = N'...\mesapp.bak'`（または SQL Server Management Studio のバックアップ）

初期状態に戻すには、データベースを削除して空のデータベースを作り直し、再起動します。

### データ保存先の鍵

データベースとは別に、データ保存先の `jwt-signing.key`（ログインの署名鍵）と `dataprotection-keys` フォルダ（[Webhook](integration.html#webhooks) の秘密の値を暗号化する鍵）も退避してください。
`dataprotection-keys` を失うと、戻したデータベースにある Webhook の秘密の値を復号できず、送信先ごとに秘密の値を再発行することになります。
