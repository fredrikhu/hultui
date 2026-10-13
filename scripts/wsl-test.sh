#!/bin/bash
# Run the tests on Linux from a Windows checkout, in a WSL clone of its own.
#
#   wsl -d FedoraLinux-44 --exec bash /mnt/c/<checkout>/scripts/wsl-test.sh [ref] [target] [dotnet test args...]
#
# ref     a branch or commit of the Windows checkout: a worktree's branch needs no push, but
#         uncommitted work is not seen, so commit it first. Default: github/main, the
#         checkout's last fetch of GitHub's main.
# target  a test project or solution filter. Default: HultUI.CrossPlatform.slnf, what CI's
#         test-linux job runs.
#
# The clone lives at ~/src/hultui-revamped on WSL's own disk, made on first use. Building on
# /mnt/c instead would be slow, and would share bin/ and obj/ with the Windows builds.
#
# From Git Bash, prefix MSYS_NO_PATHCONV=1, or /mnt/c/... becomes C:/Program Files/Git/mnt/c/...
set -euo pipefail

clone=~/src/hultui-revamped

# The checkout this script is in. A worktree's .git is a file naming the main repository in
# Windows form (gitdir: C:/.../.git/worktrees/<name>), so climb to the repository itself.
win=$(cd "$(dirname "$0")/.." && pwd)
if [ -f "$win/.git" ]; then
    gitdir=$(wslpath -u "$(sed -n 's/^gitdir: //p' "$win/.git" | tr -d '\r')")
    win=$(dirname "$(dirname "$(dirname "$gitdir")")")
fi
# Out of the Windows tree before any git command: WSL starts in the Windows working
# directory, and in a worktree every git command, even a --global one, fails on that .git file.
cd ~

# /mnt/c shows every file as root's, so git needs telling that this repository is fine.
for dir in "$win" "$win/.git"; do
    git config --global --get-all safe.directory | grep -qxF "$dir" \
        || git config --global --add safe.directory "$dir"
done

if [ ! -d "$clone" ]; then
    mkdir -p "$(dirname "$clone")"
    git clone -q --origin win "$win" "$clone"
    # The checkout's view of GitHub as github/*, so pushed branches come along too.
    git -C "$clone" config --add remote.win.fetch '+refs/remotes/origin/*:refs/remotes/github/*'
fi
cd "$clone"
# Follow the Windows checkout if its folder has moved since the clone was made.
git remote set-url win "$win"
git fetch -q --prune win

ref=${1:-github/main}
[ $# -gt 0 ] && shift
sha=
for candidate in "win/$ref" "github/$ref" "$ref"; do
    if sha=$(git rev-parse -q --verify "$candidate^{commit}"); then break; fi
done
[ -n "$sha" ] || { echo "no branch or commit '$ref' in $win" >&2; exit 2; }
git checkout -q --detach "$sha"

target=HultUI.CrossPlatform.slnf
if [ $# -gt 0 ] && [[ $1 != -* ]]; then target=$1; shift; fi

# A Linux desktop's PATH, not WSL's. WSL appends the whole Windows PATH, so a test that looks a
# tool up would find /mnt/c/... executables; and it puts sbin first, where on Fedora's merged
# /usr/sbin -> bin, 7-Zip found as /usr/sbin/7z cannot load its codecs and writes that error
# where the archive's contents should be. A Fedora user's shell has bin first.
export PATH=/usr/local/bin:/usr/bin:/usr/local/sbin:/usr/sbin

. /etc/os-release
echo "Testing $(git log --oneline -1) ($ref) on $PRETTY_NAME, .NET $(dotnet --version)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
exec dotnet test "$target" -c Release "$@"
