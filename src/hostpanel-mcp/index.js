import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { ListToolsRequestSchema, CallToolRequestSchema } from "@modelcontextprotocol/sdk/types.js";

const API_BASE = process.env.HOSTPANEL_API_URL || "http://localhost:5000/api/v1";
const API_KEY = process.env.HOSTPANEL_API_KEY || "";

// Self-signed cert support: set NODE_TLS_REJECT_UNAUTHORIZED=0 automatically
// when connecting to HTTPS with no explicit opt-out
if (API_BASE.startsWith("https://") && process.env.NODE_TLS_REJECT_UNAUTHORIZED === undefined) {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = "0";
}

async function apiCall(method, path, body = null) {
  const opts = {
    method,
    headers: {
      Authorization: `Bearer ${API_KEY}`,
      "Content-Type": "application/json",
    },
  };
  if (body) opts.body = JSON.stringify(body);

  const res = await fetch(`${API_BASE}${path}`, opts);
  const text = await res.text();
  try {
    return JSON.parse(text);
  } catch {
    return { raw: text, status: res.status };
  }
}

const server = new Server(
  { name: "hostpanel", version: "1.0.0" },
  { capabilities: { tools: {} } }
);

// Define tools
const tools = [
  {
    name: "list_domains",
    description: "List all domains managed by HostPanel",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/domains"),
  },
  {
    name: "create_domain",
    description: "Create a new domain under a subscription",
    inputSchema: {
      type: "object",
      properties: {
        subscriptionId: { type: "number", description: "Subscription ID" },
        name: { type: "string", description: "Domain name (e.g., example.com)" },
        phpVersion: { type: "string", description: "PHP version (default: 8.3)" },
      },
      required: ["subscriptionId", "name"],
    },
    handler: (args) =>
      apiCall("POST", "/domains", {
        subscriptionId: args.subscriptionId,
        name: args.name,
        phpVersion: args.phpVersion || "8.3",
      }),
  },
  {
    name: "delete_domain",
    description: "Delete a domain by ID",
    inputSchema: {
      type: "object",
      properties: { id: { type: "number" } },
      required: ["id"],
    },
    handler: (args) => apiCall("DELETE", `/domains/${args.id}`),
  },
  {
    name: "list_subscriptions",
    description: "List all hosting subscriptions",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/subscriptions"),
  },
  {
    name: "create_subscription",
    description: "Create a new hosting subscription",
    inputSchema: {
      type: "object",
      properties: {
        userId: { type: "string" },
        servicePlanId: { type: "number" },
        mainDomain: { type: "string" },
      },
      required: ["userId", "servicePlanId", "mainDomain"],
    },
    handler: (args) => apiCall("POST", "/subscriptions", args),
  },
  {
    name: "get_server_stats",
    description: "Get server statistics (CPU, RAM, disk, domains count)",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/server/stats"),
  },
  {
    name: "list_dns_records",
    description: "List DNS records for a domain",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" } },
      required: ["domainId"],
    },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/dns`),
  },
  {
    name: "add_dns_record",
    description: "Add a DNS record to a domain",
    inputSchema: {
      type: "object",
      properties: {
        domainId: { type: "number" },
        type: { type: "string", enum: ["A", "AAAA", "CNAME", "MX", "TXT", "SRV"] },
        name: { type: "string" },
        value: { type: "string" },
        ttl: { type: "number" },
        priority: { type: "number" },
      },
      required: ["domainId", "type", "name", "value"],
    },
    handler: (args) => {
      const { domainId, ...body } = args;
      return apiCall("POST", `/domains/${domainId}/dns`, body);
    },
  },
  {
    name: "issue_ssl",
    description: "Issue a Let's Encrypt SSL certificate for a domain",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" } },
      required: ["domainId"],
    },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/ssl/issue`),
  },
  {
    name: "list_mailboxes",
    description: "List email accounts for a domain",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" } },
      required: ["domainId"],
    },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/mailboxes`),
  },
  {
    name: "create_mailbox",
    description: "Create an email account",
    inputSchema: {
      type: "object",
      properties: {
        domainId: { type: "number" },
        localPart: { type: "string", description: "Part before @ (e.g., 'info')" },
        password: { type: "string" },
        quotaMb: { type: "number" },
      },
      required: ["domainId", "localPart", "password"],
    },
    handler: (args) => {
      const { domainId, ...body } = args;
      return apiCall("POST", `/domains/${domainId}/mailboxes`, body);
    },
  },
  {
    name: "create_database",
    description: "Create a hosted database (MSSQL, MySQL, MariaDB, PostgreSQL, or SQLite)",
    inputSchema: {
      type: "object",
      properties: {
        subscriptionId: { type: "number" },
        name: { type: "string" },
        type: { type: "string", enum: ["MsSql", "MySql", "MariaDb", "PostgreSql", "Sqlite"] },
      },
      required: ["subscriptionId", "name", "type"],
    },
    handler: (args) => apiCall("POST", "/databases", args),
  },
  {
    name: "list_databases",
    description: "List all hosted databases",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" } },
    },
    handler: (args) =>
      apiCall("GET", args.subscriptionId ? `/databases?subscriptionId=${args.subscriptionId}` : "/databases"),
  },
  {
    name: "list_files",
    description: "List files in a domain's document root",
    inputSchema: {
      type: "object",
      properties: {
        domainId: { type: "number" },
        path: { type: "string", description: "Directory path (default: /)" },
      },
      required: ["domainId"],
    },
    handler: (args) =>
      apiCall("GET", `/domains/${args.domainId}/files?path=${encodeURIComponent(args.path || "/")}`),
  },
  {
    name: "create_backup",
    description: "Create a backup for a subscription",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" } },
      required: ["subscriptionId"],
    },
    handler: (args) => apiCall("POST", `/subscriptions/${args.subscriptionId}/backups`),
  },
  {
    name: "list_customers",
    description: "List all customers/resellers",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/customers"),
  },
  {
    name: "get_invoices",
    description: "List invoices, optionally filtered by subscription",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" } },
    },
    handler: (args) =>
      apiCall("GET", args.subscriptionId ? `/billing/invoices?subscriptionId=${args.subscriptionId}` : "/billing/invoices"),
  },
  // --- Panel Management ---
  {
    name: "panel_version",
    description: "Get current panel version, build date, and .NET version",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/panel/version"),
  },
  {
    name: "check_update",
    description: "Check if a panel update is available",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/panel/check-update"),
  },
  {
    name: "update_panel",
    description: "Update the panel to the latest version. Full pipeline: backup, git pull, build, restart, health check. Auto-rollback on failure.",
    inputSchema: {
      type: "object",
      properties: {
        branch: { type: "string", description: "Git branch (default: main)" },
        backupDatabase: { type: "boolean", description: "Backup DB before update (default: true)" },
      },
    },
    handler: (args) => apiCall("POST", "/panel/update", args),
  },
  {
    name: "rollback_panel",
    description: "Rollback panel to previous version from backup",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("POST", "/panel/rollback"),
  },
  // --- Email ---
  {
    name: "create_email_alias",
    description: "Create an email alias/forwarding rule",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" }, source: { type: "string" }, destination: { type: "string" } },
      required: ["domainId", "source", "destination"],
    },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("POST", `/domains/${domainId}/aliases`, body); },
  },
  {
    name: "delete_mailbox",
    description: "Delete an email account",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/mailboxes/${args.id}`),
  },
  {
    name: "set_smtp_relay",
    description: "Configure SMTP relay for outbound email (bypasses port 25 blocks)",
    inputSchema: {
      type: "object",
      properties: { relayHost: { type: "string" }, relayPort: { type: "number" }, relayUsername: { type: "string" }, relayPassword: { type: "string" }, useTls: { type: "boolean" } },
      required: ["relayHost"],
    },
    handler: (args) => apiCall("PUT", "/mail/relay", args),
  },
  // --- Subscriptions ---
  {
    name: "delete_subscription",
    description: "Cancel and delete a subscription",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/subscriptions/${args.id}`),
  },
  // --- WordPress ---
  {
    name: "install_wordpress",
    description: "Install WordPress on a domain",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" }, adminUser: { type: "string" }, adminPassword: { type: "string" }, adminEmail: { type: "string" }, siteTitle: { type: "string" } },
      required: ["domainId"],
    },
    handler: (args) => apiCall("POST", "/wordpress/install", args),
  },
  {
    name: "create_staging",
    description: "Create a staging clone of a WordPress site",
    inputSchema: { type: "object", properties: { siteId: { type: "number" } }, required: ["siteId"] },
    handler: (args) => apiCall("POST", `/wordpress/${args.siteId}/staging`),
  },
  {
    name: "update_all_wordpress",
    description: "Update all WordPress sites (core, plugins, themes)",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("POST", "/wordpress/update-all"),
  },
  // --- Docker ---
  {
    name: "create_container",
    description: "Create and start a Docker container",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" }, name: { type: "string" }, image: { type: "string" }, ports: { type: "string" }, envVars: { type: "string" } },
      required: ["subscriptionId", "name", "image"],
    },
    handler: (args) => { const { subscriptionId, ...body } = args; return apiCall("POST", `/subscriptions/${subscriptionId}/docker`, body); },
  },
  // --- Git ---
  {
    name: "configure_git_deploy",
    description: "Configure Git deployment for a domain (GitHub/GitLab/Bitbucket)",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" }, repoUrl: { type: "string" }, branch: { type: "string" }, provider: { type: "string" }, autoDeploy: { type: "boolean" } },
      required: ["domainId", "repoUrl"],
    },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("POST", `/domains/${domainId}/git`, body); },
  },
  {
    name: "deploy_from_git",
    description: "Trigger a manual Git deployment for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/git/deploy`),
  },
  // --- FTP ---
  {
    name: "create_ftp_account",
    description: "Create an FTP account for a subscription",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" }, username: { type: "string" }, password: { type: "string" }, directory: { type: "string" }, quotaMb: { type: "number" } },
      required: ["subscriptionId", "username", "password"],
    },
    handler: (args) => { const { subscriptionId, ...body } = args; return apiCall("POST", `/subscriptions/${subscriptionId}/ftp`, body); },
  },
  // --- Security ---
  {
    name: "scan_domain",
    description: "Run ClamAV malware scan on a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/scan`),
  },
  {
    name: "fail2ban_status",
    description: "Get Fail2Ban status and banned IPs",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/security/fail2ban"),
  },
  // --- Firewall ---
  {
    name: "add_firewall_rule",
    description: "Add a UFW firewall rule",
    inputSchema: {
      type: "object",
      properties: { action: { type: "string", enum: ["allow", "deny"] }, port: { type: "number" }, protocol: { type: "string" }, fromIp: { type: "string" } },
      required: ["action", "port"],
    },
    handler: (args) => apiCall("POST", "/firewall/rules", args),
  },
  // --- Cron ---
  {
    name: "create_cron_job",
    description: "Create a cron job for a subscription",
    inputSchema: {
      type: "object",
      properties: { subscriptionId: { type: "number" }, command: { type: "string" }, schedule: { type: "string" }, description: { type: "string" } },
      required: ["subscriptionId", "command", "schedule"],
    },
    handler: (args) => { const { subscriptionId, ...body } = args; return apiCall("POST", `/subscriptions/${subscriptionId}/cron`, body); },
  },
  // --- Server Updates ---
  {
    name: "check_server_updates",
    description: "Check for available OS package updates",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("POST", "/server/updates/check"),
  },
  {
    name: "install_server_updates",
    description: "Install OS package updates",
    inputSchema: {
      type: "object",
      properties: { securityOnly: { type: "boolean" }, packages: { type: "array", items: { type: "string" } } },
    },
    handler: (args) => apiCall("POST", "/server/updates/install", args),
  },
  // --- Branding ---
  {
    name: "update_branding",
    description: "Update panel branding (name, colors, logo, support info)",
    inputSchema: {
      type: "object",
      properties: { panelName: { type: "string" }, primaryColor: { type: "string" }, sidebarColor: { type: "string" }, supportUrl: { type: "string" }, supportEmail: { type: "string" }, customCss: { type: "string" }, hidePoweredBy: { type: "boolean" } },
    },
    handler: (args) => apiCall("PUT", "/branding", args),
  },
  // --- Customer management ---
  {
    name: "create_customer",
    description: "Create a new customer account",
    inputSchema: {
      type: "object",
      properties: { email: { type: "string" }, password: { type: "string" }, firstName: { type: "string" }, lastName: { type: "string" }, companyName: { type: "string" }, role: { type: "string", enum: ["Customer", "Reseller"] } },
      required: ["email", "password", "firstName", "lastName"],
    },
    handler: (args) => apiCall("POST", "/customers", args),
  },
  // --- Webhooks ---
  {
    name: "create_webhook",
    description: "Register a webhook endpoint for event notifications (n8n, Zapier, etc)",
    inputSchema: {
      type: "object",
      properties: { url: { type: "string" }, events: { type: "string", description: "Comma-separated events or * for all" }, description: { type: "string" } },
      required: ["url"],
    },
    handler: (args) => apiCall("POST", "/webhooks", args),
  },
  // --- HA ---
  {
    name: "ha_status",
    description: "Get high availability cluster status",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/ha/sync-state"),
  },
  {
    name: "ha_failover",
    description: "Trigger failover to a standby server",
    inputSchema: { type: "object", properties: { promoteServerId: { type: "number" } }, required: ["promoteServerId"] },
    handler: (args) => apiCall("POST", "/ha/failover", args),
  },
  {
    name: "ha_failback",
    description: "Sync changes back and restore recovered server as primary",
    inputSchema: { type: "object", properties: { recoveredServerId: { type: "number" } }, required: ["recoveredServerId"] },
    handler: (args) => apiCall("POST", "/ha/failback", args),
  },
  // --- App Config ---
  {
    name: "configure_app",
    description: "Configure a domain's app runtime (Node.js, .NET, Python, PHP). Set build command, start command, restart method (systemd/passenger/pm2), env vars.",
    inputSchema: {
      type: "object",
      properties: {
        domainId: { type: "number" },
        runtime: { type: "string", enum: ["nodejs", "dotnet", "python", "php", "static"] },
        entryPoint: { type: "string" }, port: { type: "number" },
        restartMethod: { type: "string", enum: ["systemd", "passenger", "pm2"] },
        buildCommand: { type: "string" }, startCommand: { type: "string" },
        environmentVariables: { type: "string", description: "JSON object of env vars" },
      },
      required: ["domainId"],
    },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("PUT", `/domains/${domainId}/app`, body); },
  },
  {
    name: "build_app",
    description: "Run the build command for a domain's app (npm run build, prisma migrate, etc)",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/app/build`),
  },
  {
    name: "restart_app",
    description: "Restart a domain's app (systemd, passenger touch, or pm2 restart)",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/app/restart`),
  },
  {
    name: "set_env_vars",
    description: "Set environment variables for a domain's app (.env file)",
    inputSchema: {
      type: "object",
      properties: { domainId: { type: "number" }, environmentVariables: { type: "string", description: "JSON object: {\"KEY\":\"value\"}" } },
      required: ["domainId", "environmentVariables"],
    },
    handler: (args) => apiCall("PUT", `/domains/${args.domainId}/app/env`, { environmentVariables: args.environmentVariables }),
  },
  // --- Plesk Migration ---
  {
    name: "migrate_from_plesk",
    description: "Import domains, email, and databases from a Plesk server via API",
    inputSchema: {
      type: "object",
      properties: { pleskHost: { type: "string" }, apiKey: { type: "string" }, targetUserId: { type: "string" }, servicePlanId: { type: "number" } },
      required: ["pleskHost", "apiKey", "targetUserId", "servicePlanId"],
    },
    handler: (args) => apiCall("POST", "/migration/plesk/api", args),
  },
  // --- DNS full CRUD ---
  {
    name: "delete_dns_record",
    description: "Delete a DNS record by ID",
    inputSchema: { type: "object", properties: { recordId: { type: "number" } }, required: ["recordId"] },
    handler: (args) => apiCall("DELETE", `/dns/${args.recordId}`),
  },
  {
    name: "update_dns_record",
    description: "Update a DNS record value, TTL, or priority",
    inputSchema: { type: "object", properties: { recordId: { type: "number" }, value: { type: "string" }, ttl: { type: "number" }, priority: { type: "number" } }, required: ["recordId", "value"] },
    handler: (args) => { const { recordId, ...body } = args; return apiCall("PUT", `/dns/${recordId}`, body); },
  },
  // --- Database ---
  {
    name: "delete_database",
    description: "Delete a hosted database",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/databases/${args.id}`),
  },
  // --- Email extended ---
  {
    name: "delete_email_alias",
    description: "Delete an email alias",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/aliases/${args.id}`),
  },
  {
    name: "change_mailbox_password",
    description: "Change a mailbox password",
    inputSchema: { type: "object", properties: { id: { type: "number" }, newPassword: { type: "string" } }, required: ["id", "newPassword"] },
    handler: (args) => apiCall("PUT", `/mailboxes/${args.id}/password`, { newPassword: args.newPassword }),
  },
  {
    name: "set_email_catchall",
    description: "Set catch-all forwarding for a domain (empty forwardTo to disable)",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, forwardTo: { type: "string" } }, required: ["domainId"] },
    handler: (args) => apiCall("PUT", `/domains/${args.domainId}/catchall`, { forwardTo: args.forwardTo || null }),
  },
  // --- SSL extended ---
  {
    name: "toggle_ssl_autorenew",
    description: "Toggle SSL auto-renewal for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/ssl/toggle-auto-renew`),
  },
  // --- Docker extended ---
  {
    name: "start_container",
    description: "Start a Docker container",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, containerId: { type: "number" } }, required: ["subscriptionId", "containerId"] },
    handler: (args) => apiCall("POST", `/subscriptions/${args.subscriptionId}/docker/${args.containerId}/start`),
  },
  {
    name: "stop_container",
    description: "Stop a Docker container",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, containerId: { type: "number" } }, required: ["subscriptionId", "containerId"] },
    handler: (args) => apiCall("POST", `/subscriptions/${args.subscriptionId}/docker/${args.containerId}/stop`),
  },
  {
    name: "restart_container",
    description: "Restart a Docker container",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, containerId: { type: "number" } }, required: ["subscriptionId", "containerId"] },
    handler: (args) => apiCall("POST", `/subscriptions/${args.subscriptionId}/docker/${args.containerId}/restart`),
  },
  {
    name: "delete_container",
    description: "Delete a Docker container",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, containerId: { type: "number" } }, required: ["subscriptionId", "containerId"] },
    handler: (args) => apiCall("DELETE", `/subscriptions/${args.subscriptionId}/docker/${args.containerId}`),
  },
  {
    name: "list_containers",
    description: "List Docker containers for a subscription",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" } }, required: ["subscriptionId"] },
    handler: (args) => apiCall("GET", `/subscriptions/${args.subscriptionId}/docker`),
  },
  // --- Cron extended ---
  {
    name: "list_cron_jobs",
    description: "List cron jobs for a subscription",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" } }, required: ["subscriptionId"] },
    handler: (args) => apiCall("GET", `/subscriptions/${args.subscriptionId}/cron`),
  },
  {
    name: "delete_cron_job",
    description: "Delete a cron job",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, id: { type: "number" } }, required: ["subscriptionId", "id"] },
    handler: (args) => apiCall("DELETE", `/subscriptions/${args.subscriptionId}/cron/${args.id}`),
  },
  // --- IP Management ---
  {
    name: "list_ip_addresses",
    description: "List all server IP addresses in the pool",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/ip/addresses"),
  },
  {
    name: "add_ip_address",
    description: "Add an IP address to the server pool",
    inputSchema: { type: "object", properties: { address: { type: "string" }, isShared: { type: "boolean" }, note: { type: "string" } }, required: ["address"] },
    handler: (args) => apiCall("POST", "/ip/addresses", args),
  },
  {
    name: "remove_ip_address",
    description: "Remove an IP address from the pool",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/ip/addresses/${args.id}`),
  },
  {
    name: "block_ip",
    description: "Add an IP to the blacklist",
    inputSchema: { type: "object", properties: { address: { type: "string" }, reason: { type: "string" }, expiresInHours: { type: "number" } }, required: ["address"] },
    handler: (args) => apiCall("POST", "/ip/blacklist", args),
  },
  {
    name: "unblock_ip",
    description: "Remove an IP from the blacklist",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/ip/blacklist/${args.id}`),
  },
  {
    name: "list_ip_blacklist",
    description: "List all blocked IPs",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/ip/blacklist"),
  },
  // --- Domain settings ---
  {
    name: "set_hotlink_protection",
    description: "Enable/disable hotlink protection for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, enabled: { type: "boolean" }, allowedDomains: { type: "string" } }, required: ["domainId", "enabled"] },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("PUT", `/domains/${domainId}/settings/hotlink-protection`, body); },
  },
  {
    name: "set_ip_deny",
    description: "Set IP deny list for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, deniedIps: { type: "string", description: "Comma-separated IPs" } }, required: ["domainId", "deniedIps"] },
    handler: (args) => apiCall("PUT", `/domains/${args.domainId}/settings/ip-deny`, { deniedIps: args.deniedIps }),
  },
  {
    name: "password_protect_directory",
    description: "Add .htpasswd protection to a directory",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, directory: { type: "string" }, username: { type: "string" }, password: { type: "string" }, enabled: { type: "boolean" } }, required: ["domainId", "username", "password"] },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("PUT", `/domains/${domainId}/settings/password-protect`, body); },
  },
  // --- WordPress extended ---
  {
    name: "wp_security_scan",
    description: "Run a WordPress security scan",
    inputSchema: { type: "object", properties: { siteId: { type: "number" } }, required: ["siteId"] },
    handler: (args) => apiCall("POST", `/wordpress/${args.siteId}/security-scan`),
  },
  {
    name: "wp_push_staging",
    description: "Push staging site to production",
    inputSchema: { type: "object", properties: { siteId: { type: "number" } }, required: ["siteId"] },
    handler: (args) => apiCall("POST", `/wordpress/${args.siteId}/push-staging`),
  },
  {
    name: "wp_set_maintenance",
    description: "Enable/disable WordPress maintenance mode",
    inputSchema: { type: "object", properties: { siteId: { type: "number" }, enabled: { type: "boolean" } }, required: ["siteId", "enabled"] },
    handler: (args) => apiCall("PUT", `/wordpress/${args.siteId}/maintenance`, { enabled: args.enabled }),
  },
  // --- Audit log ---
  {
    name: "view_audit_log",
    description: "View audit log entries with optional filters",
    inputSchema: { type: "object", properties: { page: { type: "number" }, action: { type: "string" }, entityType: { type: "string" } } },
    handler: (args) => { const params = new URLSearchParams(); if (args.page) params.set("page", args.page); if (args.action) params.set("action", args.action); if (args.entityType) params.set("entityType", args.entityType); return apiCall("GET", `/audit?${params}`); },
  },
  // --- Analytics ---
  {
    name: "get_visitor_stats",
    description: "Get visitor statistics for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, period: { type: "string", enum: ["today", "week", "month"] } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/analytics/visitors?period=${args.period || "today"}`),
  },
  {
    name: "get_bandwidth_stats",
    description: "Get bandwidth usage for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, period: { type: "string" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/analytics/bandwidth?period=${args.period || "month"}`),
  },
  {
    name: "get_server_analytics",
    description: "Get server resource analytics (CPU, RAM, disk over time)",
    inputSchema: { type: "object", properties: { period: { type: "string" } } },
    handler: (args) => apiCall("GET", `/server/analytics?period=${args.period || "day"}`),
  },
  // --- App installer ---
  {
    name: "list_available_apps",
    description: "List available one-click apps (WordPress, Joomla, Nextcloud, etc)",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/apps/available`),
  },
  {
    name: "install_app",
    description: "Install a one-click app on a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, appId: { type: "string" }, adminUser: { type: "string" }, adminPassword: { type: "string" }, adminEmail: { type: "string" } }, required: ["domainId", "appId"] },
    handler: (args) => { const { domainId, ...body } = args; return apiCall("POST", `/domains/${domainId}/apps/install`, body); },
  },
  // --- HA extended ---
  {
    name: "ha_promote_permanent",
    description: "Permanently promote a server to primary (decommission old primary)",
    inputSchema: { type: "object", properties: { serverId: { type: "number" }, decommissionOldPrimary: { type: "boolean" } }, required: ["serverId"] },
    handler: (args) => apiCall("POST", "/ha/promote-permanent", args),
  },
  {
    name: "ha_configure_sync",
    description: "Configure HA sync settings (strategy, what to sync, auto-failback)",
    inputSchema: { type: "object", properties: { serverId: { type: "number" }, failoverStrategy: { type: "number", description: "0=DNS, 1=FloatingIP, 2=Cloudflare, 3=PerDomain" }, autoFailback: { type: "boolean" }, syncFiles: { type: "boolean" }, syncDatabases: { type: "boolean" }, syncEmail: { type: "boolean" }, floatingIp: { type: "string" }, cloudflareApiToken: { type: "string" }, cloudflareZoneId: { type: "string" } }, required: ["serverId"] },
    handler: (args) => { const { serverId, ...body } = args; return apiCall("PUT", `/ha/sync-state/${serverId}`, body); },
  },
  // --- Backups extended ---
  {
    name: "restore_backup",
    description: "Restore a backup by ID",
    inputSchema: { type: "object", properties: { backupId: { type: "number" } }, required: ["backupId"] },
    handler: (args) => apiCall("POST", `/backups/${args.backupId}/restore`),
  },
  // --- Customer management extended ---
  {
    name: "toggle_customer_active",
    description: "Activate or deactivate a customer account",
    inputSchema: { type: "object", properties: { id: { type: "string" } }, required: ["id"] },
    handler: (args) => apiCall("PUT", `/customers/${args.id}/toggle-active`),
  },
  // --- Webhooks extended ---
  {
    name: "list_webhooks",
    description: "List all registered webhook endpoints",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/webhooks"),
  },
  {
    name: "delete_webhook",
    description: "Delete a webhook endpoint",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/webhooks/${args.id}`),
  },
  {
    name: "test_webhook",
    description: "Send a test event to a webhook endpoint",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("POST", `/webhooks/${args.id}/test`),
  },
  // --- Files extended ---
  {
    name: "read_file",
    description: "Read a file's contents from a domain's document root",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, path: { type: "string" } }, required: ["domainId", "path"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/files/read?path=${encodeURIComponent(args.path)}`),
  },
  {
    name: "save_file",
    description: "Save/create a file in a domain's document root",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, path: { type: "string" }, content: { type: "string" } }, required: ["domainId", "path", "content"] },
    handler: (args) => apiCall("PUT", `/domains/${args.domainId}/files`, { path: args.path, content: args.content }),
  },
  {
    name: "delete_file",
    description: "Delete a file or directory from a domain's document root",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, path: { type: "string" } }, required: ["domainId", "path"] },
    handler: (args) => apiCall("DELETE", `/domains/${args.domainId}/files?path=${encodeURIComponent(args.path)}`),
  },
  {
    name: "create_directory",
    description: "Create a directory in a domain's document root",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, path: { type: "string" } }, required: ["domainId", "path"] },
    handler: (args) => apiCall("POST", `/domains/${args.domainId}/files/mkdir`, { path: args.path }),
  },
  // --- Service Plans ---
  {
    name: "list_service_plans",
    description: "List all hosting service plans",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/serviceplans"),
  },
  {
    name: "create_service_plan",
    description: "Create a new hosting service plan",
    inputSchema: { type: "object", properties: { name: { type: "string" }, diskQuotaMb: { type: "number" }, bandwidthLimitMb: { type: "number" }, maxDomains: { type: "number" }, maxEmailAccounts: { type: "number" }, maxDatabases: { type: "number" }, pricePerPeriod: { type: "number" } }, required: ["name", "diskQuotaMb", "maxDomains", "pricePerPeriod"] },
    handler: (args) => apiCall("POST", "/serviceplans", args),
  },
  {
    name: "update_service_plan",
    description: "Update a service plan",
    inputSchema: { type: "object", properties: { id: { type: "number" }, name: { type: "string" }, diskQuotaMb: { type: "number" }, bandwidthLimitMb: { type: "number" }, maxDomains: { type: "number" }, maxEmailAccounts: { type: "number" }, maxDatabases: { type: "number" }, pricePerPeriod: { type: "number" } }, required: ["id"] },
    handler: (args) => { const { id, ...body } = args; return apiCall("PUT", `/serviceplans/${id}`, body); },
  },
  {
    name: "delete_service_plan",
    description: "Deactivate a service plan",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("DELETE", `/serviceplans/${args.id}`),
  },
  // --- FTP extended ---
  {
    name: "list_ftp_accounts",
    description: "List FTP accounts for a subscription",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" } }, required: ["subscriptionId"] },
    handler: (args) => apiCall("GET", `/subscriptions/${args.subscriptionId}/ftp`),
  },
  {
    name: "delete_ftp_account",
    description: "Delete an FTP account",
    inputSchema: { type: "object", properties: { subscriptionId: { type: "number" }, id: { type: "number" } }, required: ["subscriptionId", "id"] },
    handler: (args) => apiCall("DELETE", `/subscriptions/${args.subscriptionId}/ftp/${args.id}`),
  },
  // --- Servers ---
  {
    name: "list_servers",
    description: "List all managed servers",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/servers"),
  },
  {
    name: "add_server",
    description: "Add a remote server to the cluster",
    inputSchema: { type: "object", properties: { hostname: { type: "string" }, ipAddress: { type: "string" }, agentPort: { type: "number" }, agentToken: { type: "string" }, serverGroup: { type: "string" } }, required: ["hostname", "ipAddress"] },
    handler: (args) => apiCall("POST", "/servers", args),
  },
  {
    name: "server_health_check",
    description: "Run a health check on a server",
    inputSchema: { type: "object", properties: { id: { type: "number" } }, required: ["id"] },
    handler: (args) => apiCall("POST", `/servers/${args.id}/health-check`),
  },
  // --- 2FA ---
  {
    name: "setup_2fa",
    description: "Set up two-factor authentication (returns QR code URL and recovery codes)",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("POST", "/auth/2fa/setup"),
  },
  {
    name: "get_2fa_status",
    description: "Check if 2FA is enabled for current user",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("GET", "/auth/2fa/status"),
  },
  // --- Logs ---
  {
    name: "get_access_log",
    description: "Get Nginx access log for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, lines: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/logs/access?lines=${args.lines || 100}`),
  },
  {
    name: "get_error_log",
    description: "Get Nginx error log for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" }, lines: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/logs/error?lines=${args.lines || 100}`),
  },
  // --- Domain update ---
  {
    name: "update_domain",
    description: "Update domain settings (PHP version, SSL toggle)",
    inputSchema: { type: "object", properties: { id: { type: "number" }, phpVersion: { type: "string" }, sslEnabled: { type: "boolean" } }, required: ["id"] },
    handler: (args) => { const { id, ...body } = args; return apiCall("PUT", `/domains/${id}`, body); },
  },
  // --- Invoice PDF ---
  {
    name: "download_invoice",
    description: "Get an invoice as HTML/PDF",
    inputSchema: { type: "object", properties: { invoiceId: { type: "number" } }, required: ["invoiceId"] },
    handler: (args) => apiCall("GET", `/billing/invoices/${args.invoiceId}/pdf`),
  },
  // --- App get config ---
  {
    name: "get_app_config",
    description: "Get the app configuration for a domain (runtime, ports, build command, env vars)",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/app`),
  },
  // --- Git get config ---
  {
    name: "get_git_config",
    description: "Get Git deployment configuration for a domain",
    inputSchema: { type: "object", properties: { domainId: { type: "number" } }, required: ["domainId"] },
    handler: (args) => apiCall("GET", `/domains/${args.domainId}/git`),
  },
  // --- Scan server ---
  {
    name: "scan_server",
    description: "Run a full server malware scan",
    inputSchema: { type: "object", properties: {} },
    handler: () => apiCall("POST", "/security/scan-server"),
  },
  // --- Fail2ban unban ---
  {
    name: "fail2ban_unban",
    description: "Unban an IP from a Fail2Ban jail",
    inputSchema: { type: "object", properties: { jail: { type: "string" }, ip: { type: "string" } }, required: ["jail", "ip"] },
    handler: (args) => apiCall("POST", "/security/fail2ban/unban", args),
  },
];

// Register tool listing
server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: tools.map(({ name, description, inputSchema }) => ({
    name,
    description,
    inputSchema,
  })),
}));

// Register tool execution
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const tool = tools.find((t) => t.name === request.params.name);
  if (!tool) {
    return { content: [{ type: "text", text: `Unknown tool: ${request.params.name}` }] };
  }

  try {
    const result = await tool.handler(request.params.arguments || {});
    return {
      content: [{ type: "text", text: JSON.stringify(result, null, 2) }],
    };
  } catch (error) {
    return {
      content: [{ type: "text", text: `Error: ${error.message}` }],
      isError: true,
    };
  }
});

// Start server
const transport = new StdioServerTransport();
await server.connect(transport);
