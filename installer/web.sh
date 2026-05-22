#!/bin/bash
# HostPanel F3 web installer bootstrap.
#
# One-liner the operator pastes:
#   curl -sSL https://raw.githubusercontent.com/mgardner323/hostpanel/main/installer/web.sh | sudo bash
#
# What this does:
#   1. Detects arch + asserts linux-x64
#   2. Fetches the latest installer-v* GitHub Release manifest
#   3. Downloads the binary + its .sha256 checksum file
#   4. Prints the published SHA256 + the locally-computed one SIDE BY SIDE
#      and refuses to exec unless they match (manual visual verification is
#      still a worthwhile step — the operator sees what was actually downloaded)
#   5. Chmods + execs the binary in the foreground. Wizard binds to :8080.

set -euo pipefail

# Colors so the operator can spot the checksum check at a glance.
G='\033[0;32m'; R='\033[0;31m'; Y='\033[1;33m'; N='\033[0m'

REPO="${HOSTPANEL_REPO:-mgardner323/hostpanel}"
CHANNEL="${HOSTPANEL_CHANNEL:-latest}"   # latest | beta | edge

echo ""
echo "================================================"
echo "  HostPanel F3 web installer bootstrap"
echo "================================================"
echo ""

# === 1. Sanity ===

if [ "$EUID" -ne 0 ]; then
  echo -e "${R}Must run as root.${N} Try: curl -sSL <url> | sudo bash"
  exit 1
fi

ARCH=$(uname -m)
if [ "$ARCH" != "x86_64" ]; then
  echo -e "${R}Unsupported arch: $ARCH${N}. The installer ships only as linux-x64 for now."
  exit 1
fi

for bin in curl jq; do
  if ! command -v $bin >/dev/null 2>&1; then
    echo "Installing prerequisite: $bin"
    apt-get update -qq && apt-get install -y -qq $bin
  fi
done

# === 2. Resolve release ===

# Default to the most recent installer-v* release. Operator can pin via
# HOSTPANEL_TAG=installer-v0.2.3 to install a specific build.
if [ -n "${HOSTPANEL_TAG:-}" ]; then
  TAG="$HOSTPANEL_TAG"
  echo "Pinned to tag: $TAG"
else
  echo "Looking up latest installer release in $REPO …"
  TAG=$(curl -sSL "https://api.github.com/repos/$REPO/releases" \
    | jq -r '[.[] | select(.tag_name | startswith("installer-v")) | .tag_name] | first')
  if [ -z "$TAG" ] || [ "$TAG" = "null" ]; then
    echo -e "${R}No installer-v* release found in $REPO.${N}"
    echo "Either:"
    echo "  - The repo has no published installer binary yet (build locally and run it directly)"
    echo "  - You're hitting GitHub API rate limits — set HOSTPANEL_TAG to skip the lookup"
    exit 1
  fi
  echo "Latest: $TAG"
fi

BIN_URL="https://github.com/$REPO/releases/download/$TAG/hostpanel-installer"
SHA_URL="https://github.com/$REPO/releases/download/$TAG/hostpanel-installer.sha256"
DEST="/tmp/hostpanel-installer"

# === 3. Download ===

echo ""
echo "Downloading installer binary…"
curl -sSL --fail "$BIN_URL" -o "$DEST"

echo "Downloading published checksum…"
PUBLISHED_SHA=$(curl -sSL --fail "$SHA_URL" | awk '{print $1}')

# === 4. Verify ===

LOCAL_SHA=$(sha256sum "$DEST" | awk '{print $1}')

echo ""
echo "Checksum verification:"
echo -e "  Published: $PUBLISHED_SHA"
echo -e "  Local:     $LOCAL_SHA"
if [ "$PUBLISHED_SHA" = "$LOCAL_SHA" ]; then
  echo -e "  ${G}✓ match${N}"
else
  echo -e "  ${R}✗ MISMATCH — refusing to execute${N}"
  rm -f "$DEST"
  exit 2
fi

# === 5. Exec ===

chmod +x "$DEST"
echo ""
echo -e "${G}Launching installer…${N}"
echo "The wizard will bind to http://<this-box-ip>:8080."
echo "Open that URL in a browser to continue."
echo ""

# Pass through any extra args (e.g. --dry-run, --config, --uninstall).
exec "$DEST" "$@"
