/**
 * The single entry for everyone the business deals with. A customer is an account that plays the customer role, so customers,
 * vendors, sales reps and carriers live under one menu item: "all accounts" (any role, statements) and "customers" (credit
 * terms and limits). The tab is kept in the URL so links and the browser back button work.
 */
import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Box, CircularProgress, Tab, Tabs } from '@mui/material';

const Parties = lazy(() => import('../parties/Parties'));
const Customers = lazy(() => import('../customers/Customers'));

const TABS = [
  { key: 'all', label: 'كل الحسابات', Content: Parties },
  { key: 'customers', label: 'العملاء', Content: Customers },
] as const;

export default function Accounts(): JSX.Element {
  const [params, setParams] = useSearchParams();
  const index = Math.max(0, TABS.findIndex((t) => t.key === params.get('tab')));
  const { Content } = TABS[index];

  return (
    <Box>
      <Tabs value={index} onChange={(_, i: number) => setParams({ tab: TABS[i].key })} sx={{ mb: 2 }}>
        {TABS.map((t) => <Tab key={t.key} label={t.label} />)}
      </Tabs>
      <Suspense fallback={<Box sx={{ display: 'grid', placeItems: 'center', py: 8 }}><CircularProgress /></Box>}>
        <Content />
      </Suspense>
    </Box>
  );
}
