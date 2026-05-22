import axios from 'axios';

const api = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('hp_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    // UI-BUG-014 follow-on: the previous "blindly redirect on any 401" rule swallowed
    // the requires2fa pivot from /auth/login — the login form never got to show its
    // TOTP input because the interceptor force-reloaded the page first. Skip the
    // redirect when:
    //   1. The failing request IS the login call itself (the caller handles errors).
    //   2. The response body carries requires2fa=true (caller pivots to TOTP prompt).
    //   3. We are already sitting on /login.
    const url: string = (error.config?.url || '').toString();
    const isLoginCall = url.includes('/auth/login');
    const wantsTwoFactor = error.response?.data?.requires2fa === true;
    const onLoginPage = typeof window !== 'undefined' && window.location.pathname.startsWith('/login');
    if (error.response?.status === 401 && !isLoginCall && !wantsTwoFactor && !onLoginPage) {
      localStorage.removeItem('hp_token');
      localStorage.removeItem('hp_user');
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);

export default api;
