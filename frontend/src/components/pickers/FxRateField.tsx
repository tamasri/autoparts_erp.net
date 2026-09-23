import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { Alert, Box, Button, Chip, Collapse, Link, List, ListItemButton, ListItemText, Paper, Stack, TextField, Typography } from '@mui/material';
import { fxRatesApi } from '../../api/endpoints/fxRates';
import { unwrapList, unwrapNode } from '../../api/apiData';
import { toast, extractApiError } from '../../lib/toast';
import { formatSyp } from '../../lib/format';

export type FxRate = { id: string; rateDate: string; currencyFrom: string; currencyTo: string; buyRate: number; sellRate: number; midRate: number; isActive: boolean };

type Props = {
  /** Selected fx_rates id ('' until the latest rate has loaded). */
  value: string;
  onChange: (id: string, rate: FxRate | null) => void;
  /** Document date, used to warn when the selected rate is older than the document. */
  documentDate?: string;
};

const rateText = (v: number): string => formatSyp(v);

/**
 * Exchange rate for a document. It is taken automatically from the latest rate saved in the system settings
 * (the FX Rates screen); the user can still switch to another saved rate or record a new one for this document.
 */
export default function FxRateField({ value, onChange, documentDate }: Props): JSX.Element {
  const [latest, setLatest] = useState<FxRate | null>(null);
  const [current, setCurrent] = useState<FxRate | null>(null);
  const [recent, setRecent] = useState<FxRate[]>([]);
  const [editing, setEditing] = useState(false);
  const [creating, setCreating] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ rateDate: new Date().toLocaleDateString('en-CA'), buyRate: 0, sellRate: 0, midRate: 0 });

  useEffect(() => {
    let live = true;
    fxRatesApi.getLatest()
      .then((res) => {
        if (!live) return;
        const rate = unwrapNode<FxRate>(res.data);
        setLatest(rate);
        if (rate) {
          setCurrent(rate);
          if (!value) onChange(rate.id, rate);
        }
      })
      .catch(() => { /* leave empty: the user is told below and can create a rate */ })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
    // Load once; `value` only matters for the initial default.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function openEdit(): Promise<void> {
    setEditing(true);
    try { setRecent(unwrapList<FxRate>((await fxRatesApi.getList(1, 30)).data)); } catch { setRecent([]); }
  }

  function select(rate: FxRate): void {
    setCurrent(rate);
    onChange(rate.id, rate);
    setEditing(false);
    setCreating(false);
  }

  async function createRate(): Promise<void> {
    if (!(form.midRate > 0) || !(form.buyRate > 0) || !(form.sellRate > 0)) { toast.error('أدخل أسعار الشراء والبيع والوسط'); return; }
    setBusy(true);
    try {
      const created = unwrapNode<FxRate>((await fxRatesApi.create(form)).data);
      if (created) { toast.success('تم حفظ سعر الصرف'); select(created); }
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ سعر الصرف')); }
    finally { setBusy(false); }
  }

  if (loading) return <Typography variant="body2" color="text.secondary">جارٍ تحميل سعر الصرف...</Typography>;
  if (!current) {
    return <Alert severity="error">لا يوجد سعر صرف في الإعدادات — <Link component={RouterLink} to="/fx-rates">أضف سعراً أولاً</Link></Alert>;
  }

  const isLatest = latest?.id === current.id;
  const stale = Boolean(documentDate && current.rateDate < documentDate);

  return (
    <Box>
      <Paper variant="outlined" sx={{ px: 1.5, py: 1, borderRadius: 2, display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography fontWeight={700}><bdi dir="ltr">1 {current.currencyFrom}</bdi> = {rateText(current.midRate)}</Typography>
        <Typography variant="caption" color="text.secondary">بيع {rateText(current.sellRate)} · <bdi dir="ltr">{current.rateDate}</bdi></Typography>
        <Chip size="small" color={isLatest ? 'success' : 'warning'} variant="outlined" label={isLatest ? 'تلقائي' : 'معدّل'} />
        <Box sx={{ flex: 1 }} />
        <Button size="small" onClick={() => (editing ? setEditing(false) : void openEdit())}>{editing ? 'إغلاق' : 'تغيير'}</Button>
      </Paper>
      {stale ? <Typography variant="caption" color="warning.main">⚠ سعر الصرف أقدم من تاريخ المستند — حدّثه من الإعدادات إن لزم.</Typography> : null}

      <Collapse in={editing}>
        <Paper variant="outlined" sx={{ mt: 1, p: 1.5, borderRadius: 2 }}>
          <Typography variant="caption" color="text.secondary">اختر سعراً محفوظاً:</Typography>
          <List dense sx={{ maxHeight: 180, overflowY: 'auto' }}>
            {recent.map((r) => (
              <ListItemButton key={r.id} selected={r.id === current.id} onClick={() => select(r)}>
                <ListItemText primary={`${r.rateDate} — ${rateText(r.midRate)}`} />
              </ListItemButton>
            ))}
          </List>
          <Button size="small" onClick={() => setCreating((c) => !c)}>{creating ? '✕ إلغاء' : '＋ سعر جديد'}</Button>
          <Collapse in={creating}>
            <Stack direction="row" gap={1} flexWrap="wrap" alignItems="center" sx={{ mt: 1 }}>
              <TextField size="small" type="date" label="التاريخ" InputLabelProps={{ shrink: true }} value={form.rateDate} onChange={(e) => setForm({ ...form, rateDate: e.target.value })} />
              <TextField size="small" type="number" label="شراء" sx={{ width: 120 }} value={form.buyRate} onChange={(e) => setForm({ ...form, buyRate: Number(e.target.value) })} />
              <TextField size="small" type="number" label="بيع" sx={{ width: 120 }} value={form.sellRate} onChange={(e) => setForm({ ...form, sellRate: Number(e.target.value) })} />
              <TextField size="small" type="number" label="وسط" sx={{ width: 120 }} value={form.midRate} onChange={(e) => setForm({ ...form, midRate: Number(e.target.value) })} />
              <Button variant="contained" size="small" disabled={busy} onClick={() => void createRate()}>{busy ? '...' : 'حفظ واعتماد'}</Button>
            </Stack>
          </Collapse>
        </Paper>
      </Collapse>
    </Box>
  );
}
