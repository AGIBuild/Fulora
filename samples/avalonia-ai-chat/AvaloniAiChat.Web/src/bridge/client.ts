import {
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from '@agibuild/bridge';
import { aiChatService, windowShellBridgeService } from './generated/bridge.client';

export type {
  AiModelState,
  DroppedFileResult,
  TransparencyLevel,
  WindowShellState,
  WindowShellSettings,
} from './generated/bridge.d';

const bridgeClient = createBridgeClient();

if (import.meta.env.DEV) {
  bridgeClient.use(withLogging({ maxParamLength: 200 }));
}

bridgeClient.use(withErrorNormalization());

export const bridge = bridgeClient;

export const bridgeProfile = {
  bridge,
  ready(options?: BridgeReadyOptions) {
    return bridge.ready(options);
  },
};

export function createFuloraClient() {
  return {
    aiChat: aiChatService,
    windowShellBridge: windowShellBridgeService,
  } as const;
}

export const services = createFuloraClient();

export { aiChatService, windowShellBridgeService };
