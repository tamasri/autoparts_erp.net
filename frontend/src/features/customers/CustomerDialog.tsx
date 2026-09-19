/**
 * features/customers/CustomerDialog.tsx — AutoPartsERP
 *
 * MUI Dialog + react-hook-form + Zod for create/edit customer.
 * Replaces the inline form in Customers.tsx and eliminates:
 *   - 18 useState calls for form fields
 *   - manual `if (!form.name.trim())` validation
 *   - re-rendering the entire page on each keystroke (RHF uncontrolled mode)
 *   - window.scrollTo hacks (Dialog manages focus/scroll lock)
 *
 * Phase 3 — feat(frontend): CustomerDialog with RHF + Zod + MUI (phase3)
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
  Grid,
  MenuItem,
  TextField,
  Typography,
} from '@mui/material';
import { customerSchema, type CustomerForm } from './schema';
import { useSaveCustomer, type Customer } from './queries';
import { toast, extractApiError } from '../../lib/toast';

// ── Props ────────────────────────────────────────────────────────────────────

type Props = {
  open: boolean;
  onClose: () => void;
  /** null = create mode, string = edit mode */
  editId: string | null;
  /** Populated when editId is set */
  initial?: Partial<Customer>;
};

// ── Default values ───────────────────────────────────────────────────────────

const DEFAULTS: CustomerForm = {
  code: '',
  name: '',
  type: 'RETAIL',
  phone: '',
  phone2: '',
  address: '',
  city: '',
  creditLimitSyp: 0,
  creditLimitUsd: 0,
  paymentTermsDays: 0,
  notes: '',
};

function toDefaults(initial?: Partial<Customer>): CustomerForm {
  if (!initial) return DEFAULTS;
  return {
    code:              initial.code              ?? '',
    name:              initial.name              ?? '',
    type:              (initial.type as CustomerForm['type']) ?? 'RETAIL',
    phone:             initial.phone             ?? '',
    phone2:            initial.phone2            ?? '',
    address:           initial.address           ?? '',
    city:              initial.city              ?? '',
    creditLimitSyp:    Number(initial.creditLimitSyp    ?? 0),
    creditLimitUsd:    Number(initial.creditLimitUsd    ?? 0),
    paymentTermsDays:  Number(initial.paymentTermsDays  ?? 0),
    notes:             initial.notes             ?? '',
  };
}

// ── Component ────────────────────────────────────────────────────────────────

export default function CustomerDialog({ open, onClose, editId, initial }: Props): JSX.Element {
  const isEdit = Boolean(editId);

  // Always use the full customerSchema for the form — in edit mode we simply
  // omit `code` from the submitted payload in useSaveCustomer (which calls PUT).
  // This avoids an RHF Resolver<T> incompatibility between CustomerForm and UpdateCustomerForm.
  const {
    control,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<CustomerForm>({
    resolver: zodResolver(customerSchema),
    defaultValues: DEFAULTS,
  });

  // Populate form when switching from create → edit or opening a different record
  useEffect(() => {
    if (open) {
      reset(toDefaults(initial));
    }
  }, [open, initial, reset]);

  const save = useSaveCustomer(editId);

  const onSubmit = handleSubmit(async (values) => {
    try {
      await save.mutateAsync(values);
      toast.success(isEdit ? 'تم تحديث العميل بنجاح' : 'تم إنشاء العميل بنجاح');
      onClose();
    } catch (err: unknown) {
      toast.error(extractApiError(err, 'تعذر حفظ العميل'));
    }
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm" dir="rtl">
      <DialogTitle>
        {isEdit ? '✏️ تعديل عميل' : '＋ عميل جديد'}
      </DialogTitle>

      <DialogContent sx={{ pt: 2 }}>
        <Grid container spacing={2}>
          {/* Code — disabled in edit mode */}
          {!isEdit && (
            <Grid item xs={12} sm={6}>
              <Controller
                name="code"
                control={control}
                render={({ field }) => (
                  <TextField
                    {...field}
                    label="الكود *"
                    placeholder="مثال: C001"
                    error={!!errors.code}
                    helperText={errors.code?.message}
                  />
                )}
              />
            </Grid>
          )}

          {/* Name */}
          <Grid item xs={12} sm={isEdit ? 12 : 6}>
            <Controller
              name="name"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label="الاسم *"
                  error={!!errors.name}
                  helperText={errors.name?.message}
                />
              )}
            />
          </Grid>

          {/* Type */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="type"
              control={control}
              render={({ field }) => (
                <TextField {...field} select label="النوع" error={!!errors.type} helperText={errors.type?.message}>
                  <MenuItem value="RETAIL">تجزئة</MenuItem>
                  <MenuItem value="WHOLESALE">جملة</MenuItem>
                  <MenuItem value="WORKSHOP">ورشة</MenuItem>
                </TextField>
              )}
            />
          </Grid>

          {/* Phone */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="phone"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label="الهاتف"
                  error={!!errors.phone}
                  helperText={errors.phone?.message}
                />
              )}
            />
          </Grid>

          {/* Phone 2 */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="phone2"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label="هاتف 2"
                  error={!!errors.phone2}
                  helperText={errors.phone2?.message}
                />
              )}
            />
          </Grid>

          {/* City */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="city"
              control={control}
              render={({ field }) => (
                <TextField {...field} label="المدينة" error={!!errors.city} helperText={errors.city?.message} />
              )}
            />
          </Grid>

          {/* Address */}
          <Grid item xs={12}>
            <Controller
              name="address"
              control={control}
              render={({ field }) => (
                <TextField {...field} label="العنوان" error={!!errors.address} helperText={errors.address?.message} />
              )}
            />
          </Grid>

          {/* Credit limits */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="creditLimitSyp"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  type="number"
                  label="الحد الائتماني ل.س"
                  error={!!errors.creditLimitSyp}
                  helperText={errors.creditLimitSyp?.message}
                  inputProps={{ min: 0 }}
                />
              )}
            />
          </Grid>

          <Grid item xs={12} sm={6}>
            <Controller
              name="creditLimitUsd"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  type="number"
                  label="الحد الائتماني $"
                  error={!!errors.creditLimitUsd}
                  helperText={errors.creditLimitUsd?.message}
                  inputProps={{ min: 0 }}
                />
              )}
            />
          </Grid>

          {/* Payment terms */}
          <Grid item xs={12} sm={6}>
            <Controller
              name="paymentTermsDays"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  type="number"
                  label="شروط الدفع (أيام)"
                  error={!!errors.paymentTermsDays}
                  helperText={errors.paymentTermsDays?.message}
                  inputProps={{ min: 0, max: 365 }}
                />
              )}
            />
          </Grid>

          {/* Notes */}
          <Grid item xs={12}>
            <Controller
              name="notes"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  label="ملاحظات"
                  multiline
                  rows={2}
                  error={!!errors.notes}
                  helperText={errors.notes?.message}
                />
              )}
            />
          </Grid>

          {/* Zod field errors — shown as a summary if any */}
          {Object.keys(errors).length > 0 && (
            <Grid item xs={12}>
              <Typography variant="caption" color="error" sx={{ display: 'block', mt: 0.5 }}>
                يرجى تصحيح الأخطاء المذكورة أعلاه
              </Typography>
            </Grid>
          )}
        </Grid>
      </DialogContent>

      <DialogActions>
        <Button onClick={onClose} disabled={isSubmitting}>
          إلغاء
        </Button>
        <Button
          onClick={() => void onSubmit()}
          variant="contained"
          disabled={isSubmitting}
          startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : null}
        >
          {isSubmitting ? 'جارٍ الحفظ...' : '💾 حفظ'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
