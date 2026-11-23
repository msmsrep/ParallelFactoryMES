<#
.SYNOPSIS
    Parallel Factory MES の Webhook を受ける最小の見本（PowerShell 7）

.DESCRIPTION
    署名（X-Mes-Signature）を確かめ、時刻のずれと重複を弾いて、通知の要約を表示する。
    自分の処理（在庫の取り込み、表示盤への出力など）は Invoke-MesEvent に書く。

    MES と同じ PC で動かす見本（http://localhost だけで待つので管理者の権限は要らない）。
    MES の送信先には http://localhost:9000/mes-webhook を登録する。
    別の PC や Docker の MES から受けるときは、Python の見本（receive_mes_webhook.py）か Node-RED の見本を使う。

.EXAMPLE
    ./Receive-MesWebhook.ps1 -Secret whsec_...
#>
[CmdletBinding()]
param(
    # 送信先を登録したときの秘密の値（whsec_...）。省くと環境変数 MES_WEBHOOK_SECRET を読む
    [string]$Secret = $env:MES_WEBHOOK_SECRET,
    [int]$Port = 9000
)

$ErrorActionPreference = 'Stop'
if (-not $Secret) { throw '-Secret か環境変数 MES_WEBHOOK_SECRET に秘密の値を指定してください' }

# これより古い・先の時刻の要求は、送り直し（再生攻撃）とみなして弾く
$toleranceSeconds = 300
# 重複を見分けるために覚えておく通知の数
$seenLimit = 1000
$seen = [System.Collections.Generic.Queue[string]]::new()
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Test-MesSignature([string]$timestamp, [string]$signature, [byte[]]$body) {
    # 署名は「X-Mes-Timestamp.本文」の HMAC-SHA256 を小文字の16進にし、sha256= を付けたもの。
    # 本文は受け取ったバイト列そのものを使う（JSON に読み替えてから作り直すと、数値の書き方などが変わって合わない）
    if ($timestamp -notmatch '^\d+$') { return $false }
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    if ([Math]::Abs($now - [long]$timestamp) -gt $toleranceSeconds) { return $false }
    $message = $utf8.GetBytes("$timestamp.") + $body
    $hash = [System.Security.Cryptography.HMACSHA256]::HashData($utf8.GetBytes($Secret), $message)
    $expected = 'sha256=' + [Convert]::ToHexStringLower($hash)
    return [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        $utf8.GetBytes($expected), $utf8.GetBytes($signature))
}

function Invoke-MesEvent($mesEvent) {
    # 届いた通知を処理する。ここを自分の処理に書き換える。
    # 通知は「読みに行くきっかけ」。正しい今の値は連携API（生産実績のフィードなど）で取りに行く
    Write-Host "[$($mesEvent.type)] $($mesEvent.summary)  (sequence $($mesEvent.sequence))"
}

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "http://localhost:$Port/mes-webhook で待っています（Ctrl+C で止める）"
try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response
        try {
            if ($request.HttpMethod -ne 'POST' -or $request.Url.AbsolutePath -ne '/mes-webhook') {
                $response.StatusCode = 404
                continue
            }
            $buffer = [System.IO.MemoryStream]::new()
            $request.InputStream.CopyTo($buffer)
            $body = $buffer.ToArray()
            if (-not (Test-MesSignature $request.Headers['X-Mes-Timestamp'] $request.Headers['X-Mes-Signature'] $body)) {
                Write-Host '署名か時刻が合わないので受け付けませんでした'
                $response.StatusCode = 401
                continue
            }
            $mesEvent = $utf8.GetString($body) | ConvertFrom-Json
            # 先に 2xx を返す（MES は応答を待っている。重い処理は応答の後に回す）
            $response.StatusCode = 200
            $response.Close()

            # 少なくとも1回届くので、同じ id は2回目以降を捨てる
            if ($seen.Contains($mesEvent.id)) {
                Write-Host "重複を捨てました：$($mesEvent.id)"
                continue
            }
            $seen.Enqueue($mesEvent.id)
            while ($seen.Count -gt $seenLimit) { [void]$seen.Dequeue() }
            Invoke-MesEvent $mesEvent
        }
        finally {
            # 応答を返していなければ閉じる（閉じ済みなら何もしない）
            try { $response.Close() } catch [ObjectDisposedException] { }
        }
    }
}
finally {
    $listener.Stop()
}
