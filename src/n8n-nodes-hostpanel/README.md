# n8n-nodes-hostpanel

Custom n8n nodes for automating HostPanel web hosting management.

## Nodes

### HostPanel (Action Node)
Perform actions on your HostPanel server:
- **Domains**: List, Create, Delete
- **Subscriptions**: List, Create, Delete
- **Email**: List/Create/Delete mailboxes
- **Databases**: List, Create (MSSQL, MySQL, MariaDB, PostgreSQL, SQLite), Delete
- **DNS**: List/Add/Delete records
- **SSL**: Get status, Issue Let's Encrypt certificates
- **Backups**: List, Create, Restore
- **Customers**: List, Create
- **Server**: Get stats, Check for updates

### HostPanel Trigger (Trigger Node)
Listens for HostPanel webhook events:
- Subscription lifecycle (created, activated, suspended, cancelled)
- Domain events (created, deleted, SSL issued/expiring)
- Email/Database creation
- Billing events (invoice created/paid/overdue, payment received)
- Server alerts (health down, disk warning)
- Backup events (completed, failed)

## Setup

1. Install in your n8n instance:
   ```bash
   cd ~/.n8n/custom
   npm install /path/to/n8n-nodes-hostpanel
   ```

2. Add HostPanel credentials in n8n (Settings > Credentials > HostPanel API)

3. For triggers: Register the n8n webhook URL in HostPanel (Settings > Webhooks)

## Example Workflows

### Auto-provision on payment
1. HostPanel Trigger (event: payment.received)
2. HostPanel (create subscription)
3. HostPanel (issue SSL)
4. Send welcome email via Gmail/SMTP node

### Alert on server issues
1. HostPanel Trigger (event: server.health.down)
2. Slack node (send alert to #ops channel)

### Monthly reporting
1. Schedule Trigger (1st of month)
2. HostPanel (list subscriptions)
3. HostPanel (get invoices)
4. Google Sheets (append report)
