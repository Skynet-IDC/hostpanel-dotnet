import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import api from '../api/client';

export function SsoCallbackPage() {
  const { login } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    // Token is in URL fragment: #token=xxx
    const hash = window.location.hash;
    const token = hash.replace('#token=', '');

    if (!token) {
      navigate('/login?error=sso_failed');
      return;
    }

    // Store token and fetch user info
    localStorage.setItem('hp_token', token);
    api.get('/auth/me')
      .then((res) => {
        login(token, res.data);
        navigate('/');
      })
      .catch(() => {
        localStorage.removeItem('hp_token');
        navigate('/login?error=sso_failed');
      });
  }, []);

  return (
    <div className="min-h-screen bg-gray-100 flex items-center justify-center">
      <div className="bg-white p-8 rounded-lg shadow-md text-center">
        <p className="text-gray-600">Completing sign-in...</p>
      </div>
    </div>
  );
}
