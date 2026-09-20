import { useEffect, useState } from 'react';
import { fxRatesApi } from '../api/endpoints/fxRates';
import { unwrapNode } from '../api/apiData';

let cached: Promise<number> | null = null;

/** The latest saved exchange rate (SYP per USD). Amounts kept in dollars are shown in lira with it. 0 until loaded or when no rate exists. */
export function useFxMid(): number {
  const [mid, setMid] = useState(0);
  useEffect(() => {
    let live = true;
    cached ??= fxRatesApi.getLatest().then((r) => Number(unwrapNode<{ midRate?: number }>(r.data)?.midRate ?? 0)).catch(() => { cached = null; return 0; });
    void cached.then((v) => { if (live) setMid(v); });
    return () => { live = false; };
  }, []);
  return mid;
}

/** "≈ 30,000,000 ل.س" for a dollar amount, or an empty string when there is no rate. */
export function inLira(usd: number, mid: number): string {
  return usd > 0 && mid > 0 ? `≈ ${Math.round(usd * mid).toLocaleString('en-US')} ل.س` : '';
}
