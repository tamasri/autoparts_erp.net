import type { AxiosResponse } from 'axios';
import { toast } from './toast';

/**
 * Governed actions (post an invoice, ship a transfer, ...) answer 202 with `isPending` when they were sent for approval
 * instead of executing. Say so, instead of announcing a success that has not happened yet.
 */
export function notifyResult(response: AxiosResponse, doneMessage: string): 'done' | 'pending' {
  const body = response.data as { isPending?: boolean } | undefined;
  if (body?.isPending) {
    toast.info('أُرسل الطلب للموافقة — سيُنفَّذ بعد اعتماده من مستخدم آخر (أو من قائمة الموافقات)');
    return 'pending';
  }
  toast.success(doneMessage);
  return 'done';
}
