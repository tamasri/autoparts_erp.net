/** Builds the printable/exportable document for each warehouse and payment document from the data the screens already load. */
import { api } from './wmsDocumentsApi';
import { num, ymd, type ExportDocument } from './exportClient';
import { unwrapNode } from '../api/apiData';

type Label = (id?: string | null) => string;
const unwrap = <T,>(r: { data: unknown }): T => unwrapNode<T>(r.data) as T;
const STATUS: Record<string, string> = {
  DRAFT: 'مسودة', POSTED: 'مرحّل', SHIPPED: 'مشحون', RECEIVED: 'مستلم', ISSUED: 'مصروف', PICKING: 'قيد السحب', VERIFYING: 'قيد التحقق',
  PENDING_APPROVAL: 'بانتظار الاعتماد', PLANNED: 'مخطط', COUNTING: 'قيد الجرد', APPROVED: 'معتمد',
};
const status = (s: string): string => STATUS[s] ?? s;
const dt = (v?: string | null): string => (v ? ymd(v) : '');
const sum = <T,>(rows: T[], pick: (r: T) => number): number => rows.reduce((a, r) => a + pick(r), 0);

export async function transferDocument(id: string, label: Label): Promise<ExportDocument> {
  const d = unwrap<{
    transferNo: string; sourceWarehouseId: string; destinationWarehouseId: string; status: string; shippedAt?: string; receivedAt?: string; createdAt: string;
    lines: Array<{ itemCode: string; itemName: string; sourceLocationId?: string; destinationLocationId?: string; shippedQty: number; receivedQty: number }>;
  }>(await api.transferOrder(id));
  return {
    title: `أمر تحويل ${d.transferNo}`, subtitle: status(d.status), fileName: `transfer-${d.transferNo}`,
    fields: [
      { label: 'من مستودع', value: label(d.sourceWarehouseId) }, { label: 'إلى مستودع', value: label(d.destinationWarehouseId) },
      { label: 'تاريخ الإنشاء', value: dt(d.createdAt) }, { label: 'تاريخ الشحن', value: dt(d.shippedAt) }, { label: 'تاريخ الاستلام', value: dt(d.receivedAt) },
    ],
    tables: [{
      title: 'الأصناف', columns: ['الرمز', 'الصنف', 'من موقع', 'إلى موقع', 'الكمية المشحونة', 'الكمية المستلمة'],
      rows: d.lines.map((l) => [l.itemCode, l.itemName, label(l.sourceLocationId), label(l.destinationLocationId), num(l.shippedQty), num(l.receivedQty)]),
      totals: ['', 'الإجمالي', '', '', num(sum(d.lines, (l) => l.shippedQty)), num(sum(d.lines, (l) => l.receivedQty))], numericColumns: [4, 5],
    }],
  };
}

export async function issueOrderDocument(id: string, label: Label): Promise<ExportDocument> {
  const d = unwrap<{
    order: { orderNo: string; sourceType: string; warehouseId: string; status: string; issuedAt?: string; createdAt: string };
    lines: Array<{ itemCode: string; itemName: string; requestedQty: number; pickedQty: number; verifiedQty: number; issuedQty: number; sourceLocationId?: string }>;
    pickTasks: Array<{ itemCode: string; locationCode: string; qty: number; status: string }>;
  }>(await api.issueOrder(id));
  return {
    title: `أمر صرف ${d.order.orderNo}`, subtitle: status(d.order.status), fileName: `issue-${d.order.orderNo}`,
    fields: [
      { label: 'المستودع', value: label(d.order.warehouseId) }, { label: 'المصدر', value: d.order.sourceType },
      { label: 'تاريخ الإنشاء', value: dt(d.order.createdAt) }, { label: 'تاريخ الصرف', value: dt(d.order.issuedAt) },
    ],
    tables: [
      {
        title: 'الأصناف', columns: ['الرمز', 'الصنف', 'من موقع', 'مطلوب', 'مسحوب', 'مُتحقَّق', 'مصروف'],
        rows: d.lines.map((l) => [l.itemCode, l.itemName, label(l.sourceLocationId), num(l.requestedQty), num(l.pickedQty), num(l.verifiedQty), num(l.issuedQty)]),
        numericColumns: [3, 4, 5, 6],
      },
      ...(d.pickTasks.length > 0
        ? [{ title: 'مهام السحب', columns: ['الرمز', 'الموقع', 'الكمية', 'الحالة'], rows: d.pickTasks.map((t) => [t.itemCode, t.locationCode, num(t.qty), status(t.status)]), numericColumns: [2] }]
        : []),
    ],
  };
}

export async function cycleCountDocument(id: string, label: Label): Promise<ExportDocument> {
  const d = unwrap<{
    warehouseId: string; scopeType: string; status: string; scheduledFor?: string;
    lines: Array<{ itemCode: string; itemName: string; locationCode: string; systemQty: number; countedQty: number | null; varianceQty: number }>;
  }>(await api.cycleCount(id));
  return {
    title: 'محضر جرد', subtitle: status(d.status), fileName: `cycle-count-${ymd(d.scheduledFor)}`,
    fields: [{ label: 'المستودع', value: label(d.warehouseId) }, { label: 'النطاق', value: d.scopeType }, { label: 'تاريخ التنفيذ', value: dt(d.scheduledFor) }],
    tables: [{
      title: 'نتائج الجرد', columns: ['الرمز', 'الصنف', 'الموقع', 'رصيد النظام', 'المعدود', 'الفرق'],
      rows: d.lines.map((l) => [l.itemCode, l.itemName, l.locationCode, num(l.systemQty), num(l.countedQty), num(l.varianceQty)]),
      totals: ['', 'إجمالي الفروقات', '', '', '', num(sum(d.lines, (l) => l.varianceQty))], numericColumns: [3, 4, 5],
    }],
  };
}

export async function adjustmentDocument(id: string, label: Label): Promise<ExportDocument> {
  const d = unwrap<{
    adjustmentNo: string; adjustmentType: string; warehouseId: string; reasonCode: string; status: string; postedAt?: string; createdAt: string;
    lines: Array<{ itemCode: string; itemName: string; locationId: string; qtyDelta: number; systemQtyBefore: number; systemQtyAfter: number; notes?: string }>;
  }>(await api.adjustment(id));
  return {
    title: `تسوية مخزون ${d.adjustmentNo}`, subtitle: status(d.status), fileName: `adjustment-${d.adjustmentNo}`,
    fields: [
      { label: 'المستودع', value: label(d.warehouseId) }, { label: 'النوع', value: d.adjustmentType }, { label: 'السبب', value: d.reasonCode },
      { label: 'تاريخ الإنشاء', value: dt(d.createdAt) }, { label: 'تاريخ الترحيل', value: dt(d.postedAt) },
    ],
    tables: [{
      title: 'الأصناف', columns: ['الرمز', 'الصنف', 'الموقع', 'قبل', 'التغيير', 'بعد', 'ملاحظات'],
      rows: d.lines.map((l) => [l.itemCode, l.itemName, label(l.locationId), num(l.systemQtyBefore), num(l.qtyDelta), num(l.systemQtyAfter), l.notes ?? '']),
      numericColumns: [3, 4, 5],
    }],
  };
}

export async function receivingDocument(id: string, label: Label, vendor?: string): Promise<ExportDocument> {
  const d = unwrap<{
    documentNo: string; purchaseOrderRef?: string; warehouseId: string; status: string; postedAt?: string; notes?: string;
    lines: Array<{ itemCode: string; itemName: string; expectedQty?: number; receivedQty: number; rejectedQty: number; assignedLocationId?: string; conditionStatus: string }>;
  }>(await api.receiving(id));
  return {
    title: `مستند استلام ${d.documentNo}`, subtitle: status(d.status), fileName: `receiving-${d.documentNo}`,
    fields: [
      { label: 'المستودع', value: label(d.warehouseId) }, { label: 'المورّد', value: vendor ?? '' }, { label: 'مرجع الشراء', value: d.purchaseOrderRef },
      { label: 'تاريخ الترحيل', value: dt(d.postedAt) }, { label: 'ملاحظات', value: d.notes },
    ],
    tables: [{
      title: 'الأصناف المستلمة', columns: ['الرمز', 'الصنف', 'المتوقعة', 'المستلمة', 'المرفوضة', 'الحالة', 'موقع التخزين'],
      rows: d.lines.map((l) => [l.itemCode, l.itemName, num(l.expectedQty), num(l.receivedQty), num(l.rejectedQty), l.conditionStatus, label(l.assignedLocationId)]),
      totals: ['', 'الإجمالي', '', num(sum(d.lines, (l) => l.receivedQty)), num(sum(d.lines, (l) => l.rejectedQty)), '', ''], numericColumns: [2, 3, 4],
    }],
  };
}

export function paymentDocument(
  p: { paymentNumber: string; customerName: string; paymentDate: string; amountSyp: number; amountUsd: number; unallocatedSyp: number; unallocatedUsd: number; isReversed: boolean },
  methodLabel: string,
): ExportDocument {
  return {
    title: `سند قبض ${p.paymentNumber}`, subtitle: p.isReversed ? 'معكوس' : 'فعّال', fileName: `receipt-${p.paymentNumber}`,
    fields: [{ label: 'الزبون', value: p.customerName }, { label: 'التاريخ', value: dt(p.paymentDate) }, { label: 'طريقة الدفع', value: methodLabel }],
    tables: [{
      columns: ['البند', 'ل.س', '$'],
      rows: [['المبلغ المقبوض', num(p.amountSyp), num(p.amountUsd)], ['غير الموزّع على الفواتير', num(p.unallocatedSyp), num(p.unallocatedUsd)]],
      numericColumns: [1, 2],
    }],
    footer: 'يتم توزيع المقبوض على الفواتير المفتوحة الأقدم استحقاقاً.',
  };
}
