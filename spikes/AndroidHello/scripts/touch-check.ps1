# Plan 20 phase 3: touch on a device. Starts the spike, then through `adb shell input`:
# a tap on the button (a click, stamped as a touch), a slow drag on the list (it scrolls, and
# does not throw), a fast swipe (it flings on past the finger), and a long press on the button
# (the long press, and no click). Reads the app's logcat lines (tag HultUISpike) for each.
#   pwsh spikes/AndroidHello/scripts/touch-check.ps1 [-Serial emulator-5554]
param([string] $Serial = 'emulator-5554')
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$activity = 'org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity'
function Adb { & $adb -s $Serial @args }
function Lines { @(Adb logcat -d -s HultUISpike:I | Where-Object { $_ -match 'HultUISpike' } | ForEach-Object { ($_ -split 'HultUISpike\s*:\s*', 2)[1] }) }
function Since([int] $from) { $all = Lines; if ($all.Count -gt $from) { $all[$from..($all.Count - 1)] } else { @() } }
function Px([double] $dip) { [int][math]::Round($dip * $script:scale) }
$failed = 0
function Check([string] $what, [bool] $ok, [string] $detail) {
    if (-not $ok) { $script:failed++ }
    Write-Output ("{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $what, $detail)
}

Adb shell am force-stop org.hultui.spike.androidhello
Adb logcat -c
$started = Adb shell am start -W -n $activity -e ticker 0 2>&1
if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
Start-Sleep -Seconds 4

$geometry = Lines | Where-Object { $_ -match '^geometry' } | Select-Object -Last 1
$scaleLine = Lines | Where-Object { $_ -match 'display scale' } | Select-Object -Last 1
if (-not $geometry -or -not $scaleLine) { throw "no geometry in logcat: $(Lines -join "`n")" }
$script:scale = [double]::Parse(($scaleLine -replace '.*display scale\s*', ''), [Globalization.CultureInfo]::InvariantCulture)
$nums = [regex]::Matches($geometry, '-?[\d.]+') | ForEach-Object { [double]::Parse($_.Value, [Globalization.CultureInfo]::InvariantCulture) }
$button = @{ X = $nums[0]; Y = $nums[1]; W = $nums[2]; H = $nums[3] }
$list = @{ X = $nums[4]; Y = $nums[5]; W = $nums[6]; H = $nums[7] }
Write-Output "scale $scale; button $($button.Values -join ','); list at y $($list.Y) h $($list.H)"
$bx = Px ($button.X + $button.W / 2); $by = Px ($button.Y + $button.H / 2)
$lx = Px ($list.X + $list.W / 2)

# 1. A tap clicks, as a touch pointer.
$n = (Lines).Count
Adb shell input tap $bx $by
Start-Sleep -Milliseconds 800
$got = Since $n
Check 'tap clicks' ([bool]($got -match '^click 1$')) ($got -join ' | ')
Check 'tap is a touch pointer' ([bool]($got -match '^press Touch id 2 primary True$')) ''

# 2. A slow drag on the list scrolls it about as far as the finger went, less the slop.
$n = (Lines).Count
$from = Px ($list.Y + $list.H * 0.75); $to = Px ($list.Y + $list.H * 0.35)
# 6 s: about 35 DIP a second, under the 50 a fling needs, so the list stops with the finger
# (at 4 s it was 53, and the list rightly went on 14 DIP).
Adb shell input swipe $lx $from $lx $to 6000
Start-Sleep -Milliseconds 1500
$scrolls = @(Since $n | Where-Object { $_ -match '^scrolled' } | ForEach-Object { [int]($_ -replace '\D', '') })
$expected = [math]::Round(($from - $to) / $scale) - 8
$last = if ($scrolls.Count) { $scrolls[-1] } else { 0 }
Check 'slow drag scrolls 1:1' ([math]::Abs($last - $expected) -le 12) "scrolled $last, finger moved $expected past the slop, $($scrolls.Count) steps"
$afterDrag = $last

# 3. A fast swipe flings: the list goes on well past where the finger stopped.
$n = (Lines).Count
Adb shell input swipe $lx $from $lx $to 60
Start-Sleep -Milliseconds 2500
$scrolls = @(Since $n | Where-Object { $_ -match '^scrolled' } | ForEach-Object { [int]($_ -replace '\D', '') })
$last = if ($scrolls.Count) { $scrolls[-1] } else { $afterDrag }
$finger = [math]::Round(($from - $to) / $scale)
Check 'fast swipe flings' (($last - $afterDrag) -gt ($finger * 1.5)) "moved $($last - $afterDrag) for a finger's $finger, $($scrolls.Count) steps"
$captures = Join-Path $env:TEMP 'hultui-spike-captures'
New-Item -ItemType Directory -Force $captures | Out-Null
# Through cmd: PowerShell's redirection re-encodes binary output.
cmd /c "`"$adb`" -s $Serial exec-out screencap -p > `"$(Join-Path $captures 'touch-after-fling.png')`""

# 4. A long press on the button is a long press, and no click.
$n = (Lines).Count
Adb shell input swipe $bx $by $bx $by 900
Start-Sleep -Milliseconds 800
$got = Since $n
Check 'long press' ([bool]($got -match '^long press \(Touch\)$')) ($got -join ' | ')
Check 'long press clicks nothing' (-not ($got -match '^click')) ''

if ($failed) { Write-Output "$failed check(s) failed"; exit 1 }
Write-Output 'all touch checks passed'
