/** Export button for any list: builds the document lazily (so it can fetch every page/filter) and downloads PDF / Excel / CSV. */
import { useState } from 'react';
import { Button, Menu, MenuItem } from '@mui/material';
import { downloadDocument, previewPdf, type ExportDocument, type ExportFormat } from '../../lib/exportClient';
import { toast } from '../../lib/toast';

type Props = {
  /** Build the document to export (may call the API to fetch all rows for the current filters). */
  build: () => Promise<ExportDocument>;
  label?: string;
  disabled?: boolean;
};

export default function ExportMenu({ build, label = '⬇ تصدير', disabled }: Props): JSX.Element {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(kind: ExportFormat | 'preview'): Promise<void> {
    setAnchor(null);
    setBusy(true);
    try {
      const doc = await build();
      if (kind === 'preview') await previewPdf(doc);
      else await downloadDocument(kind, doc);
    } catch {
      toast.error('تعذر تجهيز البيانات للتصدير');
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <Button variant="outlined" size="small" disabled={disabled || busy} onClick={(e) => setAnchor(e.currentTarget)}>
        {busy ? '...' : label}
      </Button>
      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
        <MenuItem onClick={() => void run('xlsx')}>Excel (.xlsx)</MenuItem>
        <MenuItem onClick={() => void run('pdf')}>PDF</MenuItem>
        <MenuItem onClick={() => void run('csv')}>CSV</MenuItem>
        <MenuItem onClick={() => void run('preview')}>معاينة PDF</MenuItem>
      </Menu>
    </>
  );
}
