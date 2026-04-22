import {
  BridgeReadyTimeoutError,
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from '@agibuild/fulora-client';
import { todoService } from './generated/bridge.client';
import { installBridgeMock } from './generated/bridge.mock';

export type { TodoItem } from './generated/bridge.d';

const bridgeClient = createBridgeClient();

if (import.meta.env.DEV) {
  bridgeClient.use(withLogging({ maxParamLength: 120 }));
}

bridgeClient.use(withErrorNormalization());

let mockInstalled = false;

function installMockOnce() {
  if (mockInstalled) {
    return;
  }

  installBridgeMock();
  mockInstalled = true;
}

if (import.meta.env.MODE === 'mock' || import.meta.env.VITE_FULORA_MOCK === 'true') {
  installMockOnce();
}

export const bridge = bridgeClient;

export const bridgeProfile = {
  bridge,
  get isMockMode() {
    return mockInstalled;
  },
  async ready(options?: BridgeReadyOptions) {
    try {
      await bridge.ready(options);
    } catch (err) {
      if (!mockInstalled && err instanceof BridgeReadyTimeoutError) {
        installMockOnce();
        await bridge.ready(options);
        return;
      }

      throw err;
    }
  },
};

export function createFuloraClient() {
  return {
    todo: todoService,
  } as const;
}

export const services = createFuloraClient();

export { todoService };
