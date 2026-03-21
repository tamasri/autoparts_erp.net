import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../api/endpoints/auth';
import { useAuthStore } from '../stores/authStore';

export default function Login() {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.login);
  const [user, setUser] = useState('');
  const [pass, setPass] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleLogin = async () => {
    if (!user || !pass) {
      setError('يرجى إدخال اسم المستخدم وكلمة المرور');
      return;
    }

    setLoading(true);
    setError('');

    try {
      const res = await authApi.login(user, pass);
      const data = res.data?.data ?? res.data;
      const userNode = data.user ?? {};
      const rolesRaw = userNode.roles ?? data.roles ?? [];
      const roles = Array.isArray(rolesRaw)
        ? rolesRaw.map((r: any) => (typeof r === 'string' ? r : r.code ?? r.name ?? '')).filter(Boolean)
        : [];
      const fullName = userNode.fullName
        ?? [userNode.firstName, userNode.lastName].filter(Boolean).join(' ')
        ?? data.fullName
        ?? user;

      setAuth({
        token: data.accessToken,
        refreshToken: data.refreshToken,
        user: {
          id: userNode.id ?? data.userId ?? '',
          username: userNode.userName ?? data.username ?? user,
          fullName,
          roles,
        },
        permissions: data.permissions ?? [],
      });

      navigate('/');
    } catch (err: any) {
      const msg = err?.response?.data?.detail
        ?? err?.response?.data?.message
        ?? err?.response?.data?.title
        ?? 'فشل تسجيل الدخول — تحقق من البيانات';
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: '#f5f5f5',
        direction: 'rtl',
      }}
    >
      <div
        style={{
          background: '#fff',
          padding: '2rem',
          borderRadius: '12px',
          width: '100%',
          maxWidth: '420px',
          boxShadow: '0 2px 16px #0001',
        }}
      >
        <h2 style={{ textAlign: 'center', marginBottom: '1.5rem' }}>
          الدخول إلى نظام الحوكمة
        </h2>

        <label style={{ display: 'block', marginBottom: '0.3rem', fontSize: '0.9rem', color: '#555' }}>
          اسم المستخدم أو البريد
        </label>
        <input
          value={user}
          onChange={(e) => setUser(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleLogin()}
          style={{
            width: '100%',
            padding: '0.75rem',
            border: '1px solid #ddd',
            borderRadius: '8px',
            marginBottom: '1rem',
            fontSize: '1rem',
            boxSizing: 'border-box',
            textAlign: 'right',
          }}
          placeholder='admin'
          autoFocus
        />

        <label style={{ display: 'block', marginBottom: '0.3rem', fontSize: '0.9rem', color: '#555' }}>
          كلمة المرور
        </label>
        <input
          type='password'
          value={pass}
          onChange={(e) => setPass(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleLogin()}
          style={{
            width: '100%',
            padding: '0.75rem',
            border: '1px solid #ddd',
            borderRadius: '8px',
            marginBottom: '1.5rem',
            fontSize: '1rem',
            boxSizing: 'border-box',
            textAlign: 'right',
          }}
          placeholder='••••••••'
        />

        {error && (
          <p style={{ color: '#d32f2f', marginBottom: '1rem', textAlign: 'center', fontSize: '0.9rem' }}>
            {error}
          </p>
        )}

        <button
          onClick={handleLogin}
          disabled={loading}
          style={{
            width: '100%',
            padding: '0.85rem',
            background: loading ? '#90a4ae' : '#00796b',
            color: '#fff',
            border: 'none',
            borderRadius: '8px',
            fontSize: '1.1rem',
            cursor: loading ? 'not-allowed' : 'pointer',
          }}
        >
          {loading ? 'جارٍ الدخول...' : 'دخول'}
        </button>
      </div>
    </div>
  );
}
