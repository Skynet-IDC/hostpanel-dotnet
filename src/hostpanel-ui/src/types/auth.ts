export type UserRole = 'Admin' | 'Reseller' | 'Customer';

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  companyName?: string;
  role: UserRole;
  isActive: boolean;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
  user: User;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface ServerStats {
  cpuUsagePercent: number;
  totalMemoryMb: number;
  usedMemoryMb: number;
  totalDiskMb: number;
  usedDiskMb: number;
  uptimeHours: number;
  hostname: string;
  osVersion: string;
  totalDomains: number;
  totalSubscriptions: number;
  activeSubscriptions: number;
}
