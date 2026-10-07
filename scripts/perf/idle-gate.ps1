<#
.SYNOPSIS
Waits until this machine is quiet enough for a timed benchmark run, then exits 0.

.DESCRIPTION
Times taken on a busy machine read 3-24 % high on every row (2026-10-05: a game and
other sessions' test runs). Run this immediately before EVERY timed run, after the
build, so the build's own CPU does not count:

    dotnet build -c Release samples/HultUI.Samples.PerfSuite
    pwsh scripts/perf/idle-gate.ps1 -Log captures.log; if ($?) { dotnet run -c Release --no-build --project samples/HultUI.Samples.PerfSuite -- --json a.json }

Quiet means: none of the blocking processes runs (another session's test host, a game),
and total CPU load stays under -MaxCpuPercent for -QuietSeconds in a row. Allocation
numbers do not drift with load; only timed runs need the gate.

Exits 1 when the machine does not get quiet within -TimeoutMinutes, and says why.
#>
param(
    [double]$MaxCpuPercent = 10,
    [int]$QuietSeconds = 20,
    [double]$TimeoutMinutes = 30,
    [string[]]$BlockingProcesses = @('testhost', 'eldenring', 'start_protected_game'),
    [string]$Log
)

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$quietSince = $null
$reason = ''

while ((Get-Date) -lt $deadline) {
    $blocking = Get-Process -Name $BlockingProcesses -ErrorAction SilentlyContinue
    if ($blocking) {
        $reason = "blocking process: " + (($blocking | Select-Object -ExpandProperty ProcessName -Unique) -join ', ')
        $quietSince = $null
        Start-Sleep -Seconds 5
        continue
    }

    # Win32_Processor works on every display language; performance counter names do not.
    $load = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
    if ($load -ge $MaxCpuPercent) {
        $reason = "CPU $load % (limit $MaxCpuPercent %)"
        $quietSince = $null
        Start-Sleep -Seconds 2
        continue
    }

    if (-not $quietSince) { $quietSince = Get-Date }
    if (((Get-Date) - $quietSince).TotalSeconds -ge $QuietSeconds) {
        $line = "{0:s} idle: CPU {1} % for {2} s" -f (Get-Date), $load, $QuietSeconds
        Write-Output $line
        if ($Log) { Add-Content -Path $Log -Value $line }
        exit 0
    }
    Start-Sleep -Seconds 2
}

$line = "{0:s} NOT idle after {1} min: {2}" -f (Get-Date), $TimeoutMinutes, $reason
Write-Output $line
if ($Log) { Add-Content -Path $Log -Value $line }
exit 1
