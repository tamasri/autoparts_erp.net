/**
 * The warehouses a user works in, and which of them they manage. A manager approves transfers into or out of their warehouses.
 * Only an administrator changes this (the change is approved like a role change), and nobody changes their own.
 */
import { useEffect, useState } from 'react';
import { Alert, Button, Checkbox, Stack, Switch, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { usersApi, type UserWarehouse } from '../../api/endpoints/users';
import { unwrapList } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';

export default function UserWarehouses({ userId, readOnly, onSaved }: { userId: string; readOnly: boolean; onSaved: () => void }): JSX.Element {
  const [rows, setRows] = useState<UserWarehouse[]>([]);
  const [initial, setInitial] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const signature = (list: UserWarehouse[]): string => list.filter((r) => r.assigned).map((r) => `${r.warehouseId}:${r.isManager}`).sort().join('|');

  useEffect(() => {
    setError('');
    usersApi.warehouses(userId)
      .then((r) => { const list = unwrapList<UserWarehouse>(r.data); setRows(list); setInitial(signature(list)); })
      .catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل المستودعات')));
  }, [userId]);

  const patch = (id: string, change: Partial<UserWarehouse>): void =>
    setRows((all) => all.map((r) => (r.warehouseId !== id ? r : { ...r, ...change, isManager: (change.assigned ?? r.assigned) && (change.isManager ?? r.isManager) })));

  async function save(): Promise<void> {
    setSaving(true); setError('');
    try {
      const res = await usersApi.setWarehouses(userId, rows.filter((r) => r.assigned).map((r) => ({ warehouseId: r.warehouseId, isManager: r.isManager })));
      if (notifyResult(res, 'تم حفظ مستودعات المستخدم') === 'done') setInitial(signature(rows));
      onSaved();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ المستودعات')); }
    finally { setSaving(false); }
  }

  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2" fontWeight={700}>المستودعات</Typography>
      {error ? <Alert severity="error">{error}</Alert> : null}
      <Table size="small">
        <TableHead><TableRow><TableCell>المستودع</TableCell><TableCell align="center">يعمل فيه</TableCell><TableCell align="center">مسؤول (يوافق على التحويلات)</TableCell></TableRow></TableHead>
        <TableBody>
          {rows.length === 0 ? <TableRow><TableCell colSpan={3} align="center" sx={{ color: 'text.secondary' }}>لا توجد مستودعات</TableCell></TableRow> : null}
          {rows.map((r) => (
            <TableRow key={r.warehouseId}>
              <TableCell>{r.name} <Typography component="span" variant="caption" color="text.secondary">({r.code})</Typography></TableCell>
              <TableCell align="center"><Checkbox size="small" disabled={readOnly} checked={r.assigned} onChange={(e) => patch(r.warehouseId, { assigned: e.target.checked })} /></TableCell>
              <TableCell align="center"><Switch size="small" disabled={readOnly || !r.assigned} checked={r.isManager} onChange={(e) => patch(r.warehouseId, { isManager: e.target.checked })} /></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {readOnly ? <Typography variant="caption" color="text.secondary">لا يمكنك تعديل مستودعات حسابك بنفسك.</Typography> : (
        <Button size="small" variant="outlined" sx={{ alignSelf: 'flex-start' }} disabled={saving || signature(rows) === initial} onClick={() => void save()}>حفظ المستودعات</Button>
      )}
    </Stack>
  );
}
