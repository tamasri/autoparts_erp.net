/**
 * components/common/ErrorBoundary.tsx — AutoPartsERP
 *
 * Route-level error boundary. Catches render errors in child trees and
 * shows a styled fallback instead of crashing the entire app.
 *
 * Usage:
 *   <ErrorBoundary>
 *     <Suspense fallback={<PageSkeleton />}>
 *       <Route />
 *     </Suspense>
 *   </ErrorBoundary>
 *
 * Phase 6 — feat(frontend): add route-level ErrorBoundary (phase6)
 */
import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Box, Button, Paper, Typography } from '@mui/material';

type Props = {
  children: ReactNode;
  /** Optional custom fallback UI */
  fallback?: ReactNode;
};

type State = {
  hasError: boolean;
  error: Error | null;
};

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  override componentDidCatch(error: Error, info: ErrorInfo): void {
    // Log to console in dev; in prod, wire to OpenTelemetry / Sentry
    console.error('[ErrorBoundary]', error, info.componentStack);
  }

  handleRetry = (): void => {
    this.setState({ hasError: false, error: null });
  };

  override render(): ReactNode {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;

      return (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: '50vh',
            p: 4,
            direction: 'rtl',
          }}
        >
          <Paper
            sx={{
              p: 4,
              maxWidth: 480,
              textAlign: 'center',
              borderTop: '4px solid',
              borderColor: 'error.main',
            }}
          >
            <Typography variant="h4" sx={{ mb: 1 }}>
              ⚠️
            </Typography>
            <Typography variant="h6" sx={{ mb: 1, fontWeight: 700 }}>
              حدث خطأ غير متوقع
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
              {this.state.error?.message ?? 'تعذر تحميل هذه الصفحة. يرجى المحاولة مجدداً.'}
            </Typography>
            <Button variant="contained" onClick={this.handleRetry}>
              إعادة المحاولة
            </Button>
          </Paper>
        </Box>
      );
    }

    return this.props.children;
  }
}

export default ErrorBoundary;
