<#
.SYNOPSIS
Before/after perf-suite measurement for one change: builds both sides, runs them idle-gated and alternated, JIT
and/or NativeAOT, and prints a short summary. Plan 17's build steps use it instead of writing their own scripts.

.DESCRIPTION
The before side is a commit (-Base), extracted with `git archive` into -Work and built there; the after side is
this repository's working tree as it is (commit the change first so the captures can be named after it). Both
sides are built in Release before any timed run. Each pair runs before then after, each behind
scripts/perf/idle-gate.ps1 (timings only count on a quiet machine; allocation numbers do not drift with load).

Output stays short: per pair the suite's slower/faster verdicts (min AND median past -Threshold, as --compare) and
the rows whose allocKB moved by more than 0.2 KB and 2 %; an A/A line (before against before) for the noise when
-Pairs is 2 or more; and a table of the -Show rows (best min, median and allocKB of each side). Everything else
(captures, logs, the suite's own output) goes to -Work.

NativeAOT: `-p:PublishAot=true` on the suite's own project fails (NETSDK1207: the global property reaches the
source generator), so each side is published through a generated wrapper project that sets PublishAot on itself
only. The wrapper and its bin/obj live under a short path in %TEMP% (link.exe fails on long scratchpad paths), and
the Visual Studio Installer folder goes on PATH for vswhere. The wrapper targets the framework its side's suite
project targets (asked of MSBuild in that side's tree), so a step that changes the target framework compares each
side on its own.

SDK: each side builds with the SDK its own tree selects (its global.json, or the newest SDK installed when it has
none); the AOT wrapper gets a copy of that global.json. -BaseSdk pins the before side to one SDK instead: the
script writes a global.json for that version (no roll-forward) into the extracted before tree and its wrapper
folder, and keeps that tree apart from an unpinned one. Use it when the SDK itself is what changes: with no
global.json both sides would otherwise build with the newest SDK. The SDK each side builds with is printed.

.EXAMPLE
pwsh scripts/perf/step-ab.ps1 -Base 58c304fc -Work C:\scratch\r9b4 -Filter text/ -Show 'wheel over a 2,000|plain label' -Runtime Jit,Aot -Slug label-lines-cull

.EXAMPLE
# a .NET upgrade: the before side on the old SDK and target framework, the after side on the new ones
pwsh scripts/perf/step-ab.ps1 -Base ca31555d -BaseSdk 10.0.300 -Work C:\scratch\r11u -Runtime Jit,Aot -Slug dotnet11

.EXAMPLE
# allocation-only rows (no timings needed): skip the gate, one pair
pwsh scripts/perf/step-ab.ps1 -Base HEAD~1 -Work C:\scratch\r9b5 -Filter a11y/ -Pairs 1 -NoGate
#>
param(
    [Parameter(Mandatory)][string]$Base,
    [Parameter(Mandatory)][string]$Work,
    [string]$Filter = '',
    [string[]]$Show = @(),
    [string[]]$Runtime = @('Jit'),
    [ValidateRange(1, 10)][int]$Pairs = 2,
    [double]$Threshold = 10,
    [string]$Slug = '',
    [string]$BaseSdk = '',
    [switch]$NoGate,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
# Under `pwsh -File` an array arrives as one string: -Runtime splits on commas, -Show on '|' (row names hold commas).
$Runtime = @($Runtime | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
foreach ($r in $Runtime) { if ($r -notin 'Jit', 'Aot') { throw "-Runtime takes Jit and/or Aot, not '$r'" } }
$Show = @($Show | ForEach-Object { $_ -split '\|' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$repo = (git -C $PSScriptRoot rev-parse --show-toplevel).Trim() -replace '/', '\'
$suiteRel = 'samples\HultUI.Samples.PerfSuite'
$baseSha = (git -C $repo rev-parse --short=8 "$Base^{commit}").Trim()
$headSha = (git -C $repo rev-parse --short=8 HEAD).Trim()
$dirty = [bool](git -C $repo status --porcelain --untracked-files=no -- src samples apps)
New-Item -ItemType Directory -Force $Work | Out-Null
$caps = Join-Path $Work 'caps'; New-Item -ItemType Directory -Force $caps | Out-Null
$logs = Join-Path $Work 'logs'; New-Item -ItemType Directory -Force $logs | Out-Null
$stamp = Get-Date -Format 'HHmmss'
function Say([string]$s) { Write-Host $s }   # Write-Host: functions below return values through the pipeline
Say "step-ab: before $baseSha, after $headSha$(if ($dirty) { ' + uncommitted changes' }); runtimes $($Runtime -join ', '); $Pairs pair(s); filter '$Filter'"

# --- the before tree -------------------------------------------------------------------------------------------
$sdkTag = if ($BaseSdk) { "-sdk$BaseSdk" } else { '' }
$baseRoot = Join-Path $Work "base-$baseSha$sdkTag"
if (-not (Test-Path (Join-Path $baseRoot '.step-ab-ready'))) {
    if (Test-Path $baseRoot) { Remove-Item -Recurse -Force $baseRoot }
    New-Item -ItemType Directory -Force $baseRoot | Out-Null
    $tar = Join-Path $Work "base-$baseSha.tar"
    git -C $repo archive --format=tar -o $tar $baseSha
    # Windows' own tar (bsdtar): a Git Bash tar earlier on PATH reads 'C:' as a remote host.
    $tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path $tarExe)) { $tarExe = 'tar' }
    & $tarExe -xf $tar -C $baseRoot
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $baseRoot "$suiteRel\HultUI.Samples.PerfSuite.csproj"))) {
        throw "extracting $baseSha into $baseRoot failed"
    }
    Remove-Item $tar
    git -C $baseRoot init -q   # its own repository: %TEMP% is a git repository, and git must not walk up into it
    if ($BaseSdk) {
        # No roll-forward: the pinned SDK or a clear error, never a quiet substitute.
        Set-Content -Path (Join-Path $baseRoot 'global.json') -Encoding utf8 `
            -Value "{ `"sdk`": { `"version`": `"$BaseSdk`", `"rollForward`": `"disable`" } }"
    }
    New-Item -ItemType File (Join-Path $baseRoot '.step-ab-ready') | Out-Null
    Say "before tree extracted: $baseRoot"
}
$roots = [ordered]@{ before = $baseRoot; after = $repo }
foreach ($side in $roots.Keys) {
    Push-Location $roots[$side]
    $v = (dotnet --version 2>&1 | Select-Object -Last 1)
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { throw "no SDK for the $side side in $($roots[$side]): $v" }
    Say "$side builds with SDK $v"
}

# --- builds ----------------------------------------------------------------------------------------------------
function Build-Jit([string]$side) {
    $log = Join-Path $logs "build-$side.log"
    Push-Location $roots[$side]
    dotnet build "$suiteRel\HultUI.Samples.PerfSuite.csproj" -c Release -v q -nologo > $log 2>&1
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { Get-Content $log | Select-String ' error ' | Select-Object -First 10 | ForEach-Object { Say $_.Line }; throw "JIT build of $side failed (log $log)" }
}
function Aot-Dir([string]$side) { Join-Path $env:TEMP "hultui-ab\$(if ($side -eq 'before') { "$baseSha$sdkTag" } else { "$headSha-head" })" }
function Build-Aot([string]$side) {
    $root = $roots[$side]
    $dir = Aot-Dir $side
    New-Item -ItemType Directory -Force $dir | Out-Null
    git -C $dir init -q 2>$null   # keep git (SourceLink, version stamps) from walking up into %TEMP%'s repository
    # The wrapper builds with its side's SDK: the dotnet host picks the SDK from the folder it starts in.
    $globalJson = Join-Path $root 'global.json'
    if (Test-Path $globalJson) { Copy-Item $globalJson (Join-Path $dir 'global.json') -Force }
    elseif (Test-Path (Join-Path $dir 'global.json')) { Remove-Item (Join-Path $dir 'global.json') }
    # The side's own target framework, as MSBuild evaluates it (a literal or one set in Directory.Build.props).
    Push-Location $root
    $tfm = (dotnet msbuild "$suiteRel\HultUI.Samples.PerfSuite.csproj" -getProperty:TargetFramework -nologo 2>&1 | Select-Object -Last 1).Trim()
    Pop-Location
    if ($tfm -notmatch '^net\d+\.\d+$') { throw "could not read the $side side's target framework (got '$tfm')" }
    [xml]$real = Get-Content (Join-Path $root "$suiteRel\HultUI.Samples.PerfSuite.csproj")
    $refs = foreach ($r in $real.Project.ItemGroup.ProjectReference) {
        if (-not $r) { continue }
        $abs = [IO.Path]::GetFullPath((Join-Path (Join-Path $root $suiteRel) $r.Include))
        $extra = ''
        if ($r.OutputItemType) { $extra += " OutputItemType=`"$($r.OutputItemType)`"" }
        if ($r.ReferenceOutputAssembly) { $extra += " ReferenceOutputAssembly=`"$($r.ReferenceOutputAssembly)`"" }
        "    <ProjectReference Include=`"$abs`"$extra />"
    }
    $proj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <!-- Generated by scripts/perf/step-ab.ps1: the perf suite's sources as a NativeAOT publish, PublishAot on this
       project only (NETSDK1207 otherwise), bin/obj under a short path for link.exe. -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$tfm</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
    <AssemblyName>HultUI.Samples.PerfSuite</AssemblyName>
    <RootNamespace>PerfSuite</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <PublishAot>true</PublishAot>
    <OutputPath>$dir\bin\</OutputPath>
    <IntermediateOutputPath>$dir\obj\</IntermediateOutputPath>
    <NoWarn>`$(NoWarn);CA1416;IL2026;IL2075;IL2080;IL3050;IL2070;IL2072;CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$root\$suiteRel\*.cs" />
$($refs -join "`n")
  </ItemGroup>
</Project>
"@
    Set-Content -Path (Join-Path $dir 'SuiteAot.csproj') -Value $proj -Encoding utf8
    $out = Join-Path $dir 'out'
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    $log = Join-Path $logs "publish-$side.log"
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvars = Join-Path $vsPath 'VC\Auxiliary\Build\vcvars64.bat'
    $cmd = "set `"PATH=$(Split-Path $vswhere -Parent);%PATH%`" && call `"$vcvars`" >nul && dotnet publish `"$(Join-Path $dir 'SuiteAot.csproj')`" -r win-x64 -c Release -o `"$out`" -nologo -v q --disable-build-servers > `"$log`" 2>&1"
    Push-Location $dir
    try { & cmd /c $cmd } finally { Pop-Location }
    if (-not (Test-Path (Join-Path $out 'HultUI.Samples.PerfSuite.exe'))) {
        Get-Content $log | Select-String ' error ' | Select-Object -First 10 | ForEach-Object { Say $_.Line }; throw "AOT publish of $side failed (log $log)"
    }
}
if (-not $SkipBuild) {
    foreach ($side in $roots.Keys) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        if ($Runtime -contains 'Jit') { Build-Jit $side }
        if ($Runtime -contains 'Aot') { Build-Aot $side }
        Say ("built {0} in {1:n0} s" -f $side, $sw.Elapsed.TotalSeconds)
    }
}

# --- alternated, gated runs -----------------------------------------------------------------------------------
function Run-Side([string]$rt, [string]$side, [int]$i) {
    $json = Join-Path $caps "$rt-$side-$i-$stamp.json"
    $label = "step-ab $side ($(if ($side -eq 'before') { $baseSha } else { $headSha + $(if ($dirty) { '-dirty' }) })), $rt, pair $i"
    $suiteArgs = @('--json', $json, '--label', $label)
    if ($Filter) { $suiteArgs = @('--filter', $Filter) + $suiteArgs }
    if (-not $NoGate) {
        & pwsh -NoProfile -File (Join-Path $repo 'scripts\perf\idle-gate.ps1') -Log (Join-Path $logs 'gate.log') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "the machine did not get quiet before $rt $side $i (see $(Join-Path $logs 'gate.log'))" }
    }
    $saved = @{}
    foreach ($k in @(Get-ChildItem env: | Where-Object { $_.Name -like 'DOTNET_*' -or $_.Name -like 'HULTUI_*' -or $_.Name -like 'PROBE_*' })) {
        $saved[$k.Name] = $k.Value; Remove-Item "env:$($k.Name)"
    }
    $txt = Join-Path $logs "$rt-$side-$i-$stamp.txt"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Push-Location $roots[$side]
    try {
        if ($rt -eq 'Aot') { & (Join-Path (Aot-Dir $side) 'out\HultUI.Samples.PerfSuite.exe') @suiteArgs > $txt 2>&1 }
        else { dotnet run -c Release --no-build --project "$suiteRel\HultUI.Samples.PerfSuite.csproj" -- @suiteArgs > $txt 2>&1 }
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
        foreach ($k in $saved.Keys) { Set-Item "env:$k" $saved[$k] }
    }
    if ($code -ne 0 -or -not (Test-Path $json)) { Get-Content $txt -Tail 5 | ForEach-Object { Say $_ }; throw "$rt $side $i failed (exit $code; log $txt)" }
    Say ("ran {0} {1} {2} in {3:n0} s" -f $rt, $side, $i, $sw.Elapsed.TotalSeconds)
    return $json
}
$runs = @{}
foreach ($rt in $Runtime) {
    for ($i = 1; $i -le $Pairs; $i++) {
        foreach ($side in 'before', 'after') { $runs["$rt-$side-$i"] = Run-Side $rt $side $i }
    }
}

# --- summary ---------------------------------------------------------------------------------------------------
function Rows([string]$path) {
    $h = @{}; foreach ($r in (Get-Content $path -Raw | ConvertFrom-Json).results) { $h[$r.name] = $r }; return $h
}
function Pct($a, $b) { if ($a -eq 0) { return 0 } ; return ($b - $a) / $a * 100 }
function Verdicts($a, $b, [string]$title) {
    $slow = @(); $fast = @(); $alloc = @()
    foreach ($n in $b.Keys) {
        if (-not $a.ContainsKey($n)) { continue }
        $x = $a[$n]; $y = $b[$n]
        $dMin = Pct $x.minMs $y.minMs; $dMed = Pct $x.medianMs $y.medianMs
        if ($dMin -gt $Threshold -and $dMed -gt $Threshold) { $slow += ('{0} {1:+0;-0}%/{2:+0;-0}%' -f $n, $dMin, $dMed) }
        if ($dMin -lt -$Threshold -and $dMed -lt -$Threshold) { $fast += ('{0} {1:+0;-0}%/{2:+0;-0}%' -f $n, $dMin, $dMed) }
        $dKB = $y.allocKB - $x.allocKB
        if ([math]::Abs($dKB) -gt 0.2 -and [math]::Abs((Pct $x.allocKB $y.allocKB)) -gt 2) { $alloc += ('{0} {1} -> {2} KB' -f $n, $x.allocKB, $y.allocKB) }
    }
    Say ("{0}: {1} slower, {2} faster (min and median past +/-{3}%), {4} rows' allocKB moved" -f $title, $slow.Count, $fast.Count, $Threshold, $alloc.Count)
    $slow | ForEach-Object { Say "    slower  $_" }
    $fast | ForEach-Object { Say "    faster  $_" }
    $alloc | Select-Object -First 20 | ForEach-Object { Say "    alloc   $_" }
    if ($alloc.Count -gt 20) { Say "    ... $($alloc.Count - 20) more" }
}
foreach ($rt in $Runtime) {
    Say ""
    Say "== $rt =="
    $data = @{}; foreach ($k in $runs.Keys) { if ($k -like "$rt-*") { $data[$k] = Rows $runs[$k] } }
    for ($i = 1; $i -le $Pairs; $i++) { Verdicts $data["$rt-before-$i"] $data["$rt-after-$i"] "pair $i" }
    if ($Pairs -ge 2) { Verdicts $data["$rt-before-1"] $data["$rt-before-2"] 'A/A (before 1 vs before 2, noise)' }
    if ($Show.Count) {
        Say ("{0,-56}{1,10}{2,10}{3,10}{4,10}{5,10}{6,10}" -f 'row (best of pairs)', 'min ms', 'after', 'med ms', 'after', 'KB/op', 'after')
        $names = $data["$rt-after-1"].Keys | Where-Object { $n = $_; $Show | Where-Object { $n -like "*$_*" } } | Sort-Object
        foreach ($n in $names) {
            $b = @(1..$Pairs | ForEach-Object { $data["$rt-before-$_"][$n] } | Where-Object { $_ })
            $a = @(1..$Pairs | ForEach-Object { $data["$rt-after-$_"][$n] } | Where-Object { $_ })
            if (-not $b -or -not $a) { Say "  $n (only on one side)"; continue }
            $bm = ($b | Measure-Object minMs -Minimum).Minimum; $am = ($a | Measure-Object minMs -Minimum).Minimum
            $bd = ($b | Measure-Object medianMs -Minimum).Minimum; $ad = ($a | Measure-Object medianMs -Minimum).Minimum
            Say ("{0,-56}{1,10:0.000}{2,10:0.000}{3,10:0.000}{4,10:0.000}{5,10:0.0}{6,10:0.0}" -f ($n.Substring(0, [math]::Min(55, $n.Length))), $bm, $am, $bd, $ad, $b[0].allocKB, $a[0].allocKB)
        }
    }
}

# --- captures to commit ----------------------------------------------------------------------------------------
if ($Slug) {
    if ($dirty) { Say "not copying captures: commit the change first, so they are named after it" }
    else {
        $date = Get-Date -Format 'yyyy-MM-dd'
        $dest = Join-Path $repo "$suiteRel\captures"
        foreach ($rt in $Runtime) {
            $suffix = if ($rt -eq 'Aot') { '-aot' } else { '' }
            foreach ($side in 'before', 'after') {
                $to = Join-Path $dest "$date-$headSha-$Slug$suffix-$side.json"
                Copy-Item $runs["$rt-$side-$Pairs"] $to -Force
                Say "capture: $to"
            }
        }
    }
}
Say "captures and logs: $Work"
