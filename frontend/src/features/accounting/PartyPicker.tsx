/** Search-and-pick a customer or supplier account (the server is asked as the user types). */
import { useEffect, useState } from 'react';
import { Autocomplete, TextField } from '@mui/material';
import { partiesApi } from '../../api/endpoints/parties';
import { unwrapPaged } from '../../api/apiData';

export type PartyOption = { id: string; label: string };
type PartyRow = { id: string; displayName?: string; displayNameAr?: string };

type Props = {
  value: PartyOption | null;
  onChange: (party: PartyOption | null) => void;
  /** CUSTOMER or VENDOR — only accounts holding that role are offered. */
  role: 'CUSTOMER' | 'VENDOR';
  label?: string;
  disabled?: boolean;
  error?: boolean;
};

const toOption = (p: PartyRow): PartyOption => ({ id: p.id, label: p.displayNameAr || p.displayName || p.id });

export default function PartyPicker({ value, onChange, role, label = 'الحساب (زبون / مورّد)', disabled, error }: Props): JSX.Element {
  const [input, setInput] = useState('');
  const [options, setOptions] = useState<PartyOption[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let live = true;
    const handle = window.setTimeout(() => {
      setLoading(true);
      partiesApi.getParties({ page: 1, pageSize: 20, typeCode: role, isActive: true, searchTerm: input.trim() || undefined })
        .then((r) => { if (live) setOptions(unwrapPaged<PartyRow>(r.data).items.map(toOption)); })
        .catch(() => { if (live) setOptions([]); })
        .finally(() => { if (live) setLoading(false); });
    }, 250);
    return () => { live = false; window.clearTimeout(handle); };
  }, [input, role]);

  return (
    <Autocomplete
      size="small" disabled={disabled} loading={loading} options={value && !options.some((o) => o.id === value.id) ? [value, ...options] : options}
      value={value} onChange={(_, o) => onChange(o)} inputValue={input} onInputChange={(_, v, reason) => { if (reason !== 'reset') setInput(v); }}
      isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(o) => o.label} filterOptions={(o) => o}
      noOptionsText="لا توجد حسابات مطابقة" loadingText="جارٍ البحث..."
      renderInput={(params) => <TextField {...params} label={label} error={error} />}
      sx={{ minWidth: 200 }}
    />
  );
}
