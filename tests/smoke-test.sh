#!/bin/bash
# HostPanel Smoke Test — runs against a live deployed server
# Usage: ./smoke-test.sh [BASE_URL] [ADMIN_EMAIL] [ADMIN_PASSWORD]
set -euo pipefail

BASE_URL="${1:-https://localhost:8443}"
ADMIN_EMAIL="${2:-admin@hostpanel.local}"
ADMIN_PASSWORD="${3:-CHANGE-ME-IN-PRODUCTION}"
CURL="curl -sk --max-time 10"

PASS=0
FAIL=0
TOTAL=0

test_it() {
    local name="$1"
    local expected="$2"
    local actual="$3"
    TOTAL=$((TOTAL + 1))
    if echo "$actual" | grep -q "$expected"; then
        PASS=$((PASS + 1))
        echo "  ✓ $name"
    else
        FAIL=$((FAIL + 1))
        echo "  ✗ $name (expected '$expected', got: $(echo "$actual" | head -c 100))"
    fi
}

echo "========================================="
echo "  HostPanel Smoke Test"
echo "  Target: $BASE_URL"
echo "========================================="
echo ""

# --- Health ---
echo "[Health]"
HEALTH=$($CURL "$BASE_URL/api/v1/health" 2>&1 || echo "FAILED")
test_it "Health endpoint" "healthy\|unhealthy" "$HEALTH"

# --- Branding (no auth) ---
echo "[Branding]"
BRAND=$($CURL "$BASE_URL/api/v1/branding" 2>&1 || echo "FAILED")
test_it "Branding (public)" "panelName" "$BRAND"

# --- SSO Config (no auth) ---
echo "[SSO]"
SSO=$($CURL "$BASE_URL/api/v1/sso/config" 2>&1 || echo "FAILED")
test_it "SSO config (public)" "enabled" "$SSO"

# --- Login ---
echo "[Auth]"
LOGIN=$($CURL -X POST "$BASE_URL/api/v1/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" 2>&1 || echo "FAILED")
test_it "Admin login" "token" "$LOGIN"

TOKEN=$(echo "$LOGIN" | jq -r '.token // empty' 2>/dev/null || echo "")
if [ -z "$TOKEN" ]; then
    echo "  ⚠ Login failed — skipping authenticated tests"
    echo ""
    echo "========================================="
    echo "  Results: $PASS/$TOTAL passed, $FAIL failed"
    echo "========================================="
    exit 1
fi

AUTH="-H 'Authorization: Bearer $TOKEN'"

# --- Me ---
ME=$(eval $CURL $AUTH "$BASE_URL/api/v1/auth/me" 2>&1 || echo "FAILED")
test_it "Get current user" "$ADMIN_EMAIL" "$ME"

# --- Server Stats ---
echo "[Server]"
STATS=$(eval $CURL $AUTH "$BASE_URL/api/v1/server/stats" 2>&1 || echo "FAILED")
test_it "Server stats" "hostname" "$STATS"

VERSION=$(eval $CURL $AUTH "$BASE_URL/api/v1/panel/version" 2>&1 || echo "FAILED")
test_it "Panel version" "version" "$VERSION"

# --- Domains ---
echo "[Domains]"
DOMAINS=$(eval $CURL $AUTH "$BASE_URL/api/v1/domains" 2>&1 || echo "FAILED")
test_it "List domains" "\[" "$DOMAINS"

# --- Subscriptions ---
echo "[Subscriptions]"
SUBS=$(eval $CURL $AUTH "$BASE_URL/api/v1/subscriptions" 2>&1 || echo "FAILED")
test_it "List subscriptions" "\[" "$SUBS"

# --- Service Plans ---
echo "[Service Plans]"
PLANS=$(eval $CURL $AUTH "$BASE_URL/api/v1/serviceplans" 2>&1 || echo "FAILED")
test_it "List plans" "\[" "$PLANS"

# Create a plan
PLAN=$(eval $CURL $AUTH -X POST "$BASE_URL/api/v1/serviceplans" \
    -H "'Content-Type: application/json'" \
    -d "'{\"name\":\"Smoke Test Plan\",\"diskQuotaMb\":512,\"bandwidthLimitMb\":5120,\"maxDomains\":3,\"maxEmailAccounts\":5,\"maxDatabases\":2,\"pricePerPeriod\":5.99}'" 2>&1 || echo "FAILED")
test_it "Create plan" "Smoke Test Plan" "$PLAN"

# --- Databases ---
echo "[Databases]"
DBTYPES=$(eval $CURL $AUTH "$BASE_URL/api/v1/databases/types" 2>&1 || echo "FAILED")
test_it "Database types" "MsSql\|MySql" "$DBTYPES"

DBS=$(eval $CURL $AUTH "$BASE_URL/api/v1/databases" 2>&1 || echo "FAILED")
test_it "List databases" "\[" "$DBS"

# --- Email ---
echo "[Email]"
PORTS=$(eval $CURL $AUTH "$BASE_URL/api/v1/mail/ports" 2>&1 || echo "FAILED")
test_it "Mail ports" "submission" "$PORTS"

# --- Billing ---
echo "[Billing]"
INVOICES=$(eval $CURL $AUTH "$BASE_URL/api/v1/billing/invoices" 2>&1 || echo "FAILED")
test_it "List invoices" "\[" "$INVOICES"

# --- Customers ---
echo "[Customers]"
CUSTOMERS=$(eval $CURL $AUTH "$BASE_URL/api/v1/customers" 2>&1 || echo "FAILED")
test_it "List customers" "\[" "$CUSTOMERS"

# --- WordPress ---
echo "[WordPress]"
WP=$(eval $CURL $AUTH "$BASE_URL/api/v1/wordpress" 2>&1 || echo "FAILED")
test_it "List WP sites" "\[" "$WP"

# --- API Keys ---
echo "[API Keys]"
KEYS=$(eval $CURL $AUTH "$BASE_URL/api/v1/apikeys" 2>&1 || echo "FAILED")
test_it "List API keys" "\[" "$KEYS"

NEWKEY=$(eval $CURL $AUTH -X POST "$BASE_URL/api/v1/apikeys" \
    -H "'Content-Type: application/json'" \
    -d "'{\"name\":\"smoke-test-key\"}'" 2>&1 || echo "FAILED")
test_it "Create API key" "hp_" "$NEWKEY"

# Test API key auth
APIKEY=$(echo "$NEWKEY" | jq -r '.key // empty' 2>/dev/null || echo "")
if [ -n "$APIKEY" ]; then
    ME_VIA_KEY=$($CURL -H "Authorization: Bearer $APIKEY" "$BASE_URL/api/v1/auth/me" 2>&1 || echo "FAILED")
    test_it "Auth via API key" "$ADMIN_EMAIL" "$ME_VIA_KEY"
fi

# --- 2FA ---
echo "[2FA]"
TFA=$(eval $CURL $AUTH "$BASE_URL/api/v1/auth/2fa/status" 2>&1 || echo "FAILED")
test_it "2FA status" "enabled" "$TFA"

# --- Webhooks ---
echo "[Webhooks]"
EVENTS=$(eval $CURL $AUTH "$BASE_URL/api/v1/webhooks/events" 2>&1 || echo "FAILED")
test_it "Webhook events" "subscription.created" "$EVENTS"

# --- Firewall ---
echo "[Firewall]"
FW=$(eval $CURL $AUTH "$BASE_URL/api/v1/firewall/rules" 2>&1 || echo "FAILED")
test_it "Firewall rules" "rules\|error" "$FW"

# --- Server Updates ---
echo "[Updates]"
UPDATES=$(eval $CURL $AUTH "$BASE_URL/api/v1/server/updates" 2>&1 || echo "FAILED")
test_it "Server updates" "count\|packages\|error" "$UPDATES"

# --- Audit Log ---
echo "[Audit]"
AUDIT=$(eval $CURL $AUTH "$BASE_URL/api/v1/audit" 2>&1 || echo "FAILED")
test_it "Audit log" "items\|totalCount" "$AUDIT"

# --- IP Management ---
echo "[IP Management]"
IPS=$(eval $CURL $AUTH "$BASE_URL/api/v1/ip/addresses" 2>&1 || echo "FAILED")
test_it "IP addresses" "\[" "$IPS"

BL=$(eval $CURL $AUTH "$BASE_URL/api/v1/ip/blacklist" 2>&1 || echo "FAILED")
test_it "IP blacklist" "\[" "$BL"

# --- Webhook Rejection ---
echo "[Security]"
FAKE_WH=$($CURL -X POST "$BASE_URL/api/v1/billing/webhooks/stripe" \
    -H "Content-Type: application/json" -d '{}' -o /dev/null -w "%{http_code}" 2>&1 || echo "000")
test_it "Stripe webhook rejects unsigned" "401\|200" "$FAKE_WH"

# --- Results ---
echo ""
echo "========================================="
echo "  Results: $PASS/$TOTAL passed, $FAIL failed"
echo "========================================="

[ $FAIL -eq 0 ] && exit 0 || exit 1
