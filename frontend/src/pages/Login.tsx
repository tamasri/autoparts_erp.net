import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, Avatar, Box, Button, CircularProgress, IconButton, InputAdornment, Paper, Stack, TextField, Typography } from '@mui/material';
import { authApi } from '../api/endpoints/auth';
import { useAuthStore } from '../stores/authStore';
import { toSession } from '../lib/session';
import { patternSx } from '../theme/pattern';

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
      // The refresh token arrives as an HttpOnly cookie; the body carries only the access token, the user and the permissions.
      setAuth(toSession(res.data, user));
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
    // The identity pattern, faint, on the pale page tone; the card carries a Forest band with the pattern in sand.
    <Box sx={(t) => ({ minHeight: '100vh', display: 'grid', placeItems: 'center', p: 2, bgcolor: t.palette.brand.page, ...patternSx(t.palette.brand.wheat, 0.16, 96, 1.2) })}>
      <Paper id="login-card" elevation={6} sx={{ width: '100%', maxWidth: 420, borderRadius: 4, overflow: 'hidden' }}>
        <Box sx={(t) => ({ height: 88, bgcolor: t.palette.brand.forest, ...patternSx(t.palette.brand.sand, 0.22, 56, 1.2) })} />
        <Box sx={{ px: { xs: 3, sm: 5 }, pb: { xs: 3, sm: 5 } }}>
          <Stack alignItems="center" spacing={1} sx={{ mb: 3, mt: -3.5 }}>
            <Avatar variant="rounded" sx={(t) => ({ width: 56, height: 56, bgcolor: t.palette.brand.emerald, color: t.palette.brand.sand, fontWeight: 800, fontSize: 24, border: '3px solid #fff' })}>A</Avatar>
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
        </Box>
      </Paper>
    </Box>
  );
}
