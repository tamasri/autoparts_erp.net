/**
 * Emotion cache configured for MUI RTL (Arabic-first).
 *
 * Wired once in main.tsx via <CacheProvider value={cacheRtl}>.
 * stylis-plugin-rtl flips CSS directions so we never need
 * per-component `direction: 'rtl'` overrides.
 *
 * Reference: https://mui.com/material-ui/customization/right-to-left/
 *
 * Note: stylis and stylis-plugin-rtl ship without TypeScript declarations.
 * They are imported via require() to avoid ESM/CJS interop issues. The
 * eslint-disable and type casts are intentional.
 */
import createCache from '@emotion/cache';

/* eslint-disable @typescript-eslint/no-require-imports, @typescript-eslint/no-explicit-any */
const stylis = require('stylis') as any;
const rtlPlugin = (() => {
  const mod = require('stylis-plugin-rtl') as any;
  return typeof mod === 'function' ? mod : mod?.default ?? mod;
})();
/* eslint-enable @typescript-eslint/no-require-imports, @typescript-eslint/no-explicit-any */

export const cacheRtl = createCache({
  key: 'muirtl',
  // prefixer must precede rtlPlugin in the stylis pipeline
  // eslint-disable-next-line @typescript-eslint/no-unsafe-member-access
  stylisPlugins: [stylis.prefixer, rtlPlugin],
});

export const cacheLtr = createCache({
  key: 'muiltr',
});
