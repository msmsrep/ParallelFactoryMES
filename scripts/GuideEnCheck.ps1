<#
.SYNOPSIS
    ユーザーガイドの英語版（docs/en/）の取りこぼしを一覧にする（docs-dev/GuideEnPlan.md）

.DESCRIPTION
    英語版は日本語版と同じファイル名で docs/en/ に置く。対応がずれても Jekyll のビルドは通るので、ここで拾う。

    検査するもの（あれば終了コード 1）:
      1. 日本語版の無い英語版ページ
      2. 前付けのずれ：nav_url がファイル名と違う、prev_url / next_url が日本語版と違う
      3. 英語版に残った日本語（かな・漢字）。訳さない行には「<!-- 訳さない -->」を書く
      4. 英語版の相対リンク・画像の切れ（英語版がまだ無いページへのリンクは除く。レイアウトが日本語版へ差し替える）
      5. 目次（_data/nav.yml）の ja と en で url の並びが違う
      6. 工場模型の英訳（assets/tour/scenes.en.js）が場面データ（scenes.js）と合わない・日本語が残る（Node で読む）

    参考として出すもの（-Strict のときだけ終了コード 1）:
      7. 英語版がまだ無いページ（日本語版だけのページ）

.EXAMPLE
    ./scripts/GuideEnCheck.ps1
    取りこぼしを一覧にする

.EXAMPLE
    ./scripts/GuideEnCheck.ps1 -Strict
    英語版がまだ無いページも取りこぼしとして扱う（全ページを訳し終えた後の点検）
#>
[CmdletBinding()]
param([switch]$Strict)

$ErrorActionPreference = 'Stop'
$docs = Join-Path $PSScriptRoot '..' 'docs' | Resolve-Path
$en = Join-Path $docs 'en'
$jaChars = '[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]'

# 対象のページ（Markdown と、レイアウトを使わない独立 HTML）
function Get-Pages([string]$dir) {
    if (-not (Test-Path $dir)) { return @() }
    # 404（404.md）はサイトに1枚だけ置き、日英を並べて書くので対象にしない
    Get-ChildItem $dir -File | Where-Object { $_.Extension -in '.md', '.html' -and $_.BaseName -ne '404' }
}
function Get-FrontMatter([string]$path) {
    $result = @{}
    $lines = Get-Content $path -Encoding utf8
    if ($lines.Count -eq 0 -or $lines[0] -ne '---') { return $result }
    for ($i = 1; $i -lt $lines.Count -and $lines[$i] -ne '---'; $i++) {
        if ($lines[$i] -match '^(\w+):\s*(.*)$') { $result[$Matches[1]] = $Matches[2].Trim().Trim('"') }
    }
    $result
}
function Get-HtmlName($file) { [IO.Path]::ChangeExtension($file.Name, '.html') }

$jaPages = @(Get-Pages $docs)
$enPages = @(Get-Pages $en)
$jaNames = @{}; $jaPages | ForEach-Object { $jaNames[(Get-HtmlName $_)] = $_ }
$enNames = @{}; $enPages | ForEach-Object { $enNames[(Get-HtmlName $_)] = $_ }

$orphans = @(); $frontMatter = @(); $japanese = @(); $broken = @(); $navDiff = @()

foreach ($page in $enPages) {
    $name = Get-HtmlName $page
    $rel = "en/$($page.Name)"

    # 1. 日本語版との対応
    if (-not $jaNames.ContainsKey($name)) { $orphans += $rel; continue }

    # 2. 前付け（独立 HTML には無い）
    if ($page.Extension -eq '.md') {
        $fmEn = Get-FrontMatter $page.FullName
        $fmJa = Get-FrontMatter $jaNames[$name].FullName
        if ($fmEn['nav_url'] -ne $name) { $frontMatter += "${rel}: nav_url が '$($fmEn['nav_url'])'（'$name' にする）" }
        foreach ($key in 'prev_url', 'next_url') {
            if ($fmEn[$key] -ne $fmJa[$key]) { $frontMatter += "${rel}: $key が日本語版（'$($fmJa[$key])'）と違う（'$($fmEn[$key])'）" }
        }
    }

    # 3. 残った日本語
    $n = 0
    foreach ($line in Get-Content $page.FullName -Encoding utf8) {
        $n++
        if ($line -match $jaChars -and $line -notmatch '<!-- 訳さない -->') { $japanese += "${rel}:${n}: $($line.Trim())" }
    }

    # 4. 相対リンク・画像
    $text = Get-Content $page.FullName -Raw -Encoding utf8
    $targets = [regex]::Matches($text, '(?:href|src)="([^"#?]+)|\]\(([^)#?\s]+)') |
        ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } }
    foreach ($t in $targets | Sort-Object -Unique) {
        if ($t -match '^(https?:|mailto:|data:|/|\{)') { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $en $t))
        if ($t -notmatch '^\.\./' -and $t.EndsWith('.html') -and -not $t.Contains('/')) {
            # 英語版の中のページ：英語版が無ければ日本語版があればよい（レイアウトが差し替える）
            if ($enNames.ContainsKey($t) -or $jaNames.ContainsKey($t)) { continue }
        }
        elseif ($t.EndsWith('.html') -and (Test-Path ([IO.Path]::ChangeExtension($target, '.md')))) { continue }
        if (-not (Test-Path $target)) { $broken += "${rel}: $t" }
    }
}

# 5. 目次の対応
$navFile = Join-Path $docs '_data' 'nav.yml'
$navUrls = @{ ja = @(); en = @() }
$current = $null
foreach ($line in Get-Content $navFile -Encoding utf8) {
    if ($line -match '^(ja|en):') { $current = $Matches[1] }
    elseif ($current -and $line -match '^\s+url:\s*(\S+)') { $navUrls[$current] += $Matches[1] }
}
if (($navUrls.ja -join ',') -ne ($navUrls.en -join ',')) {
    $navDiff += "ja: $($navUrls.ja -join ', ')"
    $navDiff += "en: $($navUrls.en -join ', ')"
}

# 6. 工場模型の英訳（assets/tour/scenes.en.js）と場面データ（scenes.js）の対応。
#    scenes.en.js は読み込み時に食い違いを TOUR_DATA.translationErrors に積むので、Node で読ませて取り出す
$tour = @()
$tourDir = Join-Path $docs 'assets' 'tour'
if (Test-Path (Join-Path $tourDir 'scenes.en.js')) {
    if (Get-Command node -ErrorAction SilentlyContinue) {
        $script = @'
const vm = require('vm'), fs = require('fs'), path = require('path');
const ctx = { window: {}, console: { error() {} } };
vm.createContext(ctx);
for (const f of ['scenes.js', 'scenes.en.js']) vm.runInContext(fs.readFileSync(path.join(process.argv[2], f), 'utf8'), ctx);
const data = ctx.window.TOUR_DATA, errors = data.translationErrors.slice();
// 上書きした後に日本語が残っている文言（英訳の漏れ）
(function walk(o, p) {
    if (typeof o === 'string') { if (/[぀-ヿ一-鿿]/.test(o)) errors.push(p + ': ' + o); return; }
    if (o && typeof o === 'object') for (const k of Object.keys(o)) if (k !== 'translationErrors') walk(o[k], p ? p + '.' + k : k);
})(data, '');
process.stdout.write(errors.join('\n'));
'@
        # node の出力は UTF-8。既定（日本語版 Windows は CP932）のまま受けると文字化けする
        $saved = [Console]::OutputEncoding
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        try { $out = $script | node - $tourDir 2>&1 } finally { [Console]::OutputEncoding = $saved }
        if ($LASTEXITCODE -ne 0) { $tour += "scenes.js / scenes.en.js を読めません: $out" }
        elseif ($out) { $tour += @($out -split "`n" | Where-Object { $_ }) }
    } else {
        Write-Warning 'node が無いので、工場模型の英訳の点検を飛ばします。'
    }
}

# 7. 英語版がまだ無いページ
$untranslated = @($jaNames.Keys | Where-Object { -not $enNames.ContainsKey($_) } | Sort-Object)

# ---- 報告 ----
function Write-Section([string]$title, $items) {
    Write-Host "== $title（$($items.Count) 件）"
    $items | ForEach-Object { Write-Host "  $_" }
}
Write-Section '日本語版の無い英語版ページ' $orphans
Write-Section '前付けのずれ' $frontMatter
Write-Section '英語版に残った日本語' $japanese
Write-Section '切れたリンク・画像' $broken
Write-Section '目次（_data/nav.yml）の ja/en のずれ' $navDiff
Write-Section '工場模型の英訳（assets/tour/scenes.en.js）のずれ' $tour
Write-Section ('英語版がまだ無いページ' + ($Strict ? '' : '（参考）')) $untranslated

$errors = $orphans.Count + $frontMatter.Count + $japanese.Count + $broken.Count + $navDiff.Count + $tour.Count
if ($Strict) { $errors += $untranslated.Count }
if ($errors -gt 0) { exit 1 }
Write-Host '取りこぼしはありません。'
