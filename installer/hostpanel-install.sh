#!/bin/bash
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive

# HostPanel Installer Script
# Bootstraps a fresh Ubuntu/Debian server with all dependencies

HOSTPANEL_VERSION="1.0.0"
INSTALL_DIR="/opt/hostpanel"
CONFIG_DIR="/etc/hostpanel"
DATA_DIR="/var/lib/hostpanel"
LOG_DIR="/var/log/hostpanel"
BACKUP_DIR="/var/backups/hostpanel"
SSL_DIR="${CONFIG_DIR}/ssl"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Parse arguments
SQL_PASSWORD=""
# Admin email/password are no longer baked in by default. If omitted, the panel boots
# admin-less and the first visitor to https://<host>:8443/ is shown a one-shot
# "create admin account" screen powered by /api/v1/setup/bootstrap-admin.
ADMIN_EMAIL=""
ADMIN_PASSWORD=""
SKIP_MSSQL=false
SKIP_MAIL=false
WITH_SDK=false
SOURCE_DIR=""
SENTRY_API_DSN=""
SENTRY_UI_DSN=""
# CIDR allowed to reach the cross-host agent TCP listener (port 50051). Empty = auto-detect
# from the default-route LAN. Override via --ha-allow-cidr for unusual subnets.
HA_ALLOW_CIDR=""
# GitHub clone fallback: if neither --source nor --git-repo is given, default to the
# operator's public repo. Override --git-repo or --git-ref for forks / branches.
GIT_REPO="https://github.com/mgardner323/hostpanel.git"
GIT_REF="main"

while [[ $# -gt 0 ]]; do
    case $1 in
        --sql-password) SQL_PASSWORD="$2"; shift 2;;
        --admin-email) ADMIN_EMAIL="$2"; shift 2;;
        --admin-password) ADMIN_PASSWORD="$2"; shift 2;;
        --skip-mssql) SKIP_MSSQL=true; shift;;
        --skip-mail) SKIP_MAIL=true; shift;;
        --with-sdk) WITH_SDK=true; shift;;
        --source) SOURCE_DIR="$2"; shift 2;;
        --git-repo) GIT_REPO="$2"; shift 2;;
        --git-ref) GIT_REF="$2"; shift 2;;
        --sentry-api-dsn) SENTRY_API_DSN="$2"; shift 2;;
        --sentry-ui-dsn) SENTRY_UI_DSN="$2"; shift 2;;
        --ha-allow-cidr) HA_ALLOW_CIDR="$2"; shift 2;;
        -h|--help) echo "Usage: $0 [--sql-password PASSWORD] [--admin-email EMAIL] [--admin-password PASSWORD] [--skip-mssql] [--skip-mail] [--with-sdk] [--source /path/to/src | --git-repo URL --git-ref BRANCH] [--sentry-api-dsn DSN] [--sentry-ui-dsn DSN] [--ha-allow-cidr CIDR]"; exit 0;;
        *) log_error "Unknown option: $1"; exit 1;;
    esac
done

# Pre-flight checks
preflight_checks() {
    log_info "Running pre-flight checks..."

    if [[ $EUID -ne 0 ]]; then
        log_error "This script must be run as root"
        exit 1
    fi

    # Check OS
    if [[ ! -f /etc/os-release ]]; then
        log_error "Cannot detect OS. Only Ubuntu 22.04/24.04 and Debian 12 are supported."
        exit 1
    fi

    source /etc/os-release
    if [[ "$ID" != "ubuntu" && "$ID" != "debian" ]]; then
        log_error "Unsupported OS: $ID. Only Ubuntu and Debian are supported."
        exit 1
    fi

    # Check minimum specs
    TOTAL_MEM=$(free -m | awk '/^Mem:/{print $2}')
    if [[ $TOTAL_MEM -lt 1800 ]]; then
        log_warn "Minimum 2GB RAM recommended. Found: ${TOTAL_MEM}MB"
    fi

    TOTAL_DISK=$(df -m / | awk 'NR==2{print $4}')
    if [[ $TOTAL_DISK -lt 15000 ]]; then
        log_warn "Minimum 20GB free disk recommended. Found: ${TOTAL_DISK}MB"
    fi

    log_info "Pre-flight checks passed (${ID} ${VERSION_ID}, ${TOTAL_MEM}MB RAM, ${TOTAL_DISK}MB disk)"
}

# Install base dependencies
install_base() {
    # Ubuntu cloud-init first-boot has THREE separate things running apt in the background:
    #   1. unattended-upgrades.service (security updates)
    #   2. apt-daily.service           (list refresh — triggered by apt-daily.timer)
    #   3. apt-daily-upgrade.service   (upgrade install — triggered by apt-daily-upgrade.timer)
    # All three hold one of /var/lib/apt/lists/lock, /var/lib/dpkg/lock, or
    # /var/lib/dpkg/lock-frontend at various points. THIRD-PARTY postinst scripts
    # (e.g. packages-microsoft-prod) shell out to apt-get without honoring our
    # DPkg::Lock::Timeout setting, so we must guarantee no contender is running.
    #
    # Strategy: stop + mask all 3 service units AND their timers, then wait for
    # any in-flight process to drain NATURALLY (killing mid-txn corrupts dpkg state).
    # Reconcile dpkg afterwards as belt-and-suspenders.
    log_info "Disabling apt background services for install duration..."
    for unit in unattended-upgrades.service apt-daily.service apt-daily-upgrade.service \
                apt-daily.timer apt-daily-upgrade.timer; do
        systemctl stop "$unit" 2>/dev/null || true
        systemctl mask "$unit" 2>/dev/null || true
    done

    # Wait up to 10 minutes for any in-flight apt/dpkg process to drain naturally.
    log_info "Waiting for any in-flight apt/dpkg process to finish (up to 10 min)..."
    for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
        if ! pgrep -f 'apt-get|unattended-upgrade|dpkg ' >/dev/null \
           && ! fuser /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock 2>/dev/null; then
            break
        fi
        log_info "Still waiting for apt to settle (${i}0s/200s)..."
        sleep 30
    done
    # If anything is still hanging after 10 min, force-kill — better than infinite wait.
    if pgrep -f 'apt-get|unattended-upgrade' >/dev/null; then
        log_warn "Apt processes still running after 10 min — force-killing"
        pkill -9 -f 'apt-get|unattended-upgrade' 2>/dev/null || true
        sleep 2
        fuser -k /var/lib/dpkg/lock-frontend /var/lib/dpkg/lock /var/lib/apt/lists/lock 2>/dev/null || true
        sleep 2
    fi
    # Always reconcile dpkg state to recover from any half-done txn.
    log_info "Reconciling dpkg state (dpkg --configure -a)..."
    DEBIAN_FRONTEND=noninteractive dpkg --configure -a 2>&1 | tail -5 || true

    # Wait up to 5 minutes for dpkg lock (belt-and-suspenders even after the disable above)
    echo 'DPkg::Lock::Timeout "300";' > /etc/apt/apt.conf.d/99-hostpanel-lock-wait

    # Force IPv4 for apt — some Proxmox/cloud-init setups deliver an IPv6 default route via RA
    # but no actual IPv6 connectivity, causing apt to hang on AAAA-first lookups (seen on VM 281 HA standby).
    echo 'Acquire::ForceIPv4 "true";' > /etc/apt/apt.conf.d/99-hostpanel-force-ipv4

    log_info "Updating package lists..."
    apt-get update -qq

    log_info "Installing base dependencies..."
    apt-get install -y -qq \
        curl wget gnupg2 apt-transport-https \
        software-properties-common ca-certificates \
        ufw fail2ban unzip tar gzip \
        quota quotatool rsync jq \
        apache2-utils vnstat clamav clamav-daemon

    # wkhtmltopdf — needed for InvoicePdfController to render real PDFs (bug #52).
    # Removed from Ubuntu 24.04 repos; pull the upstream .deb from the wkhtmltopdf GitHub release
    # (built for jammy, runs fine on noble). Falls back gracefully if download fails — the API
    # detects the missing binary and serves HTML invoices instead.
    if ! command -v wkhtmltopdf >/dev/null 2>&1; then
        log_info "Installing wkhtmltopdf (for invoice PDFs)..."
        local wkh_deb="/tmp/wkhtmltox.deb"
        if wget -q -O "$wkh_deb" "https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6.1-3/wkhtmltox_0.12.6.1-3.jammy_amd64.deb"; then
            apt-get install -y -qq "$wkh_deb" 2>&1 | tail -2 || log_warn "wkhtmltopdf install failed — invoice PDFs will fall back to HTML"
            rm -f "$wkh_deb"
        else
            log_warn "wkhtmltopdf .deb download failed — invoice PDFs will fall back to HTML"
        fi
    fi
}

# Install .NET Runtime (and optionally SDK for on-server builds)
install_dotnet() {
    log_info "Installing .NET Runtime..."

    # Add Microsoft repository
    wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
    dpkg -i /tmp/packages-microsoft-prod.deb
    rm /tmp/packages-microsoft-prod.deb
    apt-get update -qq

    apt-get install -y -qq dotnet-runtime-10.0 aspnetcore-runtime-10.0

    if [[ "$WITH_SDK" == "true" ]]; then
        log_info "Installing .NET SDK (--with-sdk)..."
        apt-get install -y -qq dotnet-sdk-10.0
        log_info ".NET SDK installed: $(dotnet --version)"
    fi

    log_info ".NET Runtime installed: $(dotnet --info | head -1)"
}

# Install Nginx
install_nginx() {
    log_info "Installing Nginx..."
    apt-get install -y -qq nginx
    systemctl enable nginx
    systemctl start nginx
    log_info "Nginx installed and running"
}

# Install PHP-FPM (multiple versions)
install_php() {
    log_info "Installing PHP-FPM..."

    # Add PHP PPA for multiple versions
    add-apt-repository -y ppa:ondrej/php 2>/dev/null || true
    apt-get update -qq

    for PHP_VER in 8.1 8.2 8.3; do
        apt-get install -y -qq \
            php${PHP_VER}-fpm php${PHP_VER}-cli php${PHP_VER}-common \
            php${PHP_VER}-mysql php${PHP_VER}-pgsql php${PHP_VER}-sqlite3 \
            php${PHP_VER}-curl php${PHP_VER}-gd php${PHP_VER}-mbstring \
            php${PHP_VER}-xml php${PHP_VER}-zip php${PHP_VER}-intl \
            php${PHP_VER}-bcmath php${PHP_VER}-soap
        systemctl enable php${PHP_VER}-fpm
        systemctl start php${PHP_VER}-fpm
        log_info "PHP ${PHP_VER}-FPM installed"
    done
}

# Install SQL Server
install_mssql() {
    if [[ "$SKIP_MSSQL" == "true" ]]; then
        log_warn "Skipping SQL Server installation (--skip-mssql)"
        return
    fi

    log_info "Installing SQL Server..."

    # Pin to 22.04 repo — Microsoft doesn't publish mssql-server-2022 for 24.04+
    local MSSQL_REPO_VERSION="22.04"
    curl -s https://packages.microsoft.com/keys/microsoft.asc | gpg --batch --no-tty --yes --dearmor -o /usr/share/keyrings/microsoft.gpg
    echo "deb [arch=amd64 signed-by=/usr/share/keyrings/microsoft.gpg] https://packages.microsoft.com/ubuntu/${MSSQL_REPO_VERSION}/mssql-server-2022 jammy main" > /etc/apt/sources.list.d/mssql-server.list
    apt-get update -qq

    # Backfill OpenLDAP 2.5 libs (mssql-server-2022 linked against 2.5; Ubuntu 24.04 ships 2.6)
    # Dynamically discover the latest 22.04 deb — Ubuntu archive rolls versions regularly
    log_info "Backfilling libldap-2.5 from Ubuntu archive..."
    local LDAP25_DIR
    LDAP25_DIR=$(mktemp -d)
    (
        cd "$LDAP25_DIR"
        local LDAP25_DEB
        LDAP25_DEB=$(curl -s http://archive.ubuntu.com/ubuntu/pool/main/o/openldap/ \
            | grep -oE 'libldap-2\.5-0_[^"]+0ubuntu0\.22\.04\.[0-9]+_amd64\.deb' \
            | sort -V | tail -1)
        if [[ -z "$LDAP25_DEB" ]]; then
            log_error "Could not find a libldap-2.5-0 22.04 deb in the Ubuntu archive"
            exit 1
        fi
        log_info "Fetching ${LDAP25_DEB}"
        wget -q "http://archive.ubuntu.com/ubuntu/pool/main/o/openldap/${LDAP25_DEB}"
        apt-get install -y -qq "./${LDAP25_DEB}"
    )
    rm -rf "$LDAP25_DIR"

    apt-get install -y -qq mssql-server

    if [[ -z "$SQL_PASSWORD" ]]; then
        SQL_PASSWORD=$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9@#' | head -c 20)
        log_warn "Generated SQL Server SA password: ${SQL_PASSWORD}"
        log_warn "Save this password! It will not be shown again."
    fi

    # Idempotency: if mssql-conf has already been run (e.g. on a re-run after a
    # failed install), mssql-server is already started and `mssql-conf setup`
    # refuses with "An instance of SQL Server is running. Please stop the SQL
    # Server service" — silently busy-looping. Detect prior config and skip.
    # NOTE: do NOT check /var/opt/mssql/mssql.conf — the apt postinst creates that
    # file with default [sqlagent] section before mssql-conf setup runs, so the
    # file existing is NOT proof of setup. The proof is master.mdf existing in /var/opt/mssql/data
    # (mssql-conf setup is what creates it).
    if [[ -f /var/opt/mssql/data/master.mdf ]]; then
        log_info "mssql-conf already configured (master.mdf present) — skipping setup"
    else
        # If mssql-server is running but unconfigured, stop it so mssql-conf can take over.
        if systemctl is-active --quiet mssql-server; then
            log_info "mssql-server is running pre-config — stopping for first-time setup"
            systemctl stop mssql-server
            sleep 2
        fi
        ACCEPT_EULA=Y MSSQL_SA_PASSWORD="$SQL_PASSWORD" MSSQL_PID="Express" \
            MSSQL_LCID=1033 MSSQL_COLLATION="SQL_Latin1_General_CP1_CI_AS" \
            /opt/mssql/bin/mssql-conf -n setup accept-eula
    fi
    systemctl enable mssql-server
    # Clear systemd's start-rate-limit. On a fresh install, apt-get's postinst
    # auto-starts mssql-server BEFORE mssql-conf accepts the EULA, so it crashes
    # 3 times in a row and systemd marks the unit as failed with "Start request
    # repeated too quickly". Our `start` then is silently ignored. Reset the
    # counter to let the now-configured server come up cleanly.
    systemctl reset-failed mssql-server 2>/dev/null || true
    systemctl start mssql-server

    # Verify it actually came up (Type=simple lies — systemctl returns 0 before crash)
    sleep 5
    if ! systemctl is-active --quiet mssql-server; then
        log_error "mssql-server failed to start:"
        journalctl -u mssql-server -n 30 --no-pager || true
        exit 1
    fi

    # Install command-line tools (mssql-tools18 replaces deprecated mssql-tools)
    ACCEPT_EULA=Y apt-get install -y -qq mssql-tools18 unixodbc-dev

    log_info "SQL Server installed and running"
}

# Install MySQL
install_mysql() {
    log_info "Installing MySQL..."
    apt-get install -y -qq mysql-server mysql-client
    systemctl enable mysql
    systemctl start mysql
    log_info "MySQL installed"
}

# Install PostgreSQL
install_postgresql() {
    log_info "Installing PostgreSQL..."
    apt-get install -y -qq postgresql postgresql-client
    systemctl enable postgresql
    systemctl start postgresql
    log_info "PostgreSQL installed"
}

# Install mail server
install_mail() {
    if [[ "$SKIP_MAIL" == "true" ]]; then
        log_warn "Skipping mail server installation (--skip-mail)"
        return
    fi

    log_info "Installing mail server (Postfix + Dovecot + Rspamd)..."

    # Postfix - set non-interactive defaults
    debconf-set-selections <<< "postfix postfix/mailname string $(hostname -f)"
    debconf-set-selections <<< "postfix postfix/main_mailer_type string 'Internet Site'"

    apt-get install -y -qq postfix dovecot-core dovecot-imapd dovecot-pop3d dovecot-lmtpd

    # imapsync — used by the Plesk migration importer (Phase 20). Idempotent if already installed.
    apt-get install -y -qq imapsync || log_warn "imapsync installation failed - install manually if Plesk migration is needed"

    # Rspamd
    apt-get install -y -qq lsb-release wget
    CODENAME=$(lsb_release -cs)
    wget -qO- https://rspamd.com/apt-stable/gpg.key | gpg --batch --no-tty --yes --dearmor -o /usr/share/keyrings/rspamd.gpg
    echo "deb [signed-by=/usr/share/keyrings/rspamd.gpg] http://rspamd.com/apt-stable/ $CODENAME main" > /etc/apt/sources.list.d/rspamd.list
    apt-get update -qq
    apt-get install -y -qq rspamd redis-server || log_warn "Rspamd installation failed - can be installed manually later"

    # Create vmail user for virtual mailboxes
    groupadd -g 5000 vmail 2>/dev/null || true
    useradd -g vmail -u 5000 -d /var/vmail -m -s /usr/sbin/nologin vmail 2>/dev/null || true

    # Deploy multi-port Postfix master.cf (25, 587, 465, 2525)
    log_info "Configuring multi-port SMTP (25/587/465/2525)..."
    cat > /etc/postfix/master.cf <<'MASTER_CF'
# HostPanel Postfix master.cf — Multi-port SMTP

# Standard SMTP (port 25) — receiving mail
smtp      inet  n       -       y       -       -       smtpd

# Submission (port 587) — client sending with STARTTLS
submission inet n       -       y       -       -       smtpd
  -o syslog_name=postfix/submission
  -o smtpd_tls_security_level=encrypt
  -o smtpd_sasl_auth_enable=yes
  -o smtpd_tls_auth_only=yes
  -o smtpd_reject_unlisted_recipient=no
  -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
  -o milter_macro_daemon_name=ORIGINATING

# SMTPS (port 465) — implicit TLS
smtps     inet  n       -       y       -       -       smtpd
  -o syslog_name=postfix/smtps
  -o smtpd_tls_wrappermode=yes
  -o smtpd_sasl_auth_enable=yes
  -o smtpd_reject_unlisted_recipient=no
  -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
  -o milter_macro_daemon_name=ORIGINATING

# Alternate submission (port 2525) — fallback if 587 blocked
2525      inet  n       -       y       -       -       smtpd
  -o syslog_name=postfix/alt-submission
  -o smtpd_tls_security_level=encrypt
  -o smtpd_sasl_auth_enable=yes
  -o smtpd_tls_auth_only=yes
  -o smtpd_reject_unlisted_recipient=no
  -o smtpd_recipient_restrictions=permit_sasl_authenticated,reject
  -o milter_macro_daemon_name=ORIGINATING

# Local delivery
pickup    unix  n       -       y       60      1       pickup
cleanup   unix  n       -       y       -       0       cleanup
qmgr      unix  n       -       n       300     1       qmgr
tlsmgr    unix  -       -       y       1000?   1       tlsmgr
rewrite   unix  -       -       y       -       -       trivial-rewrite
bounce    unix  -       -       y       -       0       bounce
defer     unix  -       -       y       -       0       bounce
trace     unix  -       -       y       -       0       bounce
verify    unix  -       -       y       -       1       verify
flush     unix  n       -       y       1000?   0       flush
proxymap  unix  -       -       n       -       -       proxymap
proxywrite unix -       -       n       -       1       proxymap
smtp      unix  -       -       y       -       -       smtp
relay     unix  -       -       y       -       -       smtp
showq     unix  n       -       y       -       -       showq
error     unix  -       -       y       -       -       error
retry     unix  -       -       y       -       -       error
discard   unix  -       -       y       -       -       discard
local     unix  -       n       n       -       -       local
virtual   unix  -       n       n       -       -       virtual
lmtp      unix  -       -       y       -       -       lmtp
anvil     unix  -       -       y       -       1       anvil
scache    unix  -       -       y       -       1       scache
postlog   unix-dgram n  -       n       -       1       postlogd
MASTER_CF

    # BUG-008: Postfix's submission/smtps need a SASL backend. Wire Dovecot's auth socket.
    # The socket lives inside Postfix's chroot at /var/spool/postfix/private/auth so postfix
    # can read it even with chroot enabled (default on Ubuntu).
    log_info "Configuring Dovecot SASL socket for Postfix submission..."
    mkdir -p /etc/dovecot/conf.d
    cat > /etc/dovecot/conf.d/10-master.conf <<'DOVECOT_MASTER'
service auth {
  unix_listener /var/spool/postfix/private/auth {
    mode = 0660
    user = postfix
    group = postfix
  }
  unix_listener auth-userdb {
    mode = 0600
    user = vmail
  }
  user = root
}

service auth-worker {
  user = vmail
}

service lmtp {
  unix_listener /var/spool/postfix/private/dovecot-lmtp {
    mode = 0600
    user = postfix
    group = postfix
  }
}
DOVECOT_MASTER

    # Point Postfix at Dovecot SASL backend
    postconf -e "smtpd_sasl_type=dovecot"
    postconf -e "smtpd_sasl_path=private/auth"
    postconf -e "smtpd_sasl_local_domain="
    postconf -e "broken_sasl_auth_clients=yes"
    # Use Dovecot LMTP for local delivery into virtual mailboxes
    postconf -e "virtual_transport=lmtp:unix:private/dovecot-lmtp"

    # Restart both — order matters (dovecot must be up before postfix starts using its socket)
    systemctl restart dovecot 2>&1 | tail -2 || log_warn "Dovecot restart failed"
    sleep 2
    systemctl restart postfix 2>&1 | tail -2 || log_warn "Postfix restart failed"
    log_info "Mail server installed with multi-port SMTP + Dovecot SASL"
}

# Install PowerDNS
install_powerdns() {
    log_info "Installing PowerDNS..."

    # Prevent conflict with systemd-resolved on port 53
    if systemctl is-active --quiet systemd-resolved; then
        log_info "Disabling systemd-resolved stub listener to free port 53..."
        mkdir -p /etc/systemd/resolved.conf.d
        cat > /etc/systemd/resolved.conf.d/no-stub.conf <<RESOLVED
[Resolve]
DNSStubListener=no
RESOLVED
        systemctl restart systemd-resolved
        # Point /etc/resolv.conf to upstream DNS
        ln -sf /run/systemd/resolve/resolv.conf /etc/resolv.conf
    fi

    apt-get install -y -qq pdns-server pdns-backend-mysql

    # Bind to server's actual IP addresses (not 0.0.0.0 which conflicts)
    local LAN_IP
    LAN_IP=$(hostname -I | awk '{print $1}')
    mkdir -p /etc/powerdns/pdns.d
    cat > /etc/powerdns/pdns.d/local-address.conf <<PDNS
local-address=${LAN_IP}
local-port=53
PDNS

    systemctl enable pdns
    systemctl restart pdns
    log_info "PowerDNS installed, listening on ${LAN_IP}:53"
}

# Install Node.js
install_nodejs() {
    log_info "Installing Node.js..."
    curl -fsSL https://deb.nodesource.com/setup_22.x | bash -
    apt-get install -y -qq nodejs
    log_info "Node.js installed: $(node --version)"
}

# Install Docker Engine
install_docker() {
    log_info "Installing Docker Engine..."
    curl -fsSL https://get.docker.com | bash -
    systemctl enable docker
    systemctl start docker
    # Allow www-data to use docker (for panel-managed containers)
    usermod -aG docker www-data 2>/dev/null || true
    log_info "Docker installed: $(docker --version)"
}

# Install WP-CLI
install_wpcli() {
    log_info "Installing WP-CLI..."
    curl -sO https://raw.githubusercontent.com/wp-cli/builds/gh-pages/phar/wp-cli.phar
    chmod +x wp-cli.phar
    mv wp-cli.phar /usr/local/bin/wp
    log_info "WP-CLI installed: $(wp --info --allow-root 2>/dev/null | head -1 || echo 'installed')"
}

# Start ClamAV freshclam update
start_clamav() {
    log_info "Updating ClamAV virus definitions..."
    systemctl stop clamav-freshclam 2>/dev/null || true
    freshclam --quiet 2>/dev/null || log_warn "ClamAV freshclam update failed - will retry on next run"
    systemctl enable clamav-freshclam clamav-daemon
    systemctl start clamav-freshclam
    systemctl start clamav-daemon 2>/dev/null || log_warn "ClamAV daemon may need virus defs first — will start after freshclam completes"
    log_info "ClamAV configured"
}

# Install Roundcube webmail
install_webmail() {
    log_info "Installing Roundcube webmail..."
    # Preseed dbconfig to prevent interactive prompts
    debconf-set-selections <<< "roundcube-core roundcube/dbconfig-install boolean false"
    debconf-set-selections <<< "roundcube-core roundcube/database-type select sqlite3"
    # Use --no-install-recommends to avoid pulling in apache2 (conflicts with nginx on :80)
    apt-get install -y -qq --no-install-recommends roundcube-core roundcube-plugins || log_warn "Roundcube installation failed - can be installed manually"
}

# Deploy HostPanel application
deploy_hostpanel() {
    log_info "Deploying HostPanel..."

    # LAN IP detected once and reused for: API appsettings (Agent:Tcp:BindAddress
    # so the local API knows what host to embed in the standby-side agent config),
    # AND for the agent's own appsettings.Production.json so TcpListenerWorker
    # binds the right NIC. Cached at function scope because the two writes are
    # ~150 lines apart.
    DEPLOY_LAN_IP=$(hostname -I | awk '{print $1}')

    # Create directories
    mkdir -p "$INSTALL_DIR/agent" "$INSTALL_DIR/wwwroot"
    mkdir -p "$CONFIG_DIR" "$DATA_DIR" "$LOG_DIR" "$BACKUP_DIR" "$SSL_DIR"
    mkdir -p /var/run/hostpanel

    # SSL dir needs www-data write access — the API (HostPanel.Modules.Ssl.SslService) runs
    # as www-data and writes the Let's Encrypt account key + per-domain cert files into $SSL_DIR.
    # 750 keeps key material unreadable to other users; nginx (also www-data) can still read.
    chown www-data:www-data "$SSL_DIR"
    chmod 750 "$SSL_DIR"

    # Generate agent token
    AGENT_TOKEN=$(openssl rand -base64 32)
    echo "$AGENT_TOKEN" > "${CONFIG_DIR}/agent.token"
    chmod 600 "${CONFIG_DIR}/agent.token"

    # If no --source was given, default to cloning from GitHub. This is the zero-touch
    # install path: the installer needs only the script + network access; everything else
    # is pulled from the public mgardner323/hostpanel repo (or whatever --git-repo points at).
    if [[ -z "$SOURCE_DIR" ]] && [[ -n "$GIT_REPO" ]]; then
        log_info "No --source given — cloning from $GIT_REPO (ref: $GIT_REF)"
        command -v git &>/dev/null || apt-get install -y -qq git
        SOURCE_DIR="/opt/hostpanel-src"
        rm -rf "$SOURCE_DIR"
        git clone --depth 1 --branch "$GIT_REF" "$GIT_REPO" "$SOURCE_DIR" \
            || { log_error "git clone failed: $GIT_REPO@$GIT_REF"; exit 1; }
        log_info "Cloned $(git -C "$SOURCE_DIR" rev-parse --short HEAD) into $SOURCE_DIR"
    fi

    # Build from source if --source provided (requires --with-sdk)
    if [[ -n "$SOURCE_DIR" ]]; then
        if [[ "$WITH_SDK" != "true" ]] && ! command -v dotnet &>/dev/null; then
            log_error "--source requires --with-sdk or .NET SDK already installed"
            exit 1
        fi

        # Repo layout: HostPanel.slnx lives under src/, not at the clone root.
        # Accept either layout — if .slnx is under src/, transparently descend.
        if [[ ! -f "$SOURCE_DIR/HostPanel.slnx" && -f "$SOURCE_DIR/src/HostPanel.slnx" ]]; then
            log_info "Detected repo layout — using $SOURCE_DIR/src as source root"
            SOURCE_DIR="$SOURCE_DIR/src"
        fi

        if [[ ! -f "$SOURCE_DIR/HostPanel.slnx" ]]; then
            log_error "Source directory does not contain HostPanel.slnx: $SOURCE_DIR"
            exit 1
        fi

        log_info "Building HostPanel from source ($SOURCE_DIR)..."

        # Build API
        dotnet publish "$SOURCE_DIR/HostPanel.Api/HostPanel.Api.csproj" \
            -c Release -o "$INSTALL_DIR" --nologo -v quiet
        log_info "API built and deployed to ${INSTALL_DIR}"

        # Build Agent
        dotnet publish "$SOURCE_DIR/HostPanel.Agent/HostPanel.Agent.csproj" \
            -c Release -o "$INSTALL_DIR/agent" --nologo -v quiet
        log_info "Agent built and deployed to ${INSTALL_DIR}/agent"

        # Build frontend
        if [[ -d "$SOURCE_DIR/hostpanel-ui" ]]; then
            log_info "Building frontend..."
            (
                cd "$SOURCE_DIR/hostpanel-ui"
                # Bake Sentry DSN into the production bundle if operator supplied one.
                # Empty .env.production means no Sentry; Sentry.init() in main.tsx no-ops.
                cat > .env.production <<EOF
VITE_SENTRY_DSN=${SENTRY_UI_DSN}
EOF
                npm ci --silent 2>/dev/null || npm install --silent
                npm run build
            )
            cp -r "$SOURCE_DIR/hostpanel-ui/dist/"* "$INSTALL_DIR/wwwroot/"
            log_info "Frontend built and deployed to ${INSTALL_DIR}/wwwroot"
        else
            log_warn "No hostpanel-ui directory found in source — frontend not deployed"
        fi
        # Copy version.json
        if [[ -f "$SOURCE_DIR/version.json" ]]; then
            cp "$SOURCE_DIR/version.json" "$INSTALL_DIR/version.json"
            log_info "version.json deployed"
        fi
    else
        log_warn "No --source specified. Binaries must be copied to ${INSTALL_DIR} manually."
    fi

    # Create appsettings for production (don't overwrite if it already exists)
    if [[ ! -f "${INSTALL_DIR}/appsettings.Production.json" ]]; then
        # Build connection string based on what's installed
        local CONN_STR="Server=localhost;Database=HostPanel;Trusted_Connection=True;TrustServerCertificate=True;"
        if [[ "$SKIP_MSSQL" == "false" && -n "$SQL_PASSWORD" ]]; then
            CONN_STR="Server=localhost;Database=HostPanel;User Id=sa;Password=${SQL_PASSWORD};TrustServerCertificate=True;"
        fi

        # Use the LAN IP detected at the top of deploy_hostpanel(). Bound to a specific NIC
        # so the listener cannot accidentally appear on a WAN-facing interface; defense-in-depth
        # with the UFW source-IP allowlist in configure_firewall.
        local LAN_IP="$DEPLOY_LAN_IP"

        cat > "${INSTALL_DIR}/appsettings.Production.json" <<SETTINGS
{
  "ConnectionStrings": {
    "DefaultConnection": "${CONN_STR}"
  },
  "Jwt": {
    "Key": "$(openssl rand -base64 48)",
    "Issuer": "HostPanel",
    "Audience": "HostPanel",
    "ExpiryHours": "24"
  },
  "Admin": {
    "Email": "${ADMIN_EMAIL}",
    "Password": "${ADMIN_PASSWORD}"
  },
  "Agent": {
    "SocketPath": "/var/run/hostpanel/agent.sock",
    "Token": "${AGENT_TOKEN}",
    "Tcp": {
      "Enabled": true,
      "Port": 50051,
      "BindAddress": "${LAN_IP}"
    }
  },
  "Ssl": {
    "CertPath": "${SSL_DIR}",
    "AccountEmail": "${ADMIN_EMAIL}",
    "UseStaging": false
  },
  "Databases": {
    "MsSql": {
      "ConnectionString": "Server=localhost;Database=master;User Id=sa;Password=${SQL_PASSWORD};TrustServerCertificate=True;"
    },
    "PostgreSql": {
      "Host": "localhost"
    }
  },
  "Sentry": {
    "Dsn": "${SENTRY_API_DSN}"
  }
}
SETTINGS
    fi

    # Ensure www-data owns the API directory (agent stays root-owned)
    chown -R www-data:www-data "$INSTALL_DIR"
    chown -R root:root "$INSTALL_DIR/agent"

    # Create systemd services
    cat > /etc/systemd/system/hostpanel-api.service <<SERVICE
[Unit]
Description=HostPanel Web API
After=network.target mssql-server.service
Wants=hostpanel-agent.service

[Service]
Type=notify
User=www-data
WorkingDirectory=${INSTALL_DIR}
ExecStart=/usr/bin/dotnet HostPanel.Api.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
SERVICE

    cat > /etc/systemd/system/hostpanel-agent.service <<SERVICE
[Unit]
Description=HostPanel System Agent
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=${INSTALL_DIR}/agent
ExecStart=/usr/bin/dotnet HostPanel.Agent.dll
Restart=always
RestartSec=10
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
SERVICE

    # Agent reads its config from ${INSTALL_DIR}/agent/, NOT from the API's
    # appsettings.Production.json one level up. Without this overlay, TcpListenerWorker
    # sees Agent:Tcp:Enabled=false (default) and exits without binding port 50051,
    # which silently breaks HA configure-failover from primary → standby agent.
    cat > "${INSTALL_DIR}/agent/appsettings.Production.json" <<AGENTSETTINGS
{
  "Agent": {
    "SocketPath": "/var/run/hostpanel/agent.sock",
    "TokenPath": "${CONFIG_DIR}/agent.token",
    "Tcp": {
      "Enabled": true,
      "Port": 50051,
      "BindAddress": "${DEPLOY_LAN_IP}"
    }
  }
}
AGENTSETTINGS

    # Ensure socket directory survives reboot
    cat > /etc/tmpfiles.d/hostpanel.conf <<TMPFILES
d /var/run/hostpanel 0755 root www-data -
TMPFILES
    systemd-tmpfiles --create

    # Nginx reverse proxy for panel UI
    cat > /etc/nginx/sites-available/hostpanel.conf <<'NGINX'
server {
    listen 8443 ssl;
    server_name _;

    # Self-signed cert for initial setup (replace with real cert)
    ssl_certificate /etc/hostpanel/ssl/panel.pem;
    ssl_certificate_key /etc/hostpanel/ssl/panel.key;

    # API
    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        # Use $http_host (includes :port) + X-Forwarded-Host so .NET ForwardedHeaders
        # middleware can rebuild the public URL — required for OIDC redirect_uri (Phase 21).
        proxy_set_header Host $http_host;
        proxy_set_header X-Forwarded-Host $http_host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Frontend
    location / {
        root /opt/hostpanel/wwwroot;
        try_files $uri $uri/ /index.html;
    }
}
NGINX

    ln -sf /etc/nginx/sites-available/hostpanel.conf /etc/nginx/sites-enabled/

    # Custom nginx log_format with $host appended so the bandwidth tracker
    # (system.getBandwidth agent method) can attribute bytes to a subscription.
    # Default ‘combined’ format omits the Host header, breaking per-customer billing.
    cat >/etc/nginx/conf.d/hostpanel-log-format.conf <<'NGINXLOG'
log_format hostpanel_combined '$remote_addr - $remote_user [$time_local] '
                              '"$request" $status $bytes_sent '
                              '"$http_referer" "$http_user_agent" host=$host';
access_log /var/log/nginx/access.log hostpanel_combined;
NGINXLOG

    # Generate self-signed certificate for panel
    openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
        -keyout "${SSL_DIR}/panel.key" \
        -out "${SSL_DIR}/panel.pem" \
        -subj "/CN=hostpanel/O=HostPanel" 2>/dev/null

    systemctl daemon-reload

    # Activate the new Nginx vhost
    nginx -t && systemctl reload nginx

    log_info "HostPanel deployed to ${INSTALL_DIR}"
}

# Build, start, and verify HostPanel services
start_hostpanel() {
    log_info "Starting HostPanel services..."

    # Only start if binaries exist
    if [[ ! -f "${INSTALL_DIR}/agent/HostPanel.Agent.dll" ]]; then
        log_warn "Agent binary not found at ${INSTALL_DIR}/agent/HostPanel.Agent.dll — skipping service start"
        return
    fi

    # Start agent first (API depends on it)
    systemctl enable hostpanel-agent
    systemctl start hostpanel-agent
    sleep 2
    if ! systemctl is-active --quiet hostpanel-agent; then
        log_error "hostpanel-agent failed to start:"
        journalctl -u hostpanel-agent -n 20 --no-pager || true
        log_warn "Continuing — agent can be started manually later"
    else
        log_info "hostpanel-agent is running"
    fi

    if [[ ! -f "${INSTALL_DIR}/HostPanel.Api.dll" ]]; then
        log_warn "API binary not found at ${INSTALL_DIR}/HostPanel.Api.dll — skipping API start"
        return
    fi

    systemctl enable hostpanel-api
    systemctl start hostpanel-api
    sleep 3
    if ! systemctl is-active --quiet hostpanel-api; then
        log_error "hostpanel-api failed to start:"
        journalctl -u hostpanel-api -n 20 --no-pager || true
        log_warn "Continuing — API can be started manually later"
    else
        log_info "hostpanel-api is running"
    fi

    # Quick health check
    sleep 2
    if curl -sk --max-time 5 https://localhost:8443/api/v1/server/stats >/dev/null 2>&1; then
        log_info "HostPanel API responding on :8443"
    else
        log_warn "API not yet responding on :8443 — may need a moment to run migrations on first start"
    fi
}

# Configure firewall
configure_sudoers() {
    log_info "Configuring sudoers for HostPanel..."
    # Allow the API (www-data) to run psql/createdb as the postgres OS user
    # so PostgreSQL provisioning works without a superuser password.
    echo 'www-data ALL=(postgres) NOPASSWD: /usr/bin/psql,/usr/bin/createdb' \
        > /etc/sudoers.d/hostpanel-pg
    chmod 440 /etc/sudoers.d/hostpanel-pg

    # BUG-009: Allow API (www-data) to run mysql as root, matching the PostgreSQL pattern.
    # Ubuntu 24.04 installs MySQL root with auth_socket (peer) — no password works, only sudo.
    echo 'www-data ALL=(root) NOPASSWD: /usr/bin/mysql,/usr/bin/mysqldump' \
        > /etc/sudoers.d/hostpanel-mysql
    chmod 440 /etc/sudoers.d/hostpanel-mysql

    # Allow the API (www-data) to drop privileges to any unix user when invoking
    # the Claude Code CLI. ClaudeController validates the tenant username against
    # ^[a-z0-9_-]+$ before passing it to sudo, so this is scoped to the single binary.
    # The --preserve-env flag in the controller carries ANTHROPIC_API_KEY through.
    cat > /etc/sudoers.d/hostpanel-claude <<'EOF'
Defaults!/usr/local/bin/claude env_keep += "ANTHROPIC_API_KEY"
www-data ALL=(ALL:ALL) NOPASSWD: /usr/local/bin/claude
EOF
    chmod 440 /etc/sudoers.d/hostpanel-claude

    log_info "Sudoers configured"
}

install_vsftpd() {
    log_info "Installing and configuring vsftpd..."
    apt-get install -y -qq vsftpd
    cat > /etc/vsftpd.conf <<'VSFCONF'
listen=NO
listen_ipv6=YES
anonymous_enable=NO
local_enable=YES
write_enable=YES
local_umask=022
dirmessage_enable=YES
use_localtime=YES
xferlog_enable=YES
connect_from_port_20=YES
chroot_local_user=YES
allow_writeable_chroot=YES
secure_chroot_dir=/var/run/vsftpd/empty
pam_service_name=vsftpd
rsa_cert_file=/etc/ssl/certs/ssl-cert-snakeoil.pem
rsa_private_key_file=/etc/ssl/private/ssl-cert-snakeoil.key
ssl_enable=NO
pasv_enable=YES
pasv_min_port=40000
pasv_max_port=40100
VSFCONF
    # Allow nologin shell for FTP-only accounts
    grep -q '/usr/sbin/nologin' /etc/shells || echo '/usr/sbin/nologin' >> /etc/shells
    systemctl enable vsftpd
    systemctl restart vsftpd
    log_info "vsftpd configured and started"
}

configure_firewall() {
    log_info "Configuring firewall..."
    ufw default deny incoming
    ufw default allow outgoing
    ufw allow 22/tcp     # SSH
    ufw allow 21/tcp     # FTP
    ufw allow 40000:40100/tcp  # FTP passive
    ufw allow 80/tcp     # HTTP
    ufw allow 443/tcp    # HTTPS
    ufw allow 8443/tcp   # HostPanel UI
    ufw allow 25/tcp     # SMTP
    ufw allow 587/tcp    # Submission
    ufw allow 465/tcp    # SMTPS
    ufw allow 2525/tcp   # Alt submission
    ufw allow 993/tcp    # IMAPS
    ufw allow 995/tcp    # POP3S
    ufw allow 53/tcp     # DNS
    ufw allow 53/udp     # DNS

    # HostPanel cross-host agent TCP listener — restricted to LAN sources so a
    # leaked agent token cannot be exercised from outside the trusted subnet.
    # CIDR defaults to the network attached to the default-route NIC; that
    # filters out container bridges (docker0 is also scope-link but on a
    # different NIC, so it must not match). --ha-allow-cidr overrides.
    local _ha_cidr="$HA_ALLOW_CIDR"
    if [[ -z "$_ha_cidr" ]]; then
        local _lan_if
        _lan_if=$(ip route show default | awk '{print $5; exit}')
        if [[ -n "$_lan_if" ]]; then
            _ha_cidr=$(ip -4 route show dev "$_lan_if" | awk '/scope link/ {print $1; exit}')
        fi
    fi
    if [[ -n "$_ha_cidr" ]]; then
        ufw allow from "$_ha_cidr" to any port 50051 proto tcp comment "HostPanel HA agent"
        log_info "Firewall: HA agent port 50051 allowed from ${_ha_cidr}"
    else
        log_warn "Firewall: could not auto-detect LAN CIDR for HA agent port 50051 — pass --ha-allow-cidr to enable cross-host HA"
    fi

    ufw --force enable
    log_info "Firewall configured and enabled"
}

# Main
main() {
    echo ""
    echo "================================================"
    echo "  HostPanel Installer v${HOSTPANEL_VERSION}"
    echo "  Web Hosting Control Panel"
    echo "================================================"
    echo ""

    preflight_checks
    install_base
    install_dotnet
    install_nginx
    install_php
    install_mssql
    install_mysql
    install_postgresql
    install_mail
    install_powerdns
    install_nodejs
    install_docker
    install_wpcli
    install_vsftpd
    start_clamav
    install_webmail
    deploy_hostpanel
    configure_sudoers
    configure_firewall
    start_hostpanel

    # Re-enable apt background services (disabled at start to avoid dpkg-lock races).
    for unit in unattended-upgrades.service apt-daily.service apt-daily-upgrade.service \
                apt-daily.timer apt-daily-upgrade.timer; do
        systemctl unmask "$unit" 2>/dev/null || true
    done
    systemctl enable --now unattended-upgrades.service apt-daily.timer apt-daily-upgrade.timer 2>/dev/null || true

    local PANEL_IP
    PANEL_IP=$(hostname -I | awk '{print $1}')

    echo ""
    echo "================================================"
    echo "  HostPanel Installation Complete!"
    echo "================================================"
    echo ""
    echo "  Panel URL:    https://${PANEL_IP}:8443"
    if [[ -n "${ADMIN_EMAIL}" && -n "${ADMIN_PASSWORD}" ]]; then
        echo "  Admin Email:  ${ADMIN_EMAIL}"
        echo "  Admin Pass:   ${ADMIN_PASSWORD}"
    else
        echo "  Admin Email:  (not seeded — open the panel URL to create one)"
        echo ""
        echo "  → First-time setup: visit https://${PANEL_IP}:8443/ in a browser."
        echo "    The panel will prompt you to create the admin account."
    fi
    echo ""
    if [[ "$SKIP_MSSQL" == "false" && -n "$SQL_PASSWORD" ]]; then
        echo "  SQL Server SA Password: ${SQL_PASSWORD}"
        echo "  (Save this - it won't be shown again)"
        echo ""
    fi
    if [[ -n "$SOURCE_DIR" ]]; then
        echo "  Built from source and deployed. Services are running."
    else
        echo "  No --source provided. To complete setup:"
        echo "    1. Copy API binaries to ${INSTALL_DIR}/"
        echo "    2. Copy Agent binaries to ${INSTALL_DIR}/agent/"
        echo "    3. Copy frontend dist to ${INSTALL_DIR}/wwwroot/"
        echo "    4. Run: systemctl start hostpanel-agent hostpanel-api"
    fi
    echo ""
    echo "================================================"
}

main "$@"
