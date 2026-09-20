/** Accounting period locks: one card per month, per module (a locked SALES month does not lock PURCHASES). */
import { useCallback, useEffect, useState } from 'react';
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, Tab, Tabs, Typography } from '@mui/material';
import { periodsApi } from '../../api/endpoints/periods';
import { unwrapList } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import PageHeader from '../../components/ui/PageHeader';
import ReasonDialog from '../../components/ui/ReasonDialog';

type PeriodLock = { id: string; periodKey?: string; moduleCode?: string; isLocked?: boolean };

const MODULES = [{ code: 'SALES', label: 'المبيعات' }, { code: 'PURCHASES', label: 'المشتريات' }, { code: 'PAYMENTS', label: 'الدفعات' }];
const MONTHS = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];

export default function PeriodLocks(): JSX.Element {
  const year = new Date().getFullYear();
  const currentMonth = new Date().getMonth();
  const [moduleIndex, setModuleIndex] = useState(0);
  const [locks, setLocks] = useState<PeriodLock[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [target, setTarget] = useState<{ key: string; lock: boolean } | null>(null);
  const moduleCode = MODULES[moduleIndex].code;

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setLocks(unwrapList<PeriodLock>((await periodsApi.getLocks(year)).data)); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل إقفال الفترات')); }
    finally { setLoading(false); }
  }, [year]);

  useEffect(() => { void load(); }, [load]);

  const isLocked = (key: string): boolean => locks.some((l) => l.periodKey === key && l.isLocked && (l.moduleCode ?? 'SALES') === moduleCode);
  const lockedCount = MONTHS.filter((_, i) => isLocked(`${year}-${String(i + 1).padStart(2, '0')}`)).length;

  async function apply(reason: string): Promise<void> {
    if (!target) return;
    try {
      const body = { periodKey: target.key, moduleCode, reason };
      await (target.lock ? periodsApi.lockPeriod(body) : periodsApi.unlockPeriod(body));
      toast.success(target.lock ? `تم إقفال ${target.key}` : `تم فتح ${target.key}`);
      setTarget(null); await load();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تنفيذ العملية')); }
  }

  return (
    <>
      <PageHeader title={`إقفال الفترات — ${year}`} subtitle="بعد الإقفال لا يمكن ترحيل أو إلغاء مستندات في ذلك الشهر لتلك الوحدة"
        actions={<><Chip color="error" variant="outlined" label={`مقفل: ${lockedCount}`} /><Chip color="success" variant="outlined" label={`مفتوح: ${12 - lockedCount}`} /></>} />
      <Tabs value={moduleIndex} onChange={(_, i: number) => setModuleIndex(i)} sx={{ mb: 2 }}>{MODULES.map((m) => <Tab key={m.code} label={m.label} />)}</Tabs>
      {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
      {loading ? <Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box> : (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 2 }}>
          {MONTHS.map((name, i) => {
            const key = `${year}-${String(i + 1).padStart(2, '0')}`;
            const locked = isLocked(key);
            return (
              <Card key={key} variant="outlined" sx={{ borderRadius: 3, borderInlineStart: 4, borderInlineStartColor: locked ? 'error.main' : 'success.main' }}>
                <CardContent>
                  <Stack direction="row" justifyContent="space-between" alignItems="flex-start">
                    <Box><Typography fontWeight={800}>{name}</Typography><Typography variant="caption" color="text.secondary">{key}</Typography></Box>
                    {i === currentMonth ? <Chip size="small" color="primary" label="الحالي" /> : null}
                  </Stack>
                  <Chip size="small" sx={{ my: 1.5 }} color={locked ? 'error' : 'success'} variant="outlined" label={locked ? 'مقفل' : 'مفتوح'} />
                  <Button fullWidth size="small" variant={locked ? 'outlined' : 'contained'} color={locked ? 'inherit' : 'error'} onClick={() => setTarget({ key, lock: !locked })}>{locked ? 'فتح' : 'إقفال'}</Button>
                </CardContent>
              </Card>
            );
          })}
        </Box>
      )}
      <ReasonDialog open={target !== null} title={target?.lock ? `إقفال ${target.key} — ${MODULES[moduleIndex].label}` : `فتح ${target?.key} — ${MODULES[moduleIndex].label}`}
        confirmLabel={target?.lock ? 'إقفال' : 'فتح'} onClose={() => setTarget(null)} onConfirm={apply} />
    </>
  );
}
