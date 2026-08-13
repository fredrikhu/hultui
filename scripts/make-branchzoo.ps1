# Generates a synthetic git repo with a configurable number of branches, for
# profiling Junction's Branches tab (which builds one row per branch).
#
#   .\scripts\make-branchzoo.ps1 -Path C:\temp\branchzoo -RemoteBranches 1000
#
# One `git fast-import` stream builds everything (a commit chain plus every ref),
# so generation takes seconds regardless of branch count. The shape mimics a work
# repo: a few dozen local branches (some with upstreams, so for-each-ref's
# %(upstream:track) has real ahead/behind work to do) and the bulk as
# remote-tracking refs under origin/. Refs are packed at the end — packed vs
# loose changes for-each-ref timings, and real repos are packed.
#
# Capture workflow (see also the JUNCTION_BENCH hook in apps/Junction/Program.cs):
#   $env:HULTUI_TRACE = 'C:\temp\trace.json'
#   $env:SDL_WINDOW_ACTIVATE_WHEN_SHOWN = '0'
#   $env:JUNCTION_BENCH = 'Branches@4000,History@12000,Branches@16000,row:5@22000,close@28000'
#   Junction.exe C:\temp\branchzoo

param(
  [Parameter(Mandatory)] [string]$Path,
  [int]$Commits = 500,
  [int]$LocalBranches = 80,
  [int]$TrackedLocals = 40,
  [int]$RemoteBranches = 1000
)
$ErrorActionPreference = 'Stop'
if (Test-Path $Path) { throw "$Path already exists — refusing to overwrite" }
New-Item -ItemType Directory $Path | Out-Null

git -C $Path init -q -b main
git -C $Path config user.name  'Bench Bot'
git -C $Path config user.email 'bench@example.com'
git -C $Path config core.autocrlf false

$sb = [System.Text.StringBuilder]::new(8MB)
[void]$sb.Append("blob`nmark :900000`ndata 12`nhello world`n`n")

# Commit chain on main, dates spread backwards ~3.5h apart so branch tips get
# distinct, realistic committer dates (the Branches tab sorts by them).
$base = 1723500000
$subjects = @(
  'Fix null deref in parser', 'Bump dependencies', 'Refactor session cache',
  'Add retry to uploader', 'Speed up cold start', 'Handle empty payloads',
  'Wire up telemetry', 'Clean up dead code', 'Improve error messages',
  'Port settings to v2 schema', 'Guard against slow disk', 'Add integration test'
)
for ($i = 1; $i -le $Commits; $i++) {
  $when = $base + ($i * 12600)
  $msg  = "$($subjects[$i % $subjects.Count]) (#$(1000 + $i))`n"
  $len  = [System.Text.Encoding]::ASCII.GetByteCount($msg)
  [void]$sb.Append("commit refs/heads/main`nmark :$i`n")
  [void]$sb.Append("author Bench Bot <bench@example.com> $when +0000`n")
  [void]$sb.Append("committer Bench Bot <bench@example.com> $when +0000`n")
  [void]$sb.Append("data $len`n$msg")
  if ($i -gt 1) { [void]$sb.Append("from :$($i - 1)`n") }
  [void]$sb.Append("M 100644 :900000 file$($i % 40).txt`n`n")
}

# Branch names in the shapes BranchName.Parse understands (feature/, wip/, users/…).
$slugs = @('login-flow','dark-mode','csv-export','rate-limits','sso-callback',
  'audit-log','search-index','onboarding','cache-warmup','flaky-e2e','db-pool',
  'retry-policy','feature-flags','pagination','webhooks','image-resize')
$kinds = @('feature','bugfix','wip','hotfix','chore','users/anna','users/petter','users/lin')
$rand  = [Random]::new(42)

function Add-Ref([string]$ref, [int]$mark) {
  [void]$script:sb.Append("reset $ref`nfrom :$mark`n`n")
}

$localNames = @()
for ($i = 0; $i -lt $LocalBranches; $i++) {
  $name = "$($kinds[$i % $kinds.Count])/PROJ-$(2000 + $i)-$($slugs[$i % $slugs.Count])"
  $localNames += $name
  Add-Ref "refs/heads/$name" ($rand.Next(1, $Commits + 1))
}
for ($i = 0; $i -lt $RemoteBranches; $i++) {
  $name = "$($kinds[$i % $kinds.Count])/PROJ-$(3000 + $i)-$($slugs[($i * 7) % $slugs.Count])"
  Add-Ref "refs/remotes/origin/$name" ($rand.Next(1, $Commits + 1))
}
Add-Ref 'refs/remotes/origin/main' $Commits

$stream = Join-Path $Path '.git\branchzoo.fi'
[System.IO.File]::WriteAllText($stream, $sb.ToString(), [System.Text.Encoding]::ASCII)
cmd /c "git -C `"$Path`" fast-import --quiet < `"$stream`""
if ($LASTEXITCODE -ne 0) { throw 'fast-import failed' }
Remove-Item $stream

# Upstreams for the first $TrackedLocals local branches, each pointed at a
# different commit than its local so ahead/behind is nonzero and must be computed.
$cfg = [System.Text.StringBuilder]::new()
[void]$cfg.Append("[remote `"origin`"]`n`turl = https://example.invalid/branchzoo.git`n`tfetch = +refs/heads/*:refs/remotes/origin/*`n")
for ($i = 0; $i -lt [Math]::Min($TrackedLocals, $localNames.Count); $i++) {
  $name = $localNames[$i]
  [void]$cfg.Append("[branch `"$name`"]`n`tremote = origin`n`tmerge = refs/heads/$name`n")
  git -C $Path update-ref "refs/remotes/origin/$name" "refs/heads/main~$($rand.Next(5, 200))"
}
Add-Content -Path (Join-Path $Path '.git\config') -Value $cfg.ToString() -NoNewline

git -C $Path checkout -q main
git -C $Path pack-refs --all

$refCount = (git -C $Path for-each-ref --format='%(refname)' | Measure-Object -Line).Lines
Write-Host "branchzoo ready: $refCount refs at $Path"
