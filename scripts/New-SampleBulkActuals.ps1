<#
.SYNOPSIS
    ダッシュボードの推移（日・週・月）と内訳（工程・品目・直・ライン・作業区・設備）を確かめるための、
    約3か月分のサンプル実績CSVを samples/actual-csv-bulk/ に作る（Spec.md 3.8）

.DESCRIPTION
    samples/master-csv/ のマスタを前提にする（品目 SF-2000 / FG-1000 の工順、設備 EQ-01〜05、不良理由 DR-xx）。
    samples/actual-csv/ とは独立しており、どちらを先に取り込んでもよい（指図番号・ロット番号が重ならない）。

    期間は 2026-06-29（月）〜 2026-09-20（日）の12週。7月・8月は丸1か月ぶん入る。
    推移に変化が見えるよう、次の癖を入れている（乱数は種を固定しているので何度作っても同じ内容になる）。
      - 不良率は7月→8月→9月と下がる（改善活動の効果）。夜勤は昼勤より高い
      - 8/10〜8/14 はお盆休みで実績なし（週次・日次の谷）
      - 8/24 の週はマシニングセンタ EQ-01 の工具摩耗で故障停止が入り、工具摩耗（DR-04）の不良が増える
      - 2号機 EQ-05 は昼勤のみで待機が多く稼働率が低い。9月は段取り改善で待機が減る
      - 7月の隔週土曜は加工だけ休日出勤

    部材の受入・投入は含めない（バックフラッシュしない）。在庫の動きを見るサンプルは samples/actual-csv/ を使う。

.EXAMPLE
    ./scripts/New-SampleBulkActuals.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../samples/actual-csv-bulk')
)

$ErrorActionPreference = 'Stop'
$rng = [System.Random]::new(20260922)
$inv = [System.Globalization.CultureInfo]::InvariantCulture

$firstMonday = [datetime]'2026-06-29'
$weeks = 12
$obonDays = @('2026-08-10', '2026-08-11', '2026-08-12', '2026-08-13', '2026-08-14')
$toolWearDays = @('2026-08-24', '2026-08-25', '2026-08-26', '2026-08-27', '2026-08-28')
$saturdayShifts = @('2026-07-04', '2026-07-18')

function Rand([int]$min, [int]$max) { $rng.Next($min, $max + 1) }
function Fmt([datetime]$t) { $t.ToString('yyyy-MM-dd HH:mm', $inv) }
function Key([datetime]$d) { $d.ToString('yyyy-MM-dd', $inv) }

# 月ごとの不良率の基準（改善で下がっていく）
function BaseRate([datetime]$d) {
    switch ($d.Month) { 6 { 0.032 } 7 { 0.030 } 8 { 0.022 } default { 0.015 } }
}

# 不良数を理由へ振り分ける（重み付き）。"DR-01=2;DR-02=1" の形で返す
function SplitDefects([int]$count, [hashtable]$weights) {
    if ($count -le 0) { return '' }
    $result = [ordered]@{}
    $total = ($weights.Values | Measure-Object -Sum).Sum
    for ($i = 0; $i -lt $count; $i++) {
        $pick = $rng.NextDouble() * $total
        foreach ($code in ($weights.Keys | Sort-Object)) {
            $pick -= $weights[$code]
            if ($pick -le 0) { $result[$code] = 1 + [int]$result[$code]; break }
        }
    }
    ($result.Keys | Sort-Object | ForEach-Object { "$_=$($result[$_])" }) -join ';'
}

function DefectCount([int]$total, [double]$rate) {
    [math]::Max(0, [int][math]::Round($total * $rate + ($rng.NextDouble() - 0.4)))
}

$orders = [System.Collections.Generic.List[string]]::new()
$production = [System.Collections.Generic.List[string]]::new()
$logs = [System.Collections.Generic.List[string]]::new()
$orders.Add('OrderNo,ProductCode,Quantity,DueDate,OrderType,SourceOrderNo,Note,Approve,Expand,OutputLotNumber')
$production.Add('OrderNo,Sequence,GoodQuantity,DefectQuantity,ScrapQuantity,ReworkQuantity,StartedAt,EndedAt,OutputLocationCode,Backflush,Defects')
$logs.Add('EquipmentAssetNo,Status,StartedAt,EndedAt,StopCause,OrderNo,Sequence,Note')

# 設備の1日の稼働を区間の並びとして書く。境目を数分ずらして日ごとの稼働率にばらつきを出す
function AddSegments([string]$asset, [datetime]$day, [object[]]$segments, [string]$orderNo, [int]$sequence) {
    $jittered = @()
    foreach ($s in $segments) {
        $jittered += , @($s[0], $day.AddMinutes($s[1]), $day.AddMinutes($s[2]), $s[3])
    }
    for ($i = 1; $i -lt $jittered.Count; $i++) {
        # 前の区間の終わりと次の区間の始まりを同じだけ動かして、隙間も重なりも作らない
        if ($jittered[$i - 1][2] -eq $jittered[$i][1]) {
            $shift = Rand -10 10
            $jittered[$i - 1][2] = $jittered[$i - 1][2].AddMinutes($shift)
            $jittered[$i][1] = $jittered[$i][1].AddMinutes($shift)
        }
    }
    foreach ($s in $jittered) {
        if ($s[2] -le $s[1]) { continue }
        $link = if ($s[0] -eq 'Running' -and $orderNo) { "$orderNo,$sequence" } else { ',' }
        $logs.Add("$asset,$($s[0]),$(Fmt $s[1]),$(Fmt $s[2]),$($s[3]),$link,")
    }
}

for ($w = 0; $w -lt $weeks; $w++) {
    $monday = $firstMonday.AddDays(7 * $w)
    $tag = $monday.ToString('MMdd', $inv)
    $sfOrder = "BLK-SF-$tag"
    $fgOrder = "BLK-FG-$tag"
    $weekDays = 0..5 | ForEach-Object { $monday.AddDays($_) } | Where-Object {
        ($_.DayOfWeek -ne 'Saturday' -or $saturdayShifts -contains (Key $_)) -and $obonDays -notcontains (Key $_)
    }
    if ($weekDays.Count -eq 0) { continue }   # お盆休みの週は指図も立てない

    $orders.Add("$sfOrder,SF-2000,450,,Normal,,ハウジング加工 $(Key $monday) 週,true,true,SF2000-B$tag")
    $orders.Add("$fgOrder,FG-1000,150,,Normal,,ギアポンプ組立 $(Key $monday) 週,true,true,FG1000-B$tag")

    $sfWeekGood = 0
    foreach ($day in $weekDays) {
        $key = Key $day
        $rate = BaseRate $day
        $toolWear = $toolWearDays -contains $key
        $saturday = $day.DayOfWeek -eq 'Saturday'

        # ---- ハウジング加工（SF-2000 工程10・マシニング加工 WC-MC1）昼勤・夜勤 ----
        $machiningWeights = @{ 'DR-01' = 4; 'DR-02' = 3.5; 'DR-03' = 2.5 }
        if ($toolWear) { $machiningWeights['DR-04'] = 12 }
        $dayRate = if ($toolWear) { $rate + 0.05 } else { $rate }
        $total = Rand 38 46
        $defect = DefectCount $total $dayRate
        $sfWeekGood += $total - $defect
        $production.Add("$sfOrder,10,$($total - $defect),$defect,,,$(Fmt $day.AddHours(8)),$(Fmt $day.AddHours(16.5)),,false,$(SplitDefects $defect $machiningWeights)")
        if (-not $saturday) {
            $total = Rand 32 40
            $defect = DefectCount $total ($dayRate * 1.6)
            $sfWeekGood += $total - $defect
            # 夜勤は20時開始・翌4時半終了。開始時刻の製造日（当日）に入る
            $production.Add("$sfOrder,10,$($total - $defect),$defect,,,$(Fmt $day.AddHours(20)),$(Fmt $day.AddHours(28.5)),,false,$(SplitDefects $defect $machiningWeights)")
        }

        # 設備 EQ-01（昼勤＋夜勤）
        $eq01Day = @(
            @('Setup', 480, 510, ''), @('Running', 510, 720, ''), @('Idle', 720, 780, ''),
            @('Running', 780, 1005, ''))
        if ($toolWear -and $day.DayOfWeek -in 'Tuesday', 'Thursday') {
            $eq01Day = @(
                @('Setup', 480, 510, ''), @('Running', 510, 600, ''), @('Failure', 600, 690, '工具折損（刃先摩耗）'),
                @('Running', 690, 720, ''), @('Idle', 720, 780, ''), @('Running', 780, 1005, ''))
        }
        elseif ((Rand 1 100) -le 15) {
            $eq01Day = @(
                @('Setup', 480, 510, ''), @('Running', 510, 720, ''), @('Idle', 720, 780, ''),
                @('Running', 780, 870, ''), @('Stopped', 870, 915, '材料待ち'), @('Running', 915, 1005, ''))
        }
        AddSegments 'EQ-01' $day $eq01Day $sfOrder 10
        if (-not $saturday) {
            # 0時をまたぐ区間も開始時刻の製造日（当日）に入る
            AddSegments 'EQ-01' $day @(
                @('Setup', 1200, 1220, ''), @('Running', 1220, 1440, ''), @('Idle', 1440, 1485, ''),
                @('Running', 1485, 1710, '')) $sfOrder 10
        }

        # 設備 EQ-05（2号機。昼勤のみ。9月は段取り改善で待機が減る）
        $idleEnd = if ($day.Month -ge 9) { 810 } else { 870 }
        $runEnd = if ($day.Month -ge 9) { 1000 } else { 930 }
        AddSegments 'EQ-05' $day @(
            @('Setup', 480, 525, ''), @('Running', 525, 690, ''), @('Idle', 690, $idleEnd, ''),
            @('Running', $idleEnd, $runEnd, ''), @('Idle', $runEnd, 1020, '')) $sfOrder 10

        if ($saturday) { continue }   # 土曜出勤は加工だけ

        # ---- ギアポンプ（FG-1000）工程10 組立 → 20 最終検査 → 30 梱包 ----
        $assembled = Rand 28 34
        $defect = DefectCount $assembled ($rate * 0.6)
        $good = $assembled - $defect
        $production.Add("$fgOrder,10,$good,$defect,,,$(Fmt $day.AddHours(8.5)),$(Fmt $day.AddHours(16.5)),,false,$(SplitDefects $defect @{ 'DR-05' = 1 })")
        $testDefect = DefectCount $good 0.008
        $tested = $good - $testDefect
        $production.Add("$fgOrder,20,$tested,$testDefect,,,$(Fmt $day.AddHours(13)),$(Fmt $day.AddHours(17)),,false,$(SplitDefects $testDefect @{ 'DR-02' = 1 })")
        $production.Add("$fgOrder,30,$tested,0,,,$(Fmt $day.AddHours(15)),$(Fmt $day.AddHours(17)),WH-P01,false,")

        AddSegments 'EQ-02' $day @(
            @('Setup', 495, 510, ''), @('Running', 510, 720, ''), @('Idle', 720, 780, ''),
            @('Running', 780, 990, '')) $fgOrder 10
        AddSegments 'EQ-03' $day @(
            @('Idle', 480, 600, ''), @('Running', 600, 720, ''), @('Idle', 720, 780, ''),
            @('Running', 780, 960, ''), @('Idle', 960, 1020, '')) $fgOrder 20
        AddSegments 'EQ-04' $day @(
            @('Setup', 885, 900, ''), @('Running', 900, 1020, '')) $fgOrder 30
    }

    # ---- 外注の表面処理（SF-2000 工程20。作業区なし）は週末にまとめて戻る ----
    $returnDay = ($weekDays | Where-Object { $_.DayOfWeek -ne 'Saturday' } | Select-Object -Last 1)
    $defect = DefectCount $sfWeekGood 0.005
    $production.Add("$sfOrder,20,$($sfWeekGood - $defect),$defect,,,$(Fmt $returnDay.AddHours(10)),$(Fmt $returnDay.AddHours(11)),WIP-02,false,$(SplitDefects $defect @{ 'DR-03' = 1 })")
}

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$bom = [System.Text.UTF8Encoding]::new($true)
[IO.File]::WriteAllLines((Join-Path $OutputDirectory '01_manufacturing-orders.csv'), $orders, $bom)
[IO.File]::WriteAllLines((Join-Path $OutputDirectory '02_production-records.csv'), $production, $bom)
[IO.File]::WriteAllLines((Join-Path $OutputDirectory '03_equipment-logs.csv'), $logs, $bom)
Write-Host "作成: $OutputDirectory（指図 $($orders.Count - 1) 行・生産実績 $($production.Count - 1) 行・設備稼働 $($logs.Count - 1) 行）"
