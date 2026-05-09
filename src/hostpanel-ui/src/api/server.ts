import api from './client';
import type { ServerStats } from '../types/auth';

export async function getServerStats(): Promise<ServerStats> {
  const res = await api.get<ServerStats>('/server/stats');
  return res.data;
}
