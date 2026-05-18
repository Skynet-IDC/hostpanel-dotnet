#!/bin/bash
# Failback: return active role from this standby (currently serving as primary post-failover)
# back to the original primary. Caller must have verified the original primary is healthy.
#
# Steps (run on the box that's CURRENTLY primary — i.e. the former standby):
#   1. Sync ANY data written during failover back to the original primary
#   2. CF API DNS flip back to original primary public IP
#   3. Stop hostpanel-api here, drop local DB (back to warm-standby state)
#   4. Reset state file
#
# Loads /etc/hostpanel/ha-failover.env (same one used by failover.sh).

set -u
ENV_FILE="/etc/hostpanel/ha-failover.env"
[[ -f "$ENV_FILE" ]] && source "$ENV_FILE"

: "${PRIMARY_PUBLIC_IP:?required}"
: "${PRIMARY_HOST:?required}"
: "${THIS_PUBLIC_IP:?required}"
: "${CF_API_TOKEN:?required}"
: "${CF_MANAGED_DOMAINS:?required}"
: "${SA_PW:?required}"
: "${STATE_FILE:=/var/lib/hostpanel-ha-watchdog/state}"
: "${LOG:=/var/log/hostpanel-ha-failback.log}"

mkdir -p "$(dirname "$STATE_FILE")" "$(dirname "$LOG")"
exec >>"$LOG" 2>&1
echo
echo "===================================="
echo "FAILBACK TRIGGERED $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "===================================="

# --- 0. Sanity: original primary must be reachable + healthy.
echo "--- verifying original primary $PRIMARY_HOST is healthy..."
if ! curl --max-time 5 -fsk -o /dev/null "https://${PRIMARY_HOST}:8443/api/v1/health"; then
  echo "ERROR: original primary $PRIMARY_HOST not healthy. Refusing to failback."
  exit 1
fi

# --- 1. Push any divergent state back to original primary.
# We assume lsyncd was NOT running on this host during the failover (only the original primary
# pushes via lsyncd). Catch-up rsync to be safe.
echo "--- pushing /var/www, /var/mail, /etc/nginx/sites-* back to primary..."
SSH_CMD="ssh -o StrictHostKeyChecking=accept-new -i /root/.ssh/id_ed25519"
for p in /var/www/ /var/mail/ /etc/nginx/sites-available/ /etc/nginx/sites-enabled/ /opt/hostpanel/wwwroot/branding/; do
  rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e "$SSH_CMD" \
    "$p" "mgardner@${PRIMARY_HOST}:$p" 2>&1 | tail -2 || echo "  WARN: rsync $p failed (non-fatal)"
done

# --- 2. SQL backup + ship to primary + restore (mirror of bootstrap flow).
echo "--- backing up current SQL state..."
SQLCMD="/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $SA_PW -C"
$SQLCMD -Q "BACKUP DATABASE [hostpanel] TO DISK = '/var/opt/mssql/data/hostpanel-failback.bak' WITH INIT, FORMAT;"
chmod 644 /var/opt/mssql/data/hostpanel-failback.bak
chown mssql:mssql /var/opt/mssql/data/hostpanel-failback.bak

echo "--- copying backup to original primary..."
$SSH_CMD mgardner@${PRIMARY_HOST} "sudo install -o mssql -g mssql -m 644 /dev/stdin /var/opt/mssql/data/hostpanel-failback.bak" < /var/opt/mssql/data/hostpanel-failback.bak

echo "--- restoring on original primary..."
$SSH_CMD mgardner@${PRIMARY_HOST} "sudo systemctl stop hostpanel-api
sudo /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '$SA_PW' -C -Q \"
IF DB_ID('hostpanel') IS NOT NULL
BEGIN
  ALTER DATABASE [hostpanel] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE [hostpanel];
END;
RESTORE DATABASE [hostpanel] FROM DISK = '/var/opt/mssql/data/hostpanel-failback.bak' WITH REPLACE,
  MOVE 'hostpanel' TO '/var/opt/mssql/data/hostpanel.mdf',
  MOVE 'hostpanel_log' TO '/var/opt/mssql/data/hostpanel_log.ldf';
\"
sudo systemctl start hostpanel-api
sleep 5
sudo systemctl is-active hostpanel-api"

# --- 3. Flip Cloudflare DNS back to original primary.
echo "--- flipping CF DNS back to $PRIMARY_PUBLIC_IP..."
IFS=',' read -ra DOMAINS <<< "$CF_MANAGED_DOMAINS"
for raw in "${DOMAINS[@]}"; do
  d="$(echo "$raw" | tr -d '[:space:]')"
  [[ -z "$d" ]] && continue
  ZID=$(curl -sk -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failback/1.0" \
        "https://api.cloudflare.com/client/v4/zones?name=$d" | python3 -c "import json,sys;d=json.load(sys.stdin);print(d['result'][0]['id'] if d.get('result') else '')")
  [[ -z "$ZID" ]] && continue
  for NAME in "$d" "*.$d"; do
    RID=$(curl -sk -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failback/1.0" \
          "https://api.cloudflare.com/client/v4/zones/$ZID/dns_records?type=A&name=$NAME" | python3 -c "import json,sys;d=json.load(sys.stdin);print(d['result'][0]['id'] if d.get('result') else '')")
    [[ -z "$RID" ]] && continue
    BODY=$(printf '{"type":"A","name":"%s","content":"%s","ttl":60,"proxied":false,"comment":"HA failback %s"}' "$NAME" "$PRIMARY_PUBLIC_IP" "$(date -u +%Y-%m-%dT%H:%M:%SZ)")
    curl -sk -X PUT -H "Authorization: Bearer $CF_API_TOKEN" -H "User-Agent: hostpanel-ha-failback/1.0" -H "Content-Type: application/json" \
      -d "$BODY" "https://api.cloudflare.com/client/v4/zones/$ZID/dns_records/$RID" \
      | python3 -c "import json,sys;d=json.load(sys.stdin);print(f\"  {d['result']['name']} -> {d['result']['content']}\" if d['success'] else f\"  FAILED: {d.get('errors')}\")"
  done
done

# --- 4. Restore this host to warm-standby mode.
echo "--- stopping hostpanel-api on this host (back to warm standby)..."
systemctl stop hostpanel-api
# Keep the restored DB intact so the next pull cron overwrites it; or drop it for cleanliness:
# $SQLCMD -Q "ALTER DATABASE [hostpanel] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [hostpanel];" || true

echo "standby" > "$STATE_FILE"
echo "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > /var/lib/hostpanel-ha-watchdog/last-failback-at

echo "--- FAILBACK COMPLETE. Original primary $PRIMARY_HOST is active. This host returns to warm-standby."
