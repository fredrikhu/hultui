# Plan 20 phase 2 exit criterion: backgrounding and foregrounding 20 times leaks no GPU memory
# and never crashes. Starts the app (ticker off, so nothing changes on its own), reads
# `dumpsys meminfo` before and after -Cycles trips to Home and back, and checks the screen is
# drawn at the end. Usage:
#   pwsh spikes/AndroidHello/scripts/cycle-check.ps1 [-Cycles 20] [-Extras "-e renderer software"]
param(
    [int] $Cycles = 20,
    [string] $Extras = ''
)
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$package = 'org.hultui.spike.androidhello'
$activity = "$package/crc64ea9bba10985cac6f.MainActivity"
$extraArgs = @(('-e rebind 0 -e ticker 0 ' + $Extras) -split ' ' | Where-Object { $_ })
Add-Type -AssemblyName System.Drawing

function Get-Memory {
    # The app summary's rows, in KB: Native Heap, Graphics, TOTAL PSS.
    $lines = & $adb shell dumpsys meminfo $package
    $row = { param($name) ($lines | Select-String "^\s*$name\s*:?\s+(\d+)" | Select-Object -First 1).Matches[0].Groups[1].Value }
    [pscustomobject]@{
        Pss      = [int](& $row 'TOTAL PSS')
        Graphics = [int](& $row 'Graphics')
        Native   = [int](& $row 'Native Heap')
    }
}

function Get-DrawnSamples {
    $png = Join-Path $env:TEMP 'hultui-cycle.png'
    cmd /c "`"$adb`" exec-out screencap -p > `"$png`""
    $b = [Drawing.Bitmap]::FromFile($png); $n = 0
    try { for ($y = 140; $y -lt 2320; $y += 6) { for ($x = 0; $x -lt 1080; $x += 6) { $p = $b.GetPixel($x, $y); if ($p.R + $p.G + $p.B -gt 30) { $n++ } } } }
    finally { $b.Dispose() }
    return $n
}

& $adb shell am force-stop $package
& $adb logcat -c
$started = & $adb shell am start -W -n $activity @extraArgs 2>&1
if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
Start-Sleep -Seconds 5
$pid0 = (& $adb shell pidof $package).Trim()
$before = Get-Memory
$drawnBefore = Get-DrawnSamples

"memory KB at trip 0: PSS {0}, Native {1}" -f $before.Pss, $before.Native
for ($i = 1; $i -le $Cycles; $i++) {
    & $adb shell input keyevent KEYCODE_HOME
    Start-Sleep -Milliseconds 1500
    & $adb shell am start -n $activity 2>&1 | Out-Null
    Start-Sleep -Milliseconds 1500
    # Every 10 trips, so warm-up (a level reached and held) shows apart from a leak (a slope).
    if ($i % 10 -eq 0) { $m = Get-Memory; "memory KB at trip {0}: PSS {1}, Native {2}" -f $i, $m.Pss, $m.Native }
}
Start-Sleep -Seconds 3
$pid1 = (& $adb shell pidof $package).Trim()
$after = Get-Memory
$drawnAfter = Get-DrawnSamples
$errors = @(& $adb logcat -d 2>&1 | Select-String " E (DOTNET|AndroidRuntime|libEGL|EGL_emulation)| F (DEBUG|libc)")

"cycles {0}, same process {1} ({2} -> {3}), errors {4}" -f $Cycles, ($pid0 -eq $pid1), $pid0, $pid1, $errors.Count
"memory KB before: PSS {0}, Graphics {1}, Native {2}" -f $before.Pss, $before.Graphics, $before.Native
"memory KB after:  PSS {0}, Graphics {1}, Native {2}" -f $after.Pss, $after.Graphics, $after.Native
"drawn samples before {0}, after {1}" -f $drawnBefore, $drawnAfter
$errors | Select-Object -First 5 | ForEach-Object { '  ' + $_.Line }
