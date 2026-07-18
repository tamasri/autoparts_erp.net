import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { Toaster } from 'sonner';
import App from './App';
import { useSignalR } from './hooks/useSignalR';

function SignalRBootstrap(): null {
  useSignalR();
  return null;
}

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element not found.');
}

ReactDOM.createRoot(rootElement).render(
  <React.StrictMode>
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
  </React.StrictMode>,
);
