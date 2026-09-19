import { useMemo } from 'react';
import { useLocations } from './useLocations';

/** id → "CODE — name" for showing warehouses/locations in tables instead of a GUID fragment. */
export function useLocationNames(): { label: (id?: string | null) => string; code: (id?: string | null) => string } {
  const { locations } = useLocations('');
  return useMemo(() => {
    const byId = new Map(locations.map((l) => [l.id, l]));
    return {
      label: (id) => { const l = id ? byId.get(id) : undefined; return l ? `${l.code} — ${l.name}` : (id ? id.slice(0, 8) : '-'); },
      code: (id) => { const l = id ? byId.get(id) : undefined; return l ? l.code : (id ? id.slice(0, 8) : '-'); },
    };
  }, [locations]);
}
