/**
 * Emotion cache configured for MUI RTL (Arabic-first).
 *
 * Wired once in main.tsx via <CacheProvider value={cacheRtl}>.
 * stylis-plugin-rtl flips CSS directions so we never need per-component `direction: 'rtl'` overrides.
 *
 * Reference: https://mui.com/material-ui/customization/right-to-left/
 *
 * Use ES imports only: `require()` works in the Vite dev server but does not exist in the production bundle
 * (it blanks the whole app with "require is not defined").
 */
import createCache from '@emotion/cache';
import { prefixer } from 'stylis';
import rtlPluginModule from 'stylis-plugin-rtl';

// stylis-plugin-rtl is CommonJS; depending on the bundler the plugin is the module itself or its `default`.
const rtlPlugin = ((rtlPluginModule as unknown as { default?: unknown }).default ?? rtlPluginModule) as typeof rtlPluginModule;

export const cacheRtl = createCache({
  key: 'muirtl',
  // prefixer must precede rtlPlugin in the stylis pipeline
  stylisPlugins: [prefixer, rtlPlugin],
});

export const cacheLtr = createCache({
  key: 'muiltr',
});
