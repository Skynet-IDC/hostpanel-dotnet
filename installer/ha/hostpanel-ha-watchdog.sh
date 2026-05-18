#!/bin/bash
# HostPanel HA watchdog — runs on the STANDBY, polls primary's /api/v1/ha/status every CHECK_INTERVAL.
# After FAIL_THRESHOLD consecutive failures, fires /api/v1/ha/failover (on this standby) which:
#   1. Promotes local SQL replica
#   2. Calls Cloudflare API to swing DNS for failover-managed domains
#   3. Reverses lsyncd direction (so the new primary starts pushing to the former primary)
#
# Config via /etc/hostpanel/ha-watchdog.env (sourced):
#   PRIMARY_HOST=192.168.10.235
#   PRIMARY_PORT=8443
#   CHECK_INTERVAL=5         # seconds
#   FAIL_THRESHOLD=3         # consecutive 5xx/timeout before failover
#   AUTH_TOKEN=<panel admin JWT or service-account token>
#   STATE_FILE=/var/lib/hostpanel-ha-watchdog/state
#   LOG_FILE=/var/log/hostpanel-ha-watchdog.log
#
# Standard tier (default): no extra deps, just curl. Premium tier: CF Load Balancer health checks
# can run alongside — they'll fire failover first (faster), watchdog as a backstop.

set -u
ENV_FILE="/etc/hostpanel/ha-watchdog.env"
[[ -f "$ENV_FILE" ]] && source "$ENV_FILE"

: "${PRIMARY_HOST:?Must set PRIMARY_HOST in $ENV_FILE}"
: "${PRIMARY_PORT:=8443}"
: "${CHECK_INTERVAL:=5}"
: "${FAIL_THRESHOLD:=3}"
AUTH_TOKEN="${AUTH_TOKEN:-}"  # Optional — used only by the /ha/status probe
: "${STATE_FILE:=/var/lib/hostpanel-ha-watchdog/state}"
: "${LOG_FILE:=/var/log/hostpanel-ha-watchdog.log}"

mkdir -p "$(dirname "$STATE_FILE")"
mkdir -p "$(dirname "$LOG_FILE")"

log() {
  echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $1" | tee -a "$LOG_FILE"
}

check_primary() {
  # Return 0 if primary healthy, non-zero otherwise.
  # Uses /api/v1/health which the HA controller exposes anonymously (no auth needed).
  curl --max-time 4 -fsk -o /dev/null \
    "https://${PRIMARY_HOST}:${PRIMARY_PORT}/api/v1/health"
  return $?
}

trigger_failover() {
  log "FAILOVER TRIGGERED — invoking /usr/local/bin/hostpanel-ha-failover.sh"
  # Standby's hostpanel-api is stopped during warm-standby mode (DB is in restore-staging state),
  # so we can't call /api/v1/ha/failover. Invoke the bash failover script directly — it restores
  # the DB, starts the API, and flips Cloudflare DNS.
  /usr/local/bin/hostpanel-ha-failover.sh 2>&1 | tee -a "$LOG_FILE"
}

main_loop() {
  local consecutive_failures=0
  local current_state="standby"
  echo "$current_state" > "$STATE_FILE"
  log "watchdog starting: primary=${PRIMARY_HOST}:${PRIMARY_PORT}, interval=${CHECK_INTERVAL}s, threshold=${FAIL_THRESHOLD}"

  while true; do
    # If we've already failed over, exit the polling loop — operator decides when to fail back.
    if [[ "$(cat "$STATE_FILE" 2>/dev/null)" == "failover" ]]; then
      log "in failover state — watchdog standing down. Reset via /api/v1/ha/failback or delete $STATE_FILE."
      sleep "$CHECK_INTERVAL"
      continue
    fi

    if check_primary; then
      if [[ "$consecutive_failures" -gt 0 ]]; then
        log "primary recovered (was at $consecutive_failures failures)"
      fi
      consecutive_failures=0
    else
      consecutive_failures=$((consecutive_failures + 1))
      log "primary check failed ($consecutive_failures/$FAIL_THRESHOLD)"
      if [[ "$consecutive_failures" -ge "$FAIL_THRESHOLD" ]]; then
        trigger_failover
      fi
    fi
    sleep "$CHECK_INTERVAL"
  done
}

main_loop
