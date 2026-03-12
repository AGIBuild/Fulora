import { useEffect, useState } from 'react';
import { bridgeProfile } from '../bridge/client';

export function useBridgeReady(timeoutMs = 3000): boolean {
  const [isReady, setReady] = useState(false);

  useEffect(() => {
    let cancelled = false;
    bridgeProfile.ready({ timeoutMs })
      .then(() => {
        if (!cancelled) {
          setReady(true);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setReady(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [timeoutMs]);

  return isReady;
}
