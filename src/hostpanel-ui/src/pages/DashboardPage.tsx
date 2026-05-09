import { useEffect, useState } from 'react';
import type { ServerStats } from '../types/auth';
import { getServerStats } from '../api/server';

function StatCard({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="bg-white rounded-lg shadow p-6">
      <p className="text-sm text-gray-500">{label}</p>
      <p className="text-2xl font-bold text-gray-900 mt-1">{value}</p>
      {sub && <p className="text-xs text-gray-400 mt-1">{sub}</p>}
    </div>
  );
}

export function DashboardPage() {
  const [stats, setStats] = useState<ServerStats | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getServerStats()
      .then(setStats)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <p className="text-gray-500">Loading server stats...</p>
      </div>
    );
  }

  if (!stats) {
    return (
      <div className="bg-red-50 text-red-600 p-4 rounded">
        Failed to load server stats.
      </div>
    );
  }

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-6">Dashboard</h2>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <StatCard
          label="Hostname"
          value={stats.hostname}
          sub={stats.osVersion}
        />
        <StatCard
          label="Total Domains"
          value={stats.totalDomains.toString()}
        />
        <StatCard
          label="Subscriptions"
          value={stats.activeSubscriptions.toString()}
          sub={`${stats.totalSubscriptions} total`}
        />
        <StatCard
          label="CPU Usage"
          value={`${stats.cpuUsagePercent.toFixed(1)}%`}
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Memory</h3>
          <div className="flex justify-between text-sm text-gray-500 mb-2">
            <span>Used: {stats.usedMemoryMb} MB</span>
            <span>Total: {stats.totalMemoryMb} MB</span>
          </div>
          <div className="w-full bg-gray-200 rounded-full h-3">
            <div
              className="bg-blue-600 h-3 rounded-full transition-all"
              style={{
                width: stats.totalMemoryMb > 0
                  ? `${(stats.usedMemoryMb / stats.totalMemoryMb) * 100}%`
                  : '0%',
              }}
            />
          </div>
        </div>

        <div className="bg-white rounded-lg shadow p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Disk</h3>
          <div className="flex justify-between text-sm text-gray-500 mb-2">
            <span>Used: {stats.usedDiskMb} MB</span>
            <span>Total: {stats.totalDiskMb} MB</span>
          </div>
          <div className="w-full bg-gray-200 rounded-full h-3">
            <div
              className="bg-green-600 h-3 rounded-full transition-all"
              style={{
                width: stats.totalDiskMb > 0
                  ? `${(stats.usedDiskMb / stats.totalDiskMb) * 100}%`
                  : '0%',
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}
