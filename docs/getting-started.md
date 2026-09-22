---
layout: default
title: 導入と初期設定
nav_url: getting-started.html
lead: システムの起動から、初期管理者アカウントの作成、ログイン、ユーザー追加までの流れです。導入担当者が最初に一度だけ行う作業です。
prev_url: index.html
prev_title: このガイドについて
next_url: basics.html
next_title: 画面構成と共通操作
---

## 入手方法を選ぶ

| 方法 | 向いている使い方 | 入手先 | 開くURL |
|---|---|---|---|
| Microsoft Store 版 | 1台のPCで機能を試す・手順を検証する（ほかの端末からは接続できません） | [Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=ja-JP&gl=JP) | アプリの画面がそのまま開きます |
| ZIP（Windows / Linux） | 社内LANのサーバーに置き、現場の端末からブラウザで使う | [GitHub Releases](https://github.com/msmsrep/ParallelFactoryMES/releases/latest) | `http://<サーバー>:5000` |
| Docker（amd64 / arm64） | 同上。コンテナで動かす | `ghcr.io/msmsrep/parallelfactorymes` | `http://<サーバー>:8080` |
| ソースから動かす | 開発・改造 | [GitHub](https://github.com/msmsrep/ParallelFactoryMES) | `http://localhost:5288` |

どの方法も無償で、アクティベーションやライセンスキーは不要です。ソースから動かす場合を除き、.NET のインストールも要りません。

## 利用に必要なもの

| 項目 | 内容 |
|---|---|
| サーバー | Windows / Linux（x64）のPC 1台、または Docker が動く環境（amd64 / arm64）。Store 版は Windows 10（2004 以降）/ 11 |
| データベース | 不要。既定ではSQLiteのファイル（`mesapp.db`）が自動生成されます。PostgreSQL / SQL Server も使えます（[管理者向け運用](operations.html#database)） |
| 利用端末 | モダンブラウザ（Chrome / Edge など）。専用アプリのインストールは不要です |
| ネットワーク | 端末からサーバーのURLにアクセスできること |

## 起動する

### Microsoft Store 版

[Microsoft Store](https://apps.microsoft.com/detail/9p9fqjzh23hc?hl=ja-JP&gl=JP) からインストールし、スタートメニューの **Parallel Factory MES** を開きます。
初期管理者は自動で作られ、ユーザー名と初期パスワードが画面の上に表示されます（下の「初期管理者アカウントを作る」は不要です）。

### ZIP

<ol class="steps">
<li><a href="https://github.com/msmsrep/ParallelFactoryMES/releases/latest">Releases</a> から <code>ParallelFactoryMES-&lt;版&gt;-win-x64.zip</code>（Linux は <code>-linux-x64.zip</code>）をダウンロードして展開します。</li>
<li>Windows は <strong>start.cmd</strong> をダブルクリック、Linux は <code>sh start.sh</code> を実行します。</li>
<li>ブラウザで <code>http://localhost:5000</code> を開きます。</li>
</ol>

<div class="warn">
<p>Windows で「WindowsによってPCが保護されました」と表示された場合は、展開する前にZIPファイルのプロパティを開き、<strong>許可する</strong>にチェックを入れてから展開し直してください（<a href="troubleshooting.html#install">困ったときは</a>）。</p>
</div>

### Docker

```bash
docker run -d -p 8080:8080 -v mesapp-data:/data ghcr.io/msmsrep/parallelfactorymes
```

ブラウザで `http://localhost:8080` を開きます。`-v mesapp-data:/data` を付けないと、コンテナを作り直したときにデータが消えます。
PostgreSQL と一緒に動かす場合は[管理者向け運用](operations.html#docker)を参照してください。

### ソースから動かす（開発用）

.NET 10 SDK を入れ、リポジトリのルートで次を実行します。既定では `http://localhost:5288` で起動します。

```bash
dotnet run --project src/MesApp.Api
```

同じLAN内の他のPCから使う場合の設定（ポート・ファイアウォール・HTTPS）は[管理者向け運用](operations.html)を参照してください。

<div class="note">
<p>データベースファイルと署名鍵は<strong>データ保存先</strong>に自動生成されます。データベースの作成作業は不要です。
保存先は既定で <code>%LOCALAPPDATA%\ParallelFactoryMES</code>（Linux は <code>~/.local/share/ParallelFactoryMES</code>、Docker は <code>/data</code>）です。</p>
</div>

## 初期管理者アカウントを作る

ユーザーが1件も登録されていない状態でブラウザからアクセスすると、ログイン画面に
「初期セットアップが必要です」と表示されます。

<ol class="steps">
<li>ログイン画面の <strong>初期管理者アカウントを作成</strong> リンクを開きます。</li>
<li><strong>ユーザー名</strong>（ログインID）、<strong>氏名（表示名）</strong>、<strong>パスワード</strong>を入力します。</li>
<li><strong>作成してログイン画面へ</strong> を押します。</li>
</ol>

パスワードは **8文字以上で、英小文字と数字を含む** 必要があります（例：`Passw0rd123`）。
条件を満たさない場合はエラーが表示され、作成されません。

<div class="warn">
<p>初期セットアップ画面は<strong>ユーザーが0件のときだけ</strong>有効です。一度アカウントを作成した後は使えません。
管理者パスワードを紛失した場合の扱いは<a href="troubleshooting.html">困ったときは</a>を参照してください。</p>
</div>

ここで作成されたアカウントには **システム管理者（SystemAdmin）** ロールが付与されます。

## ログインする

<ol class="steps">
<li>ブラウザでサーバーのURLを開きます。</li>
<li>ユーザー名とパスワードを入力し、<strong>ログイン</strong>を押します（Enterキーでも実行できます）。</li>
<li>ログインするとダッシュボードが表示されます。</li>
</ol>

ログインに失敗し続けるとアカウントが一時的にロックされます。
その場合は「アカウントが一時的にロックされています」と表示されるので、しばらく待ってから再試行してください。

### 初期パスワードの変更

管理者が作成したユーザーは、**初回ログイン時にパスワード変更が必要**です。
ダッシュボード上部に「初期パスワードのままです」という警告が表示されるので、
リンクからパスワード変更画面を開いて変更してください。

## 利用者を登録する  {#add-users}

システム管理者が **マスタ管理 → ユーザー** タブから追加します。

<ol class="steps">
<li><strong>ユーザー名</strong>と<strong>氏名（表示名）</strong>を入力します。</li>
<li><strong>初期パスワード</strong>を入力します（初回ログイン時に変更が強制されます）。</li>
<li>担当業務に合わせて<strong>ロール</strong>のチェックを入れます。複数選択できます。</li>
<li><strong>登録</strong>を押します。</li>
</ol>

ロールごとにできる操作が変わります。詳細は[ロールと権限](roles.html)を参照してください。

差立でスキル・資格の照合を使う場合は、あわせて **マスタ管理 → スキル・資格** で資格を定義し、
ユーザー行の「スキル・資格」からユーザーに紐づけておきます。

## つぎに行うこと

業務を始めるにはマスタ登録が必要です。特に **工程 → 品目 → 工順（BOP）** の順で登録しないと
製造指図を工程展開できません。[マスタ管理](masters.html)へ進んでください。

まず動きを試したい場合は、サンプルデータを入れると製造指図から出荷まで操作できる状態になります。
[マスタのサンプル](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-master-csv.zip)を
**マスタ管理**の「ZIPで一括取込」で、続けて[実績のサンプル](https://github.com/msmsrep/ParallelFactoryMES/releases/latest/download/samples-actual-csv.zip)を
**管理 → 実績CSV取込**の「ZIPで一括取込」で取り込みます（[マスタ管理のCSV](masters.html#csv)）。
