import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";

const API_BASE = process.env.HOSTPANEL_API_URL || "http://localhost:5000/api/v1";
const API_KEY = process.env.HOSTPANEL_API_KEY || "";

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
];

// Register tool listing
server.setRequestHandler("tools/list", async () => ({
  tools: tools.map(({ name, description, inputSchema }) => ({
    name,
    description,
    inputSchema,
  })),
}));

// Register tool execution
server.setRequestHandler("tools/call", async (request) => {
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
