/** Create a group or ledger account under a group, or rename / retype an existing one. Saved straight into the ERPNext chart. */
import { useEffect, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Radio, RadioGroup, Stack, TextField } from '@mui/material';
import { accountingApi, type Account } from '../../api/endpoints/accounting';
import { unwrapNode } from '../../api/apiData';
import { extractApiError, toast } from '../../lib/toast';
import AccountPicker from './AccountPicker';
import { accountTypeLabel } from './labels';

export type AccountDialogMode = { kind: 'create'; parent: string | null } | { kind: 'edit'; account: Account };

type Props = { open: boolean; mode: AccountDialogMode | null; accounts: Account[]; onClose: () => void; onSaved: (name: string) => void };

export default function AccountDialog({ open, mode, accounts, onClose, onSaved }: Props): JSX.Element {
  const [types, setTypes] = useState<string[]>([]);
  const [name, setName] = useState('');
  const [parent, setParent] = useState<string | null>(null);
  const [isGroup, setIsGroup] = useState(false);
  const [type, setType] = useState('');
  const [number, setNumber] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => { accountingApi.accountTypes().then((r) => setTypes(unwrapNode<string[]>(r.data) ?? [])).catch(() => undefined); }, []);
  useEffect(() => {
    if (!open || !mode) return;
    setError('');
    if (mode.kind === 'edit') { setName(mode.account.accountName); setType(mode.account.accountType ?? ''); setIsGroup(mode.account.isGroup); setParent(mode.account.parentAccount); setNumber(''); }
    else { setName(''); setType(''); setIsGroup(false); setParent(mode.parent); setNumber(''); }
  }, [open, mode]);

  const editing = mode?.kind === 'edit';
  const groups = accounts.filter((a) => a.isGroup);

  async function save(): Promise<void> {
    if (!mode) return;
    if (name.trim().length < 2) { setError('اكتب اسم الحساب'); return; }
    setSaving(true); setError('');
    try {
      let saved: string;
      if (mode.kind === 'edit') {
        const body = { accountName: name.trim() !== mode.account.accountName ? name.trim() : undefined, accountType: !mode.account.isGroup && type && type !== mode.account.accountType ? type : undefined };
        if (!body.accountName && !body.accountType) { onClose(); return; }
        saved = unwrapNode<string>((await accountingApi.updateAccount(mode.account.name, body)).data) ?? mode.account.name;
      } else {
        if (!parent) { setError('اختر المجموعة الأم'); setSaving(false); return; }
        saved = unwrapNode<string>((await accountingApi.createAccount({ accountName: name.trim(), parentAccount: parent, isGroup, accountType: isGroup ? null : type || null, accountNumber: number.trim() || null })).data) ?? name;
      }
      toast.success(editing ? 'تم تعديل الحساب' : 'تم إنشاء الحساب');
      onSaved(saved); onClose();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ الحساب')); }
    finally { setSaving(false); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{editing ? 'تعديل حساب' : 'حساب جديد في الشجرة'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          {!editing ? <AccountPicker accounts={groups} value={parent} onChange={(a) => setParent(a?.name ?? null)} label="المجموعة الأم *" /> : null}
          <TextField size="small" label="اسم الحساب *" value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          {editing ? <Alert severity="info" icon={false} sx={{ py: 0 }}>تغيير الاسم يغيّر الاسم الكامل للحساب في ERPNext؛ القيود المرحّلة تتبعه تلقائياً، أما المسودات فأعد اختيار الحساب فيها.</Alert> : null}
          {!editing ? (
            <RadioGroup row value={isGroup ? 'group' : 'ledger'} onChange={(e) => setIsGroup(e.target.value === 'group')}>
              <FormControlLabel value="ledger" control={<Radio size="small" />} label="حساب حركة (تُرحَّل عليه القيود)" />
              <FormControlLabel value="group" control={<Radio size="small" />} label="مجموعة" />
            </RadioGroup>
          ) : null}
          {!isGroup ? (
            <TextField select size="small" label="نوع الحساب" value={type} onChange={(e) => setType(e.target.value)} helperText="يحدد سلوكه: صندوق، مصرف، ذمم زبائن، ذمم موردين، ...">
              <MenuItem value="">بدون نوع</MenuItem>
              {types.map((t) => <MenuItem key={t} value={t}>{accountTypeLabel(t)} {accountTypeLabel(t) !== t ? `(${t})` : ''}</MenuItem>)}
            </TextField>
          ) : null}
          {!editing ? <TextField size="small" label="رقم الحساب (اختياري)" value={number} onChange={(e) => setNumber(e.target.value)} inputProps={{ dir: 'ltr', maxLength: 20 }} /> : null}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>إلغاء</Button><Button variant="contained" disabled={saving} onClick={() => void save()}>حفظ</Button></DialogActions>
    </Dialog>
  );
}
