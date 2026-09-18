import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { authApi } from '../api/endpoints/auth';
import { useAuthStore } from '../stores/authStore';

export default function Login() {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.login);
  const [user, setUser] = useState('');
  const [pass, setPass] = useState('');
  const [showPass, setShowPass] = useState(false);
  const [rememberMe, setRememberMe] = useState(false);
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
    <div className="login-bg">
      <div
        id="login-card"
        style={{
          background: '#fff',
          padding: '44px 40px 40px',
          borderRadius: 'var(--radius-xl)',
          width: '100%',
          maxWidth: 440,
          boxShadow: 'var(--shadow-lg)',
          direction: 'rtl',
        }}
      >
        {/* Logo */}
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', marginBottom: 32 }}>
          <div style={{
            width: 56,
            height: 56,
            borderRadius: 16,
            background: 'linear-gradient(135deg, #5c54ff, #7b75ff)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 24,
            fontWeight: 800,
            color: '#fff',
            boxShadow: '0 8px 24px rgba(92,84,255,0.35)',
            marginBottom: 16,
          }}>A</div>
          <h1 style={{ fontSize: 22, fontWeight: 800, color: 'var(--txt-primary)', margin: 0 }}>
            مرحباً بعودتك
          </h1>
          <p style={{ fontSize: 13.5, color: 'var(--txt-secondary)', marginTop: 6, margin: '6px 0 0' }}>
            AutoParts ERP — نظام إدارة قطع الغيار
          </p>
        </div>

        {/* Error */}
        {error && (
          <div className="vex-alert vex-alert--error">
            <span className="vex-alert__icon">⚠</span>
            <span>{error}</span>
          </div>
        )}

        {/* Username */}
        <div style={{ marginBottom: 18 }}>
          <label
            htmlFor="login-username"
            style={{ display: 'block', fontSize: 13, fontWeight: 600, color: 'var(--txt-secondary)', marginBottom: 6 }}
          >
            البريد الإلكتروني أو اسم المستخدم
          </label>
          <input
            id="login-username"
            type="text"
            className="vex-input"
            value={user}
            onChange={(e) => setUser(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleLogin()}
            placeholder="admin"
            autoFocus
            autoComplete="username"
          />
        </div>

        {/* Password */}
        <div style={{ marginBottom: 14 }}>
          <label
            htmlFor="login-password"
            style={{ display: 'block', fontSize: 13, fontWeight: 600, color: 'var(--txt-secondary)', marginBottom: 6 }}
          >
            كلمة المرور
          </label>
          <div style={{ position: 'relative' }}>
            <input
              id="login-password"
              type={showPass ? 'text' : 'password'}
              className="vex-input"
              value={pass}
              onChange={(e) => setPass(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleLogin()}
              placeholder="••••••••"
              autoComplete="current-password"
              style={{ paddingLeft: 40 }}
            />
            <button
              type="button"
              id="toggle-password-btn"
              onClick={() => setShowPass((v) => !v)}
              style={{
                position: 'absolute',
                left: 10,
                top: '50%',
                transform: 'translateY(-50%)',
                background: 'none',
                border: 'none',
                cursor: 'pointer',
                color: 'var(--txt-muted)',
                fontSize: 16,
                padding: 4,
                lineHeight: 1,
                display: 'flex',
                alignItems: 'center',
              }}
              title={showPass ? 'إخفاء' : 'إظهار'}
            >
              {showPass ? '🙈' : '👁'}
            </button>
          </div>
        </div>

        {/* Remember me + Forgot */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: 24,
        }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, color: 'var(--txt-secondary)', cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.target.checked)}
              style={{
                accentColor: 'var(--clr-primary)',
                width: 15,
                height: 15,
                cursor: 'pointer',
              }}
            />
            تذكرني
          </label>
          <button
            type="button"
            style={{
              background: 'none',
              border: 'none',
              color: 'var(--clr-primary)',
              fontSize: 13,
              cursor: 'pointer',
              fontWeight: 500,
              padding: 0,
            }}
          >
            نسيت كلمة المرور؟
          </button>
        </div>

        {/* Sign In Button */}
        <button
          id="login-submit-btn"
          type="button"
          onClick={handleLogin}
          disabled={loading}
          className="btn-primary btn-primary--lg"
          style={{ width: '100%', borderRadius: 'var(--radius-pill)', fontSize: 15, fontWeight: 700 }}
        >
          {loading ? (
            <span style={{ display: 'flex', alignItems: 'center', gap: 10, justifyContent: 'center' }}>
              <span className="vex-spinner" style={{ width: 18, height: 18, borderWidth: 2 }} />
              جارٍ الدخول...
            </span>
          ) : 'تسجيل الدخول'}
        </button>

        {/* Footer note */}
        <p style={{ textAlign: 'center', fontSize: 12, color: 'var(--txt-muted)', marginTop: 24, lineHeight: 1.5 }}>
          بتسجيل الدخول تؤكد موافقتك على سياسة الاستخدام
        </p>
      </div>
    </div>
  );
}
