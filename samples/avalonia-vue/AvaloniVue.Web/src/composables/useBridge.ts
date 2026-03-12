import { ref, onMounted } from 'vue';
import { bridgeProfile } from '@/bridge/client';

/** Returns a reactive ref that becomes true once the Agibuild WebView Bridge is ready. */
export function useBridgeReady() {
  const ready = ref(false);

  onMounted(async () => {
    try {
      await bridgeProfile.ready({ timeoutMs: 10_000 });
      ready.value = true;
    } catch {
      ready.value = false;
    }
  });

  return ready;
}
