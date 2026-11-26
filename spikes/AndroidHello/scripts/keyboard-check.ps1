# Plan 20 phase 4: the soft keyboard and touch text editing on a device. Starts the spike, then
# through `adb shell input`: a tap on the text field (the keyboard comes up, its height reaches
# Window.KeyboardInset, and the field asked for one line of text), a tap on the number field
# (the keyboard's input type is a number's), Back (the keyboard goes, and the inset), and a long
# press on the field's words (a word is selected; a screenshot shows its handles and the bar).
# Reads the app's logcat lines (tag HultUISpike) and the keyboard's EditorInfo from dumpsys.
#   pwsh spikes/AndroidHello/scripts/keyboard-check.ps1 [-Serial emulator-5554]
param([string] $Serial = 'emulator-5554')
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$activity = 'org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity'
function Adb { & $adb -s $Serial @args }
function Lines { @(Adb logcat -d -s HultUISpike:I | Where-Object { $_ -match 'HultUISpike' } | ForEach-Object { ($_ -split 'HultUISpike\s*:\s*', 2)[1] }) }
function Since([int] $from) { $all = Lines; if ($all.Count -gt $from) { $all[$from..($all.Count - 1)] } else { @() } }
function Px([double] $dip) { [int][math]::Round($dip * $script:scale) }
# The focused editor's input type, as the keyboard got it (android.text.InputType bits).
function InputType {
    $found = @(Adb shell dumpsys input_method | Select-String -Pattern 'inputType=0x([0-9a-fA-F]+)' -AllMatches |
        ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value })
    if (-not $found.Count) { return -1 }
    [Convert]::ToInt32($found[-1], 16)
}
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

$fields = Lines | Where-Object { $_ -match '^fields' } | Select-Object -Last 1
$scaleLine = Lines | Where-Object { $_ -match 'display scale' } | Select-Object -Last 1
if (-not $fields -or -not $scaleLine) { throw "no field geometry in logcat: $(Lines -join "`n")" }
$script:scale = [double]::Parse(($scaleLine -replace '.*display scale\s*', ''), [Globalization.CultureInfo]::InvariantCulture)
$nums = [regex]::Matches($fields, '-?[\d.]+') | ForEach-Object { [double]::Parse($_.Value, [Globalization.CultureInfo]::InvariantCulture) }
$text = @{ X = $nums[0]; Y = $nums[1]; W = $nums[2]; H = $nums[3] }
$number = @{ X = $nums[4]; Y = $nums[5]; W = $nums[6]; H = $nums[7] }
Write-Output "scale $scale; text field at $($text.X),$($text.Y) $($text.W)x$($text.H); number field at y $($number.Y)"

# 1. A tap on the text field: the keyboard comes up, and its height reaches the window.
$n = (Lines).Count
Adb shell input tap (Px ($text.X + $text.W * 0.95)) (Px ($text.Y + $text.H / 2))
Start-Sleep -Milliseconds 2000
$insets = @(Since $n | Where-Object { $_ -match '^keyboard inset' } | ForEach-Object { [int]($_ -replace '\D', '') })
$inset = if ($insets.Count) { $insets[-1] } else { 0 }
Check 'the keyboard''s inset reaches the window' ($inset -gt 100) "inset $inset DIP ($($insets.Count) changes)"
$type = InputType
# CLASS_TEXT (1), and no MULTI_LINE (0x20000): a one-line field gets the action key.
Check 'the text field asks for one line of text' ($type -ge 0 -and ($type -band 0xF) -eq 1 -and ($type -band 0x20000) -eq 0) ('inputType 0x{0:x}' -f $type)

# 2. A tap on the number field: a number's keyboard.
Adb shell input tap (Px ($number.X + $number.W / 2)) (Px ($number.Y + $number.H / 2))
Start-Sleep -Milliseconds 1500
$type = InputType
Check 'the number field gets a number keyboard' (($type -band 0xF) -eq 2) ('inputType 0x{0:x}' -f $type)

# 3. Back: the keyboard goes, and the inset with it.
$n = (Lines).Count
Adb shell input keyevent KEYCODE_BACK
Start-Sleep -Milliseconds 1500
$insets = @(Since $n | Where-Object { $_ -match '^keyboard inset' } | ForEach-Object { [int]($_ -replace '\D', '') })
Check 'Back hides the keyboard, and the inset goes' ($insets.Count -gt 0 -and $insets[-1] -eq 0) "insets after Back: $($insets -join ', ')"

# 4. A long press on the field's words: a word is selected.
$n = (Lines).Count
$x = Px ($text.X + 62); $y = Px ($text.Y + $text.H / 2)
Adb shell input swipe $x $y $x $y 900
Start-Sleep -Milliseconds 1200
$selected = @(Since $n | Where-Object { $_ -match '^selected' } | ForEach-Object { ($_ -replace '^selected\s*"', '') -replace '"$', '' })
$word = if ($selected.Count) { $selected[-1] } else { '' }
Check 'a long press selects a word' ($word -in @('alpha', 'bravo', 'charlie')) "selected '$word'"
$captures = Join-Path $env:TEMP 'hultui-spike-captures'
New-Item -ItemType Directory -Force $captures | Out-Null
# Through cmd: PowerShell's redirection re-encodes binary output.
cmd /c "`"$adb`" -s $Serial exec-out screencap -p > `"$(Join-Path $captures 'keyboard-selection.png')`""
Write-Output "screenshot: $(Join-Path $captures 'keyboard-selection.png')"

if ($failed) { Write-Output "$failed check(s) failed"; exit 1 }
Write-Output 'all keyboard checks passed'
