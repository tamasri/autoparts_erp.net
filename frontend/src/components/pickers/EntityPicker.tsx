import { useEffect, useRef, useState } from 'react';
import { Autocomplete, Box, CircularProgress, TextField, Typography } from '@mui/material';

export type PickerOption = { id: string; label: string; sublabel?: string; data?: unknown };

type Props = {
  value: PickerOption | null;
  onChange: (option: PickerOption | null) => void;
  /** Called with the typed text (debounced); must query the SERVER — never filter a preloaded list. */
  search: (text: string) => Promise<PickerOption[]>;
  placeholder?: string;
  label?: string;
  id?: string;
  disabled?: boolean;
};

/**
 * Searchable combobox for referencing another record (customer, supplier, ...), built on MUI Autocomplete.
 * One page of server results at a time, so it scales to any number of records.
 */
export default function EntityPicker({ value, onChange, search, placeholder = 'ابحث...', label, id, disabled }: Props): JSX.Element {
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<PickerOption[]>([]);
  const [loading, setLoading] = useState(false);
  const requestId = useRef(0);
  const searchRef = useRef(search);
  searchRef.current = search;

  useEffect(() => {
    if (!open) return undefined;
    const handle = window.setTimeout(() => {
      const current = ++requestId.current;
      setLoading(true);
      searchRef.current(text.trim())
        .then((opts) => { if (current === requestId.current) setOptions(opts); })
        .catch(() => { if (current === requestId.current) setOptions([]); })
        .finally(() => { if (current === requestId.current) setLoading(false); });
    }, 250);
    return () => window.clearTimeout(handle);
  }, [text, open]);

  return (
    <Autocomplete
      id={id}
      size="small"
      fullWidth
      open={open}
      onOpen={() => setOpen(true)}
      onClose={() => setOpen(false)}
      value={value}
      onChange={(_, v) => onChange(v)}
      inputValue={text}
      onInputChange={(_, v, reason) => { if (reason === 'input' || reason === 'clear') setText(v); if (reason === 'reset') setText(''); }}
      options={value && !options.some((o) => o.id === value.id) ? [value, ...options] : options}
      filterOptions={(x) => x}
      getOptionLabel={(o) => o.label}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      loading={loading}
      disabled={disabled}
      loadingText="جارٍ البحث..."
      noOptionsText="لا نتائج"
      renderOption={(props, o) => {
        const { key, ...rest } = props as typeof props & { key: string };
        return (
          <Box component="li" key={key} {...rest} sx={{ display: 'block !important' }}>
            <Typography fontWeight={600} variant="body2">{o.label}</Typography>
            {o.sublabel ? <Typography variant="caption" color="text.secondary">{o.sublabel}</Typography> : null}
          </Box>
        );
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          placeholder={value ? undefined : placeholder}
          helperText={value?.sublabel}
          InputProps={{ ...params.InputProps, endAdornment: <>{loading ? <CircularProgress size={16} /> : null}{params.InputProps.endAdornment}</> }}
        />
      )}
    />
  );
}
