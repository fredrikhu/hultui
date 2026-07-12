#!/usr/bin/env pwsh
#
# Compares NativeAOT vs JIT (Release) throughput on the HultUI benchmark.
#
# Runs the js-framework-benchmark suite (Program.cs `--auto`) under both a
# JIT'd Release build and a NativeAOT-published native binary, then tabulates
# the median per-operation latencies side by side.
#
# The benchmark measures each operation end to end (mutation -> painted), so
# most of each number is GPU/native work that is identical in both builds; the
# managed slice (build + style + layout) is the only part AOT can move. Expect
# broad parity — see docs/NATIVE-AOT.md. Run `--profile` for the per-pass
# breakdown that isolates the managed slice.
#
# Requires the MSVC C++ toolchain (VC.Tools.x86.x64) for the AOT link step.
# Usage:  pwsh scripts/aot-vs-jit-benchmark.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'samples/HultUI.Samples.Benchmark'
$rid  = 'win-x64'

function Get-Medians {
    param([string[]] $Lines)
    $map = [ordered]@{}
    foreach ($line in $Lines) {
        # e.g. "  create 1,000          median   12.34 ms   min   10.00 ms   (n=5)"
        if ($line -match '^\s*(\S.*?)\s+median\s+([\d.]+)\s*ms') {
            $map[$Matches[1].Trim()] = [double]$Matches[2]
        }
    }
    return $map
}

# ── 1) JIT (Release) ─────────────────────────────────────────────────────────
Write-Host '== JIT (Release): running suite ==' -ForegroundColor Cyan
$jitOut = & dotnet run --project $proj -c Release -- --auto 2>&1
$jitOut | Write-Host
$jit = Get-Medians $jitOut

# ── 2) Publish NativeAOT (inside the MSVC environment) ───────────────────────
Write-Host '== Publishing NativeAOT ==' -ForegroundColor Cyan
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere not found at $vswhere — install the VS C++ build tools." }
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw 'No VS install with VC.Tools.x86.x64 found.' }
$vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
$installerDir = Split-Path $vswhere -Parent

# vcvars sets LIB/INCLUDE/link.exe; the ILC targets also shell out to vswhere,
# which is not on PATH by default, so prepend the Installer directory too.
$cmd = "call `"$vcvars`" >nul && set `"PATH=$installerDir;%PATH%`" && dotnet publish `"$proj`" -r $rid -c Release"
& cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed (exit $LASTEXITCODE)." }

# ── 3) NativeAOT ─────────────────────────────────────────────────────────────
Write-Host '== NativeAOT: running suite ==' -ForegroundColor Cyan
$exe = Join-Path $proj "bin/x64/Release/net10.0/$rid/publish/HultUI.Samples.Benchmark.exe"
$aotOut = & $exe --auto 2>&1
$aotOut | Write-Host
$aot = Get-Medians $aotOut

# ── 4) Compare ───────────────────────────────────────────────────────────────
# Format numbers with the invariant culture so decimals are dots regardless of
# the machine locale.
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function N([object] $v) { if ($null -eq $v) { '-' } else { ([double]$v).ToString('0.00', $inv) } }

Write-Host ''
Write-Host '== median latency, mutation -> painted (lower is better) ==' -ForegroundColor Green
'{0,-24}{1,12}{2,12}{3,10}' -f 'operation', 'JIT (ms)', 'AOT (ms)', 'AOT/JIT'
'{0,-24}{1,12}{2,12}{3,10}' -f ('-' * 22), ('-' * 10), ('-' * 10), ('-' * 8)
foreach ($k in $jit.Keys) {
    $j = $jit[$k]
    $a = if ($aot.Contains($k)) { $aot[$k] } else { $null }
    $ratio = if ($a -and $j) { [string]::Format($inv, '{0:P0}', ($a / $j)) } else { '-' }
    '{0,-24}{1,12}{2,12}{3,10}' -f $k, (N $j), (N $a), $ratio
}
Write-Host ''
Write-Host 'Note: end-to-end numbers are dominated by GPU present/render (identical in' -ForegroundColor DarkGray
Write-Host 'both builds). Run the benchmark with --profile to isolate build/style/layout.' -ForegroundColor DarkGray
