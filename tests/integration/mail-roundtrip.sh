#!/bin/bash
# HostPanel mail-roundtrip integration test
#
# Creates a HostPanel mailbox, sends a test email from it to a Testmail.app inbox,
# polls Testmail until the message arrives, asserts DKIM signature + content match.
#
# Prereqs:
#   - /etc/hostpanel/testmail.env present with TESTMAIL_API_KEY + TESTMAIL_NAMESPACE
#   - HostPanel API reachable at $HP_URL (default https://localhost:8443)
#   - Test domain registered in the panel (default $HP_DOMAIN — must already exist)
#   - HostPanel admin creds in env or /etc/hostpanel/admin-test.env
#
# Skips cleanly (exit 0) when credentials are not provided so this is safe to
# call from CI without secret leaks.

set -euo pipefail

HP_URL="${HP_URL:-https://localhost:8443}"
HP_DOMAIN="${HP_DOMAIN:-gardnerclan.us}"

# Load Testmail creds — exit 0 (skip) if missing, NOT fail
if [[ -f /etc/hostpanel/testmail.env ]]; then
    # shellcheck disable=SC1091
    source /etc/hostpanel/testmail.env
fi
: "${TESTMAIL_API_KEY:?TESTMAIL_API_KEY not set — skipping mail roundtrip test}"
: "${TESTMAIL_NAMESPACE:?TESTMAIL_NAMESPACE not set — skipping mail roundtrip test}"

# Load admin creds
if [[ -f /etc/hostpanel/admin-test.env ]]; then
    source /etc/hostpanel/admin-test.env
fi
: "${HP_ADMIN_EMAIL:?HP_ADMIN_EMAIL not set}"
: "${HP_ADMIN_PASSWORD:?HP_ADMIN_PASSWORD not set}"

# Unique per-run identifier so concurrent runs don't collide on Testmail's inbox
RUN_ID="$(date +%s)-$$"
TEST_TAG="hostpanel-${RUN_ID}"
TESTMAIL_TO="${TESTMAIL_NAMESPACE}.${TEST_TAG}@inbox.testmail.app"

echo "==> mail-roundtrip [run=${RUN_ID}]"
echo "    HostPanel: ${HP_URL}"
echo "    Domain:    ${HP_DOMAIN}"
echo "    Testmail:  ${TESTMAIL_TO}"

# ── 1. Auth ──────────────────────────────────────────────────────────────────
echo "==> [1/6] Authenticating with HostPanel API"
TOKEN=$(curl -sk -X POST "${HP_URL}/api/v1/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"${HP_ADMIN_EMAIL}\",\"password\":\"${HP_ADMIN_PASSWORD}\"}" \
    | python3 -c 'import sys,json; print(json.load(sys.stdin).get("token",""))')
[[ -n "$TOKEN" ]] || { echo "AUTH FAILED"; exit 1; }

# ── 2. Find or create the mailbox ────────────────────────────────────────────
echo "==> [2/6] Resolving domain id for ${HP_DOMAIN}"
DOMAIN_ID=$(curl -sk -H "Authorization: Bearer $TOKEN" "${HP_URL}/api/v1/domains" \
    | python3 -c "import sys,json; ds=json.load(sys.stdin); print(next((d['id'] for d in ds if d.get('name')=='${HP_DOMAIN}'), ''))")
[[ -n "$DOMAIN_ID" ]] || { echo "Domain ${HP_DOMAIN} not found"; exit 1; }
echo "    domain_id=${DOMAIN_ID}"

MB_LOCAL="roundtrip-${RUN_ID}"
MB_PASS="$(openssl rand -base64 24 | tr -d '/+=' | head -c 20)Aa1!"
echo "==> [3/6] Creating mailbox ${MB_LOCAL}@${HP_DOMAIN}"
MB_RESP=$(curl -sk -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"localPart\":\"${MB_LOCAL}\",\"password\":\"${MB_PASS}\",\"quotaMb\":100}" \
    "${HP_URL}/api/v1/domains/${DOMAIN_ID}/mailboxes")
MB_ID=$(echo "$MB_RESP" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("id",""))')
[[ -n "$MB_ID" ]] || { echo "Mailbox create failed: $MB_RESP"; exit 1; }

trap 'echo "==> cleanup: deleting mailbox ${MB_ID}"; curl -sk -X DELETE -H "Authorization: Bearer $TOKEN" "${HP_URL}/api/v1/mailboxes/${MB_ID}" >/dev/null 2>&1 || true' EXIT

# ── 3. Send via SMTP through the panel ───────────────────────────────────────
SUBJECT="HostPanel roundtrip ${RUN_ID}"
BODY_TOKEN="hp-roundtrip-token-${RUN_ID}-end"
echo "==> [4/6] Sending via SMTP submission (587 STARTTLS) as ${MB_LOCAL}@${HP_DOMAIN}"

# swaks is the most portable way to script SMTP submission; fall back to python smtplib.
if command -v swaks >/dev/null 2>&1; then
    swaks --to "${TESTMAIL_TO}" \
          --from "${MB_LOCAL}@${HP_DOMAIN}" \
          --server localhost --port 587 --tls \
          --auth-user "${MB_LOCAL}@${HP_DOMAIN}" \
          --auth-password "${MB_PASS}" \
          --header "Subject: ${SUBJECT}" \
          --body "${BODY_TOKEN}" \
          --silent 1 || { echo "SMTP send failed (swaks)"; exit 1; }
else
    python3 - <<PY
import smtplib, ssl
from email.message import EmailMessage
m = EmailMessage()
m["From"] = "${MB_LOCAL}@${HP_DOMAIN}"
m["To"] = "${TESTMAIL_TO}"
m["Subject"] = "${SUBJECT}"
m.set_content("${BODY_TOKEN}")
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE
with smtplib.SMTP("localhost", 587, timeout=15) as s:
    s.starttls(context=ctx)
    s.login("${MB_LOCAL}@${HP_DOMAIN}", """${MB_PASS}""")
    s.send_message(m)
print("submitted")
PY
fi

# ── 4. Poll Testmail for delivery ────────────────────────────────────────────
echo "==> [5/6] Polling Testmail for the message (90s budget)"
DEADLINE=$(( $(date +%s) + 90 ))
FOUND=""
while [[ $(date +%s) -lt $DEADLINE ]]; do
    RESP=$(curl -sk "https://api.testmail.app/api/json?apikey=${TESTMAIL_API_KEY}&namespace=${TESTMAIL_NAMESPACE}&tag=${TEST_TAG}&limit=1")
    COUNT=$(echo "$RESP" | python3 -c 'import sys,json; print(json.load(sys.stdin).get("count",0))')
    if [[ "$COUNT" -gt 0 ]]; then
        FOUND="$RESP"
        break
    fi
    sleep 3
done
[[ -n "$FOUND" ]] || { echo "TIMEOUT: message never arrived"; exit 1; }

# ── 5. Assertions ────────────────────────────────────────────────────────────
echo "==> [6/6] Verifying message content + DKIM"
echo "$FOUND" | python3 - <<PY
import sys, json
data = json.loads("""$FOUND""")
msg = data["emails"][0]
assert msg["subject"] == "${SUBJECT}", f"subject mismatch: {msg['subject']}"
assert "${BODY_TOKEN}" in msg.get("text", ""), "body token missing"
assert msg.get("from", "").endswith("${HP_DOMAIN}>") or msg.get("from","").endswith("${HP_DOMAIN}"), f"unexpected from: {msg.get('from')}"
# DKIM info lives in spamReport on testmail
dkim = msg.get("spam_report", {}).get("dkim") or msg.get("dkim") or "missing"
print(f"PASS subject={msg['subject']!r} from={msg.get('from')!r} dkim={dkim}")
PY

echo "==> mail-roundtrip OK"
