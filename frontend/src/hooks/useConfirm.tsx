import { useCallback, useState } from 'react';
import type { ReactNode } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material';

type Pending = { message: string; title: string; confirmLabel: string; resolve: (ok: boolean) => void };

/** A yes/no question in a dialog instead of window.confirm: `if (await confirm('...')) { ... }`, and render `dialog` once in the screen. */
export function useConfirm(): { confirm: (message: string, options?: { title?: string; confirmLabel?: string }) => Promise<boolean>; dialog: ReactNode } {
  const [pending, setPending] = useState<Pending | null>(null);

  const confirm = useCallback((message: string, options?: { title?: string; confirmLabel?: string }) =>
    new Promise<boolean>((resolve) => setPending({ message, title: options?.title ?? 'تأكيد', confirmLabel: options?.confirmLabel ?? 'تأكيد', resolve })), []);

  function answer(ok: boolean): void {
    pending?.resolve(ok);
    setPending(null);
  }

  const dialog = (
    <Dialog open={pending !== null} onClose={() => answer(false)} maxWidth="xs" fullWidth>
      <DialogTitle>{pending?.title}</DialogTitle>
      <DialogContent><DialogContentText>{pending?.message}</DialogContentText></DialogContent>
      <DialogActions>
        <Button onClick={() => answer(false)}>رجوع</Button>
        <Button variant="contained" color="error" onClick={() => answer(true)}>{pending?.confirmLabel}</Button>
      </DialogActions>
    </Dialog>
  );

  return { confirm, dialog };
}
