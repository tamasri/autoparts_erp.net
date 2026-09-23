/**
 * Create a user or edit one: profile, roles, password and activation. Roles and deactivation are governed (another person approves unless the
 * signed-in user is a system administrator), and nobody can change their own roles or status — the screen says so instead of offering it.
 */
import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Autocomplete, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Stack, TextField, Typography,
} from '@mui/material';
import { usersApi, type User } from '../../api/endpoints/users';
import { rolesApi } from '../../api/endpoints/roles';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { useAuthStore } from '../../stores/authStore';
import { extractApiError, toast } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import ReasonDialog from '../../components/ui/ReasonDialog';
import UserWarehouses from './UserWarehouses';

type RoleOption = { id: string; code: string; permissionCount: number };
type Props = { open: boolean; user: User | null; onClose: () => void; onSaved: () => void };

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

export default function UserDialog({ open, user, onClose, onSaved }: Props): JSX.Element {
  const me = useAuthStore((s) => s.user);
  const editing = user !== null;
  const isSelf = editing && user.id === me?.id;

  const [roles, setRoles] = useState<RoleOption[]>([]);
  const [userName, setUserName] = useState('');
  const [email, setEmail] = useState('');
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [password, setPassword] = useState('');
  const [chosen, setChosen] = useState<RoleOption[]>([]);
  const [newPassword, setNewPassword] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [deactivating, setDeactivating] = useState(false);

  useEffect(() => {
    if (!open) return;
    rolesApi.getRoles().then((r) => setRoles(unwrapList<{ id: string; code?: string; name?: string; permissions?: string[] }>(r.data)
      .map((x) => ({ id: x.id, code: x.code ?? x.name ?? '', permissionCount: x.permissions?.length ?? 0 })))).catch(() => setRoles([]));
  }, [open]);

  useEffect(() => {
    if (!open) return;
    setError(''); setPassword(''); setNewPassword('');
    setUserName(user?.userName ?? ''); setEmail(user?.email ?? ''); setFirstName(user?.firstName ?? ''); setLastName(user?.lastName ?? '');
  }, [open, user]);

  useEffect(() => {
    setChosen(user ? roles.filter((r) => user.roles.some((x) => x.code === r.code)) : []);
  }, [roles, user, open]);

  const rolesChanged = useMemo(() => {
    const before = new Set((user?.roles ?? []).map((r) => r.code));
    return chosen.length !== before.size || chosen.some((r) => !before.has(r.code));
  }, [chosen, user]);

  function validate(): string {
    if (!editing && !/^[A-Za-z0-9._-]{3,50}$/.test(userName)) return 'اسم المستخدم: 3 إلى 50 حرفاً إنجليزياً أو أرقاماً';
    if (!EMAIL.test(email)) return 'بريد إلكتروني غير صالح';
    if (firstName.trim().length < 2 || lastName.trim().length < 2) return 'الاسم الأول والأخير مطلوبان (حرفان على الأقل)';
    if (!editing && password.length < 8) return 'كلمة السر 8 أحرف على الأقل، بحرف كبير وصغير ورقم ورمز';
    if (chosen.length === 0) return 'اختر دوراً واحداً على الأقل';
    return '';
  }

  async function save(): Promise<void> {
    const problem = validate();
    if (problem) { setError(problem); return; }
    setSaving(true); setError('');
    try {
      if (!editing) {
        await usersApi.createUser({ userName: userName.trim(), email: email.trim(), firstName: firstName.trim(), lastName: lastName.trim(), password, roleIds: chosen.map((r) => r.id) });
        toast.success('تم إنشاء المستخدم');
      } else {
        await usersApi.updateUser(user.id, { email: email.trim(), firstName: firstName.trim(), lastName: lastName.trim() });
        if (rolesChanged && !isSelf) notifyResult(await usersApi.assignRoles(user.id, { roleIds: chosen.map((r) => r.id) }), 'تم تعديل الأدوار');
        else toast.success('تم حفظ التعديلات');
      }
      onSaved(); onClose();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ المستخدم')); }
    finally { setSaving(false); }
  }

  async function reset(): Promise<void> {
    if (!user) return;
    if (newPassword.length < 8) { setError('كلمة السر الجديدة 8 أحرف على الأقل'); return; }
    try { await usersApi.resetPassword(user.id, newPassword); toast.success('تم تعيين كلمة السر الجديدة'); setNewPassword(''); setError(''); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تعيين كلمة السر')); }
  }

  async function activate(): Promise<void> {
    if (!user) return;
    try { unwrapNode(await usersApi.activateUser(user.id)); toast.success('تم تفعيل المستخدم'); onSaved(); onClose(); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر التفعيل')); }
  }

  async function deactivate(reason: string): Promise<void> {
    if (!user) return;
    try { notifyResult(await usersApi.deactivateUser(user.id, { reason }), 'تم إيقاف المستخدم'); setDeactivating(false); onSaved(); onClose(); }
    catch (e: unknown) { setDeactivating(false); setError(extractApiError(e, 'تعذر الإيقاف')); }
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? `تعديل المستخدم: ${user.userName}` : 'مستخدم جديد'}</DialogTitle>
        <DialogContent dividers>
          <Stack spacing={2}>
            {error ? <Alert severity="error">{error}</Alert> : null}
            {!editing ? <TextField size="small" label="اسم المستخدم *" value={userName} onChange={(e) => setUserName(e.target.value)} inputProps={{ dir: 'ltr' }} autoComplete="off" /> : null}
            <Stack direction="row" gap={2}>
              <TextField size="small" fullWidth label="الاسم الأول *" value={firstName} onChange={(e) => setFirstName(e.target.value)} />
              <TextField size="small" fullWidth label="اسم العائلة *" value={lastName} onChange={(e) => setLastName(e.target.value)} />
            </Stack>
            <TextField size="small" label="البريد الإلكتروني *" value={email} onChange={(e) => setEmail(e.target.value)} inputProps={{ dir: 'ltr' }} autoComplete="off" />
            {!editing ? <TextField size="small" type="password" label="كلمة السر *" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" helperText="8 أحرف على الأقل، بحرف كبير وصغير ورقم ورمز" /> : null}

            <Autocomplete
              multiple size="small" options={roles} value={chosen} disabled={isSelf} onChange={(_, v) => setChosen(v)}
              getOptionLabel={(r) => r.code} isOptionEqualToValue={(a, b) => a.id === b.id} noOptionsText="لا توجد أدوار"
              renderTags={(value, getTagProps) => value.map((r, i) => <Chip {...getTagProps({ index: i })} key={r.id} size="small" color="primary" variant="outlined" label={r.code} />)}
              renderOption={(props, r) => <Box component="li" {...props} key={r.id} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}><span>{r.code}</span><Typography variant="caption" color="text.secondary">{r.permissionCount} صلاحية</Typography></Box>}
              renderInput={(params) => <TextField {...params} label="الأدوار *" helperText={isSelf ? 'لا يمكنك تعديل أدوار حسابك بنفسك' : editing && rolesChanged ? 'تغيير الأدوار يحتاج موافقة مستخدم آخر (ما لم تكن مدير نظام)' : ' '} />}
            />

            {editing ? (
              <>
                <Divider />
                <UserWarehouses userId={user.id} readOnly={isSelf} onSaved={onSaved} />
                <Divider />
                <Stack direction="row" gap={1} alignItems="center">
                  <TextField size="small" fullWidth type="password" label="كلمة سر جديدة" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} autoComplete="new-password" />
                  <Button variant="outlined" onClick={() => void reset()} disabled={!newPassword}>تعيين</Button>
                </Stack>
                <Stack direction="row" gap={1} alignItems="center">
                  <Chip size="small" color={user.isActive ? 'success' : 'default'} label={user.isActive ? 'نشط' : 'موقوف'} />
                  {user.isActive
                    ? <Button size="small" color="error" disabled={isSelf} onClick={() => setDeactivating(true)}>إيقاف المستخدم</Button>
                    : <Button size="small" color="success" onClick={() => void activate()}>تفعيل المستخدم</Button>}
                  {isSelf ? <Typography variant="caption" color="text.secondary">لا يمكنك إيقاف حسابك بنفسك</Typography> : null}
                </Stack>
              </>
            ) : null}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>إلغاء</Button>
          <Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button>
        </DialogActions>
      </Dialog>
      <ReasonDialog open={deactivating} title={`إيقاف المستخدم ${user?.userName ?? ''}`} confirmLabel="إيقاف" minLength={5} onClose={() => setDeactivating(false)} onConfirm={deactivate} />
    </>
  );
}
