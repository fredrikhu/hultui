#!/bin/bash
# Run Vesper's DeadMountTests against a network mount that has stopped answering, on Linux.
#
#   scripts/dead-mount-test.sh [extra dotnet test args...]
#
# Mounts a folder over sshfs from a private sshd on 127.0.0.1:2222 (keys and config in a temp
# folder; nothing in ~/.ssh is touched), stops the sshfs process so the mount answers nothing,
# runs the tests with VESPER_DEAD_MOUNT pointing under it, then resumes and unmounts.
# Needs sshfs, fusermount3 and sshd (Fedora: fuse-sshfs openssh-server). Run it from a checkout
# on the Linux side; from Windows, scripts/wsl-test.sh's clone is one:
#
#   wsl -d FedoraLinux-44 --exec bash -c 'cd ~/src/hului-revamped && scripts/dead-mount-test.sh'
set -u
export PATH=/usr/local/bin:/usr/bin:/usr/local/sbin:/usr/sbin
repo=$(cd "$(dirname "$0")/.." && pwd)
w=$(mktemp -d /tmp/vesper-deadmount.XXXXXX)
mkdir -p "$w/mnt" "$w/data/folder"
echo hello > "$w/data/folder/file.txt"

ssh-keygen -q -t ed25519 -N '' -f "$w/hostkey"
ssh-keygen -q -t ed25519 -N '' -f "$w/userkey"
cp "$w/userkey.pub" "$w/authorized_keys"
cat > "$w/sshd_config" <<EOF
Port 2222
ListenAddress 127.0.0.1
HostKey $w/hostkey
AuthorizedKeysFile $w/authorized_keys
PasswordAuthentication no
KbdInteractiveAuthentication no
UsePAM no
StrictModes no
PidFile $w/sshd.pid
Subsystem sftp internal-sftp
EOF

cleanup() {
    [ -n "${sshfs_pid:-}" ] && kill -CONT "$sshfs_pid" 2>/dev/null
    fusermount3 -u "$w/mnt" 2>/dev/null || fusermount3 -uz "$w/mnt" 2>/dev/null
    [ -f "$w/sshd.pid" ] && kill "$(cat "$w/sshd.pid")" 2>/dev/null
    rm -rf "$w"
}
trap cleanup EXIT

"$(command -v sshd)" -f "$w/sshd_config" -E "$w/sshd.log" || { echo "sshd did not start"; exit 1; }
sleep 1
sshfs -p 2222 -o "IdentityFile=$w/userkey,StrictHostKeyChecking=no,UserKnownHostsFile=/dev/null" \
    "127.0.0.1:$w/data" "$w/mnt" || { echo "sshfs did not mount"; cat "$w/sshd.log"; exit 1; }
ls "$w/mnt/folder" > /dev/null || exit 1
sshfs_pid=$(pgrep -n -f "sshfs.*$w/mnt")
grep " $w/mnt " /proc/self/mountinfo

kill -STOP "$sshfs_pid"
start=$(date +%s%N)
timeout 3 stat "$w/mnt/folder" > /dev/null 2>&1
echo "stat under the stopped mount: exit $? after $(( ($(date +%s%N) - start) / 1000000 )) ms (124 = still waiting)"

cd "$repo"
dotnet build tests/VesperCommander.Tests -c Release -v q > /dev/null || exit 1
# One run each: a test that freezes leaves its session behind, stuck, and a session is one per process.
status=0
for t in TheMountIsANetworkOne GoingToAPathUnderItKeepsTheWindowWorking OpeningTheDrivePickerKeepsTheWindowWorking; do
    VESPER_DEAD_MOUNT="$w/mnt/folder" dotnet test tests/VesperCommander.Tests -c Release --no-build \
        --filter "FullyQualifiedName~DeadMountTests.$t" --logger "console;verbosity=detailed" "$@" || status=1
done
exit $status
