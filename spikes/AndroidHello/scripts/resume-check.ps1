# Plan 20 spike: start the app (with optional intent extras), send it Home, bring it back,
# and report whether anything is drawn afterwards. Usage:
#   pwsh spikes/AndroidHello/scripts/resume-check.ps1 -Name gl-rebind [-Extras "-e renderer software -e rebind 0"]
param(
    [Parameter(Mandatory)] [string] $Name,
    [string] $Extras = ''
)
$extraArgs = @($Extras -split ' ' | Where-Object { $_ })
$ErrorActionPreference = 'Stop'
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
$activity = 'org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity'
$results = Join-Path $PSScriptRoot '..\results'
Add-Type -AssemblyName System.Drawing

function Get-DrawnSamples([string] $png) {
    $b = [Drawing.Bitmap]::FromFile($png)
    try {
        $n = 0
        for ($y = 140; $y -lt 2320; $y += 6) { for ($x = 0; $x -lt 1080; $x += 6) {
            $p = $b.GetPixel($x, $y); if ($p.R + $p.G + $p.B -gt 30) { $n++ } } }
        return $n
    } finally { $b.Dispose() }
}

function Save-Screen([string] $file) {
    cmd /c "`"$adb`" exec-out screencap -p > `"$file`""
    return (Resolve-Path $file).Path
}

& $adb shell am force-stop org.hultui.spike.androidhello
& $adb logcat -c
$started = & $adb shell am start -W -n $activity @extraArgs 2>&1
if (-not ($started | Select-String 'Status: ok')) { throw "start failed: $started" }
Start-Sleep -Seconds 4
$before = Get-DrawnSamples (Save-Screen (Join-Path $results "$Name-before.png"))
& $adb shell input keyevent KEYCODE_HOME
Start-Sleep -Seconds 3
& $adb shell am start -W -n $activity | Out-Null
Start-Sleep -Seconds 3
$returned = Get-DrawnSamples (Save-Screen (Join-Path $results "$Name-returned.png"))
& $adb shell input tap 183 387
Start-Sleep -Seconds 2
$after = Get-DrawnSamples (Save-Screen (Join-Path $results "$Name-after-tap.png"))
"$Name : drawn samples before Home $before, after return $returned, after a tap $after"
& $adb logcat -d -s HultUISpike:* DOTNET:* 2>&1 | Select-String 'renderer=|rebind|frames|FOREGROUND|Exception|error' |
    Select-Object -Last 12 | ForEach-Object { $_.Line.Substring([Math]::Min(31, $_.Line.Length)) }
