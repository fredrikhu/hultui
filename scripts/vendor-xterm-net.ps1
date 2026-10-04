<#
.SYNOPSIS
    Rebuilds external/XTerm.NET: upstream XTerm.NET at the pinned commit with HultUI's patches
    applied. -Verify checks that what is committed is exactly that.

.DESCRIPTION
    external/XTerm.NET is generated and committed, and never edited by hand. Everything HultUI's
    engine differs from upstream by is a patch in external/XTerm.NET.patches, applied to the commit
    in external/XTerm.NET.version (docs/design/TERMINAL.md, "The patched engine").

    What is vendored: upstream's LICENSE, and every tracked file under src/XTerm.NET and
    src/XTerm.NET.Tests except their .csproj files, which HultUI replaces with
    src/HultUI.Terminal.XTermNet and tests/HultUI.Terminal.XTermNet.Tests. Files are copied and
    compared byte for byte; external/.gitattributes keeps git from converting them.

    Needs git and network access to the upstream URL. Updating also needs the .NET SDK, to run
    upstream's test suite on the patched source before anything is replaced.

.PARAMETER Verify
    Rebuild into a temporary folder and compare with external/XTerm.NET. Exits 1 and lists the
    differences when they differ. Changes nothing. CI runs this.

.PARAMETER Commit
    Move to this upstream commit: apply the series to it, run upstream's tests, replace the tree,
    re-export the series against the new base, and record the commit. Without it, the pinned
    commit is rebuilt.

.PARAMETER FromClone
    Finish an update from a clone where a person resolved a patch that did not apply (git am
    --continue): the series is exported from that clone, then tested and vendored as above.
    Pass the same -Commit the failed run was given.

.PARAMETER SkipTests
    Do not run upstream's test suite on the patched clone. tests/HultUI.Terminal.XTermNet.Tests
    runs the same suite in CI either way.

.EXAMPLE
    pwsh scripts/vendor-xterm-net.ps1 -Verify
.EXAMPLE
    pwsh scripts/vendor-xterm-net.ps1 -Commit 0123abcd...
#>
[CmdletBinding(DefaultParameterSetName = 'Vendor')]
param(
    [Parameter(ParameterSetName = 'Verify', Mandatory)] [switch] $Verify,
    [Parameter(ParameterSetName = 'Vendor')] [string] $Commit,
    [Parameter(ParameterSetName = 'Vendor')] [string] $FromClone,
    [Parameter(ParameterSetName = 'Vendor')] [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path $PSScriptRoot -Parent
$external = Join-Path $root 'external'
$tree = Join-Path $external 'XTerm.NET'
$patchDir = Join-Path $external 'XTerm.NET.patches'
$versionFile = Join-Path $external 'XTerm.NET.version'

# The upstream paths that are vendored, as git names them.
$vendoredPrefixes = @('src/XTerm.NET/', 'src/XTerm.NET.Tests/')

function Read-Version {
    $values = @{}
    foreach ($line in Get-Content $versionFile) {
        if ($line -match '^\s*(#|$)') { continue }
        $key, $value = $line -split '=', 2
        $values[$key.Trim()] = $value.Trim()
    }
    foreach ($required in 'url', 'commit') {
        if (-not $values.ContainsKey($required)) { throw "$versionFile has no '$required='." }
    }
    $values
}

function Invoke-Git([string] $dir, [string[]] $gitArgs) {
    & git -C $dir @gitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($gitArgs -join ' ') failed in $dir (exit $LASTEXITCODE)." }
}

function New-TempDirectory([string] $prefix) {
    $dir = Join-Path ([IO.Path]::GetTempPath()) "$prefix-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    New-Item -ItemType Directory $dir | Out-Null
    $dir
}

# A shallow clone of exactly one commit, with the series applied on top. On a patch that does
# not apply, the clone is kept for a person to resolve, and the message says what to run next.
function New-PatchedClone([string] $url, [string] $commit) {
    $clone = New-TempDirectory 'xterm-net'
    Invoke-Git $clone @('init', '-q')
    # Bytes as upstream has them, whatever this machine's global setting.
    Invoke-Git $clone @('config', 'core.autocrlf', 'false')
    Invoke-Git $clone @('fetch', '-q', '--depth', '1', $url, $commit)
    Invoke-Git $clone @('checkout', '-q', 'FETCH_HEAD')

    $series = @(Get-ChildItem $patchDir -Filter '*.patch' | Sort-Object Name | ForEach-Object FullName)
    if ($series.Count -gt 0) {
        & git -C $clone -c user.name='HultUI vendoring' -c user.email='vendoring@hultui.invalid' am -q --3way @series
        if ($LASTEXITCODE -ne 0) {
            throw ("A patch does not apply to $commit. Resolve it in $clone (edit, git add, " +
                "git -c user.name=x -c user.email=x@x am --continue), then run: " +
                "scripts/vendor-xterm-net.ps1 -FromClone '$clone' -Commit $commit")
        }
    }
    $clone
}

# The vendored file list: upstream's LICENSE plus the tracked files under the two projects,
# minus their project files.
function Get-VendoredFiles([string] $clone) {
    $files = @(& git -C $clone ls-files -- LICENSE @($vendoredPrefixes))
    if ($LASTEXITCODE -ne 0) { throw "git ls-files failed in $clone." }
    $files | Where-Object { $_ -notlike '*.csproj' } | Sort-Object
}

function Copy-Vendored([string] $clone, [string] $destination) {
    foreach ($file in Get-VendoredFiles $clone) {
        $target = Join-Path $destination $file
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        [IO.File]::WriteAllBytes($target, [IO.File]::ReadAllBytes((Join-Path $clone $file)))
    }
}

# Relative paths of every file under $dir, with forward slashes, sorted.
function Get-RelativeFiles([string] $dir) {
    if (-not (Test-Path $dir)) { return @() }
    $full = (Resolve-Path $dir).Path.TrimEnd('\', '/')
    Get-ChildItem $dir -Recurse -File | ForEach-Object {
        $_.FullName.Substring($full.Length + 1).Replace('\', '/')
    } | Sort-Object
}

# Missing, extra and differing files between two trees, compared byte for byte (by hash).
function Compare-Trees([string] $expected, [string] $actual) {
    $want = @(Get-RelativeFiles $expected)
    $have = @(Get-RelativeFiles $actual)
    $problems = [Collections.Generic.List[string]]::new()
    foreach ($file in $want) {
        $a = Join-Path $actual $file
        if (-not (Test-Path $a)) { $problems.Add("missing:   $file"); continue }
        $x = (Get-FileHash -Algorithm SHA256 (Join-Path $expected $file)).Hash
        $y = (Get-FileHash -Algorithm SHA256 $a).Hash
        if ($x -ne $y) { $problems.Add("differs:   $file") }
    }
    foreach ($file in $have) {
        if ($want -notcontains $file) { $problems.Add("extra:     $file") }
    }
    , $problems
}

function Write-Version([string] $commit) {
    $lines = (Get-Content $versionFile) -replace '^commit=.*$', "commit=$commit"
    [IO.File]::WriteAllText($versionFile, ($lines -join "`n") + "`n")
}

$version = Read-Version
$clone = $null
$ownsClone = $false
$keepClone = $false
$exitCode = 0
try {
    if ($Verify) {
        $clone = New-PatchedClone $version.url $version.commit
        $ownsClone = $true
        $expected = New-TempDirectory 'xterm-net-expected'
        try {
            Copy-Vendored $clone $expected
            $problems = Compare-Trees $expected $tree
            if ($problems.Count -gt 0) {
                Write-Host "external/XTerm.NET is not upstream $($version.commit) plus external/XTerm.NET.patches:"
                $problems | ForEach-Object { Write-Host "  $_" }
                Write-Host 'Every difference from upstream belongs in a patch; rebuild with scripts/vendor-xterm-net.ps1.'
                $exitCode = 1
            }
            else {
                $count = @(Get-RelativeFiles $tree).Count
                Write-Host "external/XTerm.NET matches upstream $($version.commit.Substring(0, 12)) plus $(@(Get-ChildItem $patchDir -Filter *.patch).Count) patches ($count files)."
            }
        }
        finally { Remove-Item -Recurse -Force $expected -ErrorAction SilentlyContinue }
    }
    else {
        $base = if ($Commit) { $Commit } else { $version.commit }
        if ($FromClone) {
            $clone = (Resolve-Path $FromClone).Path
        }
        else {
            $clone = New-PatchedClone $version.url $base
            $ownsClone = $true
        }

        # Ask git for the full id: -Commit may be abbreviated, and the version file records it whole.
        $baseFull = (& git -C $clone rev-parse --verify "$base^{commit}").Trim()
        if ($LASTEXITCODE -ne 0) { throw "$base is not a commit in $clone." }

        if (-not $SkipTests) {
            Write-Host "Running upstream's tests on the patched source..."
            & dotnet test (Join-Path $clone 'src/XTerm.NET.Tests') -c Release --nologo -v quiet
            if ($LASTEXITCODE -ne 0) {
                $keepClone = $true
                throw "Upstream's tests fail on $baseFull with the series applied; nothing was replaced. The clone is $clone."
            }
        }

        # A new base (or a person's resolution) changes the series' context and index lines, so
        # the series is exported again from the clone. Rebuilding the pinned commit leaves it
        # untouched, so a rebuild is not a diff.
        if ($FromClone -or $baseFull -ne $version.commit) {
            Get-ChildItem $patchDir -Filter '*.patch' | Remove-Item
            Invoke-Git $clone @('format-patch', '-q', '--no-signature', '-o', $patchDir, "$baseFull..HEAD")
        }

        if (Test-Path $tree) { Remove-Item -Recurse -Force $tree }
        New-Item -ItemType Directory $tree | Out-Null
        Copy-Vendored $clone $tree

        if ($baseFull -ne $version.commit) { Write-Version $baseFull }

        $count = @(Get-RelativeFiles $tree).Count
        Write-Host "external/XTerm.NET is upstream $($baseFull.Substring(0, 12)) plus $(@(Get-ChildItem $patchDir -Filter *.patch).Count) patches ($count files)."
        Write-Host 'Next: dotnet test tests/HultUI.Terminal.XTermNet.Tests -c Release, and review the diff of external/.'
    }
}
finally {
    # A clone a person has to look at is named in the error and kept. (One whose patch did not
    # apply never reaches $clone: New-PatchedClone throws before returning it.)
    if ($ownsClone -and -not $keepClone -and $clone -and (Test-Path $clone)) {
        Remove-Item -Recurse -Force $clone -ErrorAction SilentlyContinue
    }
}
exit $exitCode
