/**
 * features/customers/DeactivateDialog.tsx — AutoPartsERP
 *
 * Replaces the 6 `window.prompt` calls in the customer deactivation flow.
 * Validates the reason with Zod and uses MUI Dialog for proper focus management
 * and RTL rendering.
 *
 * Phase 3 — feat(frontend): replace window.prompt with DeactivateDialog (phase3)
 */
import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import {
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
  Typography,
} from '@mui/material';
import { deactivateReasonSchema, type DeactivateReasonForm } from './schema';
import { useDeactivateCustomer, type Customer } from './queries';
import { toast, extractApiError } from '../../lib/toast';

type Props = {
  open: boolean;
  customer: Customer | null;
  onClose: () => void;
};

export default function DeactivateDialog({ open, customer, onClose }: Props): JSX.Element {
  const {
    control,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<DeactivateReasonForm>({
    resolver: zodResolver(deactivateReasonSchema),
    defaultValues: { reason: '' },
  });

  useEffect(() => {
    if (open) reset({ reason: '' });
  }, [open, reset]);

  const deactivate = useDeactivateCustomer();

  const onSubmit = handleSubmit(async ({ reason }) => {
    if (!customer) return;
    try {
      await deactivate.mutateAsync({ id: customer.id, reason });
      toast.success('تم إلغاء تفعيل العميل');
      onClose();
    } catch (err: unknown) {
      toast.error(extractApiError(err, 'تعذر إلغاء تفعيل العميل'));
    }
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs" dir="rtl">
      <DialogTitle sx={{ color: 'error.main' }}>
        ⚠️ إلغاء تفعيل عميل
      </DialogTitle>

      <DialogContent sx={{ pt: 2 }}>
        {customer && (
          <Typography variant="body2" sx={{ mb: 2, color: 'text.secondary' }}>
            سيتم إلغاء تفعيل العميل <strong>{customer.name}</strong> ({customer.code}).
            يرجى إدخال سبب واضح لأغراض التدقيق.
          </Typography>
        )}

        <Controller
          name="reason"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              label="سبب إلغاء التفعيل *"
              multiline
              rows={3}
              error={!!errors.reason}
              helperText={errors.reason?.message ?? 'يُحفظ في سجل التدقيق'}
              autoFocus
            />
          )}
        />
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose} disabled={isSubmitting}>
          إلغاء
        </Button>
        <Button
          onClick={() => void onSubmit()}
          variant="contained"
          color="error"
          disabled={isSubmitting}
          startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : null}
        >
          {isSubmitting ? 'جارٍ التنفيذ...' : 'تأكيد الإلغاء'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
