import { create } from 'zustand';

/**
 * Live counts for the menu badges and "something changed" signals for open screens. Filled once after sign-in
 * (hooks/useRealtime.ts) and kept current by server pushes; a screen that lists approvals or alerts reloads when its version moves.
 */
type RealtimeState = {
  pendingApprovals: number;
  openStockAlerts: number;
  approvalsVersion: number;
  alertsVersion: number;
  setCounts: (counts: { pendingApprovals?: number; openStockAlerts?: number }) => void;
  approvalsChanged: (pending?: number) => void;
  alertsChanged: (raised?: number) => void;
};

export const useRealtimeStore = create<RealtimeState>()((set) => ({
  pendingApprovals: 0,
  openStockAlerts: 0,
  approvalsVersion: 0,
  alertsVersion: 0,
  setCounts: (counts) => set((s) => ({
    pendingApprovals: counts.pendingApprovals ?? s.pendingApprovals,
    openStockAlerts: counts.openStockAlerts ?? s.openStockAlerts,
  })),
  approvalsChanged: (pending) => set((s) => ({
    pendingApprovals: pending ?? s.pendingApprovals,
    approvalsVersion: s.approvalsVersion + 1,
  })),
  alertsChanged: (raised = 0) => set((s) => ({
    openStockAlerts: s.openStockAlerts + raised,
    alertsVersion: s.alertsVersion + 1,
  })),
}));
