/**
 * WhatsApp assistant administration: pair the gateway (QR), link phone numbers to users with a one-time code, revoke them.
 * Only numbers linked here get answers; the assistant answers read-only questions with the linked user's permissions.
 */
import { useCallback, useEffect, useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Stack, TextField, Typography,
} from '@mui/material';
import QRCode from 'qrcode';
import { assistantApi, type AssistantLink, type AssistantLinkCode, type AssistantStatus } from '../../api/endpoints/assistant';
import { usersApi, type User } from '../../api/endpoints/users';
import { unwrapNode } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';
import StatusChip from '../../components/ui/StatusChip';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';

const GATEWAY: Record<string, { label: string; color: 'success' | 'warning' | 'error' | 'default' }> = {
  open: { label: 'متصل', color: 'success' },
  qr: { label: 'بانتظار ربط الهاتف', color: 'warning' },
  starting: { label: 'قيد التشغيل', color: 'warning' },
  close: { label: 'منقطع — يعيد الاتصال', color: 'error' },
  offline: { label: 'غير مشغّل', color: 'default' },
};

const fmt = (iso: string | null): string => (iso ? new Date(iso).toLocaleString('ar-SY-u-nu-latn', { dateStyle: 'short', timeStyle: 'short' }) : '—');

function CodeDialog({ code, account, onClose }: { code: AssistantLinkCode | null; account: string | null; onClose: () => void }): JSX.Element {
  return (
    <Dialog open={code !== null} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>رمز الربط</DialogTitle>
      <DialogContent>
        <Stack spacing={2} alignItems="center" sx={{ pt: 1 }}>
          <Typography variant="h3" fontWeight={800} letterSpacing={6} sx={{ fontFamily: 'monospace' }}>{code?.code}</Typography>
          <Alert severity="info" sx={{ width: '100%' }}>
            من الهاتف <b dir="ltr">+{code?.phone}</b> أرسل هذا الرمز كرسالة واتساب إلى رقم المساعد{account ? <> <b dir="ltr">+{account}</b></> : null}.
            صالح حتى {code ? fmt(code.expiresAt) : ''}. لن يظهر الرمز مرة أخرى.
          </Alert>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>تم</Button></DialogActions>
    </Dialog>
  );
}

export default function WhatsAppAssistant(): JSX.Element {
  const [status, setStatus] = useState<AssistantStatus | null>(null);
  const [qrImage, setQrImage] = useState<string | null>(null);
  const [links, setLinks] = useState<AssistantLink[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [adding, setAdding] = useState(false);
  const [user, setUser] = useState<PickerOption | null>(null);
  const [phone, setPhone] = useState('');
  const [busy, setBusy] = useState(false);
  const [code, setCode] = useState<AssistantLinkCode | null>(null);
  const [revoking, setRevoking] = useState<AssistantLink | null>(null);

  const loadStatus = useCallback(async (): Promise<void> => {
    try { setStatus(unwrapNode<AssistantStatus>((await assistantApi.status()).data)); } catch { /* shown by the links call */ }
  }, []);

  const loadLinks = useCallback(async (): Promise<void> => {
    setLoading(true); setError('');
    try { setLinks(unwrapNode<AssistantLink[]>((await assistantApi.links()).data) ?? []); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل الأرقام المربوطة (يلزم صلاحية إدارة المساعد)')); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { void loadStatus(); void loadLinks(); }, [loadStatus, loadLinks]);
  // The pairing QR changes about every 20 seconds and the link state changes when a phone sends its code.
  useEffect(() => {
    const t = window.setInterval(() => { void loadStatus(); if (links.some((l) => l.status === 'PENDING')) void loadLinks(); }, 5000);
    return () => window.clearInterval(t);
  }, [loadStatus, loadLinks, links]);

  useEffect(() => {
    if (status?.gatewayState === 'qr' && status.gatewayQr) {
      QRCode.toDataURL(status.gatewayQr, { width: 260, margin: 1 }).then(setQrImage).catch(() => setQrImage(null));
    } else {
      setQrImage(null);
    }
  }, [status?.gatewayState, status?.gatewayQr]);

  async function searchUsers(text: string): Promise<PickerOption[]> {
    const data = unwrapNode<{ items: User[] }>((await usersApi.getUsers(1, 10, text, true)).data);
    return (data?.items ?? []).map((u) => ({ id: u.id, label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.userName, sublabel: u.userName }));
  }

  async function createLink(): Promise<void> {
    if (!user) return;
    setBusy(true);
    try {
      const created = unwrapNode<AssistantLinkCode>((await assistantApi.createLink(user.id, phone)).data);
      setAdding(false); setUser(null); setPhone(''); setCode(created); await loadLinks();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر ربط الرقم')); }
    finally { setBusy(false); }
  }

  async function renew(l: AssistantLink): Promise<void> {
    try { setCode(unwrapNode<AssistantLinkCode>((await assistantApi.renewCode(l.id)).data)); await loadLinks(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إصدار رمز جديد')); }
  }

  async function revoke(): Promise<void> {
    if (!revoking) return;
    try { await assistantApi.revoke(revoking.id); toast.success('أُلغي ربط الرقم'); setRevoking(null); await loadLinks(); }
    catch (e: unknown) { toast.error(extractApiError(e, 'تعذر إلغاء الربط')); }
  }

  const gw = GATEWAY[status?.gatewayState ?? 'offline'] ?? GATEWAY.offline;
  const columns: Column<AssistantLink>[] = [
    { header: 'المستخدم', render: (l) => <Box><Typography fontWeight={600} variant="body2">{l.fullName}</Typography><Typography variant="caption" color="text.secondary">{l.userName}</Typography></Box> },
    { header: 'الرقم', render: (l) => <span dir="ltr">+{l.phone}</span>, nowrap: true },
    { header: 'الحالة', render: (l) => (l.status === 'PENDING' ? <Chip size="small" color="warning" variant="outlined" label="بانتظار الرمز" /> : <StatusChip status={l.status === 'REVOKED' ? 'INACTIVE' : l.status} label={l.status === 'REVOKED' ? 'ملغى' : undefined} />) },
    { header: 'الصلاحية', render: (l) => (l.userHasAccess ? '✓' : <Typography variant="caption" color="error">لا يملك صلاحية المساعد</Typography>) },
    { header: 'آخر استخدام', render: (l) => fmt(l.lastUsedAt), nowrap: true },
    {
      header: '', render: (l) => (
        <Stack direction="row" gap={1}>
          {l.status === 'PENDING' ? <Button size="small" onClick={() => void renew(l)}>رمز جديد</Button> : null}
          {l.status !== 'REVOKED' ? <Button size="small" color="error" onClick={() => setRevoking(l)}>إلغاء الربط</Button> : null}
        </Stack>
      ),
    },
  ];

  return (
    <Box>
      <PageHeader
        title="مساعد واتساب"
        subtitle="أسئلة للاستعلام فقط (أرصدة، مخزون، فواتير، مبيعات) من أرقام مربوطة بمستخدمين، وبصلاحيات كل مستخدم"
        actions={<Button variant="contained" onClick={() => setAdding(true)}>＋ ربط رقم</Button>}
      />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}

      <Card variant="outlined" sx={{ borderRadius: 3, mb: 3 }}>
        <CardContent>
          <Stack direction={{ xs: 'column', md: 'row' }} gap={3} alignItems={{ md: 'center' }}>
            <Stack spacing={1} sx={{ flex: 1 }}>
              <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap">
                <Typography fontWeight={700}>بوابة واتساب:</Typography>
                <Chip size="small" color={gw.color} label={gw.label} />
                {status?.gatewayAccount ? <Typography variant="body2" dir="ltr">+{status.gatewayAccount}</Typography> : null}
              </Stack>
              <Typography variant="body2" color="text.secondary">
                فهم السؤال: {status?.modelConfigured ? `نموذج ${status.modelName} (يُرسل إليه نص الرسالة فقط، دون أي بيانات من النظام)` : 'كلمات مفتاحية فقط (لم يُضبط مفتاح النموذج على الخادم)'}
              </Typography>
              {status && !status.enabled ? <Alert severity="warning">المساعد موقوف من إعدادات الذكاء الاصطناعي (WHATSAPP_ASSISTANT).</Alert> : null}
              {status?.gatewayState === 'offline' ? (
                <Typography variant="body2" color="text.secondary">البوابة لا تعمل على الخادم. فعّلها بوضع WHATSAPP_ENABLED=true في ملف ‎.env.vps ثم أعد النشر.</Typography>
              ) : null}
            </Stack>
            {qrImage ? (
              <Stack alignItems="center" spacing={1}>
                <Box component="img" src={qrImage} alt="QR" sx={{ width: 220, height: 220, borderRadius: 2, border: 1, borderColor: 'divider' }} />
                <Typography variant="caption" color="text.secondary" textAlign="center" sx={{ maxWidth: 260 }}>
                  على هاتف رقم المساعد: واتساب ← الأجهزة المرتبطة ← ربط جهاز، ثم امسح الرمز. لا تعرض هذه الشاشة لأحد.
                </Typography>
              </Stack>
            ) : null}
          </Stack>
        </CardContent>
      </Card>

      {loading ? <LinearProgress sx={{ mb: 1 }} /> : null}
      <DataTable columns={columns} rows={links} getKey={(l) => l.id} empty="لا توجد أرقام مربوطة بعد" />

      <Dialog open={adding} onClose={() => setAdding(false)} maxWidth="xs" fullWidth>
        <DialogTitle>ربط رقم واتساب بمستخدم</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <EntityPicker value={user} onChange={setUser} search={searchUsers} label="المستخدم" placeholder="ابحث باسم المستخدم..." />
            <TextField label="رقم الهاتف بالصيغة الدولية" placeholder="963933123456" value={phone} onChange={(e) => setPhone(e.target.value)} inputProps={{ dir: 'ltr' }} helperText="مع رمز البلد، دون + أو 00" />
            <Alert severity="info">سيظهر رمز من 6 أرقام؛ يرسله صاحب الرقم إلى رقم المساعد خلال 15 دقيقة ليكتمل الربط. يجيب المساعد بصلاحيات هذا المستخدم فقط.</Alert>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setAdding(false)}>إلغاء</Button>
          <Button variant="contained" disabled={busy || !user || phone.trim().length < 8} onClick={() => void createLink()}>إنشاء رمز الربط</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={revoking !== null} onClose={() => setRevoking(null)} maxWidth="xs" fullWidth>
        <DialogTitle>إلغاء ربط الرقم</DialogTitle>
        <DialogContent>
          <Typography>لن يجيب المساعد بعد الآن على <b dir="ltr">+{revoking?.phone}</b> ({revoking?.fullName}).</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRevoking(null)}>تراجع</Button>
          <Button color="error" variant="contained" onClick={() => void revoke()}>إلغاء الربط</Button>
        </DialogActions>
      </Dialog>

      <CodeDialog code={code} account={status?.gatewayAccount ?? null} onClose={() => setCode(null)} />
    </Box>
  );
}
