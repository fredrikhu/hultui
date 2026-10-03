#!/usr/bin/env bash
# A wrapper around release_transport.py, kept for the jobs that call this name.
#
#   release-transport.sh ensure   <tag> [--prerelease]     create the release if missing
#   release-transport.sh upload   <tag> <file> <asset>     replace the asset of that name
#   release-transport.sh download <tag> <asset> <dest>     fetch an asset to a file
#
# The work is in the .py beside this, because the Windows runners have no bash on
# PATH; steps there call Python directly. Needs GH_TOKEN and GITHUB_REPOSITORY.
set -euo pipefail

# A python that runs: on Windows, "python3" on the PATH can be the Store's stub.
PY=
for candidate in python3 python; do
  if "$candidate" -c pass > /dev/null 2>&1; then PY=$candidate; break; fi
done
: "${PY:?no working python found}"

exec "$PY" "$(dirname "$0")/release_transport.py" "$@"
