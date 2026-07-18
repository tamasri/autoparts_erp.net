import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuthStore } from '../stores/authStore';
import { toast } from '../lib/toast';

export function useSignalR(): void {
  const token = useAuthStore((s) => s.token);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/erp', {
        accessTokenFactory: () => token,
        skipNegotiation: false,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('ReceiveAlert', (message: string) => {
      toast.info(`🚨 تنبيه: ${message}`);
    });

    connection.on('NewApprovalRequest', (message: string) => {
      toast.info(`✅ طلب موافقة جديد: ${message}`);
    });

    connection.on('LowStockAlert', (message: string) => {
      toast.error(`📦 مخزون منخفض: ${message}`);
    });

    connection.on('InvoicePosted', (message: string) => {
      toast.success(`🧾 ${message}`);
    });

    connection
      .start()
      .catch(() => {
        // silent — reconnect handles retries
      });

    connectionRef.current = connection;

    return () => {
      void connection.stop();
      connectionRef.current = null;
    };
  }, [token]);
}
