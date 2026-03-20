import { createTheme } from '@mui/material/styles';

export const appTheme = createTheme({
  direction: 'rtl',
  typography: {
    fontFamily: '"Noto Kufi Arabic", "Segoe UI", sans-serif',
    h4: { fontWeight: 700 },
    h5: { fontWeight: 700 },
  },
  palette: {
    mode: 'light',
    primary: {
      main: '#0f766e',
    },
    secondary: {
      main: '#b45309',
    },
    background: {
      default: '#f7f9f8',
      paper: '#ffffff',
    },
  },
  shape: {
    borderRadius: 12,
  },
});
