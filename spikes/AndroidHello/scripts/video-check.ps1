# Plan 20 phase 5: video on a device. Puts a clip into the spike's own files folder, plays it in
# the video sample (HultUI's player over libmpv on GL ES), and checks, from the app's logcat lines
# (tag HultUISpike) and from screenshots:
#   - the first frame arrives, and playback keeps at least 20 frames a second;
#   - the picture is on screen, whole, while playing;
#   - a tap on the picture pauses, and the paused picture stays (on GL ES it was black once:
#     src/HultUI.Media.Mpv/GlesFboFormat.cs);
#   - a second tap plays on.
# The picture checks look for colour in the middle of the screen, so the clip should be a bright
# test pattern, such as ffmpeg's testsrc2:
#   ffmpeg -f lavfi -i testsrc2=size=1920x1080:rate=30:duration=12 -f lavfi -i sine=duration=12 \
#     -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest h264-1080p.mp4
# The spike needs the Android libmpv (native/mpv/out/android-*) when it is built.
#   pwsh spikes/AndroidHello/scripts/video-check.ps1 -Clip h264-1080p.mp4 [-Serial emulator-5554]
param([Parameter(Mandatory)] [string] $Clip, [string] $Serial = 'emulator-5554')
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$pkg = 'org.hultui.spike.androidhello'
$activity = "$pkg/crc64ea9bba10985cac6f.MainActivity"
function Adb { & $adb -s $Serial @args }
function Lines { @(Adb logcat -d -s HultUISpike:I | Where-Object { $_ -match 'HultUISpike' } | ForEach-Object { ($_ -split 'HultUISpike\s*:\s*', 2)[1] }) }
$failed = 0
function Check([string] $what, [bool] $ok, [string] $detail) {
    if (-not $ok) { $script:failed++ }
    Write-Output ("{0}  {1}  {2}" -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $what, $detail)
}

# The share of the screen's middle band, sampled every 8 pixels, that is not near black. A clean
# test pattern fills it (1.0 on the emulator and on a Galaxy S25); a black picture leaves none, and
# speckled chroma (NV12 through a separable scaler on Adreno, see MpvBackend's cscale) about a third.
Add-Type -AssemblyName System.Drawing
function Colour([string] $name) {
    $device = "/sdcard/video-check.png"
    $local = Join-Path ([IO.Path]::GetTempPath()) "video-check-$name.png"
    Adb shell screencap -p $device
    Adb pull $device $local 2>&1 | Out-Null
    $bitmap = [System.Drawing.Bitmap]::new($local)
    try {
        $lit = 0; $all = 0
        for ($y = [int]($bitmap.Height * 0.45); $y -lt $bitmap.Height * 0.55; $y += 8) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 8) {
                $p = $bitmap.GetPixel($x, $y)
                if ($p.R + $p.G + $p.B -gt 90) { $lit++ }
                $all++
            }
        }
        [math]::Round($lit / $all, 2)
    } finally { $bitmap.Dispose() }
}
function LastReport { Lines | Where-Object { $_ -match '^video: (Playing|Paused) at' } | Select-Object -Last 1 }
function FramesIn([string] $report) { if ($report -match ', (\d+) frames in 2 s') { [int]$Matches[1] } else { -1 } }

# The clip goes through run-as, which a debuggable app allows on any device: the shell reads it,
# the app's own user writes it.
$name = Split-Path $Clip -Leaf
Adb push (Resolve-Path $Clip) "/data/local/tmp/$name" 2>&1 | Out-Null
Adb shell "cat /data/local/tmp/$name | run-as $pkg sh -c 'mkdir -p files && cat > files/$name'"

Adb shell am force-stop $pkg
Adb logcat -c
$started = Adb shell am start -W -n $activity -e ticker 0 -e sample video -e file "/data/user/0/$pkg/files/$name" 2>&1
if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
# An override size, when there is one, is the line after the physical one.
$size = (Adb shell wm size | Select-Object -Last 1) -replace '.*:\s*', '' -split 'x'
$cx = [int]$size[0] / 2; $cy = [int]$size[1] / 2

# 1. It plays.
$deadline = (Get-Date).AddSeconds(15)
while (-not (Lines | Where-Object { $_ -match '^video: first frame' }) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
$first = Lines | Where-Object { $_ -match '^video: (first frame|libmpv not available)' } | Select-Object -First 1
Check 'first frame' ([bool]($first -match 'first frame')) "$first"
if ($first -notmatch 'first frame') { Write-Output (Lines -join "`n"); exit 1 }
Start-Sleep -Seconds 4
$report = LastReport
Check 'plays at 20 fps or more' ((FramesIn $report) -ge 40) "$report"
$playing = Colour 'playing'
Check 'picture while playing' ($playing -ge 0.8) "$playing of the middle band lit"

# 2. A tap pauses, and the picture stays.
Adb shell input tap $cx $cy
Start-Sleep -Seconds 3
$report = LastReport
Check 'tap pauses' ([bool]($report -match '^video: Paused')) "$report"
$paused = Colour 'paused'
Check 'picture while paused' ($paused -ge 0.8) "$paused of the middle band lit"

# 3. Another plays on.
Adb shell input tap $cx $cy
Start-Sleep -Seconds 4.5
$report = LastReport
Check 'tap plays on' ([bool]($report -match '^video: Playing') -and (FramesIn $report) -ge 40) "$report"

$subtitles = Lines | Where-Object { $_ -match '^video: subtitles on' } | Select-Object -First 1
if ($subtitles) { Write-Output "note  $subtitles (look at the screenshots in $([IO.Path]::GetTempPath()))" }
if ($failed) { Write-Output "$failed check(s) failed"; exit 1 }
Write-Output 'all video checks passed'
