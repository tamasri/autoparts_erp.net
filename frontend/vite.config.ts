import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],

  server: {
    host: '0.0.0.0',
    port: 47173,
    proxy: {
      '/api': {
        target: 'http://localhost:47000',
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: 'http://localhost:47000',
        changeOrigin: true,
        ws: true,
      },
    },
  },

  build: {
    chunkSizeWarningLimit: 700,
    rollupOptions: {
      output: {
        manualChunks: {
          // MUI core — shared by all screens
          'vendor-mui': ['@mui/material', '@mui/system'],
          // MUI X DataGrid — larger package, only screens that use it need it
          'vendor-datagrid': ['@mui/x-data-grid'],
          // Emotion — MUI styling engine
          'vendor-emotion': ['@emotion/react', '@emotion/cache', '@emotion/styled'],
          // TanStack Query
          'vendor-query': ['@tanstack/react-query'],
          // Form validation
          'vendor-forms': ['react-hook-form', '@hookform/resolvers', 'zod'],
        },
      },
    },
  },
});

