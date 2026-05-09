# HostPanel Test Suite

Three layers of automated testing for the HostPanel platform.

## Layer 1: Integration Tests (no server needed)

Uses WebApplicationFactory with InMemory database. Tests all API endpoints.

```bash
# Run all tests (63 tests)
cd src
dotnet test HostPanel.Tests/HostPanel.Tests.csproj

# Run only integration tests
dotnet test HostPanel.Tests/HostPanel.Tests.csproj --filter "Category=Integration"

# Run specific test class
dotnet test HostPanel.Tests/HostPanel.Tests.csproj --filter "FullyQualifiedName~AuthTests"
```

**What it covers:** Auth (login, JWT, API keys), all list endpoints, service plan CRUD, branding, 2FA status, webhooks, tenant isolation, file manager, reconciliation, Stripe webhook signatures, process runner timeout.

## Layer 2: Smoke Tests (runs against live server)

Curl-based tests against a deployed HostPanel instance. Requires `jq`.

```bash
# On the server (or via SSH)
apt install -y jq
chmod +x tests/smoke-test.sh

# Run with defaults (localhost:8443, default admin credentials)
./tests/smoke-test.sh

# Run against a specific server
./tests/smoke-test.sh https://panel.example.com admin@example.com "YourPassword123!"
```

**What it covers:** Health endpoint, branding (public), SSO config, admin login, JWT auth, API key auth, server stats, domains, subscriptions, service plans, databases, email ports, billing, customers, WordPress, webhooks, firewall, updates, audit log, IP management, webhook signature rejection.

## Layer 3: Agent Contract Tests (runs on the server)

Tests the Agent daemon's Unix socket JSON-RPC interface. Requires `socat` and root access.

```bash
# On the server
apt install -y socat
chmod +x tests/agent-test.sh

# Run (needs root for socket access)
sudo ./tests/agent-test.sh

# With custom socket/token
sudo ./tests/agent-test.sh /var/run/hostpanel/agent.sock "your-agent-token"
```

**What it covers:** Token authentication (reject invalid), method allowlist (reject unknown), system stats, Nginx reload, updates, firewall status, log reading (valid + blocked paths), backup (param validation), SSL renewal, PHP pool (param validation), cron (param validation), Git deploy (URL validation), security/Fail2Ban, analytics, Docker (param validation), app config, HA sync.

## Quick Full Test

```bash
# From dev machine — run all 3 layers
echo "=== Layer 1: Integration ===" && \
cd src && dotnet test HostPanel.Tests/ && cd .. && \
echo "=== Layer 2: Smoke ===" && \
ssh user@server 'bash /opt/hostpanel/tests/smoke-test.sh' && \
echo "=== Layer 3: Agent ===" && \
ssh user@server 'sudo bash /opt/hostpanel/tests/agent-test.sh'
```
