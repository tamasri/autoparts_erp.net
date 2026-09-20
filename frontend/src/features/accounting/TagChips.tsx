/** The tags on an entry or ledger voucher, with a small menu to put more on or take them off. */
import { useState } from 'react';
import { Checkbox, Chip, IconButton, ListItemText, Menu, MenuItem, Stack, Typography } from '@mui/material';
import { accountingApi, type Tag, type TagTarget } from '../../api/endpoints/accounting';
import { extractApiError, toast } from '../../lib/toast';

export const tagChipSx = (color: string) => ({ bgcolor: color, color: '#fff', fontWeight: 600 });

type Props = {
  tags: Tag[];
  /** Every tag that exists, for the menu. */
  allTags: Tag[];
  target: TagTarget;
  canEdit: boolean;
  onChanged: () => void;
};

export default function TagChips({ tags, allTags, target, canEdit, onChanged }: Props): JSX.Element {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const on = new Set(tags.map((t) => t.id));

  async function toggle(tag: Tag): Promise<void> {
    try {
      if (on.has(tag.id)) await accountingApi.detachTag(tag.id, target); else await accountingApi.attachTag(tag.id, target);
      onChanged();
    } catch (e: unknown) { toast.error(extractApiError(e, 'تعذر تعديل الوسم')); }
  }

  return (
    <Stack direction="row" gap={0.5} alignItems="center" flexWrap="wrap">
      {tags.map((t) => <Chip key={t.id} size="small" label={t.name} sx={tagChipSx(t.color)} />)}
      {canEdit ? (
        <>
          <IconButton size="small" aria-label="وسوم" onClick={(e) => setAnchor(e.currentTarget)} sx={{ width: 24, height: 24, fontSize: 14 }}>＋</IconButton>
          <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
            {allTags.length === 0 ? <MenuItem disabled><Typography variant="body2">لا توجد وسوم — أنشئها من «إدارة الوسوم»</Typography></MenuItem> : null}
            {allTags.map((t) => (
              <MenuItem key={t.id} dense onClick={() => void toggle(t)}>
                <Checkbox size="small" checked={on.has(t.id)} sx={{ p: 0.5, mr: 1 }} />
                <ListItemText primary={t.name} />
                <span style={{ width: 12, height: 12, borderRadius: 6, background: t.color, marginInlineStart: 12 }} />
              </MenuItem>
            ))}
          </Menu>
        </>
      ) : null}
    </Stack>
  );
}
