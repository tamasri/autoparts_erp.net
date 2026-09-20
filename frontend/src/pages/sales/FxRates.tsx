import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Chip, Paper, Stack, TextField, Typography } from '@mui/material';
import { fxRatesApi } from '../../api/endpoints/fxRates';
import { unwrapList } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import DataTable, { type Column } from '../../components/ui/DataTable';

type FxRate = { id: string; rateDate?: string; buyRate?: number; sellRate?: number; midRate?: number };

const num = (v?: number): string => Number(v ?? 0).toLocaleString('en-US');
const today = (): string => new Date().toLocaleDateString('en-CA');

export default function FxRates(): JSX.Element {
  const [rows, setRows] = useState<FxRate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [saving, setSaving] = useState(false);
  const [rateDate, setRateDate] = useState(today());
  const [buy, setBuy] = useState(0);
  const [sell, setSell] = useState(0);
  const [mid, setMid] = useState(0);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setRows(unwrapList<FxRate>((await fxRatesApi.getList(1, 30)).data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل أسعار الصرف')); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const computedMid = buy > 0 && sell > 0 ? (buy + sell) / 2 : 0;

  async function save(): Promise<void> {
    if (!rateDate || buy <= 0 || sell <= 0) { toast.error('التاريخ وسعرا الشراء والبيع مطلوبة'); return; }
    setSaving(true);
    try {
      await fxRatesApi.create({ rateDate, buyRate: buy, sellRate: sell, midRate: mid > 0 ? mid : computedMid });
      toast.success('تم حفظ سعر الصرف');
      setBuy(0); setSell(0); setMid(0); setShowForm(false); await load();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر حفظ سعر الصرف')); }
    finally { setSaving(false); }
  }

  const columns: Column<FxRate>[] = [
    { header: 'التاريخ', render: (r) => <>{r.rateDate ?? '—'}{r.id === rows[0]?.id ? <Chip size="small" color="primary" label="الأحدث" sx={{ mx: 1 }} /> : null}</> },
    { header: 'سعر الشراء', numeric: true, render: (r) => <Typography component="span" color="success.main" fontWeight={700}>{num(r.buyRate)}</Typography> },
    { header: 'سعر البيع', numeric: true, render: (r) => <Typography component="span" color="error.main" fontWeight={700}>{num(r.sellRate)}</Typography> },
    { header: 'سعر الوسط', numeric: true, render: (r) => <strong>{num(r.midRate)}</strong> },
    { header: 'الفارق', numeric: true, render: (r) => num(Number(r.sellRate ?? 0) - Number(r.buyRate ?? 0)) },
  ];

  return (
    <>
      <PageHeader title="أسعار الصرف" subtitle="سعر الدولار مقابل الليرة السورية — يُستعمل تلقائياً في الفواتير والدفعات"
        actions={<Button variant={showForm ? 'outlined' : 'contained'} size="small" onClick={() => setShowForm((v) => !v)}>{showForm ? 'إلغاء' : '＋ سعر جديد'}</Button>} />
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {showForm ? (
        <Paper variant="outlined" sx={{ p: 2, mb: 2, borderRadius: 3 }}>
          <Stack direction="row" gap={2} flexWrap="wrap" alignItems="center">
            <TextField size="small" type="date" label="التاريخ *" value={rateDate} onChange={(e) => setRateDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            <TextField size="small" type="number" label="سعر الشراء *" value={buy || ''} onChange={(e) => setBuy(Number(e.target.value))} />
            <TextField size="small" type="number" label="سعر البيع *" value={sell || ''} onChange={(e) => setSell(Number(e.target.value))} />
            <TextField size="small" type="number" label="سعر الوسط" value={mid || ''} onChange={(e) => setMid(Number(e.target.value))} helperText={computedMid > 0 ? `يُحسب تلقائياً: ${num(computedMid)}` : ' '} />
            <Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button>
          </Stack>
        </Paper>
      ) : null}
      <DataTable columns={columns} rows={rows} getKey={(r) => r.id} loading={loading} empty="لا توجد أسعار صرف مسجّلة" />
    </>
  );
}
