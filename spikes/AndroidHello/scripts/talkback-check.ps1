# Plan 20 phase 6: TalkBack on the emulator (docs/design/TALKBACK.md). Turns TalkBack on with its
# speech in logcat, starts the spike, and checks what a TalkBack user does: the tree Android reads,
# a finger exploring the button (TalkBack says it), a double tap (the click, and the announcement it
# makes, spoken from a live region), swipes in reading order, swiping past the list's last visible
# row (TalkBack scrolls it), and a two-finger drag (passed through, the list scrolls).
#   pwsh spikes/AndroidHello/scripts/talkback-check.ps1 [-Serial emulator-5554] [-LeaveOn]
#
# Emulator only: it needs `adb root` (a google_apis image, not a Play Store one), for two reasons.
# - Touches are written to the touchscreen device itself. `adb shell input` injects past TalkBack's
#   touch explorer, so a tap is a plain click; and the emulator console's `event send` drops the
#   positions. The device has no BTN_TOUCH, and a pressure of 0 reads as hovering, so a press sets
#   one. Each press is one write of raw input_event structs: a sendevent process per event was too
#   slow for TalkBack's double tap.
# - TalkBack's developer settings (verbose log: what it speaks goes to logcat) are written into its
#   preferences while it is stopped.
# Run keyboard-check.ps1 before this, not after, or cold-boot the emulator in between: a write to the
# touchscreen device leaves Gboard in its hardware-keyboard mode (a floating toolbar, a 24 DIP inset)
# until a reboot. adb root alone does not; TalkBack is not needed for it.
param([string] $Serial = 'emulator-5554', [switch] $LeaveOn)
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$activity = 'org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity'
$talkback = 'com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService'
$prefs = '/data/user_de/0/com.google.android.marvin.talkback/shared_prefs/com.google.android.marvin.talkback_preferences.xml'
$touch = '/dev/input/event2'
function Adb { & $adb -s $Serial @args }
$failed = 0
function Check([string] $what, [bool] $ok, [string] $detail) {
    if (-not $ok) { $script:failed++ }
    Write-Output ("{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $what, $detail)
}

# What TalkBack said, what the app logged and what the bridge did, since the log was cleared.
function Heard {
    Adb logcat -d | ForEach-Object {
        if ($_ -match 'Speaking fragment text="([^"]*)"') { "said: $($Matches[1])" }
        elseif ($_ -match 'HultUISpike\s*:\s*(.*)$') { "app: $($Matches[1])" }
        elseif ($_ -match 'HultUI\s*:\s*TalkBack: (\w+) on') { "bridge: $($Matches[1])" }
    }
}

# ── Raw touches ──
$screen = (Adb shell wm size) -replace '.*:\s*', ''
$W, $H = ($screen -split 'x') | ForEach-Object { [int]$_ }
function Ev([int] $type, [int] $code, [int] $value) {
    $b = [byte[]]::new(24)   # x86_64 input_event: timeval (16, the kernel stamps it), u16, u16, s32
    [BitConverter]::GetBytes([uint16]$type).CopyTo($b, 16)
    [BitConverter]::GetBytes([uint16]$code).CopyTo($b, 18)
    [BitConverter]::GetBytes([int32]$value).CopyTo($b, 20)
    $b
}
function Pos([double] $x, [double] $y, [int] $slot) { @(Ev 3 47 $slot) + @(Ev 3 53 ([int]($x * 32767 / $W))) + @(Ev 3 54 ([int]($y * 32767 / $H))) }
$syn = Ev 0 0 0
$temp = Join-Path ([IO.Path]::GetTempPath()) 'hultui-talkback-check'
New-Item -ItemType Directory -Force $temp | Out-Null
function Put([string] $name, [byte[]] $bytes) {
    $path = Join-Path $temp $name
    [IO.File]::WriteAllBytes($path, $bytes)
    Adb push $path "/data/local/tmp/$name" 2>&1 | Out-Null
    "cat /data/local/tmp/$name > $touch"
}
function Down([double] $x, [double] $y, [int] $id, [string] $name) { Put $name (@(Ev 3 47 0) + @(Ev 3 57 $id) + (Pos $x $y 0) + @(Ev 3 58 512) + $syn) }
$up = Put 'up' (@(Ev 3 47 0) + @(Ev 3 57 -1) + $syn)
function Hold([double] $x, [double] $y) { Adb shell "$(Down $x $y 11 'd1'); sleep 0.7; $up"; Start-Sleep -Milliseconds 2000 }
function DoubleTap([double] $x, [double] $y) { Adb shell "$(Down $x $y 12 'd1'); sleep 0.04; $up; sleep 0.08; $(Down $x $y 13 'd2'); sleep 0.04; $up"; Start-Sleep -Milliseconds 2500 }
function Swipe([double] $x, [double] $y, [double] $x2, [double] $y2, [int] $fingers = 1, [int] $steps = 6, [int] $ms = 15) {
    $down = if ($fingers -eq 1) { Down $x $y 14 'd1' } else {
        Put 'd1' (@(Ev 3 47 0) + @(Ev 3 57 15) + (Pos $x $y 0) + @(Ev 3 58 512) + @(Ev 3 47 1) + @(Ev 3 57 16) + (Pos ($x + 150) $y 1) + @(Ev 3 58 512) + $syn)
    }
    $moves = foreach ($i in 1..$steps) {
        $px = $x + ($x2 - $x) * $i / $steps; $py = $y + ($y2 - $y) * $i / $steps
        $bytes = if ($fingers -eq 1) { (Pos $px $py 0) + $syn } else { (Pos $px $py 0) + (Pos ($px + 150) $py 1) + $syn }
        "sleep $($ms / 1000); $(Put "m$i" $bytes)"
    }
    $lift = if ($fingers -eq 1) { $up } else { Put 'up2' (@(Ev 3 47 1) + @(Ev 3 57 -1) + @(Ev 3 47 0) + @(Ev 3 57 -1) + $syn) }
    Adb shell (@($down) + $moves + $lift -join '; ')
    Start-Sleep -Milliseconds 2000
}
function SwipeRight { Swipe ($W * 0.3) ($H * 0.62) ($W * 0.75) ($H * 0.62) }

# ── TalkBack on, its speech in logcat ──
Adb root | Out-Null
Start-Sleep -Seconds 2
Adb wait-for-device
Adb shell settings put secure enabled_accessibility_services '""'
Start-Sleep -Seconds 2
Adb shell am force-stop com.google.android.marvin.talkback
$xml = (Adb shell cat $prefs) -join "`n"
$xml = $xml -replace '\s*<string name="pref_log_level">[^<]*</string>', '' -replace '\s*<boolean name="pref_tts_overlay"[^>]*/>', ''
$xml = $xml -replace '</map>', "    <string name=`"pref_log_level`">2</string>`n    <boolean name=`"pref_tts_overlay`" value=`"true`" />`n</map>"
[IO.File]::WriteAllText((Join-Path $temp 'prefs.xml'), $xml + "`n")
Adb push (Join-Path $temp 'prefs.xml') /data/local/tmp/tb_prefs.xml 2>&1 | Out-Null
Adb shell "cat /data/local/tmp/tb_prefs.xml > $prefs"   # in place: TalkBack's owner and mode stay
Adb shell pm grant com.google.android.marvin.talkback android.permission.POST_NOTIFICATIONS
Adb shell settings put secure enabled_accessibility_services $talkback
Adb shell settings put secure accessibility_enabled 1
Start-Sleep -Seconds 4

try {
    Adb shell am force-stop org.hultui.spike.androidhello
    Adb logcat -c
    $started = Adb shell am start -W -n $activity -e ticker 0 2>&1
    if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
    Start-Sleep -Seconds 4
    Check 'the bridge is on' ([bool](Adb logcat -d -s HultUI:I | Select-String 'TalkBack bridge on the surface; accessibility on')) ''

    # 1. The tree Android reads: uiautomator reads it as TalkBack does.
    Adb shell uiautomator dump /sdcard/hultui-tb.xml | Out-Null
    $dump = (Adb shell cat /sdcard/hultui-tb.xml) -join ''
    function Find([string] $text, [string] $class) {
        $m = [regex]::Match($dump, "<node [^>]*text=`"$([regex]::Escape($text))`"[^>]*class=`"$class`"[^>]*bounds=`"\[(\d+),(\d+)\]\[(\d+),(\d+)\]`"")
        if (-not $m.Success) { return $null }
        $n = 1..4 | ForEach-Object { [int]$m.Groups[$_].Value }
        @{ X = ($n[0] + $n[2]) / 2; Y = ($n[1] + $n[3]) / 2; Top = $n[1]; Bottom = $n[3] }
    }
    $button = Find 'Tap me' 'android.widget.Button'
    $heading = Find 'HultUI on Android' 'android.widget.TextView'
    $field = Find 'alpha bravo charlie' 'android.widget.EditText'
    $lastRow = @(1..20 | ForEach-Object { Find "Row $_" 'android.widget.(?:TextView|Button)' } | Where-Object { $_ }) | Select-Object -Last 1
    Check 'the tree: a button, a heading, a field, rows' ($button -and $heading -and $field -and $lastRow) ''
    if (-not $button) { throw "no button in the dump: $dump" }

    # 2. A finger on the button: TalkBack focuses it and says it.
    Adb logcat -c
    Hold $button.X $button.Y
    $got = Heard
    Check 'explore: the button is said' ([bool]($got -match '^said: Tap me\. Button$')) ($got -join ' | ')

    # 3. A double tap anywhere clicks it; its announcement is spoken from the live region.
    Adb logcat -c
    DoubleTap ($W / 2) ($H * 0.62)
    $got = Heard
    Check 'double tap clicks' ([bool]($got -match '^bridge: Click$') -and [bool]($got -match '^app: click 1$')) ($got -join ' | ')
    Check 'the announcement is spoken' ([bool]($got -match '^said: Counted 1$')) ''

    # 4. Swipes go on in reading order.
    Hold $heading.X $heading.Y
    Adb logcat -c
    SwipeRight; SwipeRight
    $said = @(Heard | Where-Object { $_ -like 'said:*' })
    Check 'swipes read in order' ($said.Count -ge 2 -and $said[0] -eq 'said: tick 0' -and $said[1] -like 'said: This paragraph*') ($said -join ' | ')

    # 5. Past the list's last visible row, TalkBack scrolls the list on.
    Hold ($W / 2) ($lastRow.Top + 4)
    Adb logcat -c
    foreach ($i in 1..3) { SwipeRight }
    $got = Heard
    $rows = @($got | Where-Object { $_ -match '^said: Row (\d+)$' } | ForEach-Object { [int]($_ -replace '\D', '') })
    Check 'swiping past the end scrolls the list' ([bool]($got -match '^bridge: ScrollForward$') -and ($rows | Where-Object { $_ -gt 12 })) ($got -join ' | ')

    # 6. Two fingers drag the list: TalkBack passes the drag through, and the list scrolls.
    Adb logcat -c
    Swipe ($W * 0.3) ($H * 0.85) ($W * 0.3) ($H * 0.6) -fingers 2 -steps 12 -ms 20
    $scrolled = @(Heard | Where-Object { $_ -match '^app: scrolled' })
    Check 'two fingers scroll the list' ($scrolled.Count -gt 0) "$($scrolled.Count) scroll steps"
}
finally {
    if (-not $LeaveOn) {
        Adb shell settings put secure enabled_accessibility_services '""'
        Adb shell settings put secure accessibility_enabled 0
    }
}
if ($failed) { throw "$failed check(s) failed" }
Write-Output 'all TalkBack checks passed'
