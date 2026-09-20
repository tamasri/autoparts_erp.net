/**
 * Print / export client. Every screen describes what it is showing as an ExportDocument and the server renders it as
 * PDF, Excel or CSV (one engine, Arabic RTL, embedded fonts). Nothing here re-implements a file format in the browser.
 */
import { client } from '../api/client';
import { extractApiError, toast } from './toast';

export type ExportField = { label: string; value?: string | null };
export type ExportTable = {
  title?: string;
  columns: string[];
  rows: Array<Array<string | null | undefined>>;
  totals?: Array<string | null | undefined>;
  /** 0-based column indexes holding numbers (right aligned in PDF, numeric cells in Excel). */
  numericColumns?: number[];
};
export type ExportDocument = {
  title: string;
  subtitle?: string;
  fields: ExportField[];
  tables: ExportTable[];
  footer?: string;
  fileName?: string;
};
export type ExportFormat = 'pdf' | 'xlsx' | 'csv';

/** Number → plain string for export (the server formats it; no thousands separators here). */
export const num = (v: unknown): string => (v === null || v === undefined || v === '' ? '' : String(Number(v)));

/** Local date as yyyy-MM-dd. */
export const ymd = (v?: string | Date | null): string => {
  if (!v) return '';
  const d = typeof v === 'string' ? new Date(v) : v;
  return Number.isNaN(d.getTime()) ? String(v) : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
};

async function render(format: ExportFormat, doc: ExportDocument): Promise<Blob> {
  const res = await client.post(`/exports/${format}`, doc, { responseType: 'blob', timeout: 120000 });
  return res.data as Blob;
}

async function blobError(e: unknown, fallback: string): Promise<string> {
  const data = (e as { response?: { data?: unknown } }).response?.data;
  if (data instanceof Blob) {
    try {
      const parsed = JSON.parse(await data.text()) as { detail?: string; title?: string };
      return parsed.detail ?? parsed.title ?? fallback;
    } catch {
      return fallback;
    }
  }
  return extractApiError(e, fallback);
}

export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export async function downloadDocument(format: ExportFormat, doc: ExportDocument): Promise<void> {
  try {
    const blob = await render(format, doc);
    saveBlob(blob, `${doc.fileName ?? doc.title}.${format}`);
  } catch (e) {
    toast.error(await blobError(e, 'تعذر إنشاء الملف'));
  }
}

/** Opens the rendered PDF in a new tab (the browser's own viewer: zoom, search, print). */
export async function previewPdf(doc: ExportDocument): Promise<void> {
  const tab = window.open('', '_blank');
  try {
    const blob = await render('pdf', doc);
    const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
    if (tab) tab.location.href = url;
    else window.open(url, '_blank');
    window.setTimeout(() => URL.revokeObjectURL(url), 60000);
  } catch (e) {
    tab?.close();
    toast.error(await blobError(e, 'تعذر عرض الملف'));
  }
}

/** Prints the rendered PDF through a hidden frame (no pop-up needed). */
export async function printDocument(doc: ExportDocument): Promise<void> {
  try {
    const blob = await render('pdf', doc);
    const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
    const frame = document.createElement('iframe');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0';
    frame.src = url;
    frame.onload = () => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      window.setTimeout(() => { frame.remove(); URL.revokeObjectURL(url); }, 60000);
    };
    document.body.appendChild(frame);
  } catch (e) {
    toast.error(await blobError(e, 'تعذر الطباعة'));
  }
}
