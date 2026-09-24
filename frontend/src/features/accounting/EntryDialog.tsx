/**
 * A manual accounting entry: pick its type (receipt, payment, contra, journal, ...), date and lines. Debits must equal credits.
 * A line on a receivable/payable account also names the customer/supplier. Saved as a draft, or posted straight away.
 */
import { useEffect, useMemo, useState } from 'react';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, MenuItem, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import { accountingApi, type Account, type EntryType, type JournalEntryDetail, type SaveEntryBody } from '../../api/endpoints/accounting';
import { unwrapNode } from '../../api/apiData';
import { extractApiError } from '../../lib/toast';
import { notifyResult } from '../../lib/notify';
import { money, round4, today } from '../../lib/money';
import AccountPicker from './AccountPicker';
import PartyPicker, { type PartyOption } from './PartyPicker';
import { KIND_HINT, PARTY_ACCOUNT_TYPES } from './labels';

type Line = { key: number; account: Account | null; party: PartyOption | null; debit: string; credit: string; narration: string };

let counter = 0;
const blank = (): Line => ({ key: ++counter, account: null, party: null, debit: '', credit: '', narration: '' });
const amount = (text: string): number => { const n = Number(text.replace(/,/g, '')); return Number.isFinite(n) && n > 0 ? n : 0; };

type Props = {
  open: boolean;
  /** Draft to edit, or null for a new entry. */
  editId: string | null;
  types: EntryType[];
  accounts: Account[];
  onClose: () => void;
  onSaved: () => void;
};

export default function EntryDialog({ open, editId, types, accounts, onClose, onSaved }: Props): JSX.Element {
  const [typeId, setTypeId] = useState('');
  const [date, setDate] = useState(today());
  const [narration, setNarration] = useState('');
  const [reference, setReference] = useState('');
  const [lines, setLines] = useState<Line[]>([blank(), blank()]);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const activeTypes = useMemo(() => types.filter((t) => t.isActive), [types]);
  const type = types.find((t) => t.id === typeId);

  useEffect(() => {
    if (!open) return;
    setError('');
    if (!editId) {
      setTypeId(activeTypes.find((t) => t.kind === 'JOURNAL')?.id ?? activeTypes[0]?.id ?? '');
      setDate(today()); setNarration(''); setReference(''); setLines([blank(), blank()]);
      return;
    }
    accountingApi.entry(editId).then((r) => {
      const d = unwrapNode<JournalEntryDetail>(r.data);
      if (!d) return;
      setTypeId(types.find((t) => t.code === d.entry.typeCode)?.id ?? '');
      setDate(d.entry.entryDate); setNarration(d.entry.narration ?? ''); setReference(d.referenceNumber ?? '');
      setLines(d.lines.map((l) => ({
        key: ++counter, account: accounts.find((a) => a.name === l.account) ?? null, party: l.partyId ? { id: l.partyId, label: l.partyName ?? '' } : null,
        debit: l.debit ? String(l.debit) : '', credit: l.credit ? String(l.credit) : '', narration: l.narration ?? '',
      })));
    }).catch((e: unknown) => setError(extractApiError(e, 'تعذر تحميل القيد')));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editId]);

  const debit = round4(lines.reduce((s, l) => s + amount(l.debit), 0));
  const credit = round4(lines.reduce((s, l) => s + amount(l.credit), 0));
  const balanced = debit > 0 && Math.abs(debit - credit) < 0.0001;

  const patch = (key: number, change: Partial<Line>): void => setLines((all) => all.map((l) => (l.key === key ? { ...l, ...change } : l)));

  function validate(): string {
    if (!typeId) return 'اختر نوع القيد';
    if (lines.length < 2) return 'القيد يحتاج سطرين على الأقل';
    for (const [i, l] of lines.entries()) {
      if (!l.account) return `السطر ${i + 1}: اختر الحساب`;
      if ((amount(l.debit) > 0) === (amount(l.credit) > 0)) return `السطر ${i + 1}: أدخل مبلغاً مديناً أو دائناً (وليس الاثنين)`;
      const partyRole = PARTY_ACCOUNT_TYPES[l.account.accountType ?? ''];
      if (partyRole && !l.party) return `السطر ${i + 1}: «${l.account.accountName}» يحتاج تحديد ${partyRole === 'CUSTOMER' ? 'الزبون' : 'المورّد'}`;
    }
    if (!balanced) return `القيد غير متوازن: المدين ${money(debit)} والدائن ${money(credit)}`;
    return '';
  }

  async function save(post: boolean): Promise<void> {
    const problem = validate();
    if (problem) { setError(problem); return; }
    const body: SaveEntryBody = {
      entryTypeId: typeId, entryDate: date, narration: narration.trim() || null, referenceNumber: reference.trim() || null,
      lines: lines.map((l) => ({ account: l.account!.name, partyId: l.party?.id ?? null, debit: amount(l.debit), credit: amount(l.credit), narration: l.narration.trim() || null })),
    };
    setSaving(true); setError('');
    try {
      const saved = editId ? await accountingApi.updateEntry(editId, body) : await accountingApi.createEntry(body);
      if (post) {
        const id = (saved.data as { data: string }).data;
        notifyResult(await accountingApi.postEntry(id), 'تم ترحيل القيد وإرساله إلى دفتر الأستاذ');
      }
      onSaved(); onClose();
    } catch (e: unknown) { setError(extractApiError(e, 'تعذر حفظ القيد')); }
    finally { setSaving(false); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="lg">
      <DialogTitle>{editId ? 'تعديل مسودة قيد' : 'قيد جديد'}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Stack direction={{ xs: 'column', md: 'row' }} gap={2}>
            <TextField select size="small" label="نوع القيد" value={typeId} onChange={(e) => setTypeId(e.target.value)} sx={{ minWidth: 220 }}
              disabled={Boolean(editId)} helperText={editId ? 'الرقم من سلسلة هذا النوع، فلا يتغير النوع بعد الترقيم' : undefined}>
              {activeTypes.map((t) => <MenuItem key={t.id} value={t.id}>{t.nameAr} ({t.prefix})</MenuItem>)}
            </TextField>
            <TextField size="small" type="date" label="التاريخ" value={date} onChange={(e) => setDate(e.target.value)} InputLabelProps={{ shrink: true }} />
            <TextField size="small" label="مرجع (رقم شيك / إيصال)" value={reference} onChange={(e) => setReference(e.target.value)} inputProps={{ maxLength: 60 }} />
            <TextField size="small" fullWidth label="البيان" value={narration} onChange={(e) => setNarration(e.target.value)} inputProps={{ maxLength: 500 }} />
          </Stack>
          {type ? <Alert severity="info" icon={false} sx={{ py: 0 }}>{KIND_HINT[type.kind]}</Alert> : null}

          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small">
              <TableHead>
                <TableRow sx={{ '& th': { fontWeight: 700, bgcolor: 'action.hover' } }}>
                  <TableCell width={36}>#</TableCell><TableCell sx={{ minWidth: 240 }}>الحساب</TableCell><TableCell sx={{ minWidth: 220 }}>الزبون / المورّد</TableCell>
                  <TableCell width={130}>مدين ($)</TableCell><TableCell width={130}>دائن ($)</TableCell><TableCell sx={{ minWidth: 160 }}>بيان السطر</TableCell><TableCell width={40} />
                </TableRow>
              </TableHead>
              <TableBody>
                {lines.map((l, i) => {
                  const role = PARTY_ACCOUNT_TYPES[l.account?.accountType ?? ''];
                  return (
                    <TableRow key={l.key}>
                      <TableCell>{i + 1}</TableCell>
                      <TableCell><AccountPicker accounts={accounts} value={l.account?.name ?? null} label="" onChange={(a) => patch(l.key, { account: a, party: null })} /></TableCell>
                      <TableCell>{role ? <PartyPicker role={role} label="" value={l.party} onChange={(p) => patch(l.key, { party: p })} /> : <Typography variant="caption" color="text.disabled">—</Typography>}</TableCell>
                      <TableCell><TextField size="small" value={l.debit} inputProps={{ inputMode: 'decimal', dir: 'ltr' }} onChange={(e) => patch(l.key, { debit: e.target.value, credit: e.target.value ? '' : l.credit })} /></TableCell>
                      <TableCell><TextField size="small" value={l.credit} inputProps={{ inputMode: 'decimal', dir: 'ltr' }} onChange={(e) => patch(l.key, { credit: e.target.value, debit: e.target.value ? '' : l.debit })} /></TableCell>
                      <TableCell><TextField size="small" value={l.narration} onChange={(e) => patch(l.key, { narration: e.target.value })} /></TableCell>
                      <TableCell><IconButton size="small" aria-label="حذف السطر" disabled={lines.length <= 2} onClick={() => setLines((all) => all.filter((x) => x.key !== l.key))}>✕</IconButton></TableCell>
                    </TableRow>
                  );
                })}
                <TableRow sx={{ '& td': { fontWeight: 800, bgcolor: 'action.hover' } }}>
                  <TableCell colSpan={3}>
                    <Button size="small" onClick={() => setLines((all) => [...all, blank()])}>＋ إضافة سطر</Button>
                  </TableCell>
                  <TableCell sx={{ direction: 'ltr', textAlign: 'right' }}>{money(debit)}</TableCell>
                  <TableCell sx={{ direction: 'ltr', textAlign: 'right' }}>{money(credit)}</TableCell>
                  <TableCell colSpan={2}><Chip size="small" color={balanced ? 'success' : 'warning'} label={balanced ? 'متوازن' : `الفرق ${money(Math.abs(debit - credit))}`} /></TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>إلغاء</Button>
        <Button variant="outlined" disabled={saving} onClick={() => void save(false)}>حفظ كمسودة</Button>
        <Button variant="contained" disabled={saving} onClick={() => void save(true)}>حفظ وترحيل</Button>
      </DialogActions>
    </Dialog>
  );
}
