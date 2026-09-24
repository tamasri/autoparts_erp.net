import { useCallback, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import * as signalR from '@microsoft/signalr';
import { useAuthStore } from '../stores/authStore';
import { useRealtimeStore } from '../stores/realtimeStore';
import { freshToken } from '../lib/session';
import { toast } from '../lib/toast';
import { approvalsApi } from '../api/endpoints/approvals';
import { inventoryAlertsApi } from '../api/endpoints/inventoryAlerts';
import { unwrapList, unwrapPaged } from '../api/apiData';
import { approvalActionLabel } from '../features/approvals/actionLabels';

type NewApprovalRequest = { approvalId: string; actionCode: string; requesterId: string; requesterName: string };
type ApprovalDecided = { approvalId: string; actionCode: string; status: string; reviewerName?: string | null; comment?: string | null };
type StockAlert = { count: number; outOfStock: number; items: Array<{ itemId: string; code: string; name: string; alertType: string; available: number; reorderLevel: number }> };

/**
 * Live notices from the server (SignalR). The server decides who gets what (ErpHub groups): reviewers and warehouse managers get new
 * approval requests, a requester gets the decision, users who see stock alerts get new ones. Each notice shows a toast with a button to
 * the screen, updates the menu badges and tells open screens to reload. Mounted once inside the signed-in layout.
 */
export function useRealtime(): void {
  const signedIn = useAuthStore((s) => s.isAuthenticated);
  const navigate = useNavigate();

  // Counts are asked from the API (they depend on the user: e.g. their own requests are not waiting for them). 403 = not theirs to see.
  const refreshCounts = useCallback(async (): Promise<void> => {
    const [approvals, alerts] = await Promise.allSettled([approvalsApi.getPending(1, 1), inventoryAlertsApi.list()]);
    useRealtimeStore.getState().setCounts({
      pendingApprovals: approvals.status === 'fulfilled' ? unwrapPaged(approvals.value.data).totalCount : 0,
      openStockAlerts: alerts.status === 'fulfilled' ? unwrapList<{ status: string }>(alerts.value.data).filter((a) => a.status !== 'RESOLVED').length : 0,
    });
  }, []);

  useEffect(() => {
    if (!signedIn) return undefined;
    void refreshCounts();

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/erp', { accessTokenFactory: freshToken, transport: signalR.HttpTransportType.WebSockets })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    // A warehouse manager who also reviews requests is reached through both routes; one toast per request.
    const seen = new Set<string>();
    connection.on('NewApprovalRequest', (p: NewApprovalRequest) => {
      if (p.requesterId === useAuthStore.getState().user?.id) return; // the sender already knows
      if (seen.has(p.approvalId)) return;
      seen.add(p.approvalId);
      useRealtimeStore.getState().approvalsChanged();
      void refreshCounts();
      toast.notice(`طلب موافقة جديد: ${approvalActionLabel(p.actionCode)}`, 'info',
        { label: 'فتح', onClick: () => navigate('/approvals') }, p.requesterName ? `من ${p.requesterName}` : undefined);
    });

    connection.on('ApprovalDecided', (p: ApprovalDecided) => {
      useRealtimeStore.getState().approvalsChanged();
      void refreshCounts();
      const approved = p.status === 'APPROVED';
      const who = p.reviewerName ? ` (${p.reviewerName})` : '';
      toast.notice(`${approved ? 'تمت الموافقة على' : 'رُفض'} طلبك: ${approvalActionLabel(p.actionCode)}${who}`, approved ? 'success' : 'error',
        { label: 'التفاصيل', onClick: () => navigate('/approvals') }, !approved && p.comment ? p.comment : undefined);
    });

    connection.on('StockAlert', (p: StockAlert) => {
      useRealtimeStore.getState().alertsChanged(p.count);
      const names = p.items.map((i) => i.name).join('، ');
      const title = p.outOfStock > 0 ? `نفد مخزون ${p.outOfStock} صنف` : `${p.count} صنف تحت حد إعادة الطلب`;
      toast.notice(title, p.outOfStock > 0 ? 'error' : 'warning', { label: 'التنبيهات', onClick: () => navigate('/inventory/alerts') },
        names + (p.count > p.items.length ? ` و${p.count - p.items.length} غيرها` : ''));
    });

    // After a gap the notices in between are lost; the counts are read again instead.
    connection.onreconnected(() => { void refreshCounts(); });

    connection.start().catch(() => { /* automatic reconnect retries */ });
    return () => { void connection.stop(); };
  }, [signedIn, navigate, refreshCounts]);
}
