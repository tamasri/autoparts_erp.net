/** Add a user as a sales rep, or change a rep's commission, monthly target and status. */
import { useEffect, useState } from 'react';
import {
  Alert, Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Stack, Switch, TextField,
} from '@mui/material';
import { salesRepsApi, type SalesRep, type SalesRepCandidate } from '../../api/endpoints/salesReps';
import { unwrapNode } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';

type Props = { open: boolean; rep: SalesRep | null; onClose: () => void; onSaved: () => void };

export default function SalesRepDialog({ open, rep, onClose, onSaved }: Props): JSX.Element {
  const [candidates, setCandidates] = useState<SalesRepCandidate[]>([]);
  const [user, setUser] = useState<SalesRepCandidate | null>(null);
  const [commission, setCommission] = useState(0);
  const [target, setTarget] = useState(0);
  const [active, setActive] = useState(true);
  const [notes, setNotes] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open) return;
    setError('');
    setCommission(rep?.commissionPct ?? 0);
    setTarget(rep?.monthlyTargetUsd ?? 0);
    setActive(rep?.isActive ?? true);
    setNotes(rep?.notes ?? '');
    setUser(null);
    if (!rep) {
      salesRepsApi.candidates()
        .then((r) => setCandidates(unwrapNode<SalesRepCandidate[]>(r.data) ?? []))
        .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل المستخدمين')));
    }
  }, [open, rep]);

  async function save(): Promise<void> {
    const userId = rep?.userId ?? user?.userId;
    if (!userId) { setError('اختر المستخدم'); return; }
    if (commission < 0 || commission > 100) { setError('نسبة العمولة بين 0 و 100'); return; }
    if (target < 0) { setError('الهدف لا يكون سالباً'); return; }
    setSaving(true); setError('');
    try {
      await salesRepsApi.save(userId, { commissionPct: commission, monthlyTargetUsd: target, isActive: active, notes: notes.trim() || undefined });
      toast.success(rep ? 'تم حفظ بيانات المندوب' : 'أُضيف المندوب');
      onSaved(); onClose();
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر الحفظ'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{rep ? `المندوب: ${rep.fullName}` : 'إضافة مندوب مبيعات'}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          {!rep ? (
            <Autocomplete
              options={candidates} value={user} onChange={(_, v) => setUser(v)}
              getOptionLabel={(o) => `${o.fullName} (${o.userName})`} isOptionEqualToValue={(a, b) => a.userId === b.userId}
              renderInput={(p) => <TextField {...p} size="small" label="المستخدم *" helperText="المندوب مستخدم في النظام؛ أنشئ المستخدم أولاً إن لم يكن موجوداً" />}
            />
          ) : null}
          <TextField size="small" type="number" label="نسبة العمولة %" value={commission} onChange={(e) => setCommission(Number(e.target.value))}
            inputProps={{ min: 0, max: 100, step: '0.5' }} helperText="تُحسب على صافي المبيعات بدون أجور التوصيل" />
          <TextField size="small" type="number" label="الهدف الشهري ($)" value={target} onChange={(e) => setTarget(Number(e.target.value))} inputProps={{ min: 0 }} />
          <TextField size="small" label="ملاحظات" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} />
          {rep ? <FormControlLabel control={<Switch checked={active} onChange={(e) => setActive(e.target.checked)} />} label={active ? 'نشط' : 'موقوف (لا يُختار على فواتير جديدة)'} /> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>إلغاء</Button>
        <Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button>
      </DialogActions>
    </Dialog>
  );
}
