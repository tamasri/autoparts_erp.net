/** Tabs whose selection lives in the URL (?tab=key), so a tab can be linked to and survives a refresh. Only the open tab is mounted. */
import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Box, Tab, Tabs } from '@mui/material';

export type RoutedTab = { key: string; label: string; content: ReactNode };

export default function RoutedTabs({ tabs, paramName = 'tab' }: { tabs: RoutedTab[]; paramName?: string }): JSX.Element {
  const [params, setParams] = useSearchParams();
  const index = Math.max(0, tabs.findIndex((t) => t.key === params.get(paramName)));

  function select(next: number): void {
    const copy = new URLSearchParams(params);
    if (next === 0) copy.delete(paramName); else copy.set(paramName, tabs[next].key);
    setParams(copy, { replace: true });
  }

  return (
    <>
      <Tabs value={index} onChange={(_, i: number) => select(i)} variant="scrollable" scrollButtons="auto" sx={{ mb: 2, borderBottom: 1, borderColor: 'divider' }}>
        {tabs.map((t) => <Tab key={t.key} label={t.label} />)}
      </Tabs>
      <Box>{tabs[index].content}</Box>
    </>
  );
}
