import { Alert } from '@mui/material';
import type { ReactNode } from 'react';
import { useAuthStore } from '../../stores/authStore';

type GovernanceGateProps = {
  requiredPermission: string;
  children: ReactNode;
};

export function GovernanceGate({ requiredPermission, children }: GovernanceGateProps): JSX.Element {
  const permissions = useAuthStore((state) => state.permissions);
  const isAllowed = permissions.includes(requiredPermission);

  if (!isAllowed) {
    return <Alert severity="warning">Access denied for this governance action.</Alert>;
  }

  return <>{children}</>;
}
