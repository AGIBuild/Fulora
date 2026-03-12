import { useState, useEffect } from 'react';
import { bridgeProfile } from '../bridge/client';

/** Returns true once the Agibuild WebView Bridge is ready (sticky handshake). */
export function useBridgeReady(): boolean {
  const [isReady, setReady] = useState(false);

  useEffect(() => {
    let cancelled = false;

    bridgeProfile.ready({ timeoutMs: 10_000 })
      .then(() => { if (!cancelled) setReady(true); })
      .catch(() => { if (!cancelled) setReady(false); });

    return () => { cancelled = true; };
  }, []);

  return isReady;
}
