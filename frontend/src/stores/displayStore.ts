import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/**
 * How amounts are shown (owner decision 2026-09-22: a display choice only — every amount is stored in dollars).
 * SYP: lira first with the dollar amount small; USD: dollars first with the lira small.
 */
export type PrimaryCurrency = 'SYP' | 'USD';

type DisplayState = {
  primary: PrimaryCurrency;
  setPrimary: (primary: PrimaryCurrency) => void;
};

export const useDisplayStore = create<DisplayState>()(
  persist(
    (set) => ({
      primary: 'SYP',
      setPrimary: (primary) => set({ primary }),
    }),
    { name: 'autoparts-erp-display' },
  ),
);
