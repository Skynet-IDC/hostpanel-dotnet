#!/bin/bash
# Promote this standby to primary. Called by hostpanel-ha-watchdog.sh on detected primary failure,
# or invoked manually from the panel UI's "Test failover" button.
#
# Steps:
#   1. RESTORE the staged backup WITH RECOVERY (DB becomes queryable)
#   2. Start hostpanel-api
#   3. CF API DNS flip — switch the managed apex A record(s) to this host's public IP
#   4. Mark state file so watchdog stands down
#
# Config via /etc/hostpanel/ha-failover.env:
#   PRIMARY_HOST=192.168.10.235          # the now-dead primary (for documentation only)
#   THIS_PUBLIC_IP=45.25.216.221         # this host's public IP, written to CF on flip
#   PRIMARY_PUBLIC_IP=45.25.216.220      # the IP currently in CF that we want to replace
#   CF_API_TOKEN=...                     # Cloudflare Bearer token
#   CF_MANAGED_DOMAINS="gardnerclan.us,webdesign101.dev"   # comma-separated zones to flip
#   SA_PW=Test@SqlPass2026Z
#   STATE_FILE=/var/lib/hostpanel-ha-watchdog/state

set -u
ENV_FILE="/etc/hostpanel/ha-failover.env"
[[ -f "$ENV_FILE" ]] && source "$ENV_FILE"

: "${THIS_PUBLIC_IP:?THIS_PUBLIC_IP required in $ENV_FILE}"
: "${PRIMARY_PUBLIC_IP:?PRIMARY_PUBLIC_IP required in $ENV_FILE}"
: "${CF_API_TOKEN:?CF_API_TOKEN required in $ENV_FILE}"
: "${CF_MANAGED_DOMAINS:?CF_MANAGED_DOMAINS (comma-list) required in $ENV_FILE}"
: "${SA_PW:?SA_PW required in $ENV_FILE}"
: "${STATE_FILE:=/var/lib/hostpanel-ha-watchdog/state}"
: "${BACKUP_FILE:=/var/opt/mssql/data/hostpanel-ha.bak}"
: "${LOG:=/var/log/hostpanel-ha-failover.log}"

mkdir -p "$(dirname "$STATE_FILE")" "$(dirname "$LOG")"
exec >>"$LOG" 2>&1
echo
echo "===================================="
echo "FAILOVER TRIGGERED $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "===================================="

# --- 1. Restore DB WITH RECOVERY (promotes the staged backup to live)
if [[ ! -s "$BACKUP_FILE" ]]; then
  echo "ERROR: no staged backup at $BACKUP_FILE — aborting failover."
  exit 1
fi
SQLCMD="/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $SA_PW -C"
echo "--- restoring DB from $BACKUP_FILE..."
$SQLCMD -Q "
IF DB_ID('hostpanel') IS NOT NULL
BEGIN
  ALTER DATABASE [hostpanel] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE [hostpanel];
END;
"
$SQLCMD -Q "
RESTORE DATABASE [hostpanel] FROM DISK = '$BACKUP_FILE'
WITH REPLACE,
     MOVE 'hostpanel' TO '/var/opt/mssql/data/hostpanel.mdf',
     MOVE 'hostpanel_log' TO '/var/opt/mssql/data/hostpanel_log.ldf';
" || { echo "ERROR: restore failed"; exit 2; }

# --- 2. Start hostpanel-api (was stopped during warm-standby)
echo "--- starting hostpanel-api..."
systemctl start hostpanel-api
sleep 5
if ! systemctl is-active --quiet hostpanel-api; then
  echo "ERROR: hostpanel-api did not start. Failover incomplete."
  exit 3
fi

# --- 3. Cloudflare DNS flip — for each managed zone, update apex A record
echo "--- flipping DNS..."
IFS=',' read -ra DOMAINS <<< "$CF_MANAGED_DOMAINS"
for raw in "${DOMAINS[@]}"; do
  d="$(echo "$raw" | tr -d '[:space:]')"
  [[ -z "$d" ]] && continue
  echo "  -> flipping $d apex + wildcard A to $THIS_PUBLIC_IP"
  ZID=$(curl -sk -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failover/1.0" \
        "https://api.cloudflare.com/client/v4/zones?name=$d" | python3 -c "import json,sys;d=json.load(sys.stdin);print(d['result'][0]['id'] if d.get('result') else '')")
  [[ -z "$ZID" ]] && { echo "    (zone not found, skipping)"; continue; }
  for NAME in "$d" "*.$d"; do
    RID=$(curl -sk -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failover/1.0" \
          "https://api.cloudflare.com/client/v4/zones/$ZID/dns_records?type=A&name=$NAME" | python3 -c "import json,sys;d=json.load(sys.stdin);print(d['result'][0]['id'] if d.get('result') else '')")
    [[ -z "$RID" ]] && continue
    BODY=$(printf '{"type":"A","name":"%s","content":"%s","ttl":60,"proxied":false,"comment":"HA failover %s"}' "$NAME" "$THIS_PUBLIC_IP" "$(date -u +%Y-%m-%dT%H:%M:%SZ)")
    curl -sk -X PUT -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failover/1.0" -H "Content-Type: application/json" \
      -d "$BODY" "https://api.cloudflare.com/client/v4/zones/$ZID/dns_records/$RID" \
      | python3 -c "import json,sys;d=json.load(sys.stdin);print(f\"    {d['result']['name']} -> {d['result']['content']}\" if d['success'] else f\"    FAILED: {d.get('errors')}\")"
  done
done

# --- 4. Record state
echo "failover-active" > "$STATE_FILE"
echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > /var/lib/hostpanel-ha-watchdog/last-failover-at

echo "--- FAILOVER COMPLETE — this host is now PRIMARY."
echo "--- New primary public IP: $THIS_PUBLIC_IP"
echo "--- DNS TTL is 60s; cutover completes when caches expire."
