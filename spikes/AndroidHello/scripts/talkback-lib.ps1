# What the TalkBack checks share (talkback-check.ps1, talkback-a11y-check.ps1), dot-sourced after
# $Serial is set: adb, a check's line, what TalkBack said, raw touches on the emulator's touchscreen
# device, and TalkBack switched on with its speech in logcat.
#
# Emulator only: it needs `adb root` (a google_apis image, not a Play Store one), for two reasons.
# - Touches are written to the touchscreen device itself. `adb shell input` injects past TalkBack's
#   touch explorer, so a tap is a plain click; and the emulator console's `event send` drops the
#   positions. The device has no BTN_TOUCH, and a pressure of 0 reads as hovering, so a press sets
#   one. Each press is one write of raw input_event structs: a sendevent process per event was too
#   slow for TalkBack's double tap.
# - TalkBack's developer settings (verbose log: what it speaks goes to logcat) are written into its
#   preferences while it is stopped.
# Run keyboard-check.ps1 before these, not after, or cold-boot the emulator in between: a write to the
# touchscreen device leaves Gboard in its hardware-keyboard mode (a floating toolbar, a 24 DIP inset)
# until a reboot. adb root alone does not; TalkBack is not needed for it.
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
        elseif ($_ -match 'HultUI\s*:\s*TalkBack: (.*?)(?: on \d.*)?$') { "bridge: $($Matches[1])" }
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
# A finger resting on (x, y): explore by touch.
function Hold([double] $x, [double] $y) { Adb shell "$(Down $x $y 11 'd1'); sleep 0.7; $up"; Start-Sleep -Milliseconds 2000 }
function DoubleTap([double] $x, [double] $y) { Adb shell "$(Down $x $y 12 'd1'); sleep 0.04; $up; sleep 0.08; $(Down $x $y 13 'd2'); sleep 0.04; $up"; Start-Sleep -Milliseconds 2500 }
# TalkBack's long press: a double tap whose second press stays down.
function DoubleTapHold([double] $x, [double] $y) { Adb shell "$(Down $x $y 12 'd1'); sleep 0.04; $up; sleep 0.08; $(Down $x $y 13 'd2'); sleep 1.5; $up"; Start-Sleep -Milliseconds 2500 }
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
function SwipeLeft { Swipe ($W * 0.75) ($H * 0.62) ($W * 0.3) ($H * 0.62) }
function SwipeDown { Swipe ($W * 0.5) ($H * 0.45) ($W * 0.5) ($H * 0.7) }
function SwipeUp { Swipe ($W * 0.5) ($H * 0.7) ($W * 0.5) ($H * 0.45) }

# ── TalkBack on, its speech in logcat; off again ──
function Start-TalkBack {
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
}

function Stop-TalkBack {
    Adb shell settings put secure enabled_accessibility_services '""'
    Adb shell settings put secure accessibility_enabled 0
}

# Starts the spike (fresh log), with intent extras as a flat list ('-e', 'sample', 'a11y').
function Start-Spike([string[]] $Extras = @()) {
    Adb shell am force-stop org.hultui.spike.androidhello
    Adb logcat -c
    $started = Adb shell am start -W -n $activity -e ticker 0 @Extras 2>&1
    if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
    Start-Sleep -Seconds 4
}

# The tree Android reads: uiautomator reads it as TalkBack does. Find gives a node's centre and edges.
function Read-Tree {
    Adb shell uiautomator dump /sdcard/hultui-tb.xml | Out-Null
    $script:dump = (Adb shell cat /sdcard/hultui-tb.xml) -join ''
}
function Find([string] $text, [string] $class) {
    $m = [regex]::Match($script:dump, "<node [^>]*text=`"$([regex]::Escape($text))`"[^>]*class=`"$class`"[^>]*bounds=`"\[(\d+),(\d+)\]\[(\d+),(\d+)\]`"")
    if (-not $m.Success) { return $null }
    $n = 1..4 | ForEach-Object { [int]$m.Groups[$_].Value }
    @{ X = ($n[0] + $n[2]) / 2; Y = ($n[1] + $n[3]) / 2; Top = $n[1]; Bottom = $n[3] }
}
