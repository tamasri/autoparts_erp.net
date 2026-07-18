import { toast as sonnerToast } from 'sonner';

export const toast = {
  success: (msg = 'تمت العملية بنجاح') => sonnerToast.success(msg, { duration: 3000 }),
  error: (msg = 'حدث خطأ، يرجى المحاولة مجدداً') => sonnerToast.error(msg, { duration: 5000 }),
  info: (msg: string) => sonnerToast(msg, { duration: 4000 }),
};

export function extractApiError(e: unknown, fallback = 'حدث خطأ، يرجى المحاولة مجدداً'): string {
  const r = e as { response?: { data?: { detail?: string; message?: string; title?: string } } };
  return (
    r.response?.data?.detail ??
    r.response?.data?.message ??
    r.response?.data?.title ??
    fallback
  );
}
