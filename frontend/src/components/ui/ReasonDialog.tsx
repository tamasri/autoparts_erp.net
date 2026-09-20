/** Asks for a written reason (reject, void, reverse, ...). Replaces window.prompt. */
import { useEffect, useState } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';

type Props = {
  open: boolean;
  title: string;
  confirmLabel?: string;
  minLength?: number;
  onClose: () => void;
  onConfirm: (reason: string) => void | Promise<void>;
};

export default function ReasonDialog({ open, title, confirmLabel = 'تأكيد', minLength = 3, onClose, onConfirm }: Props): JSX.Element {
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (open) setReason(''); }, [open]);

  const valid = reason.trim().length >= minLength;

  async function confirm(): Promise<void> {
    setBusy(true);
    try { await onConfirm(reason.trim()); } finally { setBusy(false); }
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <TextField autoFocus fullWidth multiline minRows={2} size="small" label="السبب *" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ mt: 1 }}
          helperText={valid || reason.length === 0 ? ' ' : `اكتب ${minLength} أحرف على الأقل`} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>رجوع</Button>
        <Button variant="contained" color="error" disabled={!valid || busy} onClick={() => void confirm()}>{confirmLabel}</Button>
      </DialogActions>
    </Dialog>
  );
}
