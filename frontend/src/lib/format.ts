/** Number formatting with the browser's Intl.NumberFormat (Western digits, as on the invoices). One place for every screen. */
const usdFormat = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const sypFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 });
const qtyFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 3 });
const pctFormat = new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 });

export const formatUsd = (v: number): string => `${v < 0 ? '-' : ''}$${usdFormat.format(Math.abs(v))}`;
export const formatSyp = (v: number): string => `${sypFormat.format(Math.round(v))} ل.س`;
export const formatQty = (v: number): string => qtyFormat.format(v);
export const formatPct = (v: number): string => `${pctFormat.format(v)}%`;
