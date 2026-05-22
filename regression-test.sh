#!/bin/bash
# Backend regression sweep for VM 280 after fresh install from main.
# Run on VM 280 as mgardner. STANDBY_TOKEN env var must contain VM 281's agent token.
set -uo pipefail
API=https://127.0.0.1:8443/api/v1
STANDBY_IP=192.168.10.236
STANDBY_TOKEN="${STANDBY_TOKEN:?STANDBY_TOKEN env var required}"
TS=$(date +%s)
HTTP_CODE_FILE=/tmp/api.code

PASS=0; FAIL=0
chk() {
    local name="$1" expected="$2" actual="$3"
    if [[ "$actual" == *"$expected"* ]]; then
        echo "PASS  $name"; PASS=$((PASS+1))
    else
        echo "FAIL  $name (expected '$expected', got '$actual')"; FAIL=$((FAIL+1))
    fi
}

# HTTP_CODE written to a tmpfile so it survives across $() subshells (the caller
# can read it after the substitution returns). curl writes the body to stdout
# normally — the caller captures it with $(api ...).
TOKEN=""
api() {
    local method="$1" path="$2" body="${3:-}"
    if [[ -n "$body" ]]; then
        curl -sk --max-time 30 -X "$method" \
            -H "Authorization: Bearer $TOKEN" \
            -H "Content-Type: application/json" \
            -d "$body" \
            -w '%{http_code}' -o /tmp/api.body "$API$path" > "$HTTP_CODE_FILE"
    else
        curl -sk --max-time 30 -X "$method" \
            -H "Authorization: Bearer $TOKEN" \
            -w '%{http_code}' -o /tmp/api.body "$API$path" > "$HTTP_CODE_FILE"
    fi
    cat /tmp/api.body
}
http_code() { cat "$HTTP_CODE_FILE" 2>/dev/null || echo "000"; }

jq_field() { JQ_KEY="$1" python3 -c 'import sys,json,os; d=json.load(sys.stdin); v=d.get(os.environ["JQ_KEY"]) if isinstance(d,dict) else None; print(v if v is not None else "")' 2>/dev/null || echo ""; }

echo "===== regression $(date) ====="

### Phase B — auth + bootstrap
B1=$(api GET "/setup/bootstrap-status" | tr -d '[:space:]')
chk "B.1 bootstrap-status adminExists=true" 'adminExists":true' "$B1"

LOGIN=$(api POST "/auth/login" '{"email":"mgardner323@gmail.com","password":"Test@HostPanel2026"}')
TOKEN=$(echo "$LOGIN" | jq_field token)
chk "B.2 login returns JWT" "ey" "${TOKEN:0:2}"

api POST "/setup/bootstrap-admin" '{"email":"x@x","password":"y"}' >/dev/null
chk "B.3 bootstrap closed (409)" "409" "$(http_code)"

### Phase C — server stats + panel
api GET "/server/stats" > /tmp/c1.body
chk "C.1 server/stats returns 200" "200" "$(http_code)"
chk "C.1 server/stats has cpuUsage" "cpuUsage" "$(cat /tmp/c1.body)"

api GET "/panel/version" >/dev/null
chk "C.2 panel/version returns 200" "200" "$(http_code)"

### Phase D — full provisioning chain
PLAN=$(api POST "/service-plans" "{\"name\":\"rt-plan-$TS\",\"pricePerPeriod\":0,\"diskQuotaMb\":2048,\"bandwidthLimitMb\":50000,\"maxDomains\":5,\"maxEmailAccounts\":5,\"maxDatabases\":3,\"defaultBillingPeriod\":\"Monthly\"}")
PLAN_ID=$(echo "$PLAN" | jq_field id)
chk "D.1 plan created (id=$PLAN_ID)" "20" "$(http_code | cut -c1-2)"

# Password must be ≥12 chars per Identity policy
CUST=$(api POST "/customers" "{\"email\":\"rt-$TS@example.com\",\"password\":\"RtCustomerPw2026!\",\"firstName\":\"Regression\",\"lastName\":\"Tester\",\"companyName\":\"RT\"}")
CUST_CODE=$(http_code)
CUST_ID=$(echo "$CUST" | jq_field id)
chk "D.2 customer created (id=${CUST_ID:0:8}...)" "20" "$(echo "$CUST_CODE" | cut -c1-2)"

SUB=$(api POST "/subscriptions" "{\"userId\":\"$CUST_ID\",\"servicePlanId\":$PLAN_ID,\"mainDomain\":\"rt$TS.example.com\"}")
SUB_CODE=$(http_code)
SUB_ID=$(echo "$SUB" | jq_field id)
chk "D.3 subscription created (id=$SUB_ID)" "20" "$(echo "$SUB_CODE" | cut -c1-2)"

if [[ -n "$SUB_ID" ]]; then
    echo "D.4 waiting for reconciler to reach Active (up to 60s)..."
    STATE=""
    for i in {1..30}; do
        S=$(api GET "/subscriptions/$SUB_ID")
        STATE=$(echo "$S" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('status','?'))" 2>/dev/null)
        if [[ "$STATE" == "Active" ]]; then break; fi
        sleep 2
    done
    chk "D.4 reconciler reaches Active" "Active" "$STATE"

    D5=$(curl -s --max-time 5 -H "Host: rt$TS.example.com" http://127.0.0.1/ -o /dev/null -w "%{http_code}")
    chk "D.5 nginx vhost serves 200" "200" "$D5"
fi

### Phase G — databases (BUG-009 MySQL sudo + 9765a25 32-char hash)
GSTATUS=$(api GET "/databases/status")
chk "G.4 databases/status mentions MySql" "MySql" "$GSTATUS"

if [[ -n "$SUB_ID" ]]; then
    api POST "/databases" "{\"subscriptionId\":$SUB_ID,\"name\":\"rt_mysql_$TS\",\"type\":\"MySql\"}" >/dev/null
    chk "G.1 MySQL db create (panel hash truncation needed for 32-char user)" "20" "$(http_code | cut -c1-2)"

    api POST "/databases" "{\"subscriptionId\":$SUB_ID,\"name\":\"rt_pg_$TS\",\"type\":\"PostgreSql\"}" >/dev/null
    chk "G.2 PostgreSQL db create (BUG-009 sudo peer auth)" "20" "$(http_code | cut -c1-2)"

    api POST "/databases" "{\"subscriptionId\":$SUB_ID,\"name\":\"rt_mssql_$TS\",\"type\":\"MsSql\"}" >/dev/null
    chk "G.3 MSSQL db create" "20" "$(http_code | cut -c1-2)"
fi

### Phase F — mail (BUG-008 dovecot SASL on Postfix)
if [[ -n "$SUB_ID" ]]; then
    DOM_F=$(api POST "/domains" "{\"subscriptionId\":$SUB_ID,\"name\":\"mail$TS.example.com\"}")
    DOM_F_ID=$(echo "$DOM_F" | jq_field id)
    chk "F.0 domain created (id=$DOM_F_ID)" "20" "$(http_code | cut -c1-2)"

    if [[ -n "$DOM_F_ID" ]]; then
        api POST "/domains/$DOM_F_ID/mailboxes" '{"localPart":"rt","password":"RtMboxPassword2026!","quotaMb":100}' >/dev/null
        chk "F.1 mailbox created" "20" "$(http_code | cut -c1-2)"
    fi
fi

### Phase J — HA: TCP listener fix end-to-end (THE HEADLINE CHECK)
echo "J.0 standby token: ${#STANDBY_TOKEN} chars"

# Use a unique hostname per run so it doesn't collide with leftover rows.
SRV=$(api POST "/servers" "{\"hostname\":\"hp281-$TS\",\"ipAddress\":\"$STANDBY_IP\",\"agentPort\":50051,\"agentToken\":\"$STANDBY_TOKEN\",\"serverGroup\":\"failover\"}")
SRV_CODE=$(http_code)
SRV_ID=$(echo "$SRV" | jq_field id)
chk "J.1 standby server registered (id=$SRV_ID)" "20" "$(echo "$SRV_CODE" | cut -c1-2)"

api POST "/ha/configure-failover" "{\"standbyServerId\":$SRV_ID}" >/dev/null
chk "J.2 configure-failover (was 504 pre-fix)" "200" "$(http_code)"

HT=$(api POST "/ha/test" "{}")
chk "J.4 ha/test returns standbyAgent check" "standbyAgent" "$HT"

AGENT_OK=$(echo "$HT" | python3 -c "import sys,json; d=json.load(sys.stdin); chks={c['check']:c.get('ok',False) for c in d.get('checks',[])}; print(chks.get('standbyAgent', False))" 2>/dev/null)
chk "J.4b ha/test standbyAgent=true (NEW: needs TCP listener)" "True" "$AGENT_OK"

api GET "/ha/replication-lag" >/dev/null
chk "J.3 replication-lag returns 2xx" "2" "$(http_code | cut -c1-1)"

echo
echo "===== RESULT: PASS=$PASS FAIL=$FAIL ====="
exit $FAIL
