module.exports = {
  description: {
    displayName: 'HostPanel',
    name: 'hostPanel',
    icon: 'file:hostpanel.svg',
    group: ['transform'],
    version: 1,
    subtitle: '={{$parameter["operation"] + ": " + $parameter["resource"]}}',
    description: 'Manage your HostPanel web hosting platform',
    defaults: { name: 'HostPanel' },
    inputs: ['main'],
    outputs: ['main'],
    credentials: [{ name: 'hostPanelApi', required: true }],
    properties: [
      {
        displayName: 'Resource',
        name: 'resource',
        type: 'options',
        noDataExpression: true,
        options: [
          { name: 'Domain', value: 'domain' },
          { name: 'Subscription', value: 'subscription' },
          { name: 'Email', value: 'email' },
          { name: 'Database', value: 'database' },
          { name: 'DNS', value: 'dns' },
          { name: 'SSL', value: 'ssl' },
          { name: 'Backup', value: 'backup' },
          { name: 'Customer', value: 'customer' },
          { name: 'Server', value: 'server' },
        ],
        default: 'domain',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['domain'] } },
        options: [
          { name: 'List', value: 'list', action: 'List all domains' },
          { name: 'Create', value: 'create', action: 'Create a domain' },
          { name: 'Delete', value: 'delete', action: 'Delete a domain' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['subscription'] } },
        options: [
          { name: 'List', value: 'list', action: 'List all subscriptions' },
          { name: 'Create', value: 'create', action: 'Create a subscription' },
          { name: 'Delete', value: 'delete', action: 'Delete a subscription' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['email'] } },
        options: [
          { name: 'List Mailboxes', value: 'list', action: 'List mailboxes' },
          { name: 'Create Mailbox', value: 'create', action: 'Create a mailbox' },
          { name: 'Delete Mailbox', value: 'delete', action: 'Delete a mailbox' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['database'] } },
        options: [
          { name: 'List', value: 'list', action: 'List databases' },
          { name: 'Create', value: 'create', action: 'Create a database' },
          { name: 'Delete', value: 'delete', action: 'Delete a database' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['dns'] } },
        options: [
          { name: 'List Records', value: 'list', action: 'List DNS records' },
          { name: 'Add Record', value: 'create', action: 'Add a DNS record' },
          { name: 'Delete Record', value: 'delete', action: 'Delete a DNS record' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['ssl'] } },
        options: [
          { name: 'Get Status', value: 'list', action: 'Get SSL status' },
          { name: 'Issue Certificate', value: 'create', action: 'Issue SSL certificate' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['backup'] } },
        options: [
          { name: 'List', value: 'list', action: 'List backups' },
          { name: 'Create', value: 'create', action: 'Create backup' },
          { name: 'Restore', value: 'restore', action: 'Restore backup' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['customer'] } },
        options: [
          { name: 'List', value: 'list', action: 'List customers' },
          { name: 'Create', value: 'create', action: 'Create customer' },
        ],
        default: 'list',
      },
      {
        displayName: 'Operation',
        name: 'operation',
        type: 'options',
        noDataExpression: true,
        displayOptions: { show: { resource: ['server'] } },
        options: [
          { name: 'Get Stats', value: 'list', action: 'Get server stats' },
          { name: 'Check Updates', value: 'checkUpdates', action: 'Check for updates' },
        ],
        default: 'list',
      },
      // Dynamic fields per resource/operation
      {
        displayName: 'Domain ID',
        name: 'domainId',
        type: 'number',
        default: 0,
        displayOptions: {
          show: {
            resource: ['domain', 'dns', 'ssl', 'email'],
            operation: ['delete', 'list', 'create'],
          },
        },
        description: 'The ID of the domain',
      },
      {
        displayName: 'Subscription ID',
        name: 'subscriptionId',
        type: 'number',
        default: 0,
        displayOptions: {
          show: { resource: ['subscription', 'backup', 'database'], operation: ['delete', 'create', 'list'] },
        },
      },
      {
        displayName: 'Domain Name',
        name: 'domainName',
        type: 'string',
        default: '',
        displayOptions: { show: { resource: ['domain'], operation: ['create'] } },
      },
      {
        displayName: 'Database Type',
        name: 'dbType',
        type: 'options',
        options: [
          { name: 'MSSQL', value: 'MsSql' },
          { name: 'MySQL', value: 'MySql' },
          { name: 'MariaDB', value: 'MariaDb' },
          { name: 'PostgreSQL', value: 'PostgreSql' },
          { name: 'SQLite', value: 'Sqlite' },
        ],
        default: 'MySql',
        displayOptions: { show: { resource: ['database'], operation: ['create'] } },
      },
      {
        displayName: 'Database Name',
        name: 'dbName',
        type: 'string',
        default: '',
        displayOptions: { show: { resource: ['database'], operation: ['create'] } },
      },
      {
        displayName: 'Email Address (local part)',
        name: 'emailLocalPart',
        type: 'string',
        default: '',
        displayOptions: { show: { resource: ['email'], operation: ['create'] } },
      },
      {
        displayName: 'Email Password',
        name: 'emailPassword',
        type: 'string',
        typeOptions: { password: true },
        default: '',
        displayOptions: { show: { resource: ['email'], operation: ['create'] } },
      },
    ],
  },

  async execute() {
    const items = this.getInputData();
    const results = [];
    const credentials = await this.getCredentials('hostPanelApi');
    const baseUrl = credentials.apiUrl;

    for (let i = 0; i < items.length; i++) {
      const resource = this.getNodeParameter('resource', i);
      const operation = this.getNodeParameter('operation', i);

      let endpoint = '';
      let method = 'GET';
      let body = undefined;

      // Route to correct endpoint
      if (resource === 'domain') {
        if (operation === 'list') endpoint = '/domains';
        else if (operation === 'create') {
          endpoint = '/domains';
          method = 'POST';
          body = {
            subscriptionId: this.getNodeParameter('subscriptionId', i),
            name: this.getNodeParameter('domainName', i),
          };
        } else if (operation === 'delete') {
          endpoint = `/domains/${this.getNodeParameter('domainId', i)}`;
          method = 'DELETE';
        }
      } else if (resource === 'subscription') {
        if (operation === 'list') endpoint = '/subscriptions';
        else if (operation === 'delete') {
          endpoint = `/subscriptions/${this.getNodeParameter('subscriptionId', i)}`;
          method = 'DELETE';
        }
      } else if (resource === 'email') {
        const domainId = this.getNodeParameter('domainId', i);
        if (operation === 'list') endpoint = `/domains/${domainId}/mailboxes`;
        else if (operation === 'create') {
          endpoint = `/domains/${domainId}/mailboxes`;
          method = 'POST';
          body = {
            localPart: this.getNodeParameter('emailLocalPart', i),
            password: this.getNodeParameter('emailPassword', i),
          };
        }
      } else if (resource === 'database') {
        if (operation === 'list') endpoint = '/databases';
        else if (operation === 'create') {
          endpoint = '/databases';
          method = 'POST';
          body = {
            subscriptionId: this.getNodeParameter('subscriptionId', i),
            name: this.getNodeParameter('dbName', i),
            type: this.getNodeParameter('dbType', i),
          };
        }
      } else if (resource === 'dns') {
        const domainId = this.getNodeParameter('domainId', i);
        if (operation === 'list') endpoint = `/domains/${domainId}/dns`;
      } else if (resource === 'ssl') {
        const domainId = this.getNodeParameter('domainId', i);
        if (operation === 'list') endpoint = `/domains/${domainId}/ssl/status`;
        else if (operation === 'create') {
          endpoint = `/domains/${domainId}/ssl/issue`;
          method = 'POST';
        }
      } else if (resource === 'backup') {
        const subId = this.getNodeParameter('subscriptionId', i);
        if (operation === 'list') endpoint = `/subscriptions/${subId}/backups`;
        else if (operation === 'create') {
          endpoint = `/subscriptions/${subId}/backups`;
          method = 'POST';
        }
      } else if (resource === 'customer') {
        if (operation === 'list') endpoint = '/customers';
      } else if (resource === 'server') {
        if (operation === 'list') endpoint = '/server/stats';
        else if (operation === 'checkUpdates') {
          endpoint = '/server/updates/check';
          method = 'POST';
        }
      }

      const options = {
        method,
        url: `${baseUrl}${endpoint}`,
        headers: { 'Content-Type': 'application/json' },
        json: true,
      };
      if (body) options.body = body;

      const response = await this.helpers.httpRequestWithAuthentication.call(this, 'hostPanelApi', options);
      results.push({ json: response });
    }

    return [results];
  },
};
