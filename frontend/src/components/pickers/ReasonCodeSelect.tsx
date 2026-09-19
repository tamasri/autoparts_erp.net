import { useEffect, useState } from 'react';
import { reasonCodesApi } from '../../api/endpoints/reasonCodes';
import { unwrapList } from '../../api/apiData';

type Reason = { code: string; description: string };

type Props = {
  /** Reason-code category, e.g. STOCK_ADJUST, VOID_INVOICE, PRICE_OVERRIDE. */
  category: string;
  value: string;
  onChange: (code: string) => void;
  id?: string;
};

/** Dropdown of the configured reason codes for one category (replaces typing a code by hand). */
export default function ReasonCodeSelect({ category, value, onChange, id }: Props): JSX.Element {
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
    <select id={id} className="vex-select" value={value} disabled={loading} onChange={(e) => onChange(e.target.value)}>
      <option value="">{loading ? 'جارٍ التحميل...' : '— اختر السبب —'}</option>
      {reasons.map((r) => <option key={r.code} value={r.code}>{r.description} ({r.code})</option>)}
    </select>
  );
}
