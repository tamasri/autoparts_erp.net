/**
 * AutoPartsERP — MUI Theme
 *
 * The single source of the look: colours, type, shapes and the few global rules (CssBaseline overrides).
 * Every screen uses MUI; the old theme.css and its utility classes were removed on 2026-09-23.
 *
 * direction: 'rtl' is set here once; the emotion cache (rtlCache.ts) does the
 * actual CSS flipping — no per-component `style={{ direction: 'rtl' }}` needed.
 *
 * Policy: PROJECT_VISION.md §2a (owner-approved 2026-09-19)
 */
import { createTheme } from '@mui/material/styles';

// ── Vex token values (kept in sync with theme.css :root) ──────────────────
const clrPrimary      = '#5c54ff';
const clrPrimaryDark  = '#4840e8';
const clrPrimaryLight = '#7b75ff';
const clrSuccess      = '#22c55e';
const clrWarning      = '#f59e0b';
const clrDanger       = '#ef4444';
const clrInfo         = '#3b82f6';
const clrBg           = '#f0f2f8';
const clrSurface      = '#ffffff';
const clrBorder       = '#e2e8f0';
const txtPrimary      = '#1a202c';
const txtSecondary    = '#718096';
const shadowCard      = '0 2px 12px rgba(0,0,0,0.06), 0 1px 3px rgba(0,0,0,0.04)';
const fontSans        = "'Inter', 'Noto Kufi Arabic', 'Segoe UI', sans-serif";

declare module '@mui/material/styles' {
  interface Palette {
    vex: {
      primaryLight: string;
      sidebarBg: string;
      sidebarText: string;
    };
  }
  interface PaletteOptions {
    vex?: {
      primaryLight?: string;
      sidebarBg?: string;
      sidebarText?: string;
    };
  }
}

export const theme = createTheme({
  direction: 'rtl',

  palette: {
    primary:    { main: clrPrimary, dark: clrPrimaryDark, light: clrPrimaryLight },
    success:    { main: clrSuccess },
    warning:    { main: clrWarning },
    error:      { main: clrDanger },
    info:       { main: clrInfo },
    background: { default: clrBg, paper: clrSurface },
    text:       { primary: txtPrimary, secondary: txtSecondary },
    divider:    clrBorder,
    vex: {
      primaryLight: '#eeecff',
      sidebarBg:    '#1a1a2e',
      sidebarText:  '#a8b2d8',
    },
  },

  typography: {
    fontFamily: fontSans,
    h1: { fontWeight: 800, fontSize: '2rem' },
    h2: { fontWeight: 700, fontSize: '1.5rem' },
    h3: { fontWeight: 700, fontSize: '1.25rem' },
    h4: { fontWeight: 700 },
    h5: { fontWeight: 700 },
    h6: { fontWeight: 600 },
    button: {
      textTransform: 'none',
      fontWeight: 700,
      fontFamily: fontSans,
    },
    body1: { fontFamily: fontSans },
    body2: { fontFamily: fontSans },
  },

  shape: {
    borderRadius: 12, // --radius-md
  },

  // Custom shadows — cast required because MUI expects exactly 25 entries
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  shadows: (
    [
      'none',
      '0 1px 4px rgba(0,0,0,0.06)',       // --shadow-sm
      shadowCard,                          // --shadow-card
      '0 4px 20px rgba(0,0,0,0.07)',      // --shadow-md
      '0 8px 40px rgba(92,84,255,0.10)',  // --shadow-lg
      '0 8px 30px rgba(92,84,255,0.14)', // --shadow-hover
      ...Array(19).fill('none'),
    ]
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ) as any,

  components: {
    // ── Global rules (were in theme.css) ─────────────────────────────────
    MuiCssBaseline: {
      styleOverrides: {
        body: { WebkitFontSmoothing: 'antialiased', MozOsxFontSmoothing: 'grayscale', lineHeight: 1.6 },
        '::-webkit-scrollbar': { width: 6, height: 6 },
        '::-webkit-scrollbar-track': { background: 'transparent' },
        '::-webkit-scrollbar-thumb': { background: '#cbd5e1', borderRadius: 99 },
        '::-webkit-scrollbar-thumb:hover': { background: '#94a3b8' },
      },
    },
    // ── Paper / Card ──────────────────────────────────────────────────────
    MuiPaper: {
      defaultProps: { elevation: 2 },
      styleOverrides: {
        root: {
          boxShadow: shadowCard,
          borderRadius: 12,
        },
      },
    },

    // ── Buttons ───────────────────────────────────────────────────────────
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: {
          borderRadius: 8,
          fontWeight: 700,
          padding: '8px 20px',
        },
        containedPrimary: {
          background: `linear-gradient(135deg, ${clrPrimary} 0%, ${clrPrimaryDark} 100%)`,
          '&:hover': {
            background: `linear-gradient(135deg, ${clrPrimaryDark} 0%, #3730c9 100%)`,
          },
        },
      },
    },

    // ── Text fields ───────────────────────────────────────────────────────
    MuiTextField: {
      defaultProps: { size: 'small', fullWidth: true, variant: 'outlined' },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: {
          borderRadius: 8,
          background: clrSurface,
          '&:hover .MuiOutlinedInput-notchedOutline': {
            borderColor: clrPrimary,
          },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
            borderColor: clrPrimary,
            borderWidth: 2,
          },
        },
      },
    },

    // ── Dialog ────────────────────────────────────────────────────────────
    MuiDialog: {
      styleOverrides: {
        paper: {
          borderRadius: 16,
          boxShadow: '0 8px 40px rgba(92,84,255,0.12)',
        },
      },
    },
    MuiDialogTitle: {
      styleOverrides: {
        root: {
          fontWeight: 700,
          fontSize: '1.1rem',
          borderBottom: `1px solid ${clrBorder}`,
          paddingBottom: 12,
        },
      },
    },
    MuiDialogActions: {
      styleOverrides: {
        root: {
          borderTop: `1px solid ${clrBorder}`,
          padding: '12px 24px',
        },
      },
    },

    // NOTE: MuiDataGrid component overrides must be added via
    //   import '@mui/x-data-grid' augmentation — not in @mui/material theme.
    //   DataGrid styles applied via sx prop on the component instead.

    // ── Chip / Badge ──────────────────────────────────────────────────────
    MuiChip: {
      styleOverrides: {
        root: {
          fontWeight: 700,
          borderRadius: 999, // --radius-pill
        },
      },
    },

    // ── Table ─────────────────────────────────────────────────────────────
    MuiTableHead: {
      styleOverrides: {
        root: {
          background: clrBg,
          '& .MuiTableCell-head': {
            fontWeight: 700,
            fontSize: '0.78rem',
            letterSpacing: '0.05em',
            color: txtSecondary,
            textTransform: 'uppercase',
          },
        },
      },
    },

    // ── Snackbar / Alert ──────────────────────────────────────────────────
    MuiAlert: {
      styleOverrides: {
        root: { borderRadius: 8 },
      },
    },
  },
});
