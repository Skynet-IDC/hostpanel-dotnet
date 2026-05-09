import api from './client';
import type { SubscriptionDto, DomainDto, ServicePlan } from '../types/domain';

export async function getSubscriptions(): Promise<SubscriptionDto[]> {
  const res = await api.get<SubscriptionDto[]>('/subscriptions');
  return res.data;
}

export async function createSubscription(data: {
  userId: string;
  servicePlanId: number;
  mainDomain: string;
  billingPeriod?: string;
}): Promise<SubscriptionDto> {
  const res = await api.post<SubscriptionDto>('/subscriptions', data);
  return res.data;
}

export async function getDomains(subscriptionId?: number): Promise<DomainDto[]> {
  const params = subscriptionId ? { subscriptionId } : {};
  const res = await api.get<DomainDto[]>('/domains', { params });
  return res.data;
}

export async function createDomain(data: {
  subscriptionId: number;
  name: string;
  phpVersion?: string;
}): Promise<DomainDto> {
  const res = await api.post<DomainDto>('/domains', data);
  return res.data;
}

export async function deleteDomain(id: number): Promise<void> {
  await api.delete(`/domains/${id}`);
}

export async function getServicePlans(): Promise<ServicePlan[]> {
  const res = await api.get<ServicePlan[]>('/serviceplans');
  return res.data;
}
