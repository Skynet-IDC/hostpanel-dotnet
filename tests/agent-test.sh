#!/bin/bash
# HostPanel Agent Contract Tests — runs against the local Agent unix socket
# Usage: sudo ./agent-test.sh [SOCKET_PATH] [TOKEN]
set -uo pipefail

SOCKET="${1:-/var/run/hostpanel/agent.sock}"
TOKEN="${2:-$(cat /etc/hostpanel/agent.token 2>/dev/null || echo 'test-token')}"

PASS=0
FAIL=0
TOTAL=0

send_rpc() {
    local method="$1"
    local params="$2"
    [ -z "$params" ] && params='{}'
    local id="test-$(date +%s%N)"
    local payload="{\"method\":\"$method\",\"params\":$params,\"id\":\"$id\",\"token\":\"$TOKEN\",\"traceId\":\"test\"}"
    echo "$payload" | socat - UNIX-CONNECT:"$SOCKET" 2>/dev/null || echo '{"error":{"message":"socket connection failed"}}'
}

test_method() {
    local name="$1"
    local method="$2"
    local params="${3:-{}}"
    local expect="${4:-success}"
    TOTAL=$((TOTAL + 1))

    local result
    result=$(send_rpc "$method" "$params")

    if [ "$expect" = "success" ]; then
        if echo "$result" | grep -q '"success":true\|"result"'; then
            PASS=$((PASS + 1))
            echo "  ✓ $name"
        else
            FAIL=$((FAIL + 1))
            echo "  ✗ $name → $(echo "$result" | head -c 120)"
        fi
    elif [ "$expect" = "error" ]; then
        if echo "$result" | grep -q '"error"\|"success":false'; then
            PASS=$((PASS + 1))
            echo "  ✓ $name (expected error)"
        else
            FAIL=$((FAIL + 1))
            echo "  ✗ $name → expected error, got: $(echo "$result" | head -c 120)"
        fi
    fi
}

test_auth() {
    TOTAL=$((TOTAL + 1))
    local payload="{\"method\":\"system.getStats\",\"params\":{},\"id\":\"auth-test\",\"token\":\"INVALID_TOKEN\",\"traceId\":\"test\"}"
    local result
    result=$(echo "$payload" | socat - UNIX-CONNECT:"$SOCKET" 2>/dev/null || echo '{"error":{"message":"socket failed"}}')

    if echo "$result" | grep -q "Unauthorized"; then
        PASS=$((PASS + 1))
        echo "  ✓ Rejects invalid token"
    else
        FAIL=$((FAIL + 1))
        echo "  ✗ Should reject invalid token → $(echo "$result" | head -c 120)"
    fi
}

test_unknown() {
    TOTAL=$((TOTAL + 1))
    local result
    result=$(send_rpc "totally.fake.method" "{}")

    if echo "$result" | grep -q "not found\|not implemented\|error"; then
        PASS=$((PASS + 1))
        echo "  ✓ Rejects unknown method"
    else
        FAIL=$((FAIL + 1))
        echo "  ✗ Should reject unknown method → $(echo "$result" | head -c 120)"
    fi
}

echo "========================================="
echo "  HostPanel Agent Contract Tests"
echo "  Socket: $SOCKET"
echo "========================================="
echo ""

# Check socket exists
if [ ! -S "$SOCKET" ]; then
    echo "ERROR: Socket not found at $SOCKET"
    echo "Is the agent running? systemctl status hostpanel-agent"
    exit 1
fi

# Check socat is available
if ! command -v socat &>/dev/null; then
    echo "ERROR: socat not installed. Run: apt install -y socat"
    exit 1
fi

# --- Auth ---
echo "[Authentication]"
test_auth
test_unknown

# --- System ---
echo "[System]"
test_method "system.getStats" "system.getStats"
test_method "system.getStats with username" "system.getStats" '{"username":"test"}'

# --- Nginx ---
echo "[Nginx]"
test_method "nginx.reload" "nginx.reload"

# --- Updates ---
echo "[Updates]"
test_method "system.listUpdates" "system.listUpdates"
test_method "system.updateHistory" "system.updateHistory"

# --- Firewall ---
echo "[Firewall]"
test_method "firewall.status" "firewall.status"

# --- Logs ---
echo "[Logs]"
test_method "system.readLog (valid path)" "system.readLog" '{"path":"/var/log/nginx/access.log","lines":5}'
test_method "system.readLog (blocked path)" "system.readLog" '{"path":"/etc/shadow","lines":5}' "error"

# --- Backup ---
echo "[Backup]"
test_method "backup.create (missing params)" "backup.create" '{}' "error"

# --- SSL ---
echo "[SSL]"
test_method "ssl.renewCertificate" "ssl.renewCertificate"

# --- PHP ---
echo "[PHP]"
test_method "php.createPool (missing params)" "php.createPool" '{}' "error"

# --- Cron ---
echo "[Cron]"
test_method "system.setCrontab (missing params)" "system.setCrontab" '{}' "error"

# --- Git ---
echo "[Git]"
test_method "git.deploy (blocked file:// URL)" "git.deploy" '{"repoUrl":"file:///etc/passwd","deployPath":"/var/www/test"}' "error"
test_method "git.deploy (missing params)" "git.deploy" '{}' "error"

# --- Security ---
echo "[Security]"
test_method "security.fail2banStatus" "security.fail2banStatus"

# --- Analytics ---
echo "[Analytics]"
test_method "analytics.serverResources" "analytics.serverResources"
test_method "analytics.visitors" "analytics.visitors" '{"domainName":"test.com"}'

# --- Docker ---
echo "[Docker]"
test_method "docker.create (missing params)" "docker.create" '{}' "error"

# --- App Config ---
echo "[App Config]"
test_method "app.restart (missing params)" "app.restart" '{}' "error"

# --- HA ---
echo "[HA]"
test_method "ha.stopSync" "ha.stopSync"

# --- Results ---
echo ""
echo "========================================="
echo "  Results: $PASS/$TOTAL passed, $FAIL failed"
echo "========================================="

[ $FAIL -eq 0 ] && exit 0 || exit 1
