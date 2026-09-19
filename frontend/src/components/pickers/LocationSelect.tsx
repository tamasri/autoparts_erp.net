import { useLocations } from '../../hooks/useLocations';

type Props = {
  value: string;
  onChange: (id: string, code: string) => void;
  /** WAREHOUSE, SHELF, VEHICLE, RETURN, QUARANTINE — omit for all. */
  type?: string;
  placeholder?: string;
  allowEmpty?: boolean;
  className?: string;
  id?: string;
};

/** Dropdown of real storage locations. Replaces every "type the Location/Warehouse ID" text box. */
export default function LocationSelect({ value, onChange, type = '', placeholder = '— اختر الموقع —', allowEmpty = true, className = 'vex-select', id }: Props): JSX.Element {
  const { locations, loading } = useLocations(type);
  return (
    <select
      id={id}
      className={className}
      value={value}
      disabled={loading}
      onChange={(e) => {
        const loc = locations.find((l) => l.id === e.target.value);
        onChange(e.target.value, loc?.code ?? '');
      }}
    >
      {allowEmpty ? <option value="">{loading ? 'جارٍ التحميل...' : placeholder}</option> : null}
      {locations.map((l) => (
        <option key={l.id} value={l.id}>{l.code} — {l.name}</option>
      ))}
    </select>
  );
}
