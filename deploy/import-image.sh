#!/bin/sh
# Imports a `docker save` tar into k3s containerd and prints the digest to deploy with: the `<repo>@sha256:...` name
# that `import --digests` registers, which is what kubelet resolves. (It is neither the digest `docker build` printed
# nor the `:candidate` tag's digest, and it cannot be computed from the tar beforehand.)
#   sudo deploy/import-image.sh ghcr.io/kar6677thik/sprawl /tmp/sprawl.tar
# CTR overrides the containerd client (default: "k3s ctr").
set -eu
repo="$1"
tar="$2"
ctr="${CTR:-k3s ctr}"
log="$(mktemp)"
trap 'rm -f "$log"' EXIT
$ctr -n k8s.io images import --base-name "$repo" --digests "$tar" > "$log" 2>&1 || { cat "$log" >&2; exit 1; }
# The full digest follows the first "...@sha256:<short>" line. Match that marker, not the repository text: ctr's
# progress display can mangle names ("sprawl-mcp" shows as "sprawl mcp"). The registration check below catches a misparse.
digest="$(tr '\r' '\n' < "$log" | awk '/@sha256:/ { found = 1; next } found && match($0, /sha256:[0-9a-f]+/) && RLENGTH == 71 { print substr($0, RSTART, RLENGTH); exit }')"
[ -n "$digest" ] || { echo "No digest in the import output:" >&2; cat "$log" >&2; exit 1; }
$ctr -n k8s.io images ls -q | grep -qx "$repo@$digest" || { echo "$repo@$digest is not registered" >&2; exit 1; }
echo "${digest#sha256:}"
