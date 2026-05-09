import api from './client';
import type { AuthResponse, LoginRequest } from '../types/auth';

export async function login(data: LoginRequest): Promise<AuthResponse> {
  const res = await api.post<AuthResponse>('/auth/login', data);
  return res.data;
}

export async function getMe() {
  const res = await api.get('/auth/me');
  return res.data;
}
