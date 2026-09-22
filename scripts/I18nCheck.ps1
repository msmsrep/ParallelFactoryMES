<#
.SYNOPSIS
    多言語対応の取りこぼしを一覧にする（Spec.md 7.9。docs-dev/I18nPlan.md の I18N-10）

.DESCRIPTION
    キーは日本語の原文そのものなので、文言を書き換えるとキーも変わり、英訳が当たらなくなる。
    また L[...] を通し忘れた文言は英語に切り替えても日本語のまま残る。どちらもビルドでは分からないので、ここで拾う。

    検査するもの:
      1. 画面（src/MesApp.Client.Web の .razor / .cs）の L["原文"] のうち、UiText.en.resx に訳が無いもの
      2. API（src/MesApp.Api の .cs）の ApiText.T("原文") のうち、ApiText.en.resx に訳が無いもの
      3. 画面の日本語（かな・漢字）のうち、L[...] を通っていないもの（コメントと「訳さない」と書いた行は除く）
    1〜3 のどれかがあれば終了コード 1 を返す。

    訳さない日本語（保存される値の既定値・言語名など）は、その行に「// …訳さない」か「@* 訳さない *@」を書く
    （scripts/I18nWrap.py も同じ目印で飛ばす）。
    API の日本語リテラルは、保存される文字列（監査ログ・状態履歴の理由・在庫トランザクションの備考）を
    包まない規則のため機械的に見分けられず、3 の対象にしない（レビューで見る）。
    CSV の列の表示名（MasterCsvKinds / ActualCsvKinds）は CsvImport.Localize が変数で訳すため 2 の対象外。
    区分値の表示名（CoreText.en.resx）は CultureTests が全件を検査している。

.EXAMPLE
    ./scripts/I18nCheck.ps1
    取りこぼしを一覧にする

.EXAMPLE
    ./scripts/I18nCheck.ps1 -ShowUnused
    画面の英訳のうち、どこからも使われていないキー（文言を書き換えた後の残骸の候補）も出す
#>
[CmdletBinding()]
param(
    # UiText.en.resx のうち、画面のどこにも原文が見当たらないキーも出す（部品に変数で渡す文言もあるので参考扱い）
    [switch]$ShowUnused
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src/MesApp.Client.Web'
$api = Join-Path $root 'src/MesApp.Api'

# かな・漢字・半角カナ（全角の記号「／」「（）」などは言語に依らないので対象にしない）
$jp = [regex]'[぀-ヿ一-鿿ｦ-ﾟ]'
$lit = '"((?:[^"\\\r\n]|\\.)*)"'

function Get-Sources([string]$dir, [string[]]$patterns) {
    Get-ChildItem -Path $dir -Recurse -File -Include $patterns |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object FullName
}

function Get-ResxKeys([string]$path) {
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($data in ([xml](Get-Content -Raw -Encoding utf8 $path)).root.data) { [void]$set.Add($data.name) }
    , $set
}

# C# の文字列リテラルの中身を実際の文字列に戻す（resx のキーと比べるため）
function ConvertFrom-CSharpLiteral([string]$s) {
    [regex]::Replace($s, '\\(.)', { param($m) switch ($m.Groups[1].Value) { 'n' { "`n" } 't' { "`t" } default { $m.Groups[1].Value } } })
}

function Get-RelativePath([string]$path) { [IO.Path]::GetRelativePath($root, $path).Replace('\', '/') }

# 1行から「訳の対象外の部分」（コメント・包み済みの原文）を除いた残りを返す
function Remove-Handled([string]$line) {
    $s = [regex]::Replace($line, '@\*.*?\*@', '')
    $s = [regex]::Replace($s, 'L\[\s*' + $lit, 'L[')
    # 行末の // コメント（文字列の外で、行頭か空白・;・{ の直後のもの。URL の // を拾わないため）
    $inQuote = $false
    for ($i = 0; $i -lt $s.Length - 1; $i++) {
        $c = $s[$i]
        if ($c -eq '"' -and ($i -eq 0 -or $s[$i - 1] -ne '\')) { $inQuote = -not $inQuote }
        if (-not $inQuote -and $c -eq '/' -and $s[$i + 1] -eq '/' -and ($i -eq 0 -or " `t;{".Contains($s[$i - 1]))) {
            return $s.Substring(0, $i)
        }
    }
    $s
}

$missing = [System.Collections.Generic.List[string]]::new()
$unwrapped = [System.Collections.Generic.List[string]]::new()

# ---- 画面 ----
$uiKeys = Get-ResxKeys (Join-Path $web 'Shared/UiText.en.resx')
$uiUsed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$uiSeen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-Sources $web @('*.razor', '*.cs')) {
    $rel = Get-RelativePath $file.FullName
    $lines = Get-Content -Encoding utf8 $file.FullName
    $inComment = $false
    for ($n = 0; $n -lt $lines.Count; $n++) {
        $line = $lines[$n]
        foreach ($m in [regex]::Matches($line, 'L\[\s*' + $lit)) {
            $key = ConvertFrom-CSharpLiteral $m.Groups[1].Value
            [void]$uiUsed.Add($key)
            if (-not $uiKeys.Contains($key) -and $uiSeen.Add($key)) { $missing.Add("${rel}:$($n + 1): $key") }
        }
        # 複数行の @* *@ コメント
        if ($inComment) { if ($line.Contains('*@')) { $inComment = $false }; continue }
        $trim = $line.Trim()
        if ($trim.StartsWith('@*') -and -not $trim.Contains('*@')) { $inComment = $true; continue }
        if ($trim.StartsWith('//') -or $trim.StartsWith('*') -or $line.Contains('訳さない')) { continue }
        if ($jp.IsMatch((Remove-Handled $line))) { $unwrapped.Add("${rel}:$($n + 1): $trim") }
    }
}

# ---- API ----
$apiKeys = Get-ResxKeys (Join-Path $api 'Localization/ApiText.en.resx')
$apiSeen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in Get-Sources $api @('*.cs')) {
    $rel = Get-RelativePath $file.FullName
    $text = Get-Content -Raw -Encoding utf8 $file.FullName
    foreach ($m in [regex]::Matches($text, 'ApiText\.T\(\s*' + $lit)) {
        $key = ConvertFrom-CSharpLiteral $m.Groups[1].Value
        if (-not $apiKeys.Contains($key) -and $apiSeen.Add($key)) {
            $lineNo = ($text.Substring(0, $m.Index) -split "`n").Count
            $missing.Add("${rel}:${lineNo}: $key")
        }
    }
}

# ---- 報告 ----
function Write-Section([string]$title, $items) {
    Write-Host "== $title（$($items.Count) 件）"
    $items | ForEach-Object { Write-Host "  $_" }
}
Write-Section 'en.resx に訳が無いキー' $missing
Write-Section 'L[...] を通っていない画面の日本語' $unwrapped
if ($ShowUnused) {
    $unused = @($uiKeys | Where-Object { -not $uiUsed.Contains($_) } | Sort-Object)
    Write-Section '画面のどこからも使われていない英訳（参考）' $unused
}

if ($missing.Count + $unwrapped.Count -gt 0) { exit 1 }
Write-Host '取りこぼしはありません。'
