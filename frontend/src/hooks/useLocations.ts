import { useEffect, useState } from 'react';
import { lookupsApi, type LocationOption } from '../api/endpoints/lookups';
import { unwrapList } from '../api/apiData';

// Locations change rarely; share one request per type across every picker on the page.
const cache = new Map<string, Promise<LocationOption[]>>();

function load(type: string): Promise<LocationOption[]> {
  let p = cache.get(type);
  if (!p) {
    p = lookupsApi.getLocations(type || undefined).then((r) => unwrapList<LocationOption>(r.data));
    p.catch(() => cache.delete(type));
    cache.set(type, p);
  }
  return p;
}

/** Forget the cached lists after a warehouse/location was created or changed. */
export function invalidateLocations(): void {
  cache.clear();
}

export function useLocations(type = ''): { locations: LocationOption[]; loading: boolean } {
  const [locations, setLocations] = useState<LocationOption[]>([]);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let live = true;
    load(type)
      .then((l) => { if (live) setLocations(l); })
      .catch(() => { if (live) setLocations([]); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [type]);
  return { locations, loading };
}
