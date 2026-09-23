import { useEffect, useState } from 'react';
import { MenuItem, TextField } from '@mui/material';
import { reasonCodesApi } from '../../api/endpoints/reasonCodes';
import { unwrapList } from '../../api/apiData';

type Reason = { code: string; description: string };

type Props = {
  /** Reason-code category, e.g. STOCK_ADJUST, VOID_INVOICE, PRICE_OVERRIDE. */
  category: string;
  value: string;
  onChange: (code: string) => void;
  label?: string;
  id?: string;
};

/** Dropdown of the configured reason codes for one category (replaces typing a code by hand). */
export default function ReasonCodeSelect({ category, value, onChange, label = 'السبب', id }: Props): JSX.Element {
  const [reasons, setReasons] = useState<Reason[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let live = true;
    reasonCodesApi.getByCategory(category)
      .then((r) => { if (live) setReasons(unwrapList<Reason>(r.data)); })
      .catch(() => { if (live) setReasons([]); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [category]);

  return (
    <TextField id={id} select size="small" fullWidth label={label} value={reasons.some((r) => r.code === value) ? value : ''} disabled={loading}
      onChange={(e) => onChange(e.target.value)} SelectProps={{ displayEmpty: true }}>
      <MenuItem value=""><em>{loading ? 'جارٍ التحميل...' : '— اختر السبب —'}</em></MenuItem>
      {reasons.map((r) => <MenuItem key={r.code} value={r.code}>{r.description} ({r.code})</MenuItem>)}
    </TextField>
  );
}
