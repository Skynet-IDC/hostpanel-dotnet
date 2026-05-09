export interface ServicePlan {
  id: number;
  name: string;
  description?: string;
  diskQuotaMb: number;
  bandwidthLimitMb: number;
  maxDomains: number;
  maxEmailAccounts: number;
  maxDatabases: number;
  pricePerPeriod: number;
}

export interface DomainDto {
  id: number;
  subscriptionId: number;
  name: string;
  documentRoot: string;
  sslEnabled: boolean;
  phpVersion: string;
  isMainDomain: boolean;
  createdAt: string;
}

export interface SubscriptionDto {
  id: number;
  userId: string;
  userEmail: string;
  servicePlanId: number;
  servicePlanName: string;
  status: string;
  billingPeriod: string;
  systemUsername?: string;
  createdAt: string;
  domains: DomainDto[];
}
