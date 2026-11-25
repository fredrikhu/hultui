# Plan 20 spike: does HultUI's UI thread run in the background, and does it stay off the GPU?
# Starts the app with the given intent extras, leaves it in the background for -Seconds (Home or
# the screen lock), brings it back, and reports from logcat:
#   ticks   - the 1 s ticker's posts that ran on the UI thread between DID_ENTER_BACKGROUND and
#             WILL_ENTER_FOREGROUND (each one also changes the tree)
#   frames  - the frame log's frames/s for reports wholly inside the background
#   errors  - EGL/GL/runtime error lines while away
# and whether the screen is drawn after return. Usage:
#   pwsh spikes/AndroidHello/scripts/background-check.ps1 -Name block-off -Extras "-e block 0 -e redraw 16" [-Lock]
param(
    [Parameter(Mandatory)] [string] $Name,
    [string] $Extras = '',
    [int] $Seconds = 8,
    [switch] $Lock
)
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$activity = 'org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity'
$extraArgs = @($Extras -split ' ' | Where-Object { $_ })
Add-Type -AssemblyName System.Drawing

& $adb shell am force-stop org.hultui.spike.androidhello
& $adb logcat -c
$started = & $adb shell am start -W -n $activity @extraArgs 2>&1
if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
Start-Sleep -Seconds 4

if ($Lock) { & $adb shell input keyevent KEYCODE_SLEEP } else { & $adb shell input keyevent KEYCODE_HOME }
Start-Sleep -Seconds $Seconds
if ($Lock) {
    & $adb shell input keyevent KEYCODE_WAKEUP
    Start-Sleep -Milliseconds 800
    & $adb shell wm dismiss-keyguard
}
& $adb shell am start -W -n $activity | Out-Null
Start-Sleep -Seconds 3

$png = Join-Path $env:TEMP "hultui-bg-$Name.png"
cmd /c "`"$adb`" exec-out screencap -p > `"$png`""
$b = [Drawing.Bitmap]::FromFile($png); $drawn = 0
for ($y = 140; $y -lt 2320; $y += 6) { for ($x = 0; $x -lt 1080; $x += 6) { $p = $b.GetPixel($x, $y); if ($p.R + $p.G + $p.B -gt 30) { $drawn++ } } }
$b.Dispose(); Remove-Item $png

$log = & $adb logcat -d -v epoch 2>&1
function Get-Time([string] $line) { if ($line -match '^\s*(\d+\.\d+)') { [double]$Matches[1] } else { $null } }
$bgAt = ($log | Select-String 'SDL_EVENT_DID_ENTER_BACKGROUND' | Select-Object -First 1) | ForEach-Object { Get-Time $_.Line }
$fgAt = ($log | Select-String 'SDL_EVENT_WILL_ENTER_FOREGROUND' | Select-Object -First 1) | ForEach-Object { Get-Time $_.Line }
if (-not $bgAt -or -not $fgAt) { "$Name : no background/foreground events in the log"; return }
$away = $log | Where-Object { $t = Get-Time $_; $t -and $t -gt $bgAt -and $t -lt $fgAt }
$ticks = @($away | Select-String 'HultUISpike: tick ').Count
$frames = @($away | Select-String 'report=\d+ ' | ForEach-Object {
    $t = Get-Time $_.Line
    if ($t - 2.0 -gt $bgAt -and $_.Line -match 'frames/s=([\d.]+)') { $Matches[1] } })
$errors = @($away | Select-String ' E (libEGL|EGL_emulation|OpenGLRenderer|DOTNET|AndroidRuntime|SDL)' )
"{0} : away {1:N1} s, ticks run while away {2}, frames/s in reports wholly away [{3}], error lines {4}, drawn samples after return {5}" -f `
    $Name, ($fgAt - $bgAt), $ticks, ($frames -join ', '), $errors.Count, $drawn
$errors | Select-Object -First 5 | ForEach-Object { "  " + $_.Line }
