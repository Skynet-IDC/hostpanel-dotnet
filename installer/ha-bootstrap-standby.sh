#!/bin/bash
# HA bootstrap: one-shot, run on the STANDBY (freshly installed HostPanel VM).
# Pulls all state from a running PRIMARY: SQL DB + tenant files + mail + panel config + certs.
# After this completes, ongoing replication (lsyncd + SQL log shipping) takes over.
#
# Usage:
#   ./ha-bootstrap-standby.sh --primary <primary-ip-or-host> \
#                             --primary-ssh-user <user, default mgardner> \
#                             --sql-sa-password <sa pw on BOTH primary and standby> \
#                             [--peer-token <random secret for /ha/peers registration>] \
#                             [--skip-services]
#
# Prereqs:
# - You can ssh keylessly: `ssh <user>@<primary>` works from this standby box.
# - mssql-tools18 + sqlcmd are installed on BOTH boxes (the panel installer ships this).
# - Both boxes are on the same SQL SA password.
# - HostPanel services (api + agent) on the standby are running OR were never started.

set -euo pipefail

PRIMARY=""
SSH_USER="mgardner"
SA_PW=""
PEER_TOKEN=""
SKIP_SERVICES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --primary) PRIMARY="$2"; shift 2 ;;
    --primary-ssh-user) SSH_USER="$2"; shift 2 ;;
    --sql-sa-password) SA_PW="$2"; shift 2 ;;
    --peer-token) PEER_TOKEN="$2"; shift 2 ;;
    --skip-services) SKIP_SERVICES=1; shift ;;
    *) echo "unknown arg: $1"; exit 2 ;;
  esac
done

if [[ -z "$PRIMARY" || -z "$SA_PW" ]]; then
  echo "Required: --primary <host> --sql-sa-password <pw>"
  exit 2
fi

REMOTE="${SSH_USER}@${PRIMARY}"
SQLCMD="/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $SA_PW -C"
echo "==> HA bootstrap: primary=${REMOTE}, standby=$(hostname) ($(hostname -I | awk '{print $1}'))"

# --- 1. Stop standby HostPanel services so files/DB are quiescent during sync.
if [[ "$SKIP_SERVICES" == 0 ]]; then
  echo "==> Stopping HostPanel services on standby (clean state for restore)..."
  sudo systemctl stop hostpanel-api hostpanel-agent 2>/dev/null || true
fi

# --- 2. SQL: full backup on primary, restore on standby.
echo "==> Taking SQL Server full backup on primary..."
ssh "$REMOTE" "sudo /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '$SA_PW' -C -Q \"BACKUP DATABASE [hostpanel] TO DISK = '/tmp/hostpanel-ha-bootstrap.bak' WITH INIT, COPY_ONLY, FORMAT;\""
# Note: dropped WITH COMPRESSION — SQL Server Express doesn't support it (Msg 1844). Standard/Enterprise do.

echo "==> Copying backup to standby..."
ssh "$REMOTE" "sudo chmod 644 /tmp/hostpanel-ha-bootstrap.bak"
mkdir -p /tmp/ha-bootstrap
scp "$REMOTE:/tmp/hostpanel-ha-bootstrap.bak" /tmp/ha-bootstrap/hostpanel-ha-bootstrap.bak
sudo mv /tmp/ha-bootstrap/hostpanel-ha-bootstrap.bak /var/opt/mssql/data/hostpanel-ha-bootstrap.bak
sudo chown mssql:mssql /var/opt/mssql/data/hostpanel-ha-bootstrap.bak

echo "==> Restoring DB on standby (drops local hostpanel DB first)..."
$SQLCMD -Q "
IF DB_ID('hostpanel') IS NOT NULL
BEGIN
  ALTER DATABASE [hostpanel] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE [hostpanel];
END;
" || true
$SQLCMD -Q "
RESTORE DATABASE [hostpanel] FROM DISK = '/var/opt/mssql/data/hostpanel-ha-bootstrap.bak'
WITH REPLACE, MOVE 'hostpanel' TO '/var/opt/mssql/data/hostpanel.mdf', MOVE 'hostpanel_log' TO '/var/opt/mssql/data/hostpanel_log.ldf';
"

# Remote: clean up the .bak so it doesn't sit on the primary fs forever.
ssh "$REMOTE" "sudo rm -f /tmp/hostpanel-ha-bootstrap.bak"
sudo rm -f /var/opt/mssql/data/hostpanel-ha-bootstrap.bak

# --- 3. File replication: pull /var/www (tenant docroots), /var/mail (Maildirs),
#         /etc/hostpanel (agent.token + ssl), /etc/letsencrypt (LE certs), /opt/hostpanel/wwwroot.
echo "==> rsyncing tenant files (/var/www)..."
sudo rsync -aHAX --delete --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/var/www/" /var/www/

echo "==> rsyncing mail spool (/var/mail)..."
sudo rsync -aHAX --delete --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/var/mail/" /var/mail/ 2>&1 | tail -3

echo "==> rsyncing panel config (/etc/hostpanel)..."
sudo rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/etc/hostpanel/" /etc/hostpanel/

echo "==> rsyncing Let's Encrypt store (optional)..."
ssh "$REMOTE" 'test -d /etc/letsencrypt' \
  && sudo rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/etc/letsencrypt/" /etc/letsencrypt/ \
  || echo "  (no /etc/letsencrypt on primary — skipping. Panel uses /etc/hostpanel/ssl for SslService certs.)"

echo "==> rsyncing panel wwwroot (UI assets + branding logos)..."
sudo rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/opt/hostpanel/wwwroot/" /opt/hostpanel/wwwroot/ 2>&1 | tail -3

echo "==> rsyncing panel appsettings (preserve any standby-specific overrides via .standby file)..."
if [[ -f /opt/hostpanel/appsettings.Standby.json ]]; then
  sudo cp /opt/hostpanel/appsettings.Standby.json /tmp/appsettings.Standby.json
fi
sudo rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/opt/hostpanel/appsettings.Production.json" /opt/hostpanel/appsettings.Production.json
if [[ -f /tmp/appsettings.Standby.json ]]; then
  sudo mv /tmp/appsettings.Standby.json /opt/hostpanel/appsettings.Standby.json
fi

# --- 4. Ownership fix-up: rsync preserves numeric uids, but mssql + www-data uids may differ.
#         For per-tenant /var/www dirs we KEEP rsync's numeric ids (so tenant user ownership maps
#         consistently across both boxes as long as each tenant was created with the same uid —
#         which the SystemCreateUserHandler does deterministically via `useradd`). For /opt/hostpanel
#         we re-chown to www-data because the *name* matters.
sudo chown -R www-data:www-data /opt/hostpanel/wwwroot /opt/hostpanel/appsettings.Production.json

# --- 5. Sync nginx vhost files so customer sites are reachable on the standby instantly.
echo "==> rsyncing nginx vhosts..."
sudo rsync -aHAX --numeric-ids --rsync-path='sudo rsync' -e ssh "$REMOTE:/etc/nginx/sites-available/" /etc/nginx/sites-available/
# Re-symlink sites-enabled — names match, but symlinks may dangle if site was deleted on primary
sudo rsync -aHAX --numeric-ids --copy-unsafe-links -e ssh "$REMOTE:/etc/nginx/sites-enabled/" /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx || echo "WARN: nginx -t failed — site files may need manual review"

# --- 6. Register the peer with the primary so it appears in the HA dashboard.
if [[ -n "$PEER_TOKEN" ]]; then
  echo "==> Registering with primary's /api/v1/ha/peers..."
  STANDBY_IP=$(hostname -I | awk '{print $1}')
  ADMIN_TOKEN=$(curl -sk -X POST -H "Content-Type: application/json" \
    -d "{\"email\":\"mgardner323@gmail.com\",\"password\":\"REDACTED-PASSWORD\"}" \
    "https://${PRIMARY}:8443/api/v1/auth/login" | python3 -c "import json,sys;print(json.load(sys.stdin).get('token',''))")
  if [[ -n "$ADMIN_TOKEN" ]]; then
    curl -sk -X POST -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
      -d "{\"host\":\"$STANDBY_IP\",\"role\":\"standby\",\"token\":\"$PEER_TOKEN\"}" \
      "https://${PRIMARY}:8443/api/v1/ha/peers" || echo "  (peer registration endpoint may not exist yet — skipping)"
  fi
fi

# --- 7. Start services
if [[ "$SKIP_SERVICES" == 0 ]]; then
  echo "==> Starting HostPanel services on standby..."
  sudo systemctl start hostpanel-agent
  sleep 2
  sudo systemctl start hostpanel-api
  sleep 5
  sudo systemctl is-active hostpanel-api hostpanel-agent
fi

echo "==> HA bootstrap complete."
echo "    Standby host:  $(hostname) ($(hostname -I | awk '{print $1}'))"
echo "    Primary host:  ${REMOTE}"
echo ""
echo "Next steps (manual for now — automation will land in a later Phase 19 pass):"
echo " 1. Configure SQL Server log shipping (primary → standby) — see Phase 19 design contract."
echo " 2. Set up lsyncd on PRIMARY watching /var/www, /var/mail, /etc/nginx/sites-* → push to standby."
echo " 3. Install hostpanel-ha-watchdog service on standby to monitor primary."
echo " 4. Add the standby's public IP to Cloudflare DNS-flip orchestration."
