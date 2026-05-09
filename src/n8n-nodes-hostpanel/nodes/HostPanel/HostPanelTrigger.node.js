module.exports = {
  description: {
    displayName: 'HostPanel Trigger',
    name: 'hostPanelTrigger',
    icon: 'file:hostpanel.svg',
    group: ['trigger'],
    version: 1,
    subtitle: '={{$parameter["event"]}}',
    description: 'Triggers when events happen in HostPanel (via webhooks)',
    defaults: { name: 'HostPanel Trigger' },
    inputs: [],
    outputs: ['main'],
    webhooks: [
      {
        name: 'default',
        httpMethod: 'POST',
        responseMode: 'onReceived',
        path: 'hostpanel-webhook',
      },
    ],
    properties: [
      {
        displayName: 'Event',
        name: 'event',
        type: 'options',
        options: [
          { name: 'All Events', value: '*' },
          { name: 'Subscription Created', value: 'subscription.created' },
          { name: 'Subscription Activated', value: 'subscription.activated' },
          { name: 'Subscription Suspended', value: 'subscription.suspended' },
          { name: 'Subscription Cancelled', value: 'subscription.cancelled' },
          { name: 'Domain Created', value: 'domain.created' },
          { name: 'Domain Deleted', value: 'domain.deleted' },
          { name: 'SSL Issued', value: 'domain.ssl.issued' },
          { name: 'SSL Expiring', value: 'domain.ssl.expiring' },
          { name: 'Mailbox Created', value: 'mailbox.created' },
          { name: 'Database Created', value: 'database.created' },
          { name: 'Invoice Created', value: 'invoice.created' },
          { name: 'Invoice Paid', value: 'invoice.paid' },
          { name: 'Invoice Overdue', value: 'invoice.overdue' },
          { name: 'Payment Received', value: 'payment.received' },
          { name: 'Customer Created', value: 'customer.created' },
          { name: 'Backup Completed', value: 'backup.completed' },
          { name: 'Backup Failed', value: 'backup.failed' },
          { name: 'Server Health Down', value: 'server.health.down' },
          { name: 'Server Disk Warning', value: 'server.disk.warning' },
        ],
        default: '*',
        description: 'Which event to listen for. Register this webhook URL in HostPanel Settings > Webhooks.',
      },
    ],
  },

  async webhook() {
    const req = this.getRequestObject();
    const body = req.body;
    const event = this.getNodeParameter('event');

    // Filter by event type if not "*"
    if (event !== '*' && body.event !== event) {
      return {}; // Ignore non-matching events
    }

    // Verify signature if configured
    const signature = req.headers['x-hostpanel-signature'];
    if (signature) {
      // Signature verification could be added here
      // For now, accept all signed requests
    }

    return {
      workflowData: [
        [
          {
            json: {
              event: body.event,
              timestamp: body.timestamp,
              data: body.data,
            },
          },
        ],
      ],
    };
  },
};
