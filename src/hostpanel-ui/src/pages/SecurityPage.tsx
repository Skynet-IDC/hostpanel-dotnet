import { useEffect, useState } from 'react';
import api from '../api/client';

export function SecurityPage() {
  const [domains, setDomains] = useState<{ id: number; name: string }[]>([]);
  const [scanning, setScanning] = useState(false);
  const [scanResult, setScanResult] = useState<any>(null);
  const [fail2ban, setFail2ban] = useState('');

  useEffect(() => {
    api.get('/domains').then(r => setDomains(r.data));
    api.get('/security/fail2ban').then(r => setFail2ban(r.data.output || '')).catch(() => {});
  }, []);

  const scanDomain = async (id: number) => {
    setScanning(true);
    const r = await api.post(`/domains/${id}/scan`);
    setScanResult(r.data);
    setScanning(false);
  };

  const scanServer = async () => {
    setScanning(true);
    const r = await api.post('/security/scan-server');
    setScanResult(r.data);
    setScanning(false);
  };

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Security</h2>

      <div className="grid grid-cols-2 gap-6 mb-6">
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold mb-4">Malware Scanner (ClamAV)</h3>
          <div className="space-y-2 mb-4">
            {domains.map(d => (
              <button key={d.id} onClick={() => scanDomain(d.id)} disabled={scanning}
                className="block w-full text-left px-4 py-2 bg-gray-50 hover:bg-gray-100 rounded text-sm">
                Scan {d.name}
              </button>
            ))}
          </div>
          <button onClick={scanServer} disabled={scanning} className="bg-red-600 text-white rounded px-4 py-2 hover:bg-red-700 disabled:opacity-50 w-full">
            {scanning ? 'Scanning...' : 'Full Server Scan'}
          </button>
        </div>

        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold mb-4">Fail2Ban Status</h3>
          <pre className="bg-gray-50 p-4 rounded text-sm font-mono whitespace-pre-wrap max-h-64 overflow-y-auto">
            {fail2ban || 'Fail2Ban not available'}
          </pre>
        </div>
      </div>

      {scanResult && (
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold mb-4">Scan Results</h3>
          <div className="grid grid-cols-2 gap-4 mb-4">
            <div className="bg-gray-50 p-3 rounded">
              <span className="text-sm text-gray-500">Infected Files:</span>
              <span className={`ml-2 font-bold ${scanResult.infectedCount > 0 ? 'text-red-600' : 'text-green-600'}`}>
                {scanResult.infectedCount ?? 0}
              </span>
            </div>
            <div className="bg-gray-50 p-3 rounded">
              <span className="text-sm text-gray-500">Scan Path:</span>
              <span className="ml-2 font-mono text-sm">{scanResult.scanPath}</span>
            </div>
          </div>
          {scanResult.output && (
            <pre className="bg-gray-50 p-4 rounded text-sm font-mono whitespace-pre-wrap max-h-64 overflow-y-auto">{scanResult.output}</pre>
          )}
        </div>
      )}
    </div>
  );
}
