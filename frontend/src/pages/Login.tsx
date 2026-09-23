import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Avatar, Box, Button, CircularProgress, IconButton, InputAdornment, Paper, Stack, TextField, Typography } from '@mui/material';
import { authApi } from '../api/endpoints/auth';
import { useAuthStore } from '../stores/authStore';

type LoginUser = { id?: string; userName?: string; fullName?: string; firstName?: string; lastName?: string; roles?: Array<string | { code?: string; name?: string }> };
type LoginResponse = { accessToken: string; refreshToken: string; user?: LoginUser; userId?: string; username?: string; fullName?: string; roles?: LoginUser['roles']; permissions?: string[] };

export default function Login(): JSX.Element {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.login);
  const [user, setUser] = useState('');
  const [pass, setPass] = useState('');
  const [showPass, setShowPass] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleLogin(): Promise<void> {
    if (!user || !pass) { setError('يرجى إدخال اسم المستخدم وكلمة المرور'); return; }
    setLoading(true);
    setError('');
    try {
      const res = await authApi.login(user, pass);
      const data = (res.data?.data ?? res.data) as LoginResponse;
      const userNode = data.user ?? {};
      const rolesRaw = userNode.roles ?? data.roles ?? [];
      const roles = rolesRaw.map((r) => (typeof r === 'string' ? r : r.code ?? r.name ?? '')).filter(Boolean);
      const fullName = userNode.fullName || [userNode.firstName, userNode.lastName].filter(Boolean).join(' ') || data.fullName || user;
      setAuth({
        token: data.accessToken,
        refreshToken: data.refreshToken,
        user: { id: userNode.id ?? data.userId ?? '', username: userNode.userName ?? data.username ?? user, fullName, roles },
        permissions: data.permissions ?? [],
      });
      navigate('/');
    } catch (err: unknown) {
      const r = err as { response?: { data?: { detail?: string; message?: string; title?: string } } };
      setError(r.response?.data?.detail ?? r.response?.data?.message ?? r.response?.data?.title ?? 'فشل تسجيل الدخول — تحقق من البيانات');
    } finally {
      setLoading(false);
    }
  }

  const onEnter = (e: React.KeyboardEvent): void => { if (e.key === 'Enter') void handleLogin(); };

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, background: (t) => `linear-gradient(135deg, ${t.palette.primary.main}22, ${t.palette.background.default})` }}>
      <Paper id="login-card" elevation={6} sx={{ width: '100%', maxWidth: 420, p: { xs: 3, sm: 5 }, borderRadius: 4 }}>
        <Stack alignItems="center" spacing={1} sx={{ mb: 3 }}>
          <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.main', fontWeight: 800, fontSize: 24 }}>A</Avatar>
          <Typography variant="h5" fontWeight={800}>مرحباً بعودتك</Typography>
          <Typography variant="body2" color="text.secondary">AutoParts ERP — نظام إدارة قطع الغيار</Typography>
        </Stack>

        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <TextField
            id="login-username" label="البريد الإلكتروني أو اسم المستخدم" value={user} onChange={(e) => setUser(e.target.value)} onKeyDown={onEnter}
            autoFocus autoComplete="username" fullWidth
          />
          <TextField
            id="login-password" label="كلمة المرور" type={showPass ? 'text' : 'password'} value={pass} onChange={(e) => setPass(e.target.value)} onKeyDown={onEnter}
            autoComplete="current-password" fullWidth
            InputProps={{
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton id="toggle-password-btn" size="small" onClick={() => setShowPass((v) => !v)} aria-label={showPass ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور'}>
                    {showPass ? '🙈' : '👁'}
                  </IconButton>
                </InputAdornment>
              ),
            }}
          />
          <Button id="login-submit-btn" variant="contained" size="large" disabled={loading} onClick={() => void handleLogin()} sx={{ borderRadius: 5, py: 1.2, fontWeight: 700 }}>
            {loading ? <><CircularProgress size={18} color="inherit" sx={{ mx: 1 }} />جارٍ الدخول...</> : 'تسجيل الدخول'}
          </Button>
          <Typography variant="caption" color="text.secondary" textAlign="center">نسيت كلمة المرور؟ يعيد مسؤول النظام تعيينها من شاشة المستخدمين.</Typography>
        </Stack>
      </Paper>
    </Box>
  );
}
