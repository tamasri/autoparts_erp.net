/**
 * Print / export client. Every screen describes what it is showing as an ExportDocument and the server renders it as
 * PDF, Excel or CSV (one engine, Arabic RTL, embedded fonts). Official documents (invoice, receipt, statement) have their own
 * PDF endpoints; both kinds are a {@link PdfSource}, printed, previewed and downloaded the same way.
 */
import type { AxiosResponse } from 'axios';
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

/** Where a PDF comes from: the generic export engine, or a document's own endpoint. */
export type PdfSource = () => Promise<Blob>;

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

/** The PDF of any ExportDocument, from the shared engine. */
export const exportPdf = (doc: ExportDocument): PdfSource => () => render('pdf', doc);

/** The PDF of a document endpoint (`invoicesApi.getPdf(id)` ...). */
export const endpointPdf = (request: () => Promise<AxiosResponse>): PdfSource => async () => (await request()).data as Blob;

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

const asPdf = (blob: Blob): Blob => (blob.type === 'application/pdf' ? blob : new Blob([blob], { type: 'application/pdf' }));

export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 30000);
}

/**
 * Whether a PDF can be printed from a hidden frame. Chrome, Edge and Firefox on a desktop can; Safari prints such a frame blank
 * and phones have no PDF viewer in a frame — there the PDF opens in its own tab, whose print button works everywhere.
 */
function framePrintWorks(): boolean {
  const ua = navigator.userAgent;
  const safari = /^((?!chrome|chromium|android|crios|fxios|edg).)*safari/i.test(ua);
  const mobile = /android|iphone|ipad|ipod|mobile/i.test(ua);
  const viewer = (navigator as Navigator & { pdfViewerEnabled?: boolean }).pdfViewerEnabled ?? true;
  return !safari && !mobile && viewer;
}

/** Opens a tab now (while the click still allows pop-ups) and points it at the PDF once it is ready. */
async function showInTab(source: PdfSource, errorMessage: string): Promise<void> {
  const tab = window.open('', '_blank');
  try {
    const url = URL.createObjectURL(asPdf(await source()));
    if (tab) tab.location.href = url;
    else window.location.assign(url);
    window.setTimeout(() => URL.revokeObjectURL(url), 5 * 60000);
  } catch (e) {
    tab?.close();
    toast.error(await blobError(e, errorMessage));
  }
}

/** Downloads the PDF under <paramref name="fileName"/>. */
export async function downloadPdf(source: PdfSource, fileName: string): Promise<void> {
  try {
    saveBlob(asPdf(await source()), fileName.endsWith('.pdf') ? fileName : `${fileName}.pdf`);
  } catch (e) {
    toast.error(await blobError(e, 'تعذر تنزيل ملف PDF'));
  }
}

/** Opens the PDF in a new tab (the browser's own viewer: zoom, search, print). */
export async function previewPdfFrom(source: PdfSource): Promise<void> {
  await showInTab(source, 'تعذر عرض الملف');
}

/**
 * Prints the PDF: through a hidden frame where that works (no pop-up, the print dialog opens at once), otherwise in a new tab.
 * A frame that cannot print (a browser policy) falls back to the tab as well.
 */
export async function printPdf(source: PdfSource): Promise<void> {
  if (!framePrintWorks()) {
    await showInTab(source, 'تعذر الطباعة');
    return;
  }

  let blob: Blob;
  try {
    blob = asPdf(await source());
  } catch (e) {
    toast.error(await blobError(e, 'تعذر الطباعة'));
    return;
  }

  const url = URL.createObjectURL(blob);
  const frame = document.createElement('iframe');
  frame.setAttribute('aria-hidden', 'true');
  frame.style.cssText = 'position:fixed;left:-10000px;top:0;width:1px;height:1px;border:0;opacity:0';
  const cleanUp = (): void => { frame.remove(); URL.revokeObjectURL(url); };
  frame.onload = () => {
    // The PDF viewer inside the frame needs a moment after "load" before print() picks up the document.
    window.setTimeout(() => {
      try {
        frame.contentWindow?.focus();
        frame.contentWindow?.print();
        window.setTimeout(cleanUp, 60000);
      } catch {
        cleanUp();
        const tab = window.open(URL.createObjectURL(blob), '_blank');
        if (!tab) saveBlob(blob, 'document.pdf');
      }
    }, 300);
  };
  frame.src = url;
  document.body.appendChild(frame);
}

export async function downloadDocument(format: ExportFormat, doc: ExportDocument): Promise<void> {
  if (format === 'pdf') {
    await downloadPdf(exportPdf(doc), doc.fileName ?? doc.title);
    return;
  }

  try {
    saveBlob(await render(format, doc), `${doc.fileName ?? doc.title}.${format}`);
  } catch (e) {
    toast.error(await blobError(e, 'تعذر إنشاء الملف'));
  }
}

/** Opens the rendered PDF of a document in a new tab. */
export const previewPdf = (doc: ExportDocument): Promise<void> => previewPdfFrom(exportPdf(doc));

/** Prints the rendered PDF of a document. */
export const printDocument = (doc: ExportDocument): Promise<void> => printPdf(exportPdf(doc));
