import { useEffect, useRef, useState } from 'react';

export type PickerOption = { id: string; label: string; sublabel?: string; data?: unknown };

type Props = {
  value: PickerOption | null;
  onChange: (option: PickerOption | null) => void;
  /** Called with the typed text (debounced); must query the SERVER — never filter a preloaded list. */
  search: (text: string) => Promise<PickerOption[]>;
  placeholder?: string;
  id?: string;
  disabled?: boolean;
};

/**
 * Searchable combobox for referencing another record (customer, supplier, ...).
 * One page of server results at a time and keyboard friendly, so it scales to any number of records
 * (the old <select> preloaded 200 rows).
 */
export default function EntityPicker({ value, onChange, search, placeholder = 'ابحث...', id, disabled }: Props): JSX.Element {
  const [text, setText] = useState('');
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<PickerOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [active, setActive] = useState(0);
  const boxRef = useRef<HTMLDivElement>(null);
  const requestId = useRef(0);
  const searchRef = useRef(search);
  searchRef.current = search;

  useEffect(() => {
    if (!open) return undefined;
    const handle = window.setTimeout(() => {
      const current = ++requestId.current;
      setLoading(true);
      searchRef.current(text.trim())
        .then((opts) => { if (current === requestId.current) { setOptions(opts); setActive(0); } })
        .catch(() => { if (current === requestId.current) setOptions([]); })
        .finally(() => { if (current === requestId.current) setLoading(false); });
    }, 250);
    return () => window.clearTimeout(handle);
  }, [text, open]);

  useEffect(() => {
    function onDoc(e: MouseEvent): void {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, []);

  function choose(o: PickerOption): void {
    onChange(o);
    setText('');
    setOpen(false);
  }

  if (value && !open) {
    return (
      <div className="vex-input" style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, minHeight: 42 }}>
        <span>
          <strong>{value.label}</strong>
          {value.sublabel ? <span style={{ color: 'var(--txt-muted)', fontSize: 12 }}> · {value.sublabel}</span> : null}
        </span>
        {disabled ? null : (
          <button type="button" className="btn-ghost" style={{ padding: '2px 10px', fontSize: 12 }} onClick={() => setOpen(true)}>تغيير</button>
        )}
      </div>
    );
  }

  return (
    <div className="vex-combobox" ref={boxRef}>
      <input
        id={id}
        className="vex-input"
        value={text}
        disabled={disabled}
        placeholder={placeholder}
        autoComplete="off"
        onFocus={() => setOpen(true)}
        onChange={(e) => { setText(e.target.value); setOpen(true); }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => Math.min(a + 1, options.length - 1)); }
          else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => Math.max(a - 1, 0)); }
          else if (e.key === 'Enter' && options[active]) { e.preventDefault(); choose(options[active]); }
          else if (e.key === 'Escape') setOpen(false);
        }}
      />
      {open ? (
        <div className="vex-combobox__list">
          {loading && options.length === 0 ? <div style={{ padding: 12, color: 'var(--txt-muted)' }}>جارٍ البحث...</div> : null}
          {!loading && options.length === 0 ? <div style={{ padding: 12, color: 'var(--txt-muted)' }}>لا نتائج</div> : null}
          {options.map((o, i) => (
            <div
              key={o.id}
              className={`vex-combobox__option${i === active ? ' vex-combobox__option--active' : ''}`}
              onMouseEnter={() => setActive(i)}
              onMouseDown={(e) => { e.preventDefault(); choose(o); }}
            >
              <div style={{ fontWeight: 600 }}>{o.label}</div>
              {o.sublabel ? <div style={{ fontSize: 12, color: 'var(--txt-muted)' }}>{o.sublabel}</div> : null}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
}
