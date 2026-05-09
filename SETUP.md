# HostPanel - Proxmox Server Setup Guide

Instructions for Claude Code to provision and configure a Proxmox VM running HostPanel.

## Prerequisites

- Proxmox VE host accessible via API or SSH
- Ubuntu 24.04 LTS ISO available in Proxmox storage
- At least one public IP or NAT port forwarding for the VM
- DNS records pointing to the server IP (optional for initial testing)

---

## Step 1: Create the Proxmox VM

```bash
# SSH into your Proxmox host first
# Create a VM with 4GB RAM, 2 vCPUs, 50GB disk

qm create 200 \
  --name hostpanel \
  --memory 4096 \
  --cores 2 \
  --sockets 1 \
  --cpu host \
  --net0 virtio,bridge=vmbr0 \
  --scsihw virtio-scsi-pci \
  --scsi0 local-lvm:50 \
  --ide2 local:iso/ubuntu-24.04-live-server-amd64.iso,media=cdrom \
  --boot order=ide2 \
  --ostype l26 \
  --agent enabled=1

# Start the VM
qm start 200
```

After Ubuntu installs:
- Set a static IP or note the DHCP address
- Enable SSH: `sudo apt install openssh-server`
- Note the IP address: `ip addr show`

---

## Step 2: SSH Into the VM and Run Installer

```bash
# From your laptop / Claude Code session
ssh root@<VM_IP>

# Download the installer (or copy from your dev machine)
# Option A: Copy from dev machine
scp installer/hostpanel-install.sh root@<VM_IP>:/root/

# Option B: If hosted in a git repo
git clone <your-repo> /tmp/hostpanel
cp /tmp/hostpanel/installer/hostpanel-install.sh /root/
```

### Run the installer

```bash
chmod +x /root/hostpanel-install.sh

# Full install with all services
/root/hostpanel-install.sh \
  --sql-password "YourSecureP@ssw0rd123" \
  --admin-email "admin@yourdomain.com" \
  --admin-password "YourAdmin@Pass1234"

# Or skip services you don't need yet
/root/hostpanel-install.sh \
  --sql-password "YourSecureP@ssw0rd123" \
  --admin-email "admin@yourdomain.com" \
  --skip-mail
```

The installer will:
1. Install .NET 10 runtime
2. Install Nginx + PHP 8.1/8.2/8.3
3. Install SQL Server Express (free)
4. Install MySQL + PostgreSQL
5. Install Postfix + Dovecot + Rspamd (unless --skip-mail)
6. Install PowerDNS
7. Install Node.js 22
8. Configure UFW firewall
9. Create systemd services for HostPanel
10. Generate self-signed SSL cert for panel

---

## Step 3: Build and Deploy HostPanel

On your **dev machine** (Windows), build the release:

```bash
cd E:/Projects/pleskreplacement/src

# Build .NET backend
dotnet publish HostPanel.Api -c Release -o ./publish/api
dotnet publish HostPanel.Agent -c Release -o ./publish/agent

# Build React frontend
cd hostpanel-ui
npm run build
```

Copy to the server:

```bash
# Copy API and Agent binaries
scp -r src/publish/api/* root@<VM_IP>:/opt/hostpanel/
scp -r src/publish/agent/* root@<VM_IP>:/opt/hostpanel/agent/

# Copy frontend build
scp -r src/hostpanel-ui/dist/* root@<VM_IP>:/opt/hostpanel/wwwroot/

# Copy production config (edit passwords first!)
scp src/HostPanel.Api/appsettings.json root@<VM_IP>:/opt/hostpanel/appsettings.json
```

---

## Step 4: Configure Production Settings

SSH into the VM and edit the config:

```bash
ssh root@<VM_IP>
nano /opt/hostpanel/appsettings.Production.json
```

**Critical settings to change:**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=HostPanel;User Id=sa;Password=YOUR_SQL_PASSWORD;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "GENERATE-A-64-CHAR-RANDOM-STRING-HERE-USE-openssl-rand-base64-48"
  },
  "Admin": {
    "Email": "your-real-email@domain.com",
    "Password": "YourSecureAdminPassword123!"
  },
  "Agent": {
    "Token": "COPY-FROM-/etc/hostpanel/agent.token"
  },
  "Ssl": {
    "UseStaging": false,
    "AccountEmail": "your-email@domain.com"
  },
  "Billing": {
    "PayPal": {
      "ClientId": "YOUR_PAYPAL_CLIENT_ID",
      "ClientSecret": "YOUR_PAYPAL_SECRET",
      "WebhookId": "YOUR_PAYPAL_WEBHOOK_ID",
      "UseSandbox": false
    },
    "Stripe": {
      "SecretKey": "sk_live_...",
      "WebhookSecret": "whsec_..."
    }
  },
  "Dns": {
    "Cloudflare": { "ApiToken": "YOUR_CF_TOKEN" }
  },
  "Sso": {
    "Authentik": {
      "Authority": "https://authentik.yourdomain.com/application/o/hostpanel/",
      "ClientId": "YOUR_AUTHENTIK_CLIENT_ID",
      "ClientSecret": "YOUR_AUTHENTIK_SECRET"
    }
  }
}
```

Generate a secure JWT key:
```bash
openssl rand -base64 48
```

Read the agent token:
```bash
cat /etc/hostpanel/agent.token
```

---

## Step 5: Create the Database

```bash
# Connect to SQL Server
/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YOUR_SQL_PASSWORD' -Q "CREATE DATABASE HostPanel"
```

Or let the app create it on first run (EF Core auto-migration is enabled).

---

## Step 6: Set Up Systemd Services

The installer already created these, but if you need to adjust:

```bash
# API service (runs as www-data, non-root)
cat /etc/systemd/system/hostpanel-api.service

# Agent service (runs as root for system operations)
cat /etc/systemd/system/hostpanel-agent.service

# Start both
systemctl daemon-reload
systemctl enable hostpanel-agent hostpanel-api
systemctl start hostpanel-agent
systemctl start hostpanel-api

# Check status
systemctl status hostpanel-agent
systemctl status hostpanel-api

# View logs
journalctl -u hostpanel-api -f
journalctl -u hostpanel-agent -f
```

---

## Step 7: Configure Nginx for Panel

The installer creates `/etc/nginx/sites-available/hostpanel.conf`. Verify it:

```bash
cat /etc/nginx/sites-enabled/hostpanel.conf
nginx -t
systemctl reload nginx
```

The panel should now be accessible at `https://<VM_IP>:8443`

---

## Step 8: Verify Everything Works

```bash
# Install jq for JSON parsing in smoke tests below
apt install -y jq

# Test API is running
curl -k https://localhost:8443/api/v1/server/stats

# Test login
curl -k -X POST https://localhost:8443/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@yourdomain.com","password":"YourAdmin@Pass1234"}'

# Test Agent communication
journalctl -u hostpanel-agent --no-pager | tail -5

# Test database engines
/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YOUR_SQL_PASSWORD' -Q "SELECT 1"
mysql -e "SELECT 1"
sudo -u postgres psql -c "SELECT 1"

# Test mail (if installed)
systemctl status postfix dovecot

# Test DNS
systemctl status pdns

# Test firewall
ufw status

# --- SMOKE TESTS (from code review) ---

# Test API key authentication (create key first via JWT, then use it)
TOKEN=$(curl -sk -X POST https://localhost:8443/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@yourdomain.com","password":"YourAdmin@Pass1234"}' | jq -r .token)

API_KEY=$(curl -sk -X POST https://localhost:8443/api/v1/apikeys \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"smoke-test"}' | jq -r .key)

# Verify API key works
curl -sk https://localhost:8443/api/v1/auth/me \
  -H "Authorization: Bearer $API_KEY"
# Should return user info (same as JWT but via hp_ key)

# Verify JWT still works alongside API key
curl -sk https://localhost:8443/api/v1/auth/me \
  -H "Authorization: Bearer $TOKEN"

# Test PayPal webhook (staging) - send a fake event and verify it's rejected
curl -sk -X POST https://localhost:8443/api/v1/billing/webhooks/paypal \
  -H "Content-Type: application/json" \
  -d '{"event_type":"INVOICING.INVOICE.PAID","resource":{"id":"fake"}}'
# Should return 401 (no valid signature)
```

---

## Step 9: Set Up Let's Encrypt (Real SSL)

Once DNS is pointing to your server:

```bash
# Install certbot
apt install certbot python3-certbot-nginx

# Get a real certificate for the panel
certbot --nginx -d panel.yourdomain.com

# The API will handle per-domain certificates automatically
```

---

## Step 10: Configure External Services

### PayPal (for billing)
1. Go to https://developer.paypal.com/dashboard/applications
2. Create an app, get Client ID and Secret
3. Set up webhook URL: `https://panel.yourdomain.com/api/v1/billing/webhooks/paypal`
4. Note the Webhook ID
5. Add all to `appsettings.Production.json`

### Stripe (for billing)
1. Go to https://dashboard.stripe.com/apikeys
2. Get Secret Key
3. Set up webhook at https://dashboard.stripe.com/webhooks
4. Endpoint URL: `https://panel.yourdomain.com/api/v1/billing/webhooks/stripe`
5. Events: `invoice.paid`
6. Get Webhook Signing Secret
7. Add to config

### Cloudflare (for DNS)
1. Go to https://dash.cloudflare.com/profile/api-tokens
2. Create token with Zone:DNS:Edit permissions
3. Add to config

### Authentik (for SSO, optional)
1. In Authentik admin, create an OAuth2/OpenID Provider
2. Set redirect URI: `https://panel.yourdomain.com/api/v1/sso/callback`
3. Create Application linked to the provider
4. Add Authority, Client ID, Client Secret to config

---

## Port Reference

| Port | Service | Protocol |
|------|---------|----------|
| 22 | SSH | TCP |
| 25 | SMTP (receive) | TCP |
| 53 | DNS | TCP/UDP |
| 80 | HTTP | TCP |
| 443 | HTTPS | TCP |
| 465 | SMTPS | TCP |
| 587 | SMTP Submission | TCP |
| 993 | IMAPS | TCP |
| 995 | POP3S | TCP |
| 2525 | Alt SMTP Submission | TCP |
| 8443 | HostPanel UI | TCP |

---

## Troubleshooting

### API won't start
```bash
journalctl -u hostpanel-api -n 50 --no-pager
# Common: SQL Server not running, wrong connection string, JWT key missing
systemctl status mssql-server
```

### Agent won't start
```bash
journalctl -u hostpanel-agent -n 50 --no-pager
# Common: socket path doesn't exist, permission issues
ls -la /var/run/hostpanel/
```

### Can't connect to panel
```bash
# Check Nginx
nginx -t
systemctl status nginx
# Check firewall
ufw status
# Check if API is listening
ss -tlnp | grep 5000
```

### Database migration fails
```bash
# Run migrations manually
cd /opt/hostpanel
dotnet HostPanel.Api.dll --migrate-only
# Or check SQL Server is running
systemctl status mssql-server
```

### Email not sending
```bash
# Check ports aren't blocked by cloud provider
telnet smtp.gmail.com 587
# If blocked, configure SMTP relay in HostPanel:
# Settings > Mail > SMTP Relay > Add SendGrid/SES/Mailgun
# This bypasses port 25 blocks
```

---

## MCP Server Setup (for Claude Code)

After the panel is running, set up the MCP server on your dev machine:

```bash
cd src/hostpanel-mcp
npm install
```

Add to `~/.claude/settings.json`:
```json
{
  "mcpServers": {
    "hostpanel": {
      "command": "node",
      "args": ["E:/Projects/pleskreplacement/src/hostpanel-mcp/index.js"],
      "env": {
        "HOSTPANEL_API_URL": "https://<VM_IP>:8443/api/v1",
        "HOSTPANEL_API_KEY": "hp_your_api_key"
      }
    }
  }
}
```

Generate an API key first via the panel UI or API:
```bash
curl -k -X POST https://<VM_IP>:8443/api/v1/apikeys \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"name":"claude-code","scopes":"*"}'
```

---

## n8n Setup

1. Register webhook in HostPanel:
```bash
curl -k -X POST https://<VM_IP>:8443/api/v1/webhooks \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"url":"https://your-n8n-instance/webhook/hostpanel","events":"*","description":"n8n automation"}'
```

2. Install n8n custom node:
```bash
cd ~/.n8n/custom
npm install /path/to/n8n-nodes-hostpanel
# Restart n8n
```

3. Add HostPanel credentials in n8n UI (Settings > Credentials)
