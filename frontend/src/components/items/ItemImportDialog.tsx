/** Item import from Excel/CSV, on the shared import dialog. */
import { itemsApi } from '../../api/endpoints/items';
import { unwrapNode } from '../../api/apiData';
import ImportDialog, { type ImportSummary } from '../ui/ImportDialog';

type Raw = {
  dryRun: boolean; total: number; created: number; duplicates: number; failed: number;
  rows: Array<{ rowNumber: number; code?: string; status: 'OK' | 'ERROR' | 'DUPLICATE'; message?: string }>;
};

const STATUS = { OK: 'ok', ERROR: 'error', DUPLICATE: 'skipped' } as const;

async function upload(file: File, dryRun: boolean): Promise<ImportSummary> {
  const data = unwrapNode<Raw>((await itemsApi.importFile(file, dryRun)).data) as Raw;
  return {
    dryRun: data.dryRun, total: data.total, created: data.created, skipped: data.duplicates, failed: data.failed,
    rows: data.rows.map((r) => ({ rowNumber: r.rowNumber, label: r.code, status: STATUS[r.status], message: r.message })),
  };
}

export default function ItemImportDialog({ open, onClose, onImported }: { open: boolean; onClose: () => void; onImported: () => void }): JSX.Element {
  return (
    <ImportDialog
      open={open} onClose={onClose} onImported={onImported} title="استيراد أصناف من Excel / CSV" noun="صنف" labelHeader="الرمز" templateFileName="items-template"
      intro="حمّل القالب، عبّئ الأصناف، ثم ارفعه. يُفحص الملف أولاً دون كتابة أي شيء. الأصناف الموجودة تُتجاوز ولا تُعدَّل، وإعادة رفع نفس الملف لا تُكرّر شيئاً."
      downloadTemplate={async (format) => (await itemsApi.importTemplate(format)).data as Blob}
      upload={upload}
    />
  );
}
