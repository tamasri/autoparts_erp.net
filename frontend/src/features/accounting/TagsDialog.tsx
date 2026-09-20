/** Create, rename, recolour and delete the tags used to label entries and ledger vouchers. */
import { useEffect, useState } from 'react';
import { Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Stack, TextField, Typography } from '@mui/material';
import { accountingApi, type Tag } from '../../api/endpoints/accounting';
import { unwrapList } from '../../api/apiData';
import { useConfirm } from '../../hooks/useConfirm';
import { extractApiError, toast } from '../../lib/toast';
import { tagChipSx } from './TagChips';

const PALETTE = ['#5c54ff', '#ff8800', '#2e9e5b', '#d64545', '#0f8fa8', '#8e44ad', '#6b7280'];

export default function TagsDialog({ open, onClose, onChanged }: { open: boolean; onClose: () => void; onChanged: () => void }): JSX.Element {
  const [tags, setTags] = useState<Tag[]>([]);
  const [name, setName] = useState('');
  const [color, setColor] = useState(PALETTE[0]);
  const [error, setError] = useState('');
  const { confirm, dialog: confirmDialog } = useConfirm();

  async function load(): Promise<void> {
    try { setTags(unwrapList<Tag>((await accountingApi.tags()).data)); } catch (e: unknown) { setError(extractApiError(e, 'تعذر تحميل الوسوم')); }
  }
  useEffect(() => { if (open) { setError(''); void load(); } }, [open]);

  async function add(): Promise<void> {
    if (!name.trim()) return;
    try { await accountingApi.saveTag(null, { name: name.trim(), color }); setName(''); setError(''); await load(); onChanged(); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر إنشاء الوسم')); }
  }

  async function remove(tag: Tag): Promise<void> {
    if (!(await confirm(`حذف الوسم «${tag.name}»؟ سيُزال من كل ما وُسم به.`, { confirmLabel: 'حذف' }))) return;
    try { await accountingApi.deleteTag(tag.id); toast.success('حُذف الوسم'); await load(); onChanged(); }
    catch (e: unknown) { setError(extractApiError(e, 'تعذر حذف الوسم')); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>إدارة الوسوم</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          {error ? <Alert severity="error">{error}</Alert> : null}
          <Stack direction="row" gap={1}>
            <TextField size="small" fullWidth label="وسم جديد" value={name} onChange={(e) => setName(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') void add(); }} />
            <Button variant="contained" onClick={() => void add()} disabled={!name.trim()}>إضافة</Button>
          </Stack>
          <Stack direction="row" gap={1}>
            {PALETTE.map((c) => (
              <IconButton key={c} size="small" aria-label={c} onClick={() => setColor(c)} sx={{ width: 26, height: 26, bgcolor: c, border: color === c ? '3px solid' : '1px solid', borderColor: color === c ? 'text.primary' : 'divider', '&:hover': { bgcolor: c } }} />
            ))}
          </Stack>
          <Stack gap={1}>
            {tags.length === 0 ? <Typography variant="body2" color="text.secondary">لا توجد وسوم بعد.</Typography> : null}
            {tags.map((t) => <Chip key={t.id} label={t.name} onDelete={() => void remove(t)} sx={{ ...tagChipSx(t.color), justifyContent: 'space-between', '& .MuiChip-deleteIcon': { color: 'rgba(255,255,255,0.8)' } }} />)}
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>إغلاق</Button></DialogActions>
      {confirmDialog}
    </Dialog>
  );
}
