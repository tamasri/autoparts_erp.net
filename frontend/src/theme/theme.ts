/**
 * AutoPartsERP — MUI Theme
 *
 * The single source of the look: colours, type, shapes and the few global rules (CssBaseline overrides).
 * Every screen uses MUI; the old theme.css and its utility classes were removed on 2026-09-23.
 *
 * direction: 'rtl' is set here once; the emotion cache (rtlCache.ts) does the
 * actual CSS flipping — no per-component `style={{ direction: 'rtl' }}` needed.
 *
 * Visual identity (owner-approved 2026-09-24, PROJECT_VISION.md §2a): a light interface — white surfaces on a pale ivory
 * page — with Emerald/Forest as the primary colour, Golden Wheat as the secondary, Damask Red for errors, and the geometric
 * star pattern (theme/pattern.ts) behind the page, in the menu, dialog titles and the sign-in screen.
 */
import { createTheme } from '@mui/material/styles';
import { patternImage } from './pattern';

// ── Identity colours ─────────────────────────────────────────────────────
export const brand = {
  forest: '#002623',
  emerald: '#054239',
  teal: '#428177',
  wheat: '#988561',
  sand: '#B9A779',
  ivory: '#EDEBE0',
  umber: '#260F14',
  cherry: '#4A151E',
  damask: '#6B1F2A',
  charcoal: '#161616',
  stone: '#3D3A3B',
} as const;

// ── Derived for a light interface ────────────────────────────────────────
const page = '#F7F6F1';          // a lighter step of Ivory Mist
const surface = '#FFFFFF';
const border = '#E4E0D3';
const textSecondary = '#6E6A62';
const primaryTint = '#E6EFEC';   // selected rows, active menu item, toggles
const shadowCard = '0 2px 12px rgba(0,38,35,0.05), 0 1px 3px rgba(0,38,35,0.04)';
const fontSans = "'Inter', 'Noto Kufi Arabic', 'Segoe UI', sans-serif";

/** Series colours for charts, in the identity's order. */
export const chartColors = [brand.emerald, brand.wheat, brand.teal, brand.damask, brand.sand, brand.stone];
/** The "target" bar next to what was achieved. */
export const chartTargetColor = '#D5CEB8';

declare module '@mui/material/styles' {
  interface Palette {
    brand: typeof brand & { primaryTint: string; page: string };
  }
  interface PaletteOptions {
    brand?: typeof brand & { primaryTint: string; page: string };
  }
}

export const theme = createTheme({
  direction: 'rtl',

  palette: {
    primary:    { main: brand.emerald, dark: brand.forest, light: brand.teal, contrastText: '#fff' },
    secondary:  { main: brand.wheat, dark: '#7A6A4C', light: brand.sand, contrastText: '#fff' },
    success:    { main: '#2F7A5F', contrastText: '#fff' },
    warning:    { main: '#A87B2C', contrastText: '#fff' },
    error:      { main: brand.damask, dark: brand.cherry, contrastText: '#fff' },
    info:       { main: brand.teal, contrastText: '#fff' },
    background: { default: page, paper: surface },
    text:       { primary: brand.charcoal, secondary: textSecondary },
    divider:    border,
    brand:      { ...brand, primaryTint, page },
  },

  typography: {
    fontFamily: fontSans,
    h1: { fontWeight: 800, fontSize: '2rem', color: brand.forest },
    h2: { fontWeight: 700, fontSize: '1.5rem', color: brand.forest },
    h3: { fontWeight: 700, fontSize: '1.25rem', color: brand.forest },
    h4: { fontWeight: 700, color: brand.forest },
    h5: { fontWeight: 700, color: brand.forest },
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
    borderRadius: 12,
  },

  // Custom shadows — cast required because MUI expects exactly 25 entries
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  shadows: (
    [
      'none',
      '0 1px 4px rgba(0,38,35,0.06)',
      shadowCard,
      '0 4px 20px rgba(0,38,35,0.07)',
      '0 8px 40px rgba(0,38,35,0.10)',
      '0 8px 30px rgba(0,38,35,0.14)',
      ...Array(19).fill('none'),
    ]
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  ) as any,

  components: {
    // ── Global rules ─────────────────────────────────────────────────────
    MuiCssBaseline: {
      styleOverrides: {
        // The pattern runs faintly behind every page; cards and tables sit on white above it.
        body: {
          WebkitFontSmoothing: 'antialiased', MozOsxFontSmoothing: 'grayscale', lineHeight: 1.6,
          backgroundColor: page, backgroundImage: patternImage(brand.sand, 0.12, 1), backgroundSize: '96px 96px', backgroundAttachment: 'fixed',
        },
        '::-webkit-scrollbar': { width: 6, height: 6 },
        '::-webkit-scrollbar-track': { background: 'transparent' },
        '::-webkit-scrollbar-thumb': { background: brand.sand, borderRadius: 99 },
        '::-webkit-scrollbar-thumb:hover': { background: brand.wheat },
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
          background: `linear-gradient(135deg, ${brand.emerald} 0%, ${brand.forest} 100%)`,
          '&:hover': { background: brand.forest },
        },
        outlinedPrimary: {
          borderColor: brand.teal,
        },
      },
    },

    // ── Toggle buttons (currency switch and filters) ──────────────────────
    MuiToggleButton: {
      styleOverrides: {
        root: {
          '&.Mui-selected': { backgroundColor: primaryTint, color: brand.forest, fontWeight: 700 },
          '&.Mui-selected:hover': { backgroundColor: '#D9E7E2' },
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
          background: surface,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: '#D9D4C4' },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: brand.teal },
          '&.Mui-focused .MuiOutlinedInput-notchedOutline': { borderColor: brand.emerald, borderWidth: 2 },
        },
      },
    },

    // ── Dialog: the title sits on a band of the pattern ───────────────────
    MuiDialog: {
      styleOverrides: {
        paper: {
          borderRadius: 16,
          boxShadow: '0 8px 40px rgba(0,38,35,0.14)',
        },
      },
    },
    MuiDialogTitle: {
      styleOverrides: {
        root: {
          fontWeight: 700,
          fontSize: '1.1rem',
          color: brand.forest,
          backgroundColor: '#FBFAF5',
          backgroundImage: patternImage(brand.sand, 0.16, 1),
          backgroundSize: '64px 64px',
          borderBottom: `1px solid ${border}`,
          paddingBottom: 12,
        },
      },
    },
    MuiDialogActions: {
      styleOverrides: {
        root: {
          borderTop: `1px solid ${border}`,
          padding: '12px 24px',
        },
      },
    },

    // ── Chip / Badge ──────────────────────────────────────────────────────
    MuiChip: {
      styleOverrides: {
        root: {
          fontWeight: 700,
          borderRadius: 999,
        },
      },
    },

    // ── Tabs ──────────────────────────────────────────────────────────────
    MuiTab: {
      styleOverrides: {
        root: { fontWeight: 600, '&.Mui-selected': { fontWeight: 700 } },
      },
    },

    // ── Table ─────────────────────────────────────────────────────────────
    MuiTableHead: {
      styleOverrides: {
        root: {
          background: brand.ivory,
          '& .MuiTableCell-head': {
            fontWeight: 700,
            fontSize: '0.78rem',
            letterSpacing: '0.05em',
            color: brand.stone,
            textTransform: 'uppercase',
          },
        },
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: { '&.MuiTableRow-hover:hover': { backgroundColor: '#FBFAF6' } },
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
