import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useEffect, useRef } from 'react';
import { useAuthStore } from '../stores/authStore';

export function useErpHub(isEnabled = true): HubConnection | null {
  const connectionRef = useRef<HubConnection | null>(null);
  const token = useAuthStore((state) => state.token);

  useEffect(() => {
    if (!isEnabled || !token) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/erp', {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.start().catch(() => {
      // Ignore transient startup errors in local dev.
    });

    return () => {
      void connection.stop();
      connectionRef.current = null;
    };
  }, [isEnabled, token]);

  return connectionRef.current;
}
