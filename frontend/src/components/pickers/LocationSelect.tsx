import { MenuItem, TextField } from '@mui/material';
import { useLocations } from '../../hooks/useLocations';

type Props = {
  value: string;
  onChange: (id: string, code: string) => void;
  /** WAREHOUSE, SHELF, VEHICLE, RETURN, QUARANTINE — omit for all. */
  type?: string;
  placeholder?: string;
  label?: string;
  allowEmpty?: boolean;
  id?: string;
};

/** Dropdown of real storage locations. Replaces every "type the Location/Warehouse ID" text box. */
export default function LocationSelect({ value, onChange, type = '', placeholder = '— اختر الموقع —', label, allowEmpty = true, id }: Props): JSX.Element {
  const { locations, loading } = useLocations(type);
  return (
    <TextField
      id={id}
      select
      size="small"
      fullWidth
      label={label}
      value={locations.some((l) => l.id === value) ? value : ''}
      disabled={loading}
      onChange={(e) => {
        const loc = locations.find((l) => l.id === e.target.value);
        onChange(e.target.value, loc?.code ?? '');
      }}
      SelectProps={{ displayEmpty: true }}
    >
      {allowEmpty ? <MenuItem value=""><em>{loading ? 'جارٍ التحميل...' : placeholder}</em></MenuItem> : null}
      {locations.map((l) => <MenuItem key={l.id} value={l.id}>{l.code} — {l.name}</MenuItem>)}
    </TextField>
  );
}
