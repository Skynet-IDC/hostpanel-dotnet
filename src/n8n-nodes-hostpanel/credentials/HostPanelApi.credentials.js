module.exports = {
  name: 'hostPanelApi',
  displayName: 'HostPanel API',
  documentationUrl: 'https://github.com/your-repo/hostpanel',
  properties: [
    {
      displayName: 'API URL',
      name: 'apiUrl',
      type: 'string',
      default: 'https://your-server:8443/api/v1',
      placeholder: 'https://panel.example.com/api/v1',
      required: true,
    },
    {
      displayName: 'API Key',
      name: 'apiKey',
      type: 'string',
      typeOptions: { password: true },
      default: '',
      required: true,
    },
  ],
  authenticate: {
    type: 'generic',
    properties: {
      headers: {
        Authorization: '=Bearer {{$credentials.apiKey}}',
      },
    },
  },
  test: {
    request: {
      baseURL: '={{$credentials.apiUrl}}',
      url: '/auth/me',
    },
  },
};
