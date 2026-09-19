/**
 * main.tsx — AutoPartsERP
 *
 * Provider stack (outermost → innermost):
 *   CacheProvider (emotion RTL)        — flips all MUI CSS to RTL via stylis-plugin-rtl
 *   QueryClientProvider                — TanStack Query (staleTime 30s, no window-focus refetch)
 *   ThemeProvider                      — MUI theme with direction:'rtl' + Vex tokens
 *   CssBaseline                        — MUI CSS reset
 *   BrowserRouter → App               — routing (unchanged)
 *   Toaster                            — sonner notifications (unchanged)
 *   SignalRBootstrap                   — SignalR connection (unchanged)
 *
 * Phase 0 — feat(frontend): wire QueryClient + ThemeProvider + RTL (phase0)
 * Policy: PROJECT_VISION.md §2a (owner-approved 2026-09-19)
 */
import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CacheProvider } from '@emotion/react';
import { ThemeProvider, CssBaseline } from '@mui/material';
import { Toaster } from 'sonner';
import { cacheRtl } from './lib/rtlCache';
import { theme } from './theme/theme';
import './styles/theme.css';
import './i18n';
import App from './App';
import { useSignalR } from './hooks/useSignalR';

// ── SignalR connection bootstrapped inside React tree ───────────────────────
function SignalRBootstrap(): null {
  useSignalR();
  return null;
}

// ── TanStack Query client ───────────────────────────────────────────────────
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,          // 30 s — navigating back to a list uses the cache
      retry: 1,
      refetchOnWindowFocus: false, // ERP desktop app — background refetch is distracting
      gcTime: 5 * 60_000,         // keep unused data 5 min before GC
    },
    mutations: {
      retry: 0,
    },
  },
});

// ── Mount ───────────────────────────────────────────────────────────────────
const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element not found — check index.html for <div id="root">.');
}

ReactDOM.createRoot(rootElement).render(
  <React.StrictMode>
    {/* emotion RTL cache must wrap everything that uses MUI */}
    <CacheProvider value={cacheRtl}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider theme={theme}>
          {/* MUI CSS reset — sets box-sizing, removes default margin etc. */}
          <CssBaseline />
          <BrowserRouter>
            <Toaster
              position="top-right"
              dir="rtl"
              richColors
              closeButton
              toastOptions={{ style: { fontFamily: 'Noto Kufi Arabic, sans-serif', fontSize: '14px' } }}
            />
            <SignalRBootstrap />
            <App />
          </BrowserRouter>
        </ThemeProvider>
      </QueryClientProvider>
    </CacheProvider>
  </React.StrictMode>,
);
