/** Money and dates as the accounting screens show them (Western digits, two decimals). */
export const money = (v: number | null | undefined): string =>
  v === null || v === undefined ? '' : v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** Blank instead of "0.00" — for debit/credit columns where most cells are empty. */
export const moneyOrBlank = (v: number | null | undefined): string => (v ? money(v) : '');

/** Today as yyyy-MM-dd in local time. */
export const today = (): string => new Date().toLocaleDateString('en-CA');

/** First day of the current year as yyyy-MM-dd. */
export const yearStart = (): string => `${new Date().getFullYear()}-01-01`;

/** Round to the four decimals the ledger stores. */
export const round4 = (v: number): number => Math.round(v * 10000) / 10000;
