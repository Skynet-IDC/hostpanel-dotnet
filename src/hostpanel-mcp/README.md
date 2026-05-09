# HostPanel MCP Server

MCP (Model Context Protocol) server that enables AI tools like Claude Code and Cursor to manage your HostPanel hosting platform.

## Setup

1. Install dependencies:
   ```bash
   cd src/hostpanel-mcp
   npm install
   ```

2. Set environment variables:
   ```bash
   export HOSTPANEL_API_URL=https://your-server:8443/api/v1
   export HOSTPANEL_API_KEY=hp_your_api_key_here
   ```

3. Add to Claude Code config (`~/.claude/settings.json`):
   ```json
   {
     "mcpServers": {
       "hostpanel": {
         "command": "node",
         "args": ["/path/to/hostpanel-mcp/index.js"],
         "env": {
           "HOSTPANEL_API_URL": "https://your-server:8443/api/v1",
           "HOSTPANEL_API_KEY": "hp_your_api_key_here"
         }
       }
     }
   }
   ```

## Available Tools

- `list_domains` / `create_domain` / `delete_domain`
- `list_subscriptions` / `create_subscription`
- `get_server_stats`
- `list_dns_records` / `add_dns_record`
- `issue_ssl`
- `list_mailboxes` / `create_mailbox`
- `create_database` / `list_databases`
- `list_files`
- `create_backup`
- `list_customers`
- `get_invoices`
