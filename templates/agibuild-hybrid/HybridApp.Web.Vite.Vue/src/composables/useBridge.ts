import { ref, onMounted } from "vue";
import { bridgeProfile } from "../bridge/client";

export function useBridgeReady(timeoutMs = 3000) {
  const isReady = ref(false);
  const error = ref<string | null>(null);

  onMounted(() => {
    bridgeProfile.ready({ timeoutMs })
      .then(() => {
        isReady.value = true;
      })
      .catch((err: Error) => {
        error.value = err.message;
      });
  });

  return { ready: isReady, error };
}
