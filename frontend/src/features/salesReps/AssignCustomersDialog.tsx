/** Hand one or more customers to a rep. New invoices for them default to this rep; existing invoices keep theirs. */
import { useCallback, useState } from 'react';
import { Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { salesRepsApi } from '../../api/endpoints/salesReps';
import { customersApi } from '../../api/endpoints/customers';
import { unwrapPaged } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import EntityPicker, { type PickerOption } from '../../components/pickers/EntityPicker';

type CustomerRow = { id: string; code?: string; name?: string; phone?: string; assignedSalesRep?: string | null };
type Props = { open: boolean; repId: string; repName: string; onClose: () => void; onSaved: () => void };

export default function AssignCustomersDialog({ open, repId, repName, onClose, onSaved }: Props): JSX.Element {
  const [picked, setPicked] = useState<PickerOption[]>([]);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const search = useCallback(async (text: string): Promise<PickerOption[]> => {
    const res = await customersApi.getCustomers({ page: 1, pageSize: 10, searchTerm: text || undefined, isActive: true });
    return unwrapPaged<CustomerRow>(res.data).items.map((c) => ({
      id: c.id,
      label: c.name ?? c.id.slice(0, 8),
      sublabel: [c.code, c.phone, c.assignedSalesRep === repId ? 'لدى هذا المندوب' : c.assignedSalesRep ? 'لدى مندوب آخر' : ''].filter(Boolean).join(' · '),
    }));
  }, [repId]);

  function close(): void { setPicked([]); setError(''); onClose(); }

  async function save(): Promise<void> {
    if (picked.length === 0) { setError('اختر زبوناً واحداً على الأقل'); return; }
    setSaving(true); setError('');
    try {
      await salesRepsApi.assignCustomers(repId, picked.map((p) => p.id));
      toast.success(`نُقل ${picked.length} زبون إلى ${repName}`);
      setPicked([]); onSaved(); onClose();
    } catch (e: unknown) {
      setError(extractApiError(e, 'تعذر نقل الزبائن'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="sm">
      <DialogTitle>إسناد زبائن إلى {repName}</DialogTitle>
      <DialogContent dividers sx={{ minHeight: 260 }}>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <EntityPicker value={null} onChange={(o) => { if (o && !picked.some((p) => p.id === o.id)) setPicked([...picked, o]); }} search={search} placeholder="ابحث عن زبون بالاسم أو الكود أو الهاتف..." />
          <Stack direction="row" flexWrap="wrap" gap={1}>
            {picked.map((p) => <Chip key={p.id} label={p.label} onDelete={() => setPicked(picked.filter((x) => x.id !== p.id))} />)}
          </Stack>
          <Typography variant="caption" color="text.secondary">الفواتير السابقة تبقى باسم مندوبها؛ الفواتير الجديدة لهؤلاء الزبائن تُسند تلقائياً إلى هذا المندوب.</Typography>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>إلغاء</Button>
        <Button variant="contained" disabled={saving || picked.length === 0} onClick={() => void save()}>إسناد ({picked.length})</Button>
      </DialogActions>
    </Dialog>
  );
}
