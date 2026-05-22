import api from './client';

export interface ServiceStats {
  cpuUsagePercent?: number;
  totalMemoryMb?: number;
  usedMemoryMb?: number;
  totalDiskMb?: number;
  usedDiskMb?: number;
  uptimeSeconds?: number;
  hostname?: string;
  osVersion?: string;
}

export interface PanelSubscription {
  id: number;
  userId: string;
  servicePlanId: number;
  status: string;
  createdAt: string;
  systemUsername?: string;
  domains?: Array<{ id: number; name: string }>;
}

export interface PanelPlan {
  id: number;
  name: string;
  pricePerPeriod: number;
  diskQuotaMb: number;
  bandwidthLimitMb: number;
  maxDomains: number;
  maxEmailAccounts: number;
  maxDatabases: number;
  isCustom?: boolean;
  customerUserId?: string | null;
}

export interface PanelCustomer {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  companyName?: string;
  role: string;
  isActive: boolean;
}

export interface PanelAuditEntry {
  id: number;
  timestamp: string;
  userId?: string;
  userEmail?: string;
  action: string;
  resource?: string;
  resourceId?: string;
  ipAddress?: string;
}

export const panelApi = {
  serverStats: () => api.get<ServiceStats>('/server/stats').then(r => r.data),
  listSubscriptions: () => api.get<PanelSubscription[]>('/subscriptions').then(r => r.data),
  listPlans: () => api.get<PanelPlan[]>('/serviceplans').then(r => r.data),
  listCustomers: () => api.get<PanelCustomer[]>('/customers').then(r => r.data),
  // /audit was paginated in a later phase — returns { items, totalCount, page, ... }
  // not a bare array. Unwrap to keep the existing callers (which expect [])
  // working without touching every consumer. If the backend reverts to a bare
  // array, this still handles it.
  recentAudit: (limit = 8) => api.get<{ items?: PanelAuditEntry[] } | PanelAuditEntry[]>('/audit', { params: { limit } })
    .then(r => Array.isArray(r.data) ? r.data : (r.data?.items ?? [])),
};
